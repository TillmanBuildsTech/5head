using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace FiveHead.State.Tests;

[CollectionDefinition("state-api", DisableParallelization = true)]
public sealed class StateApiCollection : ICollectionFixture<StateApiFixture>;

public sealed class StateApiFixture : IAsyncLifetime
{
    public const string StateToken = "test-token";
    public const string AdminToken = "admin-token";

    private readonly string _adminConnection;
    private readonly string _databaseName = "state_it_" + Guid.NewGuid().ToString("N");
    private WebApplicationFactory<Program>? _factory;
    private WebApplicationFactory<Program>? _adminFactory;

    public StateApiFixture()
    {
        _adminConnection = Environment.GetEnvironmentVariable("STATE_API_TEST_CONNECTION")
            ?? "Host=localhost;Port=5432;Username=state;Password=state;Database=postgres";
    }

    public string DatabaseConnection { get; private set; } = "";

    public HttpClient CreateClient(bool authenticate = true) =>
        Create(_factory, authenticate ? StateToken : null);

    public HttpClient CreateAdminClient(string? bearer = null, bool handleCookies = true) =>
        Create(_adminFactory, bearer, handleCookies);

    public WebApplicationFactory<Program> CreateFactory(bool adminEnabled, string? adminToken) =>
        new StateFactory(DatabaseConnection, adminEnabled, adminToken);

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(DatabaseConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("TRUNCATE events, matters RESTART IDENTITY CASCADE;", connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task InitializeAsync()
    {
        await using (var connection = new NpgsqlConnection(_adminConnection))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(_adminConnection)
        {
            Database = _databaseName
        };
        DatabaseConnection = builder.ConnectionString;
        _factory = new StateFactory(DatabaseConnection, adminEnabled: false, adminToken: null);
        _adminFactory = new StateFactory(DatabaseConnection, adminEnabled: true, adminToken: AdminToken);
        using var warmup = CreateClient();
        using var health = await warmup.GetAsync("/health");
        health.EnsureSuccessStatusCode();
        using var adminWarmup = CreateAdminClient();
        using var adminHealth = await adminWarmup.GetAsync("/health");
        adminHealth.EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync()
    {
        if (_adminFactory is not null)
        {
            await _adminFactory.DisposeAsync();
        }

        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using var terminate = new NpgsqlCommand(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = $1 AND pid <> pg_backend_pid();",
            connection);
        terminate.Parameters.AddWithValue(_databaseName);
        await terminate.ExecuteNonQueryAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\"", connection);
        await drop.ExecuteNonQueryAsync();
    }

    private static HttpClient Create(WebApplicationFactory<Program>? factory, string? bearer, bool handleCookies = true)
    {
        var client = factory!.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = handleCookies,
            AllowAutoRedirect = false
        });
        if (bearer is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return client;
    }

    private sealed class StateFactory(string connectionString, bool adminEnabled, string? adminToken) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var settings = new Dictionary<string, string?>
            {
                ["STATE_DATABASE_URL"] = connectionString,
                ["STATE_AUTH_TOKEN"] = StateToken,
                ["FIVEHEAD_ADMIN_ENABLED"] = adminEnabled ? "true" : "false",
                ["FIVEHEAD_ADMIN_TOKEN"] = adminToken ?? ""
            };
            foreach (var pair in settings)
            {
                builder.UseSetting(pair.Key, pair.Value);
            }

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(settings);
            });
        }
    }
}
