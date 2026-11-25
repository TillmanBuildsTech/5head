namespace FiveHead.State.Client;

/// <summary>
/// Where 5head sends shared-state calls. Absent when the process is memory-only.
/// </summary>
public sealed class StateClientSettings
{
    public const string UrlVariable = "FIVEHEAD_STATE_URL";
    public const string TokenVariable = "FIVEHEAD_STATE_TOKEN";
    public const string AgentVariable = "FIVEHEAD_STATE_AGENT";

    public StateClientSettings(string baseUrl, string token, string agent)
    {
        BaseUrl = baseUrl;
        Token = token;
        Agent = agent;
    }

    public string BaseUrl { get; }
    public string Token { get; }
    public string Agent { get; }

    /// <summary>
    /// Environment values win over config.yaml. Both the URL and the token are
    /// required. Neither means the state tools stay unregistered. Exactly one
    /// is a misconfiguration and throws.
    /// </summary>
    public static StateClientSettings? Resolve(
        string? configUrl,
        string? configToken,
        string? configAgent,
        Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        var url = FirstNonEmpty(environment(UrlVariable), configUrl);
        var token = FirstNonEmpty(environment(TokenVariable), configToken);
        var agent = FirstNonEmpty(environment(AgentVariable), configAgent) ?? "5head";

        var hasUrl = url is not null;
        var hasToken = token is not null;
        if (!hasUrl && !hasToken)
        {
            return null;
        }

        if (!hasUrl || !hasToken)
        {
            throw new InvalidOperationException(
                "Shared state needs both a URL and a token. Set FIVEHEAD_STATE_URL and FIVEHEAD_STATE_TOKEN, or stateApiUrl and stateAuthToken in .agent-memory/config.yaml.");
        }

        return new StateClientSettings(url!, token!, agent);
    }

    private static string? FirstNonEmpty(string? primary, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback.Trim();
        }

        return null;
    }
}
