namespace FiveHead.Mcp.Core.Config;

public sealed class MemoryRootLocator : IMemoryRootLocator
{
    public string Locate(string startingPath)
    {
        var current = File.Exists(startingPath)
            ? Path.GetDirectoryName(startingPath)
            : startingPath;

        if (string.IsNullOrWhiteSpace(current))
        {
            current = Directory.GetCurrentDirectory();
        }

        var directory = new DirectoryInfo(Path.GetFullPath(current));
        while (directory is not null)
        {
            var memoryRoot = Path.Combine(directory.FullName, ".agent-memory");
            if (Directory.Exists(memoryRoot))
            {
                return memoryRoot;
            }

            var gitPath = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return memoryRoot;
            }

            directory = directory.Parent;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".agent-memory",
            "global");
    }
}
