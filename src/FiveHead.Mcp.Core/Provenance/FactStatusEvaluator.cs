namespace FiveHead.Mcp.Core.Provenance;

public static class FactStatusEvaluator
{
    public static FactStatus Evaluate(ProvenanceRow row, DateTime utcNow, int stalenessThresholdDays)
    {
        if (row.RetractedAt is not null)
        {
            return FactStatus.Retracted;
        }

        if (row.ConfirmedAt is not null)
        {
            return FactStatus.Confirmed;
        }

        if (row.CreatedAt <= utcNow.AddDays(-stalenessThresholdDays))
        {
            return FactStatus.Uncertain;
        }

        return FactStatus.Active;
    }
}
