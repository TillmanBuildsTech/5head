using System.Text.Json.Serialization;
using FiveHead.Mcp.Core.Provenance;

namespace FiveHead.Mcp.Core.Serialization;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(FactKey[]))]
[JsonSerializable(typeof(List<FactKey>))]
internal sealed partial class CoreJsonSerializerContext : JsonSerializerContext
{
}
