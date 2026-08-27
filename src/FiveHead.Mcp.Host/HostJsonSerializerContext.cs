using System.Text.Json.Serialization;
using FiveHead.Mcp.Core.Memory;
using FiveHead.Mcp.Core.Provenance;

namespace FiveHead.Mcp.Host;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WriteMemoryResult))]
[JsonSerializable(typeof(ContradictionCheckResult))]
[JsonSerializable(typeof(ContradictionCheckPayload))]
[JsonSerializable(typeof(ListFactsPayload))]
[JsonSerializable(typeof(ProvenanceHistoryPayload))]
[JsonSerializable(typeof(ConflictInfo))]
[JsonSerializable(typeof(ProvenanceRow))]
[JsonSerializable(typeof(FactKey))]
[JsonSerializable(typeof(IReadOnlyList<ProvenanceRow>))]
[JsonSerializable(typeof(ProvenanceRow[]))]
[JsonSerializable(typeof(ConflictInfo[]))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class HostJsonSerializerContext : JsonSerializerContext
{
}
