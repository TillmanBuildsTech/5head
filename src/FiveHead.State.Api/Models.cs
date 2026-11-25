using System.Text.Json;

namespace FiveHead.State.Api;

public sealed record Matter(
    Guid Id,
    string Title,
    string Owner,
    string Status,
    string? NextStep,
    string MetadataJson,
    string? VectorRef,
    long Version,
    string? ClaimedBy,
    DateTimeOffset? ClaimExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public object ToResponse(IReadOnlyList<MatterEvent>? events = null) => new
    {
        id = Id,
        title = Title,
        owner = Owner,
        status = Status,
        nextStep = NextStep,
        metadata = JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(MetadataJson) ? "{}" : MetadataJson),
        vectorRef = VectorRef,
        version = Version,
        claimedBy = ClaimedBy,
        claimExpiresAt = ClaimExpiresAt,
        createdAt = CreatedAt,
        updatedAt = UpdatedAt,
        events = events?.Select(e => e.ToResponse()).ToArray()
    };
}

public sealed record MatterEvent(
    long Id,
    Guid MatterId,
    string Agent,
    string Kind,
    string Summary,
    string PayloadJson,
    DateTimeOffset CreatedAt)
{
    public object ToResponse() => new
    {
        id = Id,
        matterId = MatterId,
        agent = Agent,
        kind = Kind,
        summary = Summary,
        payload = JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(PayloadJson) ? "{}" : PayloadJson),
        createdAt = CreatedAt
    };
}

public enum MutationKind
{
    Ok,
    NotFound,
    Conflict,
    Forbidden
}

public sealed record MutationResult(MutationKind Kind, Matter? Matter, string? Message, string Code = "")
{
    public static MutationResult Ok(Matter matter) => new(MutationKind.Ok, matter, null);
    public static MutationResult NotFound(string message) => new(MutationKind.NotFound, null, message);
    public static MutationResult Conflict(Matter matter, string message, string code = "version_conflict") =>
        new(MutationKind.Conflict, matter, message, code);
    public static MutationResult Forbidden(Matter matter, string message) => new(MutationKind.Forbidden, matter, message);
}

public sealed record MatterPatch(
    long Version,
    string Agent,
    bool HasTitle,
    string? Title,
    bool HasStatus,
    string? Status,
    bool HasOwner,
    string? Owner,
    bool HasNextStep,
    string? NextStep,
    bool HasMetadata,
    string? MetadataJson,
    bool HasVectorRef,
    string? VectorRef);
