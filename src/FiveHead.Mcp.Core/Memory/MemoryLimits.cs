namespace FiveHead.Mcp.Core.Memory;

/// <summary>Size limit constants (in characters) per memory file.</summary>
public static class MemoryLimits
{
    public const int Soul   = 1_500;
    public const int Memory = 2_200;
    public const int User   = 1_375;

    public static int For(MemoryFile file) => file switch
    {
        MemoryFile.Soul   => Soul,
        MemoryFile.Memory => Memory,
        MemoryFile.User   => User,
        _                 => throw new ArgumentOutOfRangeException(nameof(file))
    };

    public static string FileName(MemoryFile file) => file switch
    {
        MemoryFile.Soul   => "SOUL.md",
        MemoryFile.Memory => "MEMORY.md",
        MemoryFile.User   => "USER.md",
        _                 => throw new ArgumentOutOfRangeException(nameof(file))
    };
}
