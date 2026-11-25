using System.ComponentModel;
using ModelContextProtocol.Server;

namespace FiveHead.State.Mcp;

[McpServerToolType]
public sealed class StateMcpTools(StateApiClient client)
{
    [McpServerTool, Description("List shared matters. Optional status and owner filters.")]
    public Task<string> list_matters(string? status = null, string? owner = null, CancellationToken ct = default) =>
        client.ListMattersAsync(status, owner, ct);

    [McpServerTool, Description("Fetch one matter, including its append-only events.")]
    public Task<string> get_matter(string id, CancellationToken ct = default) =>
        client.GetMatterAsync(id, ct);

    [McpServerTool, Description("Create a matter. The owner is the only agent allowed to change status or owner later.")]
    public Task<string> create_matter(
        string title,
        string owner,
        string? status = null,
        string? nextStep = null,
        string? metadataJson = null,
        string? vectorRef = null,
        CancellationToken ct = default) =>
        client.CreateMatterAsync(title, owner, status, nextStep, metadataJson, vectorRef, ct);

    [McpServerTool, Description("Update a matter if version matches. A mismatch throws with HTTP 409 and the current row. Only the owner may change status or owner.")]
    public Task<string> update_matter(
        string id,
        long version,
        string? title = null,
        string? status = null,
        string? owner = null,
        string? nextStep = null,
        string? vectorRef = null,
        string? metadataJson = null,
        string? agent = null,
        CancellationToken ct = default) =>
        client.UpdateMatterAsync(id, version, title, status, owner, nextStep, vectorRef, metadataJson, agent, ct);

    [McpServerTool, Description("Append an event. Events are never updated or deleted. Any authenticated agent may append.")]
    public Task<string> append_event(
        string id,
        string kind,
        string summary,
        string? payloadJson = null,
        string? agent = null,
        CancellationToken ct = default) =>
        client.AppendEventAsync(id, kind, summary, payloadJson, agent, ct);

    [McpServerTool, Description("Claim the next available matter. Uses SKIP LOCKED on the server so two agents cannot take the same one.")]
    public Task<string> claim_next(int ttlSeconds = 300, string? agent = null, CancellationToken ct = default) =>
        client.ClaimNextAsync(ttlSeconds, agent, ct);

    [McpServerTool, Description("Release a claim held by this agent. An expired claim can be cleared by anyone.")]
    public Task<string> release_claim(string id, string? agent = null, CancellationToken ct = default) =>
        client.ReleaseClaimAsync(id, agent, ct);
}
