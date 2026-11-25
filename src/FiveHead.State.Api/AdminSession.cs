using System.Security.Cryptography;
using System.Text;

namespace FiveHead.State.Api;

/// <summary>
/// HttpOnly session cookie. The raw bearer token is checked once at login
/// and is not stored in the browser script.
/// </summary>
public static class AdminSession
{
    public const string CookieName = "fivehead_admin";
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    public static string Issue(string adminToken, DateTimeOffset now)
    {
        var exp = now.Add(Lifetime).ToUnixTimeSeconds();
        return $"{exp.ToString(System.Globalization.CultureInfo.InvariantCulture)}.{Convert.ToHexString(Sign(adminToken, exp))}";
    }

    public static bool IsValid(string? value, string adminToken, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var dot = value.IndexOf('.');
        if (dot <= 0 || dot != value.LastIndexOf('.'))
        {
            return false;
        }

        if (!long.TryParse(
                value[..dot],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var exp))
        {
            return false;
        }

        if (now.ToUnixTimeSeconds() > exp)
        {
            return false;
        }

        byte[] presented;
        try
        {
            presented = Convert.FromHexString(value[(dot + 1)..]);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = Sign(adminToken, exp);
        return presented.Length == expected.Length && CryptographicOperations.FixedTimeEquals(presented, expected);
    }

    public static void Set(HttpContext context, string adminToken)
    {
        context.Response.Cookies.Append(CookieName, Issue(adminToken, DateTimeOffset.UtcNow), new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/admin",
            MaxAge = Lifetime,
            IsEssential = true
        });
    }

    public static void Clear(HttpContext context)
    {
        // Max-Age is omitted. Browsers prefer Max-Age over Expires, so a
        // delete that copied the login Max-Age would refresh the cookie.
        context.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/admin",
            IsEssential = true
        });
    }

    private static byte[] Sign(string adminToken, long exp)
    {
        var message = Encoding.UTF8.GetBytes($"fivehead-admin.v1.{exp.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(adminToken), message);
    }
}
