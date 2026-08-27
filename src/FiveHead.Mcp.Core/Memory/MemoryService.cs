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
        CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        var targetFile = MemoryLimits.FileName(file);
        var trackedFacts = mode == WriteMode.Remove ? ExtractFacts(content) : ExtractFacts(content);
        var conflicts = mode == WriteMode.Remove
            ? []
            : await FindConflictsAsync(file, content, ignoreTarget: mode == WriteMode.Replace ? targetFile : null, ct);

        if (conflicts.Count > 0)
        {
            throw new ContradictionException(conflicts);
        }

        var existingRows = await _provenanceStore.ListAsync(targetFile, ct);
        await _fileStore.WriteAsync(file, mode, content, ct);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var provenanceId = await _provenanceStore.RecordAsync(new ProvenanceRow
        {
            Target = targetFile,
            Mode = mode.ToString().ToLowerInvariant(),
            SourceKind = sourceKind,
            Content = content,
            FactKeys = trackedFacts.Count == 0
                ? null
                : JsonSerializer.Serialize(trackedFacts.ToArray(), CoreJsonSerializerContext.Default.FactKeyArray),
            ThreadId = threadId,
            TaskId = taskId,
            CreatedAt = now
        }, ct);

        if (mode == WriteMode.Replace)
        {
            await RetractRowsMissingFromReplacementAsync(existingRows, trackedFacts, targetFile, ct);
        }
        else if (mode == WriteMode.Remove)
        {
            await RetractRemovedRowsAsync(existingRows, trackedFacts, ct);
        }

        return new WriteMemoryResult
        {
            ProvenanceId = provenanceId,
            Warnings = trackedFacts.Count == 0
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
        await InitializeAsync(ct);
        await _provenanceStore.ConfirmAsync(provenanceId, ct);
    }

    public async Task RetractFactAsync(long provenanceId, string reason, CancellationToken ct = default)
    {
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

    private async Task RetractRowsMissingFromReplacementAsync(
        IReadOnlyList<ProvenanceRow> existingRows,
        IReadOnlyList<FactKey> replacementFacts,
        string targetFile,
        CancellationToken ct)
    {
        foreach (var row in existingRows.Select(ApplyStatus).Where(row => row.Status is FactStatus.Active or FactStatus.Confirmed))
        {
            var rowFacts = DeserializeFacts(row);
            if (rowFacts.Count == 0)
            {
                continue;
            }

            var stillPresent = rowFacts.All(existingFact => replacementFacts.Any(candidate => FactsMatch(existingFact, candidate)));
            if (!stillPresent)
            {
                await _provenanceStore.RetractAsync(row.Id, $"Superseded by replace in {targetFile}.", ct);
            }
        }
    }

    private async Task RetractRemovedRowsAsync(
        IReadOnlyList<ProvenanceRow> existingRows,
        IReadOnlyList<FactKey> removedFacts,
        CancellationToken ct)
    {
        if (removedFacts.Count == 0)
        {
            return;
        }

        foreach (var row in existingRows.Select(ApplyStatus).Where(row => row.Status is FactStatus.Active or FactStatus.Confirmed))
        {
            var rowFacts = DeserializeFacts(row);
            if (rowFacts.Count > 0 && rowFacts.All(existingFact => removedFacts.Any(removed => FactsMatch(existingFact, removed))))
            {
                await _provenanceStore.RetractAsync(row.Id, "Removed from active memory.", ct);
            }
        }
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
