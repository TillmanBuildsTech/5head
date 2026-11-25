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
    private readonly string _adminConnection;
    private readonly string _databaseName = "state_it_" + Guid.NewGuid().ToString("N");
    private WebApplicationFactory<Program>? _factory;

    public StateApiFixture()
    {
        _adminConnection = Environment.GetEnvironmentVariable("STATE_API_TEST_CONNECTION")
            ?? "Host=localhost;Port=5432;Username=state;Password=state;Database=postgres";
    }

    public string DatabaseConnection { get; private set; } = "";

    public HttpClient CreateClient(bool authenticate = true)
    {
        var client = _factory!.CreateClient();
        if (authenticate)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        }

        return client;
    }

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
        _factory = new StateFactory(DatabaseConnection);
        // Touch the server once so migrations finish before tests race the first request.
        using var warmup = CreateClient();
        using var health = await warmup.GetAsync("/health");
        health.EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync()
    {
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

    private sealed class StateFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("STATE_DATABASE_URL", connectionString);
            builder.UseSetting("STATE_AUTH_TOKEN", "test-token");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["STATE_DATABASE_URL"] = connectionString,
                    ["STATE_AUTH_TOKEN"] = "test-token"
                });
            });
        }
    }
}
