namespace FiveHead.Mcp.Core.Memory;

public sealed class ContradictionException(IReadOnlyList<ConflictInfo> conflicts)
    : Exception(BuildMessage(conflicts))
{
    public IReadOnlyList<ConflictInfo> Conflicts { get; } = conflicts;

    private static string BuildMessage(IReadOnlyList<ConflictInfo> conflicts)
    {
        if (conflicts.Count == 0)
        {
            return "Write rejected because it contradicts active memory.";
        }

        var first = conflicts[0];
        return $"Write rejected because {first.CandidateFact.Subject}.{first.CandidateFact.Predicate}='{first.CandidateFact.Object}' conflicts with active fact '{first.ExistingFact.Object}' in row {first.ExistingRowId}.";
    }
}
