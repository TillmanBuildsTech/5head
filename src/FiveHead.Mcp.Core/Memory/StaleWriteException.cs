namespace FiveHead.Mcp.Core.Memory;

/// <summary>
/// The file changed after the caller read it. The write was not applied.
/// </summary>
public sealed class StaleWriteException(MemoryFile file, string expectedHash, string actualHash)
    : Exception($"{MemoryLimits.FileName(file)} changed since it was read (expected {expectedHash}, current {actualHash}). Re-read the file and retry.")
{
    public MemoryFile File { get; } = file;
    public string ExpectedHash { get; } = expectedHash;
    public string ActualHash { get; } = actualHash;
}
