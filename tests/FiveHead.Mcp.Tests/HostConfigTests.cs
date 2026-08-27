using System.Reflection;
using FiveHead.Mcp.Core.Config;
using FiveHead.Mcp.Host;

namespace FiveHead.Mcp.Tests;

public sealed class HostConfigTests : IDisposable
{
    private readonly string _workspaceRoot;
    private readonly string _memoryRoot;

    public HostConfigTests()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), "fivehead-host-config-tests", Guid.NewGuid().ToString("N"));
        _memoryRoot = Path.Combine(_workspaceRoot, ".agent-memory");
        Directory.CreateDirectory(_memoryRoot);
    }

    [Fact]
    public void LoadConfig_ReturnsDefaultsWhenFileMissing()
    {
        var config = InvokeLoadConfig(_memoryRoot);

        Assert.Equal(30, config.StalenessThresholdDays);
        Assert.Null(config.ProvenanceDbPath);
    }

    [Fact]
    public async Task LoadConfig_ReadsOnlyV1FieldsFromRepoLocalYaml()
    {
        var configPath = Path.Combine(_memoryRoot, "config.yaml");
        await File.WriteAllTextAsync(configPath, "stalenessThresholdDays: 14\nprovenanceDbPath: .cache/custom/provenance.db\nperPackageMode: true\n");

        var config = InvokeLoadConfig(_memoryRoot);

        Assert.Equal(14, config.StalenessThresholdDays);
        Assert.Equal(".cache/custom/provenance.db", config.ProvenanceDbPath);
    }

    [Fact]
    public void HostPaths_Create_UsesRepoRelativeDbOverride()
    {
        var config = new AgentMemoryConfig
        {
            ProvenanceDbPath = ".cache/custom/provenance.db"
        };

        var paths = InvokeHostPathsCreate(_memoryRoot, config);

        Assert.Equal(_memoryRoot, paths.MemoryRoot);
        Assert.Equal(Path.Combine(_memoryRoot, ".cache", "custom", "provenance.db"), paths.ProvenanceDbPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private static AgentMemoryConfig InvokeLoadConfig(string memoryRoot)
    {
        var method = typeof(MemoryMcpTools).Assembly
            .GetType("FiveHead.Mcp.Host.McpServer", throwOnError: true)!
            .GetMethod("LoadConfig", BindingFlags.Static | BindingFlags.NonPublic)!;

        return (AgentMemoryConfig)method.Invoke(null, [memoryRoot])!;
    }

    private static HostPathsResult InvokeHostPathsCreate(string memoryRoot, AgentMemoryConfig config)
    {
        var assembly = typeof(MemoryMcpTools).Assembly;
        var hostPathsType = assembly.GetType("FiveHead.Mcp.Host.McpServer+HostPaths", throwOnError: true)!;
        var createMethod = hostPathsType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public)!;
        var result = createMethod.Invoke(null, [memoryRoot, config])!;

        return new HostPathsResult(
            (string)hostPathsType.GetProperty("MemoryRoot")!.GetValue(result)!,
            (string)hostPathsType.GetProperty("ProvenanceDbPath")!.GetValue(result)!);
    }

    private sealed record HostPathsResult(string MemoryRoot, string ProvenanceDbPath);
}
