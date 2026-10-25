using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Features.Updates.Application;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GZCTF.Test.UnitTests.Features.Updates;

public sealed class UpdateAgentClientTests
{
    [Theory]
    [InlineData(null, null, null, false)]
    [InlineData(null, "/tmp/agent.sock", null, false)]
    [InlineData(null, null, "/tmp/token", false)]
    [InlineData(null, "/tmp/agent.sock", "/tmp/token", true)]
    [InlineData("http://host.docker.internal:51237", null, "/tmp/token", true)]
    [InlineData("https://internal.example.test:51237", null, "/tmp/token", true)]
    [InlineData("http://host.docker.internal:51237", null, null, false)]
    public void Configuration_requires_token_and_a_supported_transport(string? baseUrl, string? socket,
        string? tokenFile, bool expected) => Assert.Equal(expected, Client(baseUrl, socket, tokenFile).Configured);

    [Theory]
    [InlineData("ftp://localhost:51237")]
    [InlineData("/relative")]
    [InlineData("http://user:secret@localhost:51237")]
    [InlineData("http://localhost:51237?token=secret")]
    [InlineData("http://localhost:51237#fragment")]
    [InlineData("not a URL")]
    public async Task Invalid_explicit_base_url_is_rejected_even_when_socket_exists(string baseUrl)
    {
        var client = Client(baseUrl, "/tmp/agent.sock", "/tmp/token");
        Assert.False(client.Configured);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SendAsync(System.Net.Http.HttpMethod.Get, "/status", null, CancellationToken.None));
    }

    [Fact]
    public async Task Http_transport_prefers_base_url_and_forwards_bearer_path_body_and_response()
    {
        var tokenFile = Path.GetTempFileName();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            const string secret = "0123456789abcdef0123456789abcdef";
            await File.WriteAllTextAsync(tokenFile, secret + "\n", timeout.Token);
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var received = ReceiveAsync(listener, timeout.Token);
            var client = Client($"http://127.0.0.1:{port}", "/nonexistent/preferred-over.sock", tokenFile);
            var result = await client.SendAsync(System.Net.Http.HttpMethod.Post, "/validate?refresh=1",
                new { source = new { kind = "directory", location = "/updates" } }, timeout.Token);
            var request = await received;
            Assert.Equal("POST /validate?refresh=1 HTTP/1.1", request.StartLine);
            Assert.Equal("Bearer " + secret, request.Headers["Authorization"]);
            using var body = JsonDocument.Parse(request.Body);
            Assert.Equal("/updates", body.RootElement.GetProperty("source").GetProperty("location").GetString());
            Assert.Equal(202, result.StatusCode);
            Assert.Equal("validating", result.Payload.GetProperty("phase").GetString());
        }
        finally { File.Delete(tokenFile); }
    }

    private static UpdateAgentClient Client(string? baseUrl, string? socket, string? tokenFile) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Updater:BaseUrl"] = baseUrl,
            ["Updater:SocketPath"] = socket,
            ["Updater:TokenFile"] = tokenFile
        }).Build());

    private static async Task<(string StartLine, Dictionary<string, string> Headers, string Body)> ReceiveAsync(
        TcpListener listener, CancellationToken token)
    {
        using var connection = await listener.AcceptTcpClientAsync(token);
        await using var stream = connection.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var startLine = await reader.ReadLineAsync(token) ?? "";
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadLineAsync(token) is { Length: > 0 } line)
        {
            var separator = line.IndexOf(':');
            headers[line[..separator]] = line[(separator + 1)..].Trim();
        }
        var body = new char[int.Parse(headers["Content-Length"])];
        Assert.Equal(body.Length, await reader.ReadBlockAsync(body.AsMemory(), token));
        var response = Encoding.UTF8.GetBytes("HTTP/1.1 202 Accepted\r\nContent-Type: application/json\r\nContent-Length: 22\r\nConnection: close\r\n\r\n{\"phase\":\"validating\"}");
        await stream.WriteAsync(response, token);
        return (startLine, headers, new string(body));
    }
}
