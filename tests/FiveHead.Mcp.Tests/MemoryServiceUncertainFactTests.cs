using FiveHead.Mcp.Core.Config;
using FiveHead.Mcp.Core.Memory;
using FiveHead.Mcp.Core.Provenance;
using FiveHead.Mcp.Core.Storage;

namespace FiveHead.Mcp.Tests;

public sealed class MemoryServiceUncertainFactTests : IDisposable
{
    private readonly string _workspaceRoot;
    private readonly FakeTimeProvider _timeProvider;
    private readonly MemoryService _service;

    public MemoryServiceUncertainFactTests()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), "fivehead-tests", Guid.NewGuid().ToString("N"));
        var memoryRoot = Path.Combine(_workspaceRoot, ".agent-memory");
        var databasePath = Path.Combine(_workspaceRoot, "cache", "provenance.db");
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 4, 18, 12, 0, 0, TimeSpan.Zero));

        var fileStore = new MemoryFileStore(memoryRoot);
        var provenanceStore = new SqliteProvenanceStore(databasePath, _timeProvider);
        _service = new MemoryService(
            fileStore,
            provenanceStore,
            new AgentMemoryConfig { StalenessThresholdDays = 30 },
            _timeProvider);
    }

    [Fact]
    public async Task CheckAndWrite_IgnoresConflictingUncertainFact()
    {
        await _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Append, "- language: C#", "test");
        _timeProvider.Advance(TimeSpan.FromDays(31));

        var check = await _service.CheckContradictionsAsync(MemoryFile.Memory, "- language: Rust");
        var write = await _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Append, "- language: Rust", "test");

        Assert.Empty(check.Conflicts);
        Assert.True(write.ProvenanceId > 0);
    }

    [Fact]
    public async Task ListFactsAndHistory_KeepUncertainFactVisibleAfterConflictingWrite()
    {
        var originalWrite = await _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Append, "- language: C#", "test");
        _timeProvider.Advance(TimeSpan.FromDays(31));
        var replacementWrite = await _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Append, "- language: Rust", "test");

        var uncertain = await _service.ListFactsAsync(MemoryFile.Memory, FactStatus.Uncertain);
        var active = await _service.ListFactsAsync(MemoryFile.Memory, FactStatus.Active);
        var history = await _service.GetProvenanceAsync("language", "value");

        Assert.Collection(uncertain,
            row =>
            {
                Assert.Equal(originalWrite.ProvenanceId, row.Id);
                Assert.Equal(FactStatus.Uncertain, row.Status);
            });

        Assert.Collection(active,
            row =>
            {
                Assert.Equal(replacementWrite.ProvenanceId, row.Id);
                Assert.Equal(FactStatus.Active, row.Status);
            });

        Assert.Collection(history,
            row =>
            {
                Assert.Equal(originalWrite.ProvenanceId, row.Id);
                Assert.Equal(FactStatus.Uncertain, row.Status);
            },
            row =>
            {
                Assert.Equal(replacementWrite.ProvenanceId, row.Id);
                Assert.Equal(FactStatus.Active, row.Status);
            });
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
