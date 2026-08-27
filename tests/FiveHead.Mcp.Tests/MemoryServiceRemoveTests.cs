using FiveHead.Mcp.Core.Config;
using FiveHead.Mcp.Core.Memory;
using FiveHead.Mcp.Core.Provenance;
using FiveHead.Mcp.Core.Storage;

namespace FiveHead.Mcp.Tests;

public sealed class MemoryServiceRemoveTests : IDisposable
{
    private readonly string _workspaceRoot;
    private readonly string _memoryRoot;
    private readonly string _databasePath;
    private readonly FakeTimeProvider _timeProvider;
    private readonly MemoryService _service;

    public MemoryServiceRemoveTests()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), "fivehead-tests", Guid.NewGuid().ToString("N"));
        _memoryRoot = Path.Combine(_workspaceRoot, ".agent-memory");
        _databasePath = Path.Combine(_workspaceRoot, "cache", "provenance.db");
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 4, 18, 12, 0, 0, TimeSpan.Zero));

        var fileStore = new MemoryFileStore(_memoryRoot);
        var provenanceStore = new SqliteProvenanceStore(_databasePath, _timeProvider);
        _service = new MemoryService(
            fileStore,
            provenanceStore,
            new AgentMemoryConfig { StalenessThresholdDays = 30 },
            _timeProvider);
    }

    [Fact]
    public async Task WriteMemoryAsync_RemoveNoOpLeavesFileAndProvenanceUnchanged()
    {
        await _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Append, "- language: C#", "test");

        await _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Remove, "- editor: vim", "test");

        var content = await _service.ReadMemoryAsync(MemoryFile.Memory);
        var retracted = await _service.ListFactsAsync(MemoryFile.Memory, FactStatus.Retracted);
        var history = await _service.GetProvenanceAsync("language", "value");

        Assert.Equal("- language: C#", content);
        Assert.Empty(retracted);
        Assert.Single(history);
        Assert.Equal("append", history[0].Mode);
    }

    [Fact]
    public async Task WriteMemoryAsync_RemovePartialContentOnlyChangesFileContent()
    {
        await _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Append, "- language: C# (primary)", "test");

        await _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Remove, " (primary)", "test");

        var content = await _service.ReadMemoryAsync(MemoryFile.Memory);
        var retracted = await _service.ListFactsAsync(MemoryFile.Memory, FactStatus.Retracted);
        var history = await _service.GetProvenanceAsync("language", "value");

        Assert.Equal("- language: C#", content);
        Assert.Empty(retracted);
        Assert.Single(history);
        Assert.Equal("C# (primary)", history[0].Content.Split(':', 2)[1].Trim());
    }

    [Fact]
    public async Task WriteMemoryAsync_RemoveDuplicateFactLinesRetractsAllMatchingRows()
    {
        await _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Append, "- language: C#", "test");
        await _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Append, "- language: C#", "test");

        await _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Remove, "- language: C#", "test");

        var content = await _service.ReadMemoryAsync(MemoryFile.Memory);
        var retracted = await _service.ListFactsAsync(MemoryFile.Memory, FactStatus.Retracted);
        var history = await _service.GetProvenanceAsync("language", "value");

        Assert.DoesNotContain("language: C#", content, StringComparison.Ordinal);
        Assert.Equal(2, retracted.Count);
        Assert.Equal(2, history.Count(row => row.Mode == "retract"));
    }

    [Fact]
    public async Task WriteMemoryAsync_RemoveFormattingVariationKeepsFileButRetractsMatchingFact()
    {
        await _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Append, "- language: C#", "test");

        await _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Remove, "1. language: C#", "test");

        var content = await _service.ReadMemoryAsync(MemoryFile.Memory);
        var retracted = await _service.ListFactsAsync(MemoryFile.Memory, FactStatus.Retracted);
        var history = await _service.GetProvenanceAsync("language", "value");

        Assert.Equal("- language: C#", content);
        Assert.Single(retracted);
        Assert.Contains(history, row => row.Mode == "retract" && row.Content == "Removed from active memory.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
