using System.Text.Json;
using System.Text.Json.Serialization;

namespace FiveHead.State.Client;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CreateMatterRequest))]
[JsonSerializable(typeof(UpdateMatterRequest))]
[JsonSerializable(typeof(AppendEventRequest))]
[JsonSerializable(typeof(ClaimNextRequest))]
[JsonSerializable(typeof(ReleaseClaimRequest))]
[JsonSerializable(typeof(StateErrorBody))]
[JsonSerializable(typeof(StateMatterRef))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class StateJsonContext : JsonSerializerContext
{
}
