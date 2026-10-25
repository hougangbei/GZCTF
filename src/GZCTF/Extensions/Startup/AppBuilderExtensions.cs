using System.Net;
using Serilog;
using StackExchange.Redis;

namespace GZCTF.Extensions.Startup;

internal static class AppBuilderExtensions
{
    extension(WebApplicationBuilder builder)
    {
        internal void ConfigureWebHost()
        {
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.ConfigCustomSerializerOptions();
            });

            builder.Services.AddLocalization(options => options.ResourcesPath = "Resources")
                .Configure<RequestLocalizationOptions>(options =>
                {
                    options
                        .AddSupportedCultures(SupportedCultures)
                        .AddSupportedUICultures(SupportedCultures);

                    options.ApplyCurrentCultureToResponseHeaders = true;
                });

            builder.WebHost.ConfigureKestrel(options =>
            {
                var kestrelSection = builder.Configuration.GetSection("Kestrel");
                options.Configure(kestrelSection);
                kestrelSection.Bind(options);
            }).UseKestrel(options =>
            {
                var address = Environment.GetEnvironmentVariable("GZCTF_BIND_ADDRESS");
                if (!string.IsNullOrWhiteSpace(address))
                {
                    if (!IPAddress.TryParse(address, out var bindAddress))
                        throw new InvalidOperationException("GZCTF_BIND_ADDRESS must be an IP address.");
                    options.Listen(bindAddress, ServerPort);
                    options.Listen(bindAddress, MetricPort);
                }
                else
                {
                    options.ListenAnyIP(ServerPort);
                    options.ListenAnyIP(MetricPort);
                }
            });

            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Trace);
            builder.Logging.AddSerilog(dispose: true);
            builder.Host.UseSerilog(dispose: true);
            builder.Configuration.AddEnvironmentVariables("GZCTF_");

            builder.Services.AddServiceDiscovery();
            builder.Services.ConfigureHttpClientDefaults(http =>
            {
                http.AddStandardResilienceHandler();
                http.AddServiceDiscovery();
            });
        }

        internal void ConfigureCacheAndSignalR()
        {
            var signalrBuilder = builder.Services.AddSignalR().AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.ConfigCustomSerializerOptions();
            });

            var connectionString = builder.Configuration.GetConnectionString("RedisCache");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                builder.Services.AddDistributedMemoryCache();
            }
            else
            {
                builder.Services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = connectionString;
                });

                signalrBuilder.AddStackExchangeRedis(connectionString, options =>
                {
                    options.Configuration.ChannelPrefix = new RedisChannel("GZCTF", RedisChannel.PatternMode.Literal);
                });
            }

            builder.Services.AddMemoryCache();
        }
    }
}
