using FiveHead.Mcp.Core.Memory;
using ModelContextProtocol.Protocol;

namespace FiveHead.Mcp.Host;

public sealed class MemoryMcpResources(IMemoryService memoryService)
{
    private readonly IMemoryService _memoryService = memoryService;

    public Task<ListResourcesResult> ListAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new ListResourcesResult
        {
            Resources =
            [
                CreateResource("memory://soul", "soul", "SOUL.md", "Stable agent identity and principles."),
                CreateResource("memory://memory", "memory", "MEMORY.md", "Learned project facts and conventions."),
                CreateResource("memory://user", "user", "USER.md", "Persistent operator profile summary.")
            ]
        });
    }

    public async Task<ReadResourceResult> ReadAsync(string uri, CancellationToken ct = default)
    {
        var (file, canonicalUri) = uri switch
        {
            "memory://soul" => (MemoryFile.Soul, "memory://soul"),
            "memory://memory" => (MemoryFile.Memory, "memory://memory"),
            "memory://user" => (MemoryFile.User, "memory://user"),
            _ => throw new ArgumentOutOfRangeException(nameof(uri), $"Unknown resource '{uri}'.")
        };

        var content = await _memoryService.ReadMemoryAsync(file, ct);
        return new ReadResourceResult
        {
            Contents =
            [
                new TextResourceContents
                {
                    Uri = canonicalUri,
                    MimeType = "text/markdown",
                    Text = content
                }
            ]
        };
    }

    private static Resource CreateResource(string uri, string name, string title, string description) => new()
    {
        Uri = uri,
        Name = name,
        Title = title,
        MimeType = "text/markdown",
        Description = description
    };
}
