namespace FiveHead.Mcp.Core.Config;

/// <summary>
/// Optional per-repo configuration loaded from <c>.agent-memory/config.yaml</c>.
/// V1 keeps this intentionally small and only supports fields used by the host/runtime.
/// </summary>
public sealed class AgentMemoryConfig
{
    /// <summary>Override the staleness window for marking facts "uncertain". Default: 30 days.</summary>
    public int StalenessThresholdDays { get; set; } = 30;

    /// <summary>Optional path override for the provenance DB file.</summary>
    public string? ProvenanceDbPath { get; set; }
}
