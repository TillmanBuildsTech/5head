using System.Net;
using System.Text.Json;

namespace FiveHead.State.Client;

public class StateApiException : Exception
{
    public StateApiException(HttpStatusCode status, string body, string message)
        : base(message)
    {
        Status = status;
        Body = body;
    }

    public HttpStatusCode Status { get; }
    public string Body { get; }
}

/// <summary>
/// HTTP 409 from a version-checked update or a claim held by someone else.
/// <see cref="CurrentVersion"/> is the version in the response body when the
/// server sent one.
/// </summary>
public sealed class StateConflictException : StateApiException
{
    public StateConflictException(string body, long? currentVersion, string? apiMessage)
        : base(HttpStatusCode.Conflict, body, BuildMessage(body, currentVersion, apiMessage))
    {
        CurrentVersion = currentVersion;
    }

    public long? CurrentVersion { get; }

    public static StateConflictException FromBody(string body)
    {
        long? version = null;
        string? message = null;
        try
        {
            var parsed = JsonSerializer.Deserialize(body, StateJsonContext.Default.StateErrorBody);
            version = parsed?.Matter?.Version;
            message = parsed?.Message;
        }
        catch (JsonException)
        {
            version = null;
        }

        return new StateConflictException(body, version, message);
    }

    private static string BuildMessage(string body, long? currentVersion, string? apiMessage)
    {
        var versionText = currentVersion?.ToString() ?? "unknown";
        var detail = string.IsNullOrWhiteSpace(apiMessage) ? body : apiMessage;
        return $"State API returned 409. Current version is {versionText}. Re-read the matter and retry. {detail}";
    }
}
