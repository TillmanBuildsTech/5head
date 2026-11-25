using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FiveHead.Mcp.Host;
using FiveHead.State.Client;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace FiveHead.Mcp.Tests;

public sealed class StateClientTests
{
    [Fact]
    public async Task UpdateMatter_SendsBearerToken_AndOmitsUnsetFields()
    {
        using (var created = StateApiClient.CreateHttpClient("http://state.local", "test-token"))
        {
            Assert.Equal("http://state.local/", created.BaseAddress?.ToString());
            Assert.Equal("Bearer", created.DefaultRequestHeaders.Authorization?.Scheme);
            Assert.Equal("test-token", created.DefaultRequestHeaders.Authorization?.Parameter);
        }

        var handler = new StubHandler
        {
            Status = HttpStatusCode.OK,
            Body = """{"id":"matter-1","version":4}"""
        };
        using var owned = new HttpClient(handler) { BaseAddress = new Uri("http://state.local/") };
        owned.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");
        var client = new StateApiClient(owned, "hermes");

        var body = await client.UpdateMatterAsync(
            "matter-1",
            3,
            title: null,
            status: "done",
            owner: " ",
            nextStep: null,
            vectorRef: null,
            metadataJson: """{"links":["https://example.test"]}""",
            agent: null,
            CancellationToken.None);

        Assert.Contains("\"version\":4", body, StringComparison.Ordinal);
        Assert.Equal("Bearer", handler.Scheme);
        Assert.Equal("test-token", handler.Parameter);
        Assert.Equal("/matters/matter-1", handler.Path);
        Assert.Contains("\"version\":3", handler.Content, StringComparison.Ordinal);
        Assert.Contains("\"agent\":\"hermes\"", handler.Content, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"done\"", handler.Content, StringComparison.Ordinal);
        Assert.Contains("\"metadata\":{\"links\":[\"https://example.test\"]}", handler.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("\"title\"", handler.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("\"owner\"", handler.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateMatter_ConflictExposesTheCurrentVersion()
    {
        var handler = new StubHandler
        {
            Status = HttpStatusCode.Conflict,
            Body = """{"error":"version_conflict","message":"stale","matter":{"id":"matter-1","version":4,"title":"Cut the tunnel"}}"""
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://state.local/") };
        var tools = new StateMcpTools(new StateApiClient(http, "hermes"));

        var ex = await Assert.ThrowsAsync<McpException>(() => tools.update_matter("matter-1", 3, status: "done"));

        Assert.Contains("You sent version 3", ex.Message, StringComparison.Ordinal);
        Assert.Contains("the current version is 4", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Re-read the matter and retry", ex.Message, StringComparison.Ordinal);
        Assert.Contains("\"version\":4", ex.Message, StringComparison.Ordinal);
        Assert.IsType<StateConflictException>(ex.InnerException);
    }

    [Fact]
    public async Task ClaimNext_Turns204IntoAnEmptyClaim()
    {
        var handler = new StubHandler { Status = HttpStatusCode.NoContent, Body = "" };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://state.local/") };
        var client = new StateApiClient(http, "hermes");

        var body = await client.ClaimNextAsync(30, null, CancellationToken.None);

        Assert.Contains("\"claimed\":false", body, StringComparison.Ordinal);
        Assert.Equal("/claims/next", handler.Path);
        Assert.Contains("\"agent\":\"hermes\"", handler.Content, StringComparison.Ordinal);
        Assert.Contains("\"ttlSeconds\":30", handler.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RegistersOnlyWhenUrlAndTokenAreBothPresent()
    {
        Assert.Null(StateClientSettings.Resolve(null, null, null, _ => null));

        var fromEnv = StateClientSettings.Resolve(
            "http://from-config",
            "config-token",
            "config-agent",
            name => name switch
            {
                StateClientSettings.UrlVariable => "http://from-env/",
                StateClientSettings.TokenVariable => "env-token",
                _ => null
            });
        Assert.Equal("http://from-env/", fromEnv!.BaseUrl);
        Assert.Equal("env-token", fromEnv.Token);
        Assert.Equal("config-agent", fromEnv.Agent);

        Assert.Throws<InvalidOperationException>(() =>
            StateClientSettings.Resolve("http://only-url", null, null, _ => null));
    }

    [Fact]
    public void StateToolGroup_IsAbsentWhenUnconfigured_AndPresentWhenConfigured()
    {
        string[] memoryTools =
        [
            "read_memory",
            "write_memory",
            "check_contradictions",
            "list_facts",
            "confirm_fact",
            "retract_fact",
            "get_provenance"
        ];
        string[] stateTools =
        [
            "list_matters",
            "get_matter",
            "create_matter",
            "update_matter",
            "append_event",
            "claim_next",
            "release_claim"
        ];

        using var unconfigured = Register(StateClientSettings.Resolve(null, null, null, _ => null));
        Assert.Null(unconfigured.Settings);
        foreach (var name in memoryTools)
        {
            Assert.Contains(name, unconfigured.ToolNames);
        }

        foreach (var name in stateTools)
        {
            Assert.DoesNotContain(name, unconfigured.ToolNames);
        }

        Assert.Empty(unconfigured.Provider.GetServices<HttpClient>());
        Assert.Empty(unconfigured.Provider.GetServices<StateApiClient>());

        var configuredSettings = StateClientSettings.Resolve(
            null,
            null,
            null,
            name => name switch
            {
                StateClientSettings.UrlVariable => "http://127.0.0.1:9",
                StateClientSettings.TokenVariable => "test-token",
                _ => null
            });
        using var configured = Register(configuredSettings);
        Assert.NotNull(configured.Settings);
        foreach (var name in memoryTools)
        {
            Assert.Contains(name, configured.ToolNames);
        }

        foreach (var name in stateTools)
        {
            Assert.Contains(name, configured.ToolNames);
        }

        var http = Assert.Single(configured.Provider.GetServices<HttpClient>());
        Assert.Equal("http://127.0.0.1:9/", http.BaseAddress?.ToString());
        Assert.Single(configured.Provider.GetServices<StateApiClient>());
    }

    [Fact]
    public void LoadConfig_ReadsSharedStateFields()
    {
        var root = Path.Combine(Path.GetTempPath(), "fivehead-state-config", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(
                Path.Combine(root, "config.yaml"),
                """
                stateApiUrl: http://state.local
                stateAuthToken: yaml-token
                stateAgent: hermes-lab
                """);
            var config = FiveHead.Mcp.Host.McpServer.LoadConfig(root);
            Assert.Equal("http://state.local", config.StateApiUrl);
            Assert.Equal("yaml-token", config.StateAuthToken);
            Assert.Equal("hermes-lab", config.StateAgent);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static RegisteredServer Register(StateClientSettings? settings)
    {
        var serializer = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                McpJsonUtilities.DefaultOptions.TypeInfoResolver,
                HostJsonSerializerContext.Default)
        };
        var services = new ServiceCollection();
        var mcp = services
            .AddMcpServer()
            .WithTools<MemoryMcpTools>(serializer);
        if (settings is not null)
        {
            StateToolRegistration.Add(services, mcp, settings, serializer);
        }

        var provider = services.BuildServiceProvider();
        var names = provider.GetServices<McpServerTool>()
            .Select(tool => tool.ProtocolTool.Name)
            .ToArray();
        return new RegisteredServer(provider, settings, names);
    }

    private sealed class RegisteredServer : IDisposable
    {
        public RegisteredServer(ServiceProvider provider, StateClientSettings? settings, string[] toolNames)
        {
            Provider = provider;
            Settings = settings;
            ToolNames = toolNames;
        }

        public ServiceProvider Provider { get; }
        public StateClientSettings? Settings { get; }
        public string[] ToolNames { get; }

        public void Dispose() => Provider.Dispose();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string Body { get; set; } = "{}";
        public string? Scheme { get; private set; }
        public string? Parameter { get; private set; }
        public string Path { get; private set; } = "";
        public string Content { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Scheme = request.Headers.Authorization?.Scheme;
            Parameter = request.Headers.Authorization?.Parameter;
            Path = request.RequestUri?.AbsolutePath ?? "";
            Content = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(Body)
            };
        }
    }
}
