using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FiveHead.Mcp.Core.Config;
using FiveHead.Mcp.Core.Memory;
using FiveHead.Mcp.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FiveHead.Mcp.Host;

internal static class McpServer
{
    internal static async Task RunAsync(string[] args)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton<IMemoryRootLocator, MemoryRootLocator>();
        builder.Services.AddSingleton(sp =>
        {
            var locator = sp.GetRequiredService<IMemoryRootLocator>();
            var memoryRoot = locator.Locate(Directory.GetCurrentDirectory());
            return LoadConfig(memoryRoot);
        });
        builder.Services.AddSingleton(sp =>
        {
            var locator = sp.GetRequiredService<IMemoryRootLocator>();
            var memoryRoot = locator.Locate(Directory.GetCurrentDirectory());
            var config = sp.GetRequiredService<AgentMemoryConfig>();
            return HostPaths.Create(memoryRoot, config);
        });
        builder.Services.AddSingleton<IMemoryFileStore>(sp =>
        {
            var paths = sp.GetRequiredService<HostPaths>();
            return new MemoryFileStore(paths.MemoryRoot);
        });
        builder.Services.AddSingleton<IProvenanceStore>(sp =>
        {
            var paths = sp.GetRequiredService<HostPaths>();
            return new SqliteProvenanceStore(paths.ProvenanceDbPath);
        });
        builder.Services.AddSingleton<IMemoryService, MemoryService>();
        builder.Services.AddSingleton<MemoryMcpTools>();
        builder.Services.AddSingleton<MemoryMcpResources>();

        var serializerOptions = CreateSerializerOptions();

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<MemoryMcpTools>(serializerOptions)
            .WithListResourcesHandler(async (request, ct) =>
            {
                var resources = request.Services.GetRequiredService<MemoryMcpResources>();
                return await resources.ListAsync(ct);
            })
            .WithReadResourceHandler(async (request, ct) =>
            {
                var resources = request.Services.GetRequiredService<MemoryMcpResources>();
                return await resources.ReadAsync(request.Params.Uri, ct);
            });

        var host = builder.Build();
        await host.RunAsync();
    }

    internal static AgentMemoryConfig LoadConfig(string memoryRoot)
    {
        var configPath = Path.Combine(memoryRoot, "config.yaml");
        if (!File.Exists(configPath))
        {
            return new AgentMemoryConfig();
        }

        var config = new AgentMemoryConfig();
        foreach (var rawLine in File.ReadAllLines(configPath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0 || separatorIndex == line.Length - 1)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim().Trim('"', '\'');

            if (key.Equals(nameof(AgentMemoryConfig.StalenessThresholdDays), StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value, out var stalenessThresholdDays))
            {
                config.StalenessThresholdDays = stalenessThresholdDays;
                continue;
            }

            if (key.Equals(nameof(AgentMemoryConfig.ProvenanceDbPath), StringComparison.OrdinalIgnoreCase))
            {
                config.ProvenanceDbPath = string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return config;
    }

    internal sealed record HostPaths(string MemoryRoot, string ProvenanceDbPath)
    {
        public static HostPaths Create(string memoryRoot, AgentMemoryConfig config)
        {
            var provenanceDbPath = ResolveProvenanceDbPath(memoryRoot, config);
            return new HostPaths(memoryRoot, provenanceDbPath);
        }

        private static string ResolveProvenanceDbPath(string memoryRoot, AgentMemoryConfig config)
        {
            if (!string.IsNullOrWhiteSpace(config.ProvenanceDbPath))
            {
                return Path.GetFullPath(config.ProvenanceDbPath, memoryRoot);
            }

            var repoRoot = Directory.GetParent(memoryRoot)?.FullName ?? memoryRoot;
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(repoRoot));
            var repoHash = Convert.ToHexString(hashBytes).ToLowerInvariant()[..16];
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".agent-memory", "cache", repoHash, "provenance.db");
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        return new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                McpJsonUtilities.DefaultOptions.TypeInfoResolver,
                HostJsonSerializerContext.Default)
        };
    }
}
