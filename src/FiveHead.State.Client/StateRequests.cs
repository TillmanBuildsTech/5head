using System.Text.Json;

namespace FiveHead.State.Client;

internal sealed class CreateMatterRequest
{
    public required string Title { get; init; }
    public required string Owner { get; init; }
    public string? Status { get; init; }
    public string? NextStep { get; init; }
    public string? VectorRef { get; init; }
    public JsonElement? Metadata { get; init; }
}

internal sealed class UpdateMatterRequest
{
    public required long Version { get; init; }
    public required string Agent { get; init; }
    public string? Title { get; init; }
    public string? Status { get; init; }
    public string? Owner { get; init; }
    public string? NextStep { get; init; }
    public string? VectorRef { get; init; }
    public JsonElement? Metadata { get; init; }
}

internal sealed class AppendEventRequest
{
    public required string Agent { get; init; }
    public required string Kind { get; init; }
    public required string Summary { get; init; }
    public JsonElement? Payload { get; init; }
}

internal sealed class ClaimNextRequest
{
    public required string Agent { get; init; }
    public int TtlSeconds { get; init; }
}

internal sealed class ReleaseClaimRequest
{
    public required string Agent { get; init; }
}

internal sealed class StateErrorBody
{
    public string? Error { get; set; }
    public string? Message { get; set; }
    public StateMatterRef? Matter { get; set; }
}

internal sealed class StateMatterRef
{
    public long? Version { get; set; }
}
