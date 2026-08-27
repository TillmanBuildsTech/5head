using FiveHead.Mcp.Core.Memory;
using FiveHead.Mcp.Core.Provenance;

namespace FiveHead.Mcp.Core.Storage;

/// <summary>Read/write access to the markdown memory files on disk.</summary>
public interface IMemoryFileStore
{
    /// <summary>Returns the content of the file, or an empty string if not yet created.</summary>
    Task<string> ReadAsync(MemoryFile file, CancellationToken ct = default);

    /// <summary>
    /// Writes content to the file using the specified mode.
    /// Throws <see cref="SizeLimitExceededException"/> if the result would exceed the limit.
    /// </summary>
    Task WriteAsync(
        MemoryFile file,
        WriteMode  mode,
        string     content,
        CancellationToken ct = default);

    /// <summary>Ensures the <c>.agent-memory/</c> directory and default files exist.</summary>
    Task EnsureInitializedAsync(CancellationToken ct = default);
}

public sealed class SizeLimitExceededException(MemoryFile file, int current, int limit)
    : Exception($"{MemoryLimits.FileName(file)} would be {current} chars, limit is {limit}.")
{
    public MemoryFile File    { get; } = file;
    public int        Current { get; } = current;
    public int        Limit   { get; } = limit;
}
