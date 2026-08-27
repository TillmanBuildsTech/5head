using System.Text.Json;
using FiveHead.Mcp.Core.Memory;
using FiveHead.Mcp.Core.Provenance;
using FiveHead.Mcp.Core.Storage;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FiveHead.Mcp.Host;

[McpServerToolType]
public sealed class MemoryMcpTools(IMemoryService memoryService)
{
    private readonly IMemoryService _memoryService = memoryService;

    [McpServerTool]
    public Task<string> read_memory(string file, CancellationToken ct = default) =>
        ExecuteAsync(() => _memoryService.ReadMemoryAsync(ParseMemoryFile(file), ct));

    [McpServerTool]
    public Task<CallToolResult> write_memory(
        string file,
        string mode,
        string content,
        string? sourceKind = null,
        string? threadId = null,
        string? taskId = null,
        CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            var result = await _memoryService.WriteMemoryAsync(
                ParseMemoryFile(file),
                ParseWriteMode(mode),
                content,
                string.IsNullOrWhiteSpace(sourceKind) ? "mcp" : sourceKind,
                threadId,
                taskId,
                ct);

            var warningText = result.Warnings.Length == 0
                ? "none"
                : string.Join(", ", result.Warnings);

            return SuccessText($"provenanceId={result.ProvenanceId}; warnings={warningText}");
        });

    [McpServerTool]
    public Task<CallToolResult> check_contradictions(
        string file,
        string candidateContent,
        CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            var result = await _memoryService.CheckContradictionsAsync(ParseMemoryFile(file), candidateContent, ct);
            var memoryFile = ParseMemoryFile(file);

            var suggestionText = result.Suggestions.Length == 0
                ? "none"
                : string.Join(", ", result.Suggestions);

            return Success(
                $"conflicts={result.Conflicts.Length}; suggestions={suggestionText}",
                new ContradictionCheckPayload
                {
                    File = MemoryLimits.FileName(memoryFile),
                    Candidate = candidateContent,
                    ConflictCount = result.Conflicts.Length,
                    Conflicts = result.Conflicts,
                    Suggestions = result.Suggestions
                },
                HostJsonSerializerContext.Default.ContradictionCheckPayload);
        });

    [McpServerTool]
    public Task<CallToolResult> list_facts(
        string? file = null,
        string? status = null,
        CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            MemoryFile? memoryFile = string.IsNullOrWhiteSpace(file) ? null : ParseMemoryFile(file);
            FactStatus? factStatus = string.IsNullOrWhiteSpace(status) ? null : ParseFactStatus(status);
            var result = await _memoryService.ListFactsAsync(
                memoryFile,
                factStatus,
                ct);

            return Success(
                $"count={result.Count}",
                new ListFactsPayload
                {
                    File = memoryFile is null ? null : MemoryLimits.FileName(memoryFile.Value),
                    Status = factStatus?.ToString().ToLowerInvariant(),
                    Count = result.Count,
                    Facts = result.ToArray()
                },
                HostJsonSerializerContext.Default.ListFactsPayload);
        });

    [McpServerTool]
    public Task<CallToolResult> confirm_fact(long provenanceId, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            await _memoryService.ConfirmFactAsync(provenanceId, ct);
            return SuccessText($"confirmed provenanceId={provenanceId}");
        });

    [McpServerTool]
    public Task<CallToolResult> retract_fact(long provenanceId, string reason, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            await _memoryService.RetractFactAsync(provenanceId, reason, ct);
            return SuccessText($"retracted provenanceId={provenanceId}; reason={reason}");
        });

    [McpServerTool]
    public Task<CallToolResult> get_provenance(
        string subject,
        string predicate,
        CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            var result = await _memoryService.GetProvenanceAsync(subject, predicate, ct);
            return Success(
                $"count={result.Count}",
                new ProvenanceHistoryPayload
                {
                    Subject = subject,
                    Predicate = predicate,
                    Count = result.Count,
                    History = result.ToArray()
                },
                HostJsonSerializerContext.Default.ProvenanceHistoryPayload);
        });

    internal static MemoryFile ParseMemoryFile(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        return normalized switch
        {
            "SOUL" or "SOUL.MD" => MemoryFile.Soul,
            "MEMORY" or "MEMORY.MD" => MemoryFile.Memory,
            "USER" or "USER.MD" => MemoryFile.User,
            _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown memory file '{value}'. Use SOUL, MEMORY, or USER.")
        };
    }

    internal static WriteMode ParseWriteMode(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "append" => WriteMode.Append,
            "replace" => WriteMode.Replace,
            "remove" => WriteMode.Remove,
            _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown write mode '{value}'. Use append, replace, or remove.")
        };
    }

    internal static FactStatus ParseFactStatus(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "active" => FactStatus.Active,
            "confirmed" => FactStatus.Confirmed,
            "uncertain" => FactStatus.Uncertain,
            "retracted" => FactStatus.Retracted,
            _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown fact status '{value}'. Use active, confirmed, uncertain, or retracted.")
        };
    }

    private static async Task ExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (TryConvertToMcpException(ex, out var mcpException))
        {
            throw mcpException;
        }
    }

    private static async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (TryConvertToMcpException(ex, out var mcpException))
        {
            throw mcpException;
        }
    }

    private static bool TryConvertToMcpException(Exception exception, out McpException mcpException)
    {
        switch (exception)
        {
            case McpException alreadyMcp:
                mcpException = alreadyMcp;
                return true;
            case ContradictionException contradiction:
                mcpException = new McpException(contradiction.Message, contradiction);
                return true;
            case SizeLimitExceededException sizeLimit:
                mcpException = new McpException(sizeLimit.Message, sizeLimit);
                return true;
            case ArgumentOutOfRangeException argument:
                mcpException = new McpException(argument.Message, argument);
                return true;
            case InvalidOperationException invalidOperation:
                mcpException = new McpException(invalidOperation.Message, invalidOperation);
                return true;
            default:
                mcpException = new McpException($"{exception.GetType().Name}: {exception.Message}", exception);
                return true;
        }
    }

    private static CallToolResult SuccessText(string text)
    {
        return new CallToolResult
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

    private static CallToolResult Success<T>(string text, T? payload, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>? jsonTypeInfo) where T : class
    {
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = text
                }
            ],
            StructuredContent = payload is null || jsonTypeInfo is null
                ? null
                : JsonSerializer.SerializeToElement(payload, jsonTypeInfo)
        };
    }
}
