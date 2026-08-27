using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using FiveHead.Mcp.Host;

namespace FiveHead.Mcp.Tests;

public sealed class StdioMcpIntegrationTests : IDisposable
{
    private readonly string _workspaceRoot;
    private readonly string _homeRoot;

    public StdioMcpIntegrationTests()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), "fivehead-stdio-tests", Guid.NewGuid().ToString("N"));
        _homeRoot = Path.Combine(_workspaceRoot, "home");

        Directory.CreateDirectory(_workspaceRoot);
        Directory.CreateDirectory(_homeRoot);
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, ".git"));
    }

    [Fact]
    public async Task StdioServer_ExposesToolsAndResources_EndToEnd()
    {
        var stderr = new List<string>();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "5head-test",
            Command = "dotnet",
            Arguments = [typeof(MemoryMcpTools).Assembly.Location],
            WorkingDirectory = _workspaceRoot,
            StandardErrorLines = line =>
            {
                lock (stderr)
                {
                    stderr.Add(line);
                }
            },
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["HOME"] = _homeRoot
            }
        });

        McpClient client;
        try
        {
            client = await McpClient.CreateAsync(transport);
        }
        catch (Exception ex)
        {
            throw new Xunit.Sdk.XunitException($"Failed to start stdio MCP server. Stderr:{Environment.NewLine}{string.Join(Environment.NewLine, stderr)}{Environment.NewLine}{ex}");
        }

        await using var _ = client;

        var tools = await client.ListToolsAsync();
        var resources = await client.ListResourcesAsync();

        Assert.Contains(tools, tool => tool.Name == "read_memory");
        Assert.Contains(tools, tool => tool.Name == "write_memory");
        Assert.Contains(tools, tool => tool.Name == "get_provenance");
        Assert.Contains(resources, resource => resource.Name == "memory" && resource.Title == "MEMORY.md");
        Assert.Contains(resources, resource => resource.Name == "soul" && resource.Title == "SOUL.md");

        var initialWrite = await client.CallToolAsync("write_memory", new Dictionary<string, object?>
        {
            ["file"] = "memory",
            ["mode"] = "append",
            ["content"] = "- language: C#",
            ["sourceKind"] = "integration-test"
        });

        var readMemory = await client.CallToolAsync("read_memory", new Dictionary<string, object?>
        {
            ["file"] = "memory"
        });

        var conflictingWrite = await client.CallToolAsync("write_memory", new Dictionary<string, object?>
        {
            ["file"] = "memory",
            ["mode"] = "append",
            ["content"] = "- language: Rust",
            ["sourceKind"] = "integration-test"
        });

        var memoryResource = Assert.Single(resources, resource => resource.Name == "memory");
        var resource = await client.ReadResourceAsync(memoryResource.Uri);
        var memoryFilePath = Path.Combine(_workspaceRoot, ".agent-memory", "MEMORY.md");
        var diskContent = await File.ReadAllTextAsync(memoryFilePath);

        if (initialWrite.IsError == true)
        {
            throw new Xunit.Sdk.XunitException($"Initial write failed: {GetPayload(initialWrite)}{Environment.NewLine}Stderr:{Environment.NewLine}{string.Join(Environment.NewLine, stderr)}");
        }

        Assert.NotEqual(true, readMemory.IsError);
        Assert.Equal(true, conflictingWrite.IsError);
        Assert.Contains("write_memory", GetPayload(conflictingWrite), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("- language: C#", diskContent);

        var textResource = Assert.IsType<TextResourceContents>(Assert.Single(resource.Contents));
        Assert.Equal("memory://memory", textResource.Uri);
        Assert.Equal("text/markdown", textResource.MimeType);
    }

    [Fact]
    public async Task StdioServer_ReturnsStructuredPayloads()
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "5head-structured-test",
            Command = "dotnet",
            Arguments = [typeof(MemoryMcpTools).Assembly.Location],
            WorkingDirectory = _workspaceRoot,
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["HOME"] = _homeRoot
            }
        });

        await using var client = await McpClient.CreateAsync(transport);

        await client.CallToolAsync("write_memory", new Dictionary<string, object?>
        {
            ["file"] = "memory",
            ["mode"] = "append",
            ["content"] = "- language: C#",
            ["sourceKind"] = "integration-test"
        });

        var contradictions = await client.CallToolAsync("check_contradictions", new Dictionary<string, object?>
        {
            ["file"] = "memory",
            ["candidateContent"] = "- language: Rust"
        });

        var facts = await client.CallToolAsync("list_facts", new Dictionary<string, object?>
        {
            ["file"] = "memory"
        });

        var provenance = await client.CallToolAsync("get_provenance", new Dictionary<string, object?>
        {
            ["subject"] = "language",
            ["predicate"] = "value"
        });

        AssertStructuredPayload(contradictions, payload =>
        {
            Assert.Equal("MEMORY.md", payload.GetProperty("file").GetString());
            Assert.Equal(1, payload.GetProperty("conflictCount").GetInt32());
        });

        AssertStructuredPayload(facts, payload =>
        {
            Assert.Equal(1, payload.GetProperty("count").GetInt32());
            Assert.Equal(1, payload.GetProperty("facts").GetArrayLength());
        });

        AssertStructuredPayload(provenance, payload =>
        {
            Assert.Equal(1, payload.GetProperty("count").GetInt32());
            Assert.Equal(1, payload.GetProperty("history").GetArrayLength());
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private static string GetText(IList<ContentBlock> blocks)
    {
        return string.Concat(blocks.OfType<TextContentBlock>().Select(block => block.Text));
    }

    private static string GetPayload(CallToolResult result)
    {
        var text = GetText(result.Content);
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return result.StructuredContent?.ToString()
            ?? JsonSerializer.Serialize(result);
    }

    private static void AssertStructuredPayload(CallToolResult result, Action<JsonElement> assertPayload)
    {
        Assert.NotEqual(true, result.IsError);
        Assert.True(result.StructuredContent.HasValue);
        assertPayload(result.StructuredContent.Value);
    }
}
