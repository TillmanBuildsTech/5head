using FiveHead.Mcp.Core.Memory;
using FiveHead.Mcp.Core.Provenance;

namespace FiveHead.Mcp.Core.Memory;

/// <summary>
/// Orchestrates memory reads, writes, contradiction checking, and provenance.
/// This is the primary domain service consumed by the MCP tools layer.
/// </summary>
public interface IMemoryService
{
    Task<string> ReadMemoryAsync(MemoryFile file, CancellationToken ct = default);

    Task<WriteMemoryResult> WriteMemoryAsync(
        MemoryFile  file,
        WriteMode   mode,
        string      content,
        string      sourceKind,
        string?     threadId = null,
        string?     taskId   = null,
        CancellationToken ct = default);

    Task<ContradictionCheckResult> CheckContradictionsAsync(
        MemoryFile file,
        string     candidateContent,
        CancellationToken ct = default);

    Task<IReadOnlyList<ProvenanceRow>> ListFactsAsync(
        MemoryFile?  file   = null,
        FactStatus?  status = null,
        CancellationToken ct = default);

    Task ConfirmFactAsync(long provenanceId, CancellationToken ct = default);

    Task RetractFactAsync(long provenanceId, string reason, CancellationToken ct = default);

    Task<IReadOnlyList<ProvenanceRow>> GetProvenanceAsync(
        string subject, string predicate, CancellationToken ct = default);
}
