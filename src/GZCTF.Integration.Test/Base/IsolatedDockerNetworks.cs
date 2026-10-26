using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace GZCTF.Integration.Test.Base;

/// <summary>Own only these explicit-subnet networks; never consume or prune shared default pools.</summary>
internal sealed class IsolatedDockerNetworks(DockerClient client, string prefix) : IAsyncDisposable
{
    private readonly List<string> created = [];
    public async Task CreateAsync()
    {
        foreach (var mode in new[] { "open", "isolated" })
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var response = await client.Networks.CreateNetworkAsync(new NetworksCreateParameters
                    {
                        Name = $"{prefix}-{mode}", Driver = "bridge", Attachable = true,
                        IPAM = new IPAM { Config = [new IPAMConfig
                        { Subnet = $"10.{Random.Shared.Next(128, 255)}.{Random.Shared.Next(256)}.0/28" }] },
                        Options = mode == "isolated" ? new Dictionary<string, string>
                            { ["com.docker.network.bridge.enable_ip_masquerade"] = "false" } : new Dictionary<string, string>()
                    });
                    created.Add(response.ID);
                    break;
                }
                catch (DockerApiException error) when (attempt < 10 && error.StatusCode == HttpStatusCode.Forbidden &&
                    error.ResponseBody?.Contains("overlap", StringComparison.OrdinalIgnoreCase) == true) { }
            }
        }
    }
    public async ValueTask DisposeAsync()
    {
        foreach (var id in created)
            try { await client.Networks.DeleteNetworkAsync(id); }
            catch (DockerApiException error) when (error.StatusCode == HttpStatusCode.NotFound) { }
        created.Clear();
    }
}
