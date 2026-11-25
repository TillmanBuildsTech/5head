using System.Text.Json;
using System.Text.RegularExpressions;
using FiveHead.Mcp.Core.Config;
using FiveHead.Mcp.Core.Provenance;
using FiveHead.Mcp.Core.Serialization;
using FiveHead.Mcp.Core.Storage;

namespace FiveHead.Mcp.Core.Memory;

public sealed partial class MemoryService(
    IMemoryFileStore fileStore,
    IProvenanceStore provenanceStore,
    AgentMemoryConfig? config = null,
    TimeProvider? timeProvider = null) : IMemoryService
{
    private readonly IMemoryFileStore _fileStore = fileStore;
    private readonly IProvenanceStore _provenanceStore = provenanceStore;
    private readonly AgentMemoryConfig _config = config ?? new AgentMemoryConfig();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<string> ReadMemoryAsync(MemoryFile file, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        return await _fileStore.ReadAsync(file, ct);
    }

    public async Task<WriteMemoryResult> WriteMemoryAsync(
        MemoryFile file,
        WriteMode mode,
        string content,
        string sourceKind,
        string? threadId = null,
        string? taskId = null,
        string? expectedContentHash = null,
        CancellationToken ct = default)
    {
        content ??= string.Empty;
        await using var writeLock = await MemoryWriteLock.AcquireAsync(_fileStore.RootPath, ct);
        await InitializeAsync(ct);

        var targetFile = MemoryLimits.FileName(file);
        var current = await _fileStore.ReadAsync(file, ct);
        var currentHash = MemoryContentHash.Compute(current);
        if (!string.IsNullOrWhiteSpace(expectedContentHash)
            && !MemoryContentHash.Matches(expectedContentHash, currentHash))
        {
            throw new StaleWriteException(file, MemoryContentHash.Normalize(expectedContentHash), currentHash);
        }

        var conflicts = mode == WriteMode.Remove
            ? []
            : await FindConflictsAsync(file, content, ignoreTarget: mode == WriteMode.Replace ? targetFile : null, ct);

        if (conflicts.Count > 0)
        {
            throw new ContradictionException(conflicts);
        }

        var next = MemoryFileStore.ApplyMode(current, mode, content);
        var existingRows = await _provenanceStore.ListAsync(targetFile, ct);
        var recordedFacts = RecordedFacts(mode, content, next, targetFile, existingRows, out var retractions);

        await _fileStore.WriteAsync(file, mode, content, ct);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        long provenanceId;
        try
        {
            provenanceId = await _provenanceStore.RecordAndRetractAsync(new ProvenanceRow
            {
                Target = targetFile,
                Mode = mode.ToString().ToLowerInvariant(),
                SourceKind = sourceKind,
                Content = content,
                FactKeys = recordedFacts.Count == 0
                    ? null
                    : JsonSerializer.Serialize(recordedFacts.ToArray(), CoreJsonSerializerContext.Default.FactKeyArray),
                ThreadId = threadId,
                TaskId = taskId,
                CreatedAt = now
            }, retractions, ct);
        }
        catch (Exception provenanceFailure)
        {
            try
            {
                await _fileStore.ReplaceAllAsync(file, current, ct);
            }
            catch (Exception restoreFailure)
            {
                throw new IOException(
                    $"Provenance update failed and restoring {targetFile} also failed. The file and the provenance database may have drifted.",
                    new AggregateException(provenanceFailure, restoreFailure));
            }

            throw;
        }

        var written = await _fileStore.ReadAsync(file, ct);
        return new WriteMemoryResult
        {
            ProvenanceId = provenanceId,
            ContentHash = MemoryContentHash.Compute(written),
            Warnings = recordedFacts.Count == 0 && retractions.Count == 0
                ? ["No structured facts were extracted from this write."]
                : []
        };
    }

    public async Task<ContradictionCheckResult> CheckContradictionsAsync(
        MemoryFile file,
        string candidateContent,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        var conflicts = await FindConflictsAsync(file, candidateContent, ignoreTarget: null, ct);
        return new ContradictionCheckResult
        {
            Conflicts = conflicts.ToArray(),
            Suggestions = conflicts.Count == 0
                ? []
                : ["Retract the conflicting fact before writing the new value."]
        };
    }

    public async Task<IReadOnlyList<ProvenanceRow>> ListFactsAsync(
        MemoryFile? file = null,
        FactStatus? status = null,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        var targetFile = file is null ? null : MemoryLimits.FileName(file.Value);
        var rows = await _provenanceStore.ListAsync(targetFile, ct);
        var projected = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.FactKeys))
            .Select(ApplyStatus)
            .Where(row => status is null || row.Status == status)
            .ToArray();

        return projected;
    }

    public async Task ConfirmFactAsync(long provenanceId, CancellationToken ct = default)
    {
        await using var writeLock = await MemoryWriteLock.AcquireAsync(_fileStore.RootPath, ct);
        await InitializeAsync(ct);
        await _provenanceStore.ConfirmAsync(provenanceId, ct);
    }

    public async Task RetractFactAsync(long provenanceId, string reason, CancellationToken ct = default)
    {
        await using var writeLock = await MemoryWriteLock.AcquireAsync(_fileStore.RootPath, ct);
        await InitializeAsync(ct);
        await _provenanceStore.RetractAsync(provenanceId, reason, ct);
    }

    public async Task<IReadOnlyList<ProvenanceRow>> GetProvenanceAsync(
        string subject,
        string predicate,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        var rows = await _provenanceStore.GetHistoryAsync(subject, predicate, ct);
        return rows.Select(ApplyStatus).ToArray();
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        await _fileStore.EnsureInitializedAsync(ct);
        await _provenanceStore.InitializeAsync(ct);
    }

    private async Task<List<ConflictInfo>> FindConflictsAsync(
        MemoryFile file,
        string candidateContent,
        string? ignoreTarget,
        CancellationToken ct)
    {
        var candidateFacts = ExtractFacts(candidateContent);
        if (candidateFacts.Count == 0)
        {
            return [];
        }

        var rows = await _provenanceStore.ListAsync(null, ct);
        var blockingRows = rows
            .Where(row => ignoreTarget is null || !string.Equals(row.Target, ignoreTarget, StringComparison.OrdinalIgnoreCase))
            .Select(ApplyStatus)
            .Where(row => row.Status is FactStatus.Active or FactStatus.Confirmed)
            .ToArray();

        var conflicts = new List<ConflictInfo>();
        foreach (var row in blockingRows)
        {
            foreach (var existingFact in DeserializeFacts(row))
            {
                foreach (var candidateFact in candidateFacts)
                {
                    if (FactsConflict(existingFact, candidateFact))
                    {
                        conflicts.Add(new ConflictInfo
                        {
                            ExistingFact = existingFact,
                            ExistingRowId = row.Id,
                            CandidateFact = candidateFact
                        });
                    }
                }
            }
        }

        return conflicts;
    }

    private List<FactKey> RecordedFacts(
        WriteMode mode,
        string content,
        string next,
        string targetFile,
        IReadOnlyList<ProvenanceRow> existingRows,
        out List<FactRetraction> retractions)
    {
        retractions = [];
        if (mode == WriteMode.Append)
        {
            return ExtractFacts(content);
        }

        var surviving = mode == WriteMode.Remove ? ExtractFacts(next) : ExtractFacts(content);
        var reason = mode == WriteMode.Remove
            ? "Removed from active memory."
            : $"Superseded by replace in {targetFile}.";

        foreach (var row in existingRows.Select(ApplyStatus).Where(row => row.Status is FactStatus.Active or FactStatus.Confirmed))
        {
            var rowFacts = DeserializeFacts(row);
            if (rowFacts.Count == 0)
            {
                continue;
            }

            var stillPresent = rowFacts.All(existingFact => surviving.Any(candidate => FactsMatch(existingFact, candidate)));
            if (!stillPresent)
            {
                retractions.Add(new FactRetraction(row.Id, reason));
            }
        }

        if (mode == WriteMode.Replace)
        {
            return surviving;
        }

        var kept = new List<FactKey>();
        foreach (var row in existingRows.Select(ApplyStatus).Where(row => row.Status is FactStatus.Active or FactStatus.Confirmed))
        {
            if (retractions.Any(retraction => retraction.Id == row.Id))
            {
                continue;
            }

            kept.AddRange(DeserializeFacts(row));
        }

        return surviving.Where(fact => !kept.Any(existing => FactsMatch(existing, fact))).ToList();
    }

    private ProvenanceRow ApplyStatus(ProvenanceRow row) => row with
    {
        Status = FactStatusEvaluator.Evaluate(
            row,
            _timeProvider.GetUtcNow().UtcDateTime,
            Math.Max(1, _config.StalenessThresholdDays))
    };

    private static List<FactKey> DeserializeFacts(ProvenanceRow row)
    {
        if (string.IsNullOrWhiteSpace(row.FactKeys))
        {
            return [];
        }

        return JsonSerializer.Deserialize(row.FactKeys, CoreJsonSerializerContext.Default.ListFactKey) ?? [];
    }

    internal static List<FactKey> ExtractFacts(string content)
    {
        var results = new List<FactKey>();
        foreach (var rawLine in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = NormalizeFactLine(rawLine);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0 || separatorIndex == line.Length - 1)
            {
                continue;
            }

            var left = line[..separatorIndex].Trim();
            var right = line[(separatorIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                continue;
            }

            var (subject, predicate) = SplitKey(left);
            var fact = new FactKey(subject, predicate, right);
            if (!results.Any(existing => FactsMatch(existing, fact)))
            {
                results.Add(fact);
            }
        }

        return results;
    }

    private static bool FactsConflict(FactKey existing, FactKey candidate) =>
        string.Equals(existing.Subject, candidate.Subject, StringComparison.OrdinalIgnoreCase)
        && string.Equals(existing.Predicate, candidate.Predicate, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(existing.Object, candidate.Object, StringComparison.Ordinal);

    private static bool FactsMatch(FactKey left, FactKey right) =>
        string.Equals(left.Subject, right.Subject, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Predicate, right.Predicate, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Object, right.Object, StringComparison.Ordinal);

    private static (string Subject, string Predicate) SplitKey(string key)
    {
        var dotIndex = key.IndexOf('.');
        if (dotIndex > 0 && dotIndex < key.Length - 1)
        {
            return (key[..dotIndex].Trim(), key[(dotIndex + 1)..].Trim());
        }

        return (key.Trim(), "value");
    }

    private static string NormalizeFactLine(string rawLine)
    {
        var line = rawLine.Trim();
        if (string.IsNullOrWhiteSpace(line)
            || line.StartsWith('#')
            || line == "---")
        {
            return string.Empty;
        }

        line = MarkdownBulletRegex().Replace(line, string.Empty).Trim();
        return line;
    }

    [GeneratedRegex(@"^(?:[-+*]\s+|\d+\.\s+)")]
    private static partial Regex MarkdownBulletRegex();
}
