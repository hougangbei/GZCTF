using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace GZCTF.Features.Updates.Application;

public sealed record UpdateAgentResult(int StatusCode, JsonElement Payload);

public sealed class UpdateAgentClient(IConfiguration configuration)
{
    private readonly string? _socketPath = configuration["Updater:SocketPath"];
    private readonly string? _tokenFile = configuration["Updater:TokenFile"];
    private readonly bool _useBaseUrl = !string.IsNullOrWhiteSpace(configuration["Updater:BaseUrl"]);
    private readonly Uri? _baseUrl = ParseBaseUrl(configuration["Updater:BaseUrl"]);

    public bool Configured => !string.IsNullOrWhiteSpace(_tokenFile) &&
        (_useBaseUrl ? _baseUrl is not null : !string.IsNullOrWhiteSpace(_socketPath));

    private static Uri? ParseBaseUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host) &&
        uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
            ? uri : null;

    public async Task<UpdateAgentResult> SendAsync(HttpMethod method, string path, object? body,
        CancellationToken token, string? actor = null)
    {
        using var content = body is null ? null : new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8,
            "application/json");
        return await SendCoreAsync(method, path, content, TimeSpan.FromSeconds(20), token, actor: actor);
    }

    public async Task<UpdateAgentResult> UploadAsync(Stream stream, long length, string fileName,
        CancellationToken token, string? actor = null)
    {
        if (length is <= 0 or > 1073741824) throw new ArgumentOutOfRangeException(nameof(length));
        using var content = new UploadContent(stream, length);
        return await SendCoreAsync(HttpMethod.Post, "/packages/upload", content, TimeSpan.FromMinutes(15), token,
            Uri.EscapeDataString(fileName), actor);
    }

    private async Task<UpdateAgentResult> SendCoreAsync(HttpMethod method, string path, HttpContent? content,
        TimeSpan timeout, CancellationToken token, string? fileName = null, string? actor = null)
    {
        if (!Configured) throw new InvalidOperationException("Update agent is not configured.");
        var secret = (await File.ReadAllTextAsync(_tokenFile!, token)).Trim();
        if (secret.Length < 32) throw new InvalidOperationException("Update agent token is invalid.");

        using var handler = new SocketsHttpHandler { UseProxy = false };
        if (!_useBaseUrl)
            handler.ConnectCallback = async (_, cancellationToken) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath!), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            };
        using var client = new HttpClient(handler) { BaseAddress = _baseUrl ?? new Uri("http://localhost"),
            Timeout = timeout };
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        request.Content = content;
        if (fileName is not null) request.Headers.Add("X-File-Name", fileName);
        if (Guid.TryParse(actor, out var actorId) && actorId != Guid.Empty)
            request.Headers.Add("X-Update-Actor", actorId.ToString("D"));
        using var response = await client.SendAsync(request, token);
        await using var responseStream = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: token);
        return new UpdateAgentResult((int)response.StatusCode, document.RootElement.Clone());
    }

    // Never buffer an update archive in memory or dispose ASP.NET's request body.
    private sealed class UploadContent(Stream source, long length) : HttpContent
    {
        protected override bool TryComputeLength(out long value) { value = length; return true; }
        protected override Task SerializeToStreamAsync(Stream target, System.Net.TransportContext? context) =>
            CopyAsync(target, CancellationToken.None);
        protected override Task SerializeToStreamAsync(Stream target, System.Net.TransportContext? context,
            CancellationToken cancellationToken) => CopyAsync(target, cancellationToken);

        private async Task CopyAsync(Stream target, CancellationToken token)
        {
            var buffer = new byte[128 * 1024];
            var remaining = length;
            while (remaining > 0)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), token);
                if (read == 0) throw new IOException("Update upload ended before its declared length.");
                await target.WriteAsync(buffer.AsMemory(0, read), token);
                remaining -= read;
            }
        }
    }
}
