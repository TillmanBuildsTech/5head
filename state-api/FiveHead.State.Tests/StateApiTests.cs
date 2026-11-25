using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace FiveHead.State.Tests;

[Collection("state-api")]
public sealed class StateApiTests(StateApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task VersionConflict_Returns409WithTheCurrentRow()
    {
        await fixture.ResetAsync();
        using var client = fixture.CreateClient();
        var created = await CreateMatter(client, "Tunnel", "alice");
        var id = created.GetProperty("id").GetGuid();

        var first = await client.PatchAsJsonAsync($"/matters/{id}", new { version = 1, agent = "alice", nextStep = "publish" });
        var winner = await Read(first);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(2, winner.GetProperty("version").GetInt64());

        var second = await client.PatchAsJsonAsync($"/matters/{id}", new { version = 1, agent = "alice", nextStep = "retry" });
        var conflict = await Read(second);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("version_conflict", conflict.GetProperty("error").GetString());
        Assert.Equal(2, conflict.GetProperty("matter").GetProperty("version").GetInt64());
        Assert.Equal("publish", conflict.GetProperty("matter").GetProperty("nextStep").GetString());
    }

    [Fact]
    public async Task OnlyTheOwner_MayChangeStatus()
    {
        await fixture.ResetAsync();
        using var client = fixture.CreateClient();
        var created = await CreateMatter(client, "Status rule", "alice");
        var id = created.GetProperty("id").GetGuid();

        var forbidden = await client.PatchAsJsonAsync($"/matters/{id}", new { version = 1, agent = "bob", status = "done" });
        var denied = await Read(forbidden);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("forbidden", denied.GetProperty("error").GetString());
        Assert.Equal("open", denied.GetProperty("matter").GetProperty("status").GetString());
        Assert.Equal(1, denied.GetProperty("matter").GetProperty("version").GetInt64());

        var note = await client.PostAsJsonAsync($"/matters/{id}/events", new
        {
            agent = "bob",
            kind = "note",
            summary = "bob can still write the log",
            payload = new { source = "test" }
        });
        Assert.Equal(HttpStatusCode.Created, note.StatusCode);

        var step = await client.PatchAsJsonAsync($"/matters/{id}", new { version = 1, agent = "bob", nextStep = "ask alice" });
        Assert.Equal(HttpStatusCode.OK, step.StatusCode);

        var owned = await client.PatchAsJsonAsync($"/matters/{id}", new { version = 2, agent = "alice", status = "done" });
        var updated = await Read(owned);
        Assert.Equal(HttpStatusCode.OK, owned.StatusCode);
        Assert.Equal("done", updated.GetProperty("status").GetString());

        var fetched = await Read(await client.GetAsync($"/matters/{id}"));
        Assert.Contains(fetched.GetProperty("events").EnumerateArray(), e => e.GetProperty("agent").GetString() == "bob");
    }

    [Fact]
    public async Task ClaimNext_IsExclusiveUnderConcurrency()
    {
        await fixture.ResetAsync();
        using var client = fixture.CreateClient();
        for (var i = 0; i < 4; i++)
        {
            await CreateMatter(client, $"job-{i}", "alice");
        }

        var claims = await Task.WhenAll(Enumerable.Range(0, 12).Select(async i =>
        {
            using var claimClient = fixture.CreateClient();
            var response = await claimClient.PostAsJsonAsync("/claims/next", new { agent = $"agent-{i}", ttlSeconds = 120 });
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return (Guid?)null;
            }

            var failureText = response.StatusCode == HttpStatusCode.OK
                ? ""
                : await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, failureText);
            var body = await Read(response);
            return (Guid?)body.GetProperty("id").GetGuid();
        }));

        var won = claims.Where(id => id is not null).Select(id => id!.Value).ToArray();
        Assert.Equal(4, won.Length);
        Assert.Equal(4, won.Distinct().Count());

        using var followUp = fixture.CreateClient();
        var empty = await followUp.PostAsJsonAsync("/claims/next", new { agent = "late", ttlSeconds = 30 });
        Assert.Equal(HttpStatusCode.NoContent, empty.StatusCode);

        var release = await followUp.PostAsJsonAsync($"/matters/{won[0]}/release", new { agent = "not-the-claimant" });
        Assert.Equal(HttpStatusCode.Conflict, release.StatusCode);

        var released = await followUp.PostAsJsonAsync($"/matters/{won[0]}/release", new { agent = (await Claimant(client, won[0])) });
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
    }

    [Fact]
    public async Task Auth_RejectsMissingAndWrongTokens_AndHealthStaysOpen()
    {
        await fixture.ResetAsync();
        using var anonymous = fixture.CreateClient(authenticate: false);
        var health = await anonymous.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var missing = await anonymous.GetAsync("/matters");
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        anonymous.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "wrong-token-value");
        var wrong = await anonymous.GetAsync("/matters");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        using var authed = fixture.CreateClient();
        var ok = await authed.GetAsync("/matters");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        using var spec = fixture.CreateClient(authenticate: false);
        var openapi = await spec.GetAsync("/openapi/v1.json");
        var document = await openapi.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, openapi.StatusCode);
        Assert.Contains("/matters", document, StringComparison.Ordinal);
        Assert.Contains("/claims/next", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Events_AreAppendOnlyInTheDatabase()
    {
        await fixture.ResetAsync();
        using var client = fixture.CreateClient();
        var created = await CreateMatter(client, "log", "alice");
        var id = created.GetProperty("id").GetGuid();
        await client.PostAsJsonAsync($"/matters/{id}/events", new { agent = "alice", kind = "note", summary = "keep" });

        await using var connection = new NpgsqlConnection(fixture.DatabaseConnection);
        await connection.OpenAsync();
        var failure = await Record.ExceptionAsync(async () =>
        {
            await using var command = new NpgsqlCommand("UPDATE events SET summary = 'changed';", connection);
            await command.ExecuteNonQueryAsync();
        });

        Assert.NotNull(failure);
        Assert.Contains("append-only", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgresUrl_BecomesAnNpgsqlConnectionString()
    {
        var converted = FiveHead.State.Api.ConnectionStrings.ToNpgsql("postgres://state:s%40cret@db.internal:5433/state");
        Assert.Contains("Host=db.internal", converted, StringComparison.Ordinal);
        Assert.Contains("Port=5433", converted, StringComparison.Ordinal);
        Assert.Contains("Password=s@cret", converted, StringComparison.Ordinal);
        Assert.Contains("Database=state", converted, StringComparison.Ordinal);
    }

    private static async Task<string> Claimant(HttpClient client, Guid id)
    {
        var body = await Read(await client.GetAsync($"/matters/{id}"));
        return body.GetProperty("claimedBy").GetString()!;
    }

    private static async Task<JsonElement> CreateMatter(HttpClient client, string title, string owner)
    {
        var response = await client.PostAsJsonAsync("/matters", new
        {
            title,
            owner,
            metadata = new { links = new[] { "https://example.test/matter" } },
            vectorRef = "qdrant://memories/example"
        }, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Read(response);
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        return document.RootElement.Clone();
    }
}
