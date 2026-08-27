using FiveHead.Mcp.Core.Provenance;

namespace FiveHead.Mcp.Core.Storage;

/// <summary>
/// Append-only SQLite-backed provenance store.
/// Lives at <c>~/.agent-memory/cache/{repo-hash}/provenance.db</c>.
/// </summary>
public interface IProvenanceStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<long> RecordAsync(ProvenanceRow row, CancellationToken ct = default);

    Task ConfirmAsync(long id, CancellationToken ct = default);

    Task RetractAsync(long id, string reason, CancellationToken ct = default);

    Task<ProvenanceRow?> GetByIdAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<ProvenanceRow>> GetHistoryAsync(
        string subject, string predicate, CancellationToken ct = default);

    Task<IReadOnlyList<ProvenanceRow>> ListAsync(
        string? targetFile = null, CancellationToken ct = default);
}
