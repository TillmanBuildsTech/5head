namespace FiveHead.Mcp.Core.Memory;

/// <summary>The three curated memory files a client can read or write.</summary>
public enum MemoryFile
{
    /// <summary>Stable agent identity and principles. Max 1,500 chars.</summary>
    Soul,

    /// <summary>Learned project facts and conventions. Max 2,200 chars.</summary>
    Memory,

    /// <summary>Operator profile summary. Max 1,375 chars.</summary>
    User
}
