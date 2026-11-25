using System.Security.Cryptography;
using System.Text;

namespace FiveHead.State.Api;

/// <summary>Compares bearer tokens without leaking the expected value through an early length check.</summary>
public static class BearerToken
{
    public static bool Matches(string? presented, string expected)
    {
        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presented ?? string.Empty));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(presentedHash, expectedHash);
    }
}

public static class ConnectionStrings
{
    public static string ToNpgsql(string raw)
    {
        if (!raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }

        var uri = new Uri(raw);
        var user = string.Empty;
        var password = string.Empty;
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            user = Uri.UnescapeDataString(parts[0]);
            password = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
        }

        var database = uri.AbsolutePath.Trim('/');
        var port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port;
        return $"Host={uri.Host};Port={port};Username={user};Password={password};Database={database}";
    }
}
