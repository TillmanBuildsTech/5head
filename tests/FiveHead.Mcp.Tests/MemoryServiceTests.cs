using FiveHead.Mcp.Core.Config;
using FiveHead.Mcp.Core.Memory;
using FiveHead.Mcp.Core.Provenance;
using FiveHead.Mcp.Core.Storage;

namespace FiveHead.Mcp.Tests;

public sealed class MemoryServiceTests : IDisposable
{
    private readonly string _workspaceRoot;
    private readonly string _memoryRoot;
    private readonly string _databasePath;
    private readonly FakeTimeProvider _timeProvider;
    private readonly MemoryService _service;

    public MemoryServiceTests()
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
    public async Task WriteMemoryAsync_AppendsRepoMemoryAndRecordsProvenance()
    {
        var result = await _service.WriteMemoryAsync(
            MemoryFile.Memory,
            WriteMode.Append,
            "- language: C#",
            "test");

        var content = await _service.ReadMemoryAsync(MemoryFile.Memory);
        var facts = await _service.ListFactsAsync(MemoryFile.Memory);

        Assert.True(result.ProvenanceId > 0);
        Assert.Equal("- language: C#", content);
        Assert.Single(facts);
        Assert.Equal(FactStatus.Active, facts[0].Status);
        Assert.Equal("MEMORY.md", facts[0].Target);
    }

    [Fact]
    public async Task CheckAndWrite_RejectsConflictingActiveFact()
    {
        await _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Append, "- language: C#", "test");

        var check = await _service.CheckContradictionsAsync(MemoryFile.Memory, "- language: Rust");
        var ex = await Assert.ThrowsAsync<ContradictionException>(() =>
            _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Append, "- language: Rust", "test"));

        Assert.Single(check.Conflicts);
        Assert.Single(ex.Conflicts);
        Assert.Equal("C#", check.Conflicts[0].ExistingFact.Object);
        Assert.Equal("Rust", check.Conflicts[0].CandidateFact.Object);
    }

    [Fact]
    public async Task ConfirmAndRetract_UpdateFactLifecycleAndHistory()
    {
        var write = await _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Append, "- language: C#", "test");

        await _service.ConfirmFactAsync(write.ProvenanceId);
        var confirmed = await _service.ListFactsAsync(MemoryFile.Memory, FactStatus.Confirmed);

        await _service.RetractFactAsync(write.ProvenanceId, "Language changed");
        var retracted = await _service.ListFactsAsync(MemoryFile.Memory, FactStatus.Retracted);
        var history = await _service.GetProvenanceAsync("language", "value");

        Assert.Single(confirmed);
        Assert.Single(retracted);
        Assert.Equal(write.ProvenanceId, retracted[0].Id);
        Assert.Equal(2, history.Count);
        Assert.Contains(history, row => row.RetractsId == write.ProvenanceId && row.Mode == "retract");
    }

    [Fact]
    public async Task ListFactsAsync_MarksOldFactsUncertainAfterStalenessWindow()
    {
        await _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Append, "- language: C#", "test");
        _timeProvider.Advance(TimeSpan.FromDays(31));

        var uncertain = await _service.ListFactsAsync(MemoryFile.Memory, FactStatus.Uncertain);

        Assert.Single(uncertain);
        Assert.Equal(FactStatus.Uncertain, uncertain[0].Status);
    }

    [Fact]
    public async Task WriteMemoryAsync_EnforcesFileLimit()
    {
        var oversized = new string('x', MemoryLimits.User + 1);

        await Assert.ThrowsAsync<SizeLimitExceededException>(() =>
            _service.WriteMemoryAsync(MemoryFile.User, WriteMode.Replace, oversized, "test"));
    }

    [Fact]
    public async Task WriteMemoryAsync_ReplaceRetractsSupersededFactsInSameFile()
    {
        await _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Append, "- language: C#", "test");

        var replacement = await _service.WriteMemoryAsync(
            MemoryFile.Memory,
            WriteMode.Replace,
            "- language: Rust",
            "test");

        var content = await _service.ReadMemoryAsync(MemoryFile.Memory);
        var active = await _service.ListFactsAsync(MemoryFile.Memory, FactStatus.Active);
        var retracted = await _service.ListFactsAsync(MemoryFile.Memory, FactStatus.Retracted);

        Assert.True(replacement.ProvenanceId > 0);
        Assert.Equal("- language: Rust", content);
        Assert.Single(active);
        Assert.Equal("replace", active[0].Mode);
        Assert.Equal("- language: Rust", active[0].Content);
        Assert.Single(retracted);
        Assert.Equal("append", retracted[0].Mode);
        Assert.Equal("- language: C#", retracted[0].Content);
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
