namespace FiveHead.Mcp.Core.Provenance;

/// <summary>Derived status of a provenance row. Not stored — computed on read.</summary>
public enum FactStatus
{
    Active,
    Uncertain,
    Confirmed,
    Retracted
}

/// <summary>
/// A single fact extracted from a memory write, stored in SQLite.
/// Maps to the <c>provenance</c> table.
/// </summary>
public sealed record class ProvenanceRow
{
    public long      Id           { get; init; }
    public string    Target       { get; init; } = "";   // SOUL.md / MEMORY.md / USER.md
    public string    Mode         { get; init; } = "";   // append / replace / remove
    public string    SourceKind   { get; init; } = "";   // tool name, agent identifier
    public string    Content      { get; init; } = "";   // raw content written or removed
    public string?   FactKeys     { get; init; }         // JSON array of {subject,predicate,object,scope}
    public string?   ThreadId     { get; init; }
    public string?   TaskId       { get; init; }
    public DateTime  CreatedAt    { get; init; }
    public DateTime? ConfirmedAt  { get; init; }
    public DateTime? RetractedAt  { get; init; }
    public long?     RetractsId   { get; init; }         // FK to row this one retracts
    public FactStatus Status      { get; init; }
}

/// <summary>A structured fact key extracted from memory content.</summary>
public sealed record FactKey(
    string Subject,
    string Predicate,
    string Object,
    string Scope = "repo");
