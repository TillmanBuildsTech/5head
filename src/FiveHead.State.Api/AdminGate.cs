namespace FiveHead.State.Api;

/// <summary>
/// The admin console is off unless FIVEHEAD_ADMIN_ENABLED is an explicit
/// true value. Unrecognized values stay off.
/// </summary>
public static class AdminGate
{
    public const string EnabledKey = "FIVEHEAD_ADMIN_ENABLED";
    public const string TokenKey = "FIVEHEAD_ADMIN_TOKEN";

    public static bool IsEnabled(IConfiguration configuration) => IsEnabled(configuration[EnabledKey]);

    public static bool IsEnabled(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            _ => false
        };
    }

    /// <summary>
    /// A distinct FIVEHEAD_ADMIN_TOKEN is optional. When it is unset, the
    /// console accepts the same bearer token as the agent API. When it is
    /// set, agent tokens are rejected on /admin.
    /// </summary>
    public static string ResolveToken(IConfiguration configuration, string stateToken)
    {
        var configured = configuration[TokenKey];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return stateToken;
        }

        if (configured.Length < 8)
        {
            throw new InvalidOperationException("FIVEHEAD_ADMIN_TOKEN must be at least 8 characters when it is set.");
        }

        return configured;
    }

    public static bool IsAnonymous(PathString path, string method)
    {
        var value = path.Value ?? string.Empty;
        if (HttpMethods.IsGet(method)
            && (value.Equals("/admin", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/admin/", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/admin/app.js", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/admin/app.css", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return value.Equals("/admin/session", StringComparison.OrdinalIgnoreCase)
            && (HttpMethods.IsPost(method) || HttpMethods.IsDelete(method));
    }

    public static bool IsAuthorized(HttpContext context, string adminToken)
    {
        if (AdminSession.IsValid(context.Request.Cookies[AdminSession.CookieName], adminToken, DateTimeOffset.UtcNow))
        {
            return true;
        }

        var header = context.Request.Headers.Authorization.ToString();
        var presented = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : string.Empty;
        return BearerToken.Matches(presented, adminToken);
    }
}

public static class AdminActions
{
    public const string Actor = "admin";
    public const string UpdateKind = "admin.update";
    public const string ReleaseKind = "admin.force_release";
}
