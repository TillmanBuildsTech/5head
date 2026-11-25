using System.ComponentModel;
using FiveHead.State.Client;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FiveHead.Mcp.Host;

[McpServerToolType]
public sealed class StateMcpTools
{
    private readonly StateApiClient _client;

    public StateMcpTools(StateApiClient client)
    {
        _client = client;
    }

    [McpServerTool, Description("List shared matters. Optional status and owner filters. Registered only when shared state is configured.")]
    public Task<CallToolResult> list_matters(string? status = null, string? owner = null, CancellationToken ct = default) =>
        CallAsync(() => _client.ListMattersAsync(status, owner, ct));

    [McpServerTool, Description("Fetch one shared matter, including its append-only events and current version.")]
    public Task<CallToolResult> get_matter(string id, CancellationToken ct = default) =>
        CallAsync(() => _client.GetMatterAsync(id, ct));

    [McpServerTool, Description("Create a shared matter. The owner is the only agent allowed to change status or owner later.")]
    public Task<CallToolResult> create_matter(
        string title,
        string owner,
        string? status = null,
        string? nextStep = null,
        string? metadataJson = null,
        string? vectorRef = null,
        CancellationToken ct = default) =>
        CallAsync(() => _client.CreateMatterAsync(title, owner, status, nextStep, metadataJson, vectorRef, ct));

    [McpServerTool, Description("Update a shared matter when version matches. A stale version is a tool error that includes the current version; re-read and retry. Only the owner may change status or owner.")]
    public Task<CallToolResult> update_matter(
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
        CallAsync(
            () => _client.UpdateMatterAsync(id, version, title, status, owner, nextStep, vectorRef, metadataJson, agent, ct),
            conflict => Conflict(id, version, conflict));

    [McpServerTool, Description("Append an event to a shared matter. Events are never updated or deleted. Any agent that can call this server may append.")]
    public Task<CallToolResult> append_event(
        string id,
        string kind,
        string summary,
        string? payloadJson = null,
        string? agent = null,
        CancellationToken ct = default) =>
        CallAsync(() => _client.AppendEventAsync(id, kind, summary, payloadJson, agent, ct));

    [McpServerTool, Description("Claim the next available shared matter. The server locks one row so two agents cannot take the same one. Returns {\"claimed\":false} when nothing is waiting.")]
    public Task<CallToolResult> claim_next(int ttlSeconds = 300, string? agent = null, CancellationToken ct = default) =>
        CallAsync(() => _client.ClaimNextAsync(ttlSeconds, agent, ct));

    [McpServerTool, Description("Release a claim on a shared matter. An active claim can be released by the claimant. An expired claim can be cleared by anyone.")]
    public Task<CallToolResult> release_claim(string id, string? agent = null, CancellationToken ct = default) =>
        CallAsync(() => _client.ReleaseClaimAsync(id, agent, ct));

    private static Task<CallToolResult> CallAsync(Func<Task<string>> action, Func<StateConflictException, McpException>? conflict = null) =>
        ExecuteAsync(async () =>
        {
            var body = await action();
            return Text(body);
        }, conflict);

    private static async Task<CallToolResult> ExecuteAsync(
        Func<Task<CallToolResult>> action,
        Func<StateConflictException, McpException>? conflict)
    {
        try
        {
            return await action();
        }
        catch (StateConflictException ex)
        {
            throw conflict?.Invoke(ex) ?? new McpException(ex.Message, ex);
        }
        catch (StateApiException ex)
        {
            throw new McpException(ex.Message, ex);
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    private static McpException Conflict(string id, long sentVersion, StateConflictException conflict)
    {
        var current = conflict.CurrentVersion?.ToString() ?? "unknown";
        return new McpException(
            $"Version conflict for matter {id}. You sent version {sentVersion}; the current version is {current}. Re-read the matter and retry. {conflict.Body}",
            conflict);
    }

    private static CallToolResult Text(string text) =>
        new()
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = text
                }
            ]
        };
}
