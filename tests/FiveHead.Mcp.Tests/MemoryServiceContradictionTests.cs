using FiveHead.Mcp.Core.Config;
using FiveHead.Mcp.Core.Memory;
using FiveHead.Mcp.Core.Provenance;
using FiveHead.Mcp.Core.Storage;

namespace FiveHead.Mcp.Tests;

public sealed class MemoryServiceContradictionTests : IDisposable
{
    private readonly string _workspaceRoot;
    private readonly string _memoryRoot;
    private readonly string _databasePath;
    private readonly FakeTimeProvider _timeProvider;
    private readonly MemoryService _service;

    public MemoryServiceContradictionTests()
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
    public async Task WriteMemoryAsync_Replace_AllowsChangedFactsInSameFile()
    {
        await _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Append, "- language: C#", "test");

        var ex = await Record.ExceptionAsync(() =>
            _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Replace, "- language: Rust", "test"));
        var content = await _service.ReadMemoryAsync(MemoryFile.Memory);

        Assert.Null(ex);
        Assert.Equal("- language: Rust", content);
    }

    [Fact]
    public async Task CheckAndWrite_DetectsCrossFileConflictsFromActiveAndConfirmedFacts()
    {
        await _service.WriteMemoryAsync(MemoryFile.User, WriteMode.Append, "- language: C#", "test");

        var activeCheck = await _service.CheckContradictionsAsync(MemoryFile.Memory, "- language: Rust");
        var activeWrite = await Assert.ThrowsAsync<ContradictionException>(() =>
            _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Append, "- language: Rust", "test"));

        Assert.Single(activeCheck.Conflicts);
        Assert.Single(activeWrite.Conflicts);
        Assert.Equal("USER.md", await GetTargetAsync(activeCheck.Conflicts[0].ExistingRowId));
        Assert.Equal("C#", activeCheck.Conflicts[0].ExistingFact.Object);
        Assert.Equal("Rust", activeCheck.Conflicts[0].CandidateFact.Object);

        var confirmedWrite = await _service.WriteMemoryAsync(MemoryFile.Soul, WriteMode.Append, "- runtime: .NET", "test");
        await _service.ConfirmFactAsync(confirmedWrite.ProvenanceId);

        var confirmedCheck = await _service.CheckContradictionsAsync(MemoryFile.Memory, "- runtime: Node.js");
        var confirmedWriteEx = await Assert.ThrowsAsync<ContradictionException>(() =>
            _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Append, "- runtime: Node.js", "test"));

        Assert.Single(confirmedCheck.Conflicts);
        Assert.Single(confirmedWriteEx.Conflicts);
        Assert.Equal("SOUL.md", await GetTargetAsync(confirmedCheck.Conflicts[0].ExistingRowId));
        Assert.Equal(".NET", confirmedCheck.Conflicts[0].ExistingFact.Object);
        Assert.Equal("Node.js", confirmedCheck.Conflicts[0].CandidateFact.Object);
    }

    [Fact]
    public async Task WriteMemoryAsync_Replace_RetractsSupersededFactsFromSameFile()
    {
        var original = await _service.WriteMemoryAsync(
            MemoryFile.Memory,
            WriteMode.Append,
            "- language: C#\n- runtime: .NET",
            "test");

        var replacement = await _service.WriteMemoryAsync(
            MemoryFile.Memory,
            WriteMode.Replace,
            "- language: Rust\n- package_manager: cargo",
            "test");

        var activeFacts = await _service.ListFactsAsync(MemoryFile.Memory, FactStatus.Active);
        var retractedFacts = await _service.ListFactsAsync(MemoryFile.Memory, FactStatus.Retracted);
        var languageHistory = await _service.GetProvenanceAsync("language", "value");
        var runtimeHistory = await _service.GetProvenanceAsync("runtime", "value");
        var packageManagerHistory = await _service.GetProvenanceAsync("package_manager", "value");

        Assert.Equal(replacement.ProvenanceId, Assert.Single(activeFacts).Id);
        Assert.Equal("Rust", activeFacts[0].Content.Split(':', 2)[1].Trim().Split('\n')[0]);
        Assert.Equal(original.ProvenanceId, Assert.Single(retractedFacts).Id);
        Assert.Contains(languageHistory, row => row.Id == original.ProvenanceId && row.Status == FactStatus.Retracted);
        Assert.Contains(languageHistory, row => row.Id == replacement.ProvenanceId && row.Status == FactStatus.Active);
        Assert.Contains(runtimeHistory, row => row.Id == original.ProvenanceId && row.Status == FactStatus.Retracted);
        Assert.Single(packageManagerHistory);
        Assert.Equal(replacement.ProvenanceId, packageManagerHistory[0].Id);
        Assert.Equal(FactStatus.Active, packageManagerHistory[0].Status);
    }

    [Fact]
    public async Task CheckAndWrite_IgnoresCrossFileConflictsFromUncertainFacts()
    {
        await _service.WriteMemoryAsync(MemoryFile.User, WriteMode.Append, "- language: C#", "test");
        _timeProvider.Advance(TimeSpan.FromDays(31));

        var check = await _service.CheckContradictionsAsync(MemoryFile.Memory, "- language: Rust");
        var ex = await Record.ExceptionAsync(() =>
            _service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Append, "- language: Rust", "test"));
        var memoryFacts = await _service.ListFactsAsync(MemoryFile.Memory, FactStatus.Active);
        var userFacts = await _service.ListFactsAsync(MemoryFile.User, FactStatus.Uncertain);

        Assert.Empty(check.Conflicts);
        Assert.Null(ex);
        Assert.Single(memoryFacts);
        Assert.Single(userFacts);
        Assert.Equal(FactStatus.Uncertain, userFacts[0].Status);
        Assert.Equal("- language: Rust", memoryFacts[0].Content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private async Task<string> GetTargetAsync(long provenanceId)
    {
        var rows = await _service.ListFactsAsync();
        return Assert.Single(rows, row => row.Id == provenanceId).Target;
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
