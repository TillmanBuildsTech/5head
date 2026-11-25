using System.Security.Cryptography;
using System.Text;

namespace FiveHead.Mcp.Core.Memory;

/// <summary>SHA-256 of the exact UTF-8 bytes of a memory file. Used as the optimistic-concurrency token.</summary>
public static class MemoryContentHash
{
    public static string Compute(string? content)
    {
        var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static bool Matches(string expected, string actual)
    {
        var normalized = Normalize(expected);
        var expectedBytes = Encoding.UTF8.GetBytes(normalized);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    public static string Normalize(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            throw new ArgumentException("Expected content hash is empty.", nameof(hash));
        }

        var normalized = hash.Trim().ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(c => !Uri.IsHexDigit(c)))
        {
            throw new ArgumentException(
                $"Expected content hash '{hash}' is not a 64-character SHA-256 hex digest.",
                nameof(hash));
        }

        return normalized;
    }
}
