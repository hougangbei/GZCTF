using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Features.ChallengeRuntime.Application;

public sealed record LearningInstanceSettings(int MaxConcurrentPerUser = 2, int LifetimeMinutes = 120)
{
    public bool IsValid => MaxConcurrentPerUser is >= 1 and <= 20 && LifetimeMinutes is >= 10 and <= 1440;
}

public sealed class ChallengeInstanceLimitException : Exception
{
    public ChallengeInstanceLimitException() : base("challenge.instance_limit_reached") { }
}

public sealed class LearningInstanceSettingsService(AppDbContext db)
{
    private const string ConfigKey = "LearningInstanceSettings";

    public async Task<LearningInstanceSettings> GetAsync(CancellationToken token = default)
    {
        var value = await db.Configs.AsNoTracking()
            .Where(item => item.ConfigKey == ConfigKey)
            .Select(item => item.Value)
            .SingleOrDefaultAsync(token);
        if (string.IsNullOrWhiteSpace(value)) return new LearningInstanceSettings();
        try
        {
            var settings = JsonSerializer.Deserialize<LearningInstanceSettings>(value);
            return settings is { IsValid: true } ? settings : new LearningInstanceSettings();
        }
        catch (JsonException)
        {
            return new LearningInstanceSettings();
        }
    }

    public async Task SaveAsync(LearningInstanceSettings settings, CancellationToken token = default)
    {
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        var config = await db.Configs.SingleOrDefaultAsync(item => item.ConfigKey == ConfigKey, token);
        if (config is null)
        {
            config = new Config(ConfigKey, JsonSerializer.Serialize(settings));
            db.Configs.Add(config);
        }
        else
        {
            config.Value = JsonSerializer.Serialize(settings);
        }
        await db.SaveChangesAsync(token);
    }
}
