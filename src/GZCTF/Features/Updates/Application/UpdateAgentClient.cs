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
        CancellationToken token)
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
            Timeout = TimeSpan.FromSeconds(20) };
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8,
                "application/json");
        using var response = await client.SendAsync(request, token);
        await using var content = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: token);
        return new UpdateAgentResult((int)response.StatusCode, document.RootElement.Clone());
    }
}
