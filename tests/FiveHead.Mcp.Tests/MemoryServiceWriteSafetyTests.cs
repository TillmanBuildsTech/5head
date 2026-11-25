using FiveHead.Mcp.Core.Config;
using FiveHead.Mcp.Core.Memory;
using FiveHead.Mcp.Core.Provenance;
using FiveHead.Mcp.Core.Storage;
using Microsoft.Data.Sqlite;

namespace FiveHead.Mcp.Tests;

public sealed class MemoryServiceWriteSafetyTests : IDisposable
{
    private readonly string _workspaceRoot;
    private readonly string _memoryRoot;
    private readonly string _databasePath;

    public MemoryServiceWriteSafetyTests()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), "fivehead-tests", Guid.NewGuid().ToString("N"));
        _memoryRoot = Path.Combine(_workspaceRoot, ".agent-memory");
        _databasePath = Path.Combine(_workspaceRoot, "cache", "provenance.db");
    }

    [Fact]
    public async Task StaleExpectedHash_FailsWithoutClobbering()
    {
        var service = CreateService();
        await service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Replace, "- language: C#", "test");
        var current = await service.ReadMemoryAsync(MemoryFile.Memory);
        var hash = MemoryContentHash.Compute(current);

        await service.WriteMemoryAsync(
            MemoryFile.Memory,
            WriteMode.Replace,
            "- language: Rust",
            "test",
            expectedContentHash: hash);

        var stale = await Assert.ThrowsAsync<StaleWriteException>(() =>
            service.WriteMemoryAsync(
                MemoryFile.Memory,
                WriteMode.Replace,
                "- language: Go",
                "test",
                expectedContentHash: hash));

        var content = await service.ReadMemoryAsync(MemoryFile.Memory);
        Assert.Equal("- language: Rust", content);
        Assert.Equal(hash, stale.ExpectedHash);
        Assert.Equal(MemoryContentHash.Compute(content), stale.ActualHash);
        Assert.Contains("Re-read", stale.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentReplacesWithTheSameHash_OneWinsAndTheOtherFails()
    {
        var first = CreateService();
        var second = CreateService();
        var hash = MemoryContentHash.Compute(await first.ReadMemoryAsync(MemoryFile.Memory));
        var arrived = 0;
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<Exception?> Replace(MemoryService service, string content)
        {
            if (Interlocked.Increment(ref arrived) == 2)
            {
                go.TrySetResult();
            }

            var started = await Task.WhenAny(go.Task, Task.Delay(10_000));
            if (started != go.Task)
            {
                return new TimeoutException("Writers did not arrive together.");
            }

            try
            {
                await service.WriteMemoryAsync(
                    MemoryFile.Memory,
                    WriteMode.Replace,
                    content,
                    "test",
                    expectedContentHash: hash);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        var results = await Task.WhenAll(
            Replace(first, "- item: alpha"),
            Replace(second, "- item: beta"));

        Assert.Single(results, result => result is null);
        var failure = Assert.Single(results, result => result is not null);
        Assert.IsType<StaleWriteException>(failure);

        var content = await first.ReadMemoryAsync(MemoryFile.Memory);
        Assert.True(content is "- item: alpha" or "- item: beta");
        Assert.Single(await first.ListFactsAsync(MemoryFile.Memory, FactStatus.Active));
        Assert.DoesNotContain(Directory.GetFiles(_memoryRoot), path => path.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConcurrentAppends_KeepBothLines()
    {
        var first = CreateService();
        var second = CreateService();
        var arrived = 0;
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task Append(MemoryService service, string line)
        {
            if (Interlocked.Increment(ref arrived) == 2)
            {
                go.TrySetResult();
            }

            var started = await Task.WhenAny(go.Task, Task.Delay(10_000));
            if (started != go.Task)
            {
                throw new TimeoutException("Writers did not arrive together.");
            }

            await service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Append, line, "test");
        }

        await Task.WhenAll(
            Append(first, "- language: C#"),
            Append(second, "- editor: helix"));

        var content = await first.ReadMemoryAsync(MemoryFile.Memory);
        Assert.Contains("language: C#", content, StringComparison.Ordinal);
        Assert.Contains("editor: helix", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvenanceFailure_RestoresThePreviousFile()
    {
        var inner = new SqliteProvenanceStore(_databasePath);
        var store = new FailOnSecondRecordStore(inner);
        var files = new MemoryFileStore(_memoryRoot);
        var service = new MemoryService(files, store, new AgentMemoryConfig());

        await service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Append, "- language: C#", "test");
        var ex = await Assert.ThrowsAsync<IOException>(() =>
            service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Append, "- editor: helix", "test"));

        var content = await service.ReadMemoryAsync(MemoryFile.Memory);
        var facts = await service.ListFactsAsync(MemoryFile.Memory);
        Assert.Equal("- language: C#", content);
        Assert.Single(facts);
        Assert.Contains("injected provenance failure", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Directory.GetFiles(_memoryRoot), path => path.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConfirmAndRetract_RejectMissingAndRepeatIds()
    {
        var service = CreateService();
        var missingConfirm = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfirmFactAsync(42));
        var missingRetract = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RetractFactAsync(42, "gone"));

        Assert.Contains("42", missingConfirm.Message, StringComparison.Ordinal);
        Assert.Contains("does not exist", missingConfirm.Message, StringComparison.Ordinal);
        Assert.Contains("does not exist", missingRetract.Message, StringComparison.Ordinal);

        var write = await service.WriteMemoryAsync(MemoryFile.Memory, WriteMode.Append, "- language: C#", "test");
        await service.RetractFactAsync(write.ProvenanceId, "first");
        var repeat = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RetractFactAsync(write.ProvenanceId, "second"));

        Assert.Contains("already retracted", repeat.Message, StringComparison.Ordinal);
        var history = await service.GetProvenanceAsync("language", "value");
        Assert.Single(history, row => row.Mode == "retract");
    }

    [Fact]
    public async Task BusyTimeout_WaitsBeforeFailingALockedDatabase()
    {
        var store = new SqliteProvenanceStore(_databasePath, busyTimeout: TimeSpan.FromMilliseconds(400));
        await store.InitializeAsync();

        await using var held = new SqliteConnection($"Data Source={_databasePath}");
        await held.OpenAsync();
        await using var hold = held.CreateCommand();
        hold.CommandText = "BEGIN EXCLUSIVE;";
        await hold.ExecuteNonQueryAsync();

        var started = System.Diagnostics.Stopwatch.StartNew();
        var failure = await Record.ExceptionAsync(() => store.RecordAsync(new ProvenanceRow
        {
            Target = "MEMORY.md",
            Mode = "append",
            SourceKind = "test",
            Content = "- language: C#",
            CreatedAt = DateTime.UtcNow
        }));
        started.Stop();

        await using var release = held.CreateCommand();
        release.CommandText = "ROLLBACK;";
        await release.ExecuteNonQueryAsync();

        Assert.NotNull(failure);
        Assert.InRange(started.ElapsedMilliseconds, 300, 5_000);
    }

    [Fact]
    public void ExpectedHash_MustBeSha256Hex()
    {
        var ex = Assert.Throws<ArgumentException>(() => MemoryContentHash.Normalize("not-a-hash"));
        Assert.Contains("SHA-256", ex.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private MemoryService CreateService() => new(
        new MemoryFileStore(_memoryRoot),
        new SqliteProvenanceStore(_databasePath),
        new AgentMemoryConfig());

    private sealed class FailOnSecondRecordStore(IProvenanceStore inner) : IProvenanceStore
    {
        private int _records;

        public Task InitializeAsync(CancellationToken ct = default) => inner.InitializeAsync(ct);

        public Task<long> RecordAsync(ProvenanceRow row, CancellationToken ct = default) => inner.RecordAsync(row, ct);

        public Task<long> RecordAndRetractAsync(
            ProvenanceRow row,
            IReadOnlyList<FactRetraction> retractions,
            CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _records) >= 2)
            {
                throw new IOException("injected provenance failure");
            }

            return inner.RecordAndRetractAsync(row, retractions, ct);
        }

        public Task ConfirmAsync(long id, CancellationToken ct = default) => inner.ConfirmAsync(id, ct);

        public Task RetractAsync(long id, string reason, CancellationToken ct = default) => inner.RetractAsync(id, reason, ct);

        public Task<ProvenanceRow?> GetByIdAsync(long id, CancellationToken ct = default) => inner.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<ProvenanceRow>> GetHistoryAsync(string subject, string predicate, CancellationToken ct = default) =>
            inner.GetHistoryAsync(subject, predicate, ct);

        public Task<IReadOnlyList<ProvenanceRow>> ListAsync(string? targetFile = null, CancellationToken ct = default) =>
            inner.ListAsync(targetFile, ct);
    }
}
