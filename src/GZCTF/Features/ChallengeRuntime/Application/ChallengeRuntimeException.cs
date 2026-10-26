namespace GZCTF.Features.ChallengeRuntime.Application;

/// <summary>Safe, stable diagnostics for a challenge host operation.</summary>
public sealed class ChallengeRuntimeException(string code, int statusCode, string userMessage,
    Exception? innerException = null) : InvalidOperationException(code, innerException)
{
    public string Code => Message;
    public int StatusCode { get; } = statusCode;
    public string UserMessage { get; } = userMessage;

    public static ChallengeRuntimeException Unavailable(Exception cause) => new(
        "challenge.runtime_unavailable", StatusCodes.Status503ServiceUnavailable,
        "The challenge runtime is unavailable. Ask an administrator to configure or check the challenge host connection; a container registry alone does not provide a runtime.", cause);

    public static ChallengeRuntimeException StartFailed(Exception? cause = null) => new(
        "challenge.container_start_failed", StatusCodes.Status502BadGateway,
        "The challenge container could not start. Ask an administrator to check the image, registry access and challenge host resources.", cause);

    public static ChallengeRuntimeException StopFailed(Exception? cause = null) => new(
        "challenge.container_stop_failed", StatusCodes.Status502BadGateway,
        "The challenge container could not stop. Ask an administrator to check the challenge host and retry.", cause);
}
