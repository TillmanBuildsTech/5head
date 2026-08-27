using FiveHead.Mcp.Core.Provenance;

namespace FiveHead.Mcp.Core.Memory;

/// <summary>Result of a write_memory tool call.</summary>
public sealed class WriteMemoryResult
{
    public long     ProvenanceId { get; init; }
    public string[] Warnings     { get; init; } = [];
}

/// <summary>Result of a check_contradictions call.</summary>
public sealed class ContradictionCheckResult
{
    public ConflictInfo[] Conflicts    { get; init; } = [];
    public string[]       Suggestions  { get; init; } = [];
}

public sealed class ContradictionCheckPayload
{
    public string                    File         { get; init; } = "";
    public string                    Candidate    { get; init; } = "";
    public int                       ConflictCount { get; init; }
    public ConflictInfo[]            Conflicts    { get; init; } = [];
    public string[]                  Suggestions  { get; init; } = [];
}

public sealed class ListFactsPayload
{
    public string?                   File      { get; init; }
    public string?                   Status    { get; init; }
    public int                       Count     { get; init; }
    public ProvenanceRow[]           Facts     { get; init; } = [];
}

public sealed class ProvenanceHistoryPayload
{
    public string                    Subject   { get; init; } = "";
    public string                    Predicate { get; init; } = "";
    public int                       Count     { get; init; }
    public ProvenanceRow[]           History   { get; init; } = [];
}

public sealed class ConflictInfo
{
    public FactKey   ExistingFact     { get; init; } = null!;
    public long      ExistingRowId    { get; init; }
    public FactKey   CandidateFact    { get; init; } = null!;
}
