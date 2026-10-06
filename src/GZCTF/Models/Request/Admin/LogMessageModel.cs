using System.Net;
using System.Text.Json.Serialization;

namespace GZCTF.Models.Request.Admin;

/// <summary>
/// Log information (Admin)
/// </summary>
public class LogMessageModel
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    /// <summary>
    /// Log time
    /// </summary>
    [JsonPropertyName("time")]
    public DateTimeOffset Time { get; set; }

    /// <summary>
    /// Username
    /// </summary>
    [JsonPropertyName("name")]
    public string? UserName { get; set; }

    [JsonPropertyName("level")]
    public string? Level { get; set; }

    /// <summary>
    /// IP address
    /// </summary>
    [JsonPropertyName("ip")]
    public IPAddress? IP { get; set; }

    /// <summary>
    /// Log message
    /// </summary>
    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("exception")]
    public string? Exception { get; set; }

    /// <summary>
    /// Task status
    /// </summary>
    [JsonPropertyName("status")]
    public TaskStatus? Status { get; set; }

    public static LogMessageModel FromLogModel(LogModel logInfo) =>
        new()
        {
            Id = logInfo.Id,
            Time = logInfo.TimeUtc,
            Level = logInfo.Level,
            UserName = logInfo.UserName,
            IP = logInfo.RemoteIP,
            Msg = logInfo.Message,
            Source = logInfo.Logger,
            Exception = logInfo.Exception,
            Status = logInfo.Status
        };
}
