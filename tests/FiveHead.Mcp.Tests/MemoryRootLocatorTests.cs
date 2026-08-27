using FiveHead.Mcp.Core.Config;

namespace FiveHead.Mcp.Tests;

public sealed class MemoryRootLocatorTests : IDisposable
{
    private readonly string _workspaceRoot;
    private readonly MemoryRootLocator _locator = new();

    public MemoryRootLocatorTests()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), "fivehead-root-locator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspaceRoot);
    }

    [Fact]
    public void Locate_ReturnsRepoLocalMemoryRootWhenGitBoundaryIsFound()
    {
        var repoRoot = CreateDirectory("repo");
        Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
        var nested = Directory.CreateDirectory(Path.Combine(repoRoot, "src", "feature"));

        var memoryRoot = _locator.Locate(nested.FullName);

        Assert.Equal(Path.Combine(repoRoot, ".agent-memory"), memoryRoot);
    }

    [Fact]
    public void Locate_PrefersNearestRepoBoundaryOverParentGlobalLikeMemoryRoot()
    {
        var parent = CreateDirectory("parent");
        Directory.CreateDirectory(Path.Combine(parent, ".agent-memory"));

        var repoRoot = Directory.CreateDirectory(Path.Combine(parent, "repo")).FullName;
        Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
        var nested = Directory.CreateDirectory(Path.Combine(repoRoot, "src", "feature"));

        var memoryRoot = _locator.Locate(nested.FullName);

        Assert.Equal(Path.Combine(repoRoot, ".agent-memory"), memoryRoot);
    }

    [Fact]
    public void Locate_FallsBackToUserGlobalMemoryWhenNoRepoBoundaryExists()
    {
        var standalone = CreateDirectory("standalone");

        var memoryRoot = _locator.Locate(standalone);

        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agent-memory", "global"),
            memoryRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private string CreateDirectory(string name)
    {
        var path = Path.Combine(_workspaceRoot, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
