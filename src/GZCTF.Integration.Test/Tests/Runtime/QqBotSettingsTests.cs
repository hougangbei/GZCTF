using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Request.Account;
using GZCTF.Utils;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Runtime;

[Collection(nameof(IntegrationTestCollection))]
public sealed class QqBotSettingsTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Admin_can_configure_and_test_delivery_without_exposing_token()
    {
        using var visitor = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await visitor.GetAsync("/api/admin/qq-bot/settings")).StatusCode);

        using var admin = await CreateAdminClientAsync();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync();
            await using var stream = peer.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var headers = new List<string>();
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync())) headers.Add(line);
            var payload = Encoding.UTF8.GetBytes("{\"status\":\"ok\",\"retcode\":0}");
            var response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response);
            await stream.WriteAsync(payload);
            return headers;
        });

        try
        {
            var invalid = await admin.PutAsJsonAsync("/api/admin/qq-bot/settings", new
            {
                enabled = true, baseUrl = "", accessToken = "secret", clearAccessToken = false,
                groupId = "123", messageTemplate = "{member} {challenge}",
                notifyLearningSolves = true, notifyGameSolves = true
            });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

            var saved = await admin.PutAsJsonAsync("/api/admin/qq-bot/settings", new
            {
                enabled = true, baseUrl = $"http://127.0.0.1:{port}", accessToken = "secret",
                clearAccessToken = false, groupId = "123", messageTemplate = "{member} 解出了 {challenge}",
                notifyLearningSolves = true, notifyGameSolves = true
            });
            saved.EnsureSuccessStatusCode();
            var view = await admin.GetFromJsonAsync<JsonElement>("/api/admin/qq-bot/settings");
            Assert.True(view.GetProperty("accessTokenConfigured").GetBoolean());
            Assert.False(view.TryGetProperty("accessToken", out _));

            (await admin.PostAsync("/api/admin/qq-bot/test", null)).EnsureSuccessStatusCode();
            var headers = await received.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains(headers, line => line.StartsWith("POST /send_group_msg "));
            Assert.Contains(headers, line => line == "Authorization: Bearer secret");
        }
        finally
        {
            await admin.PutAsJsonAsync("/api/admin/qq-bot/settings", new
            {
                enabled = false, baseUrl = "", accessToken = (string?)null,
                clearAccessToken = true, groupId = "", messageTemplate = "🎉 {member} 解出了 {challenge}（{source}）",
                notifyLearningSolves = true, notifyGameSolves = true
            });
        }
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        const string password = "QqBot!Admin2026";
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password,
            role: Role.Admin);
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/Account/LogIn",
            new LoginModel { UserName = user.UserName, Password = password })).EnsureSuccessStatusCode();
        return client;
    }
}
