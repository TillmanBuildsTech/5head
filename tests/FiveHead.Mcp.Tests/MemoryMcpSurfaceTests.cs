using FiveHead.Mcp.Core.Config;
using FiveHead.Mcp.Core.Memory;
using FiveHead.Mcp.Core.Storage;
using FiveHead.Mcp.Host;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace FiveHead.Mcp.Tests;

public sealed class MemoryMcpSurfaceTests : IDisposable
{
    private readonly string _workspaceRoot;
    private readonly MemoryMcpTools _tools;
    private readonly MemoryMcpResources _resources;

    public MemoryMcpSurfaceTests()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), "fivehead-host-tests", Guid.NewGuid().ToString("N"));
        var memoryRoot = Path.Combine(_workspaceRoot, ".agent-memory");
        var databasePath = Path.Combine(_workspaceRoot, "cache", "provenance.db");

        var service = new MemoryService(
            new MemoryFileStore(memoryRoot),
            new SqliteProvenanceStore(databasePath),
            new AgentMemoryConfig());

        _tools = new MemoryMcpTools(service);
        _resources = new MemoryMcpResources(service);
    }

    [Fact]
    public async Task ToolSurface_WritesAndReadsRepoMemory()
    {
        var write = await _tools.write_memory("memory", "append", "- language: C#", "test");

        var content = await _tools.read_memory("MEMORY");
        var facts = await _tools.list_facts(file: "memory");

        Assert.NotEqual(true, write.IsError);
        Assert.Equal("- language: C#", content);
        Assert.NotEqual(true, facts.IsError);
    }

    [Fact]
    public async Task ToolSurface_ReturnsStructuredPayloads()
    {
        await _tools.write_memory("memory", "append", "- language: C#", "test");

        var contradictions = await _tools.check_contradictions("memory", "- language: Rust");
        var facts = await _tools.list_facts(file: "memory");
        var provenance = await _tools.get_provenance("language", "value");

        AssertStructuredPayload(contradictions, payload =>
        {
            Assert.Equal("MEMORY.md", payload.GetProperty("file").GetString());
            Assert.Equal(1, payload.GetProperty("conflictCount").GetInt32());
            Assert.Equal(1, payload.GetProperty("conflicts").GetArrayLength());
            Assert.Equal(1, payload.GetProperty("suggestions").GetArrayLength());
        });

        AssertStructuredPayload(facts, payload =>
        {
            Assert.Equal("MEMORY.md", payload.GetProperty("file").GetString());
            Assert.Equal(1, payload.GetProperty("count").GetInt32());
            Assert.Equal(1, payload.GetProperty("facts").GetArrayLength());
        });

        AssertStructuredPayload(provenance, payload =>
        {
            Assert.Equal("language", payload.GetProperty("subject").GetString());
            Assert.Equal("value", payload.GetProperty("predicate").GetString());
            Assert.Equal(1, payload.GetProperty("count").GetInt32());
            Assert.Equal(1, payload.GetProperty("history").GetArrayLength());
        });
    }

    [Fact]
    public async Task ResourceSurface_ReadsMemoryUris()
    {
        await _tools.write_memory("soul", "replace", "principle: be concise", "test");

        var soul = await _resources.ReadAsync("memory://soul");
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextResourceContents>(Assert.Single(soul.Contents));

        Assert.Equal("principle: be concise", text.Text);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private static void AssertStructuredPayload(CallToolResult result, Action<JsonElement> assertPayload)
    {
        Assert.NotEqual(true, result.IsError);
        Assert.True(result.StructuredContent.HasValue);
        assertPayload(result.StructuredContent.Value);
    }
}
