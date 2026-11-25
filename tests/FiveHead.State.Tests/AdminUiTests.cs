using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace FiveHead.State.Tests;

public sealed class AdminGateTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    [InlineData("off", false)]
    [InlineData("enabled", false)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData(" yes ", true)]
    [InlineData("on", true)]
    [InlineData("1", true)]
    public void IsEnabled_FailsClosed(string? raw, bool expected)
    {
        Assert.Equal(expected, FiveHead.State.Api.AdminGate.IsEnabled(raw));
    }

    [Fact]
    public void SessionCookie_IsAnHmacBoundToTheAdminTokenAndExpiry()
    {
        var now = DateTimeOffset.Parse("2026-11-25T12:00:00Z");
        var issued = FiveHead.State.Api.AdminSession.Issue("admin-token", now);

        Assert.True(FiveHead.State.Api.AdminSession.IsValid(issued, "admin-token", now.AddHours(11)));
        Assert.False(FiveHead.State.Api.AdminSession.IsValid(issued, "admin-token", now.AddHours(13)));
        Assert.False(FiveHead.State.Api.AdminSession.IsValid(issued, "test-token", now));
        Assert.False(FiveHead.State.Api.AdminSession.IsValid(issued + "aa", "admin-token", now));
        Assert.False(FiveHead.State.Api.AdminSession.IsValid("1.abcd", "admin-token", now));
        Assert.False(FiveHead.State.Api.AdminSession.IsValid(null, "admin-token", now));
    }
}

[Collection("state-api")]
public sealed class AdminUiTests(StateApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Disabled_AdminRoutesDoNotExist()
    {
        await fixture.ResetAsync();
        using var authed = fixture.CreateClient();
        using var anon = fixture.CreateClient(authenticate: false);
        foreach (var path in new[]
        {
            "/admin",
            "/admin/",
            "/admin/app.js",
            "/admin/app.css",
            "/admin/api/overview",
            "/admin/api/matters"
        })
        {
            var withToken = await authed.GetAsync(path);
            var withoutToken = await anon.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, withToken.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, withoutToken.StatusCode);
            Assert.Equal("not_found", (await Read(withToken)).GetProperty("error").GetString());
        }

        var session = await authed.PostAsJsonAsync("/admin/session", new { token = StateApiFixture.StateToken });
        Assert.Equal(HttpStatusCode.NotFound, session.StatusCode);

        var release = await authed.PostAsync($"/admin/api/matters/{Guid.NewGuid()}/force-release", content: null);
        Assert.Equal(HttpStatusCode.NotFound, release.StatusCode);

        var health = await anon.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Enabled_ShellIsPublic_AndApiRequiresTheAdminToken()
    {
        await fixture.ResetAsync();
        using var anon = fixture.CreateAdminClient();
        var page = await anon.GetAsync("/admin");
        var html = await page.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Admin console", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/admin/app.css\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/admin/app.js\"", html, StringComparison.Ordinal);
        Assert.Contains("memory files", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(StateApiFixture.AdminToken, html, StringComparison.Ordinal);
        Assert.True(page.Headers.Contains("Content-Security-Policy"));
        Assert.Contains("script-src 'self'", page.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);

        var slash = await anon.GetAsync("/admin/");
        Assert.Equal(HttpStatusCode.OK, slash.StatusCode);
        var script = await anon.GetAsync("/admin/app.js");
        var js = await script.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.Equal("text/javascript", script.Content.Headers.ContentType?.MediaType);
        Assert.Contains("/admin/api/overview", js, StringComparison.Ordinal);
        Assert.Contains("same-origin", js, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", js, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", js, StringComparison.Ordinal);
        var css = await anon.GetAsync("/admin/app.css");
        Assert.Equal(HttpStatusCode.OK, css.StatusCode);

        var overview = await anon.GetAsync("/admin/api/overview");
        Assert.Equal(HttpStatusCode.Unauthorized, overview.StatusCode);

        using var agent = fixture.CreateAdminClient(StateApiFixture.StateToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.GetAsync("/admin/api/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsJsonAsync("/admin/session", new { token = StateApiFixture.StateToken })).StatusCode);
        var matters = await agent.GetAsync("/matters");
        Assert.Equal(HttpStatusCode.OK, matters.StatusCode);

        using var wrongAdmin = fixture.CreateAdminClient("not-the-admin-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrongAdmin.GetAsync("/admin/api/overview")).StatusCode);

        using var admin = fixture.CreateAdminClient(StateApiFixture.AdminToken);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/admin/api/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/matters")).StatusCode);

        var forged = FiveHead.State.Api.AdminSession.Issue(StateApiFixture.StateToken, DateTimeOffset.UtcNow);
        using var forgedClient = fixture.CreateAdminClient(bearer: null, handleCookies: false);
        forgedClient.DefaultRequestHeaders.Add("Cookie", $"{FiveHead.State.Api.AdminSession.CookieName}={forged}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await forgedClient.GetAsync("/admin/api/overview")).StatusCode);

        using var cookieProbe = fixture.CreateAdminClient(bearer: null, handleCookies: false);
        var rejected = await cookieProbe.PostAsJsonAsync("/admin/session", new { token = "wrong-token-value" });
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.False(rejected.Headers.Contains("Set-Cookie"));

        var loginProbe = await cookieProbe.PostAsJsonAsync("/admin/session", new { token = StateApiFixture.AdminToken });
        Assert.Equal(HttpStatusCode.NoContent, loginProbe.StatusCode);
        Assert.True(loginProbe.Headers.TryGetValues("Set-Cookie", out var cookies));
        var setCookie = string.Join(";", cookies);
        Assert.Contains("fivehead_admin=", setCookie, StringComparison.Ordinal);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/admin", setCookie, StringComparison.OrdinalIgnoreCase);

        using var browser = fixture.CreateAdminClient();
        var login = await browser.PostAsJsonAsync("/admin/session", new { token = StateApiFixture.AdminToken });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var authed = await browser.GetAsync("/admin/api/overview");
        Assert.Equal(HttpStatusCode.OK, authed.StatusCode);
        Assert.False(browser.DefaultRequestHeaders.Contains("Authorization"));

        var bye = await browser.DeleteAsync("/admin/session");
        Assert.Equal(HttpStatusCode.NoContent, bye.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/admin/api/overview")).StatusCode);

        using var spec = fixture.CreateAdminClient();
        var document = await (await spec.GetAsync("/openapi/v1.json")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("/admin", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminUpdate_BypassesOwner_AuditsAsAdmin_AndReturns409()
    {
        await fixture.ResetAsync();
        using var agents = fixture.CreateAdminClient(StateApiFixture.StateToken);
        using var admin = fixture.CreateAdminClient(StateApiFixture.AdminToken);
        var created = await CreateMatter(agents, "Tunnel", "alice");
        var id = created.GetProperty("id").GetGuid();

        var forbidden = await agents.PatchAsJsonAsync($"/matters/{id}", new { version = 1, agent = "bob", status = "done" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(1, (await Read(forbidden)).GetProperty("matter").GetProperty("version").GetInt64());

        var saved = await admin.PatchAsJsonAsync($"/admin/api/matters/{id}", new
        {
            version = 1,
            agent = "bob",
            status = "done",
            owner = "ops",
            nextStep = "review"
        });
        var updated = await Read(saved);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("done", updated.GetProperty("status").GetString());
        Assert.Equal("ops", updated.GetProperty("owner").GetString());
        Assert.Equal("review", updated.GetProperty("nextStep").GetString());
        Assert.Equal(2, updated.GetProperty("version").GetInt64());

        var detail = await Read(await admin.GetAsync($"/admin/api/matters/{id}"));
        var audit = detail.GetProperty("events").EnumerateArray().Single();
        Assert.Equal("admin", audit.GetProperty("agent").GetString());
        Assert.Equal("admin.update", audit.GetProperty("kind").GetString());
        Assert.Equal("status open -> done; owner alice -> ops; next step (none) -> review", audit.GetProperty("summary").GetString());
        Assert.Equal("open", audit.GetProperty("payload").GetProperty("before").GetProperty("status").GetString());
        Assert.Equal("ops", audit.GetProperty("payload").GetProperty("after").GetProperty("owner").GetString());

        var conflict = await admin.PatchAsJsonAsync($"/admin/api/matters/{id}", new { version = 1, status = "open" });
        var body = await Read(conflict);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("version_conflict", body.GetProperty("error").GetString());
        Assert.Equal(2, body.GetProperty("matter").GetProperty("version").GetInt64());
        Assert.Equal("done", body.GetProperty("matter").GetProperty("status").GetString());

        var stillForbidden = await agents.PatchAsJsonAsync($"/matters/{id}", new { version = 2, agent = "bob", status = "open" });
        Assert.Equal(HttpStatusCode.Forbidden, stillForbidden.StatusCode);

        var missing = await admin.PatchAsJsonAsync($"/admin/api/matters/{Guid.NewGuid()}", new { version = 1, status = "done" });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var bad = await admin.PatchAsJsonAsync($"/admin/api/matters/{id}", new { agent = "admin" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task ForceRelease_ClearsAnotherAgentsClaim_AndAuditsIt()
    {
        await fixture.ResetAsync();
        using var agents = fixture.CreateAdminClient(StateApiFixture.StateToken);
        using var admin = fixture.CreateAdminClient(StateApiFixture.AdminToken);
        var created = await CreateMatter(agents, "Stuck job", "alice");
        var id = created.GetProperty("id").GetGuid();
        var idle = await CreateMatter(agents, "Unclaimed", "alice");
        var idleId = idle.GetProperty("id").GetGuid();

        var claim = await agents.PostAsJsonAsync("/claims/next", new { agent = "worker-1", ttlSeconds = 300 });
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        var claimedId = (await Read(claim)).GetProperty("id").GetGuid();
        var otherId = claimedId == id ? idleId : id;

        var blocked = await agents.PostAsJsonAsync($"/matters/{claimedId}/release", new { agent = "someone-else" });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        var released = await admin.PostAsync($"/admin/api/matters/{claimedId}/force-release", content: null);
        var after = await Read(released);
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        Assert.Equal(JsonValueKind.Null, after.GetProperty("claimedBy").ValueKind);
        Assert.Equal(3, after.GetProperty("version").GetInt64());

        var detail = await Read(await admin.GetAsync($"/admin/api/matters/{claimedId}"));
        var audit = detail.GetProperty("events").EnumerateArray().Single();
        Assert.Equal("admin", audit.GetProperty("agent").GetString());
        Assert.Equal("admin.force_release", audit.GetProperty("kind").GetString());
        Assert.Equal("Cleared claim held by worker-1.", audit.GetProperty("summary").GetString());
        Assert.Equal("worker-1", audit.GetProperty("payload").GetProperty("claimedBy").GetString());
        Assert.False(audit.GetProperty("payload").GetProperty("expired").GetBoolean());

        var again = await admin.PostAsync($"/admin/api/matters/{claimedId}/force-release", content: null);
        var againBody = await Read(again);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("not_claimed", againBody.GetProperty("error").GetString());
        Assert.Equal(3, againBody.GetProperty("matter").GetProperty("version").GetInt64());
        Assert.Single((await Read(await admin.GetAsync($"/admin/api/matters/{claimedId}"))).GetProperty("events").EnumerateArray());

        var idleRelease = await admin.PostAsync($"/admin/api/matters/{otherId}/force-release", content: null);
        Assert.Equal(HttpStatusCode.Conflict, idleRelease.StatusCode);
        Assert.Equal("not_claimed", (await Read(idleRelease)).GetProperty("error").GetString());
        Assert.Equal(1, (await Read(await agents.GetAsync($"/matters/{otherId}"))).GetProperty("version").GetInt64());

        var missing = await admin.PostAsync($"/admin/api/matters/{Guid.NewGuid()}/force-release", content: null);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using var anon = fixture.CreateAdminClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync($"/admin/api/matters/{claimedId}/force-release", content: null)).StatusCode);
    }

    [Fact]
    public async Task OverviewAndFilters_ReportSharedState()
    {
        await fixture.ResetAsync();
        using var agents = fixture.CreateAdminClient(StateApiFixture.StateToken);
        using var admin = fixture.CreateAdminClient(StateApiFixture.AdminToken);
        var first = await CreateMatter(agents, "Alpha", "alice");
        await CreateMatter(agents, "Beta", "bob");
        var done = await CreateMatter(agents, "Gamma", "alice");
        var doneId = done.GetProperty("id").GetGuid();
        await agents.PatchAsJsonAsync($"/matters/{doneId}", new { version = 1, agent = "alice", status = "done" });
        var claimed = await agents.PostAsJsonAsync("/claims/next", new { agent = "worker-1", ttlSeconds = 120 });
        Assert.NotEqual(doneId, (await Read(claimed)).GetProperty("id").GetGuid());

        await using var connection = new NpgsqlConnection(fixture.DatabaseConnection);
        await connection.OpenAsync();
        await using var stale = new NpgsqlCommand(
            "UPDATE matters SET claimed_by = 'ghost', claim_expires_at = now() - interval '5 minutes', version = version + 1 WHERE id = $1;",
            connection);
        stale.Parameters.AddWithValue(doneId);
        await stale.ExecuteNonQueryAsync();

        var overview = await Read(await admin.GetAsync("/admin/api/overview"));
        Assert.Equal(3, overview.GetProperty("total").GetInt32());
        Assert.Equal(1, overview.GetProperty("activeClaims").GetInt32());
        Assert.Equal(1, overview.GetProperty("staleClaims").GetInt32());
        var counts = overview.GetProperty("byStatus").EnumerateArray().ToDictionary(
            row => row.GetProperty("status").GetString()!,
            row => row.GetProperty("count").GetInt32());
        Assert.Equal(2, counts["open"]);
        Assert.Equal(1, counts["done"]);
        Assert.Equal(2, overview.GetProperty("claims").GetArrayLength());
        Assert.Contains(overview.GetProperty("claims").EnumerateArray(), row => row.GetProperty("active").GetBoolean());
        Assert.Contains(overview.GetProperty("claims").EnumerateArray(), row => !row.GetProperty("active").GetBoolean() && row.GetProperty("claimedBy").GetString() == "ghost");

        var openAlice = await Read(await admin.GetAsync("/admin/api/matters?status=open&owner=alice"));
        var matches = openAlice.EnumerateArray().ToArray();
        Assert.Single(matches);
        Assert.Equal("Alpha", matches[0].GetProperty("title").GetString());

        var none = await Read(await admin.GetAsync("/admin/api/matters?status=blocked"));
        Assert.Empty(none.EnumerateArray());
    }

    [Fact]
    public async Task EnabledWithoutADistinctToken_ReusesTheStateBearer_AndAShortTokenRefusesToBoot()
    {
        await fixture.ResetAsync();
        await using var shared = fixture.CreateFactory(adminEnabled: true, adminToken: null);
        using var client = shared.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", StateApiFixture.StateToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/api/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/matters")).StatusCode);

        await using var off = fixture.CreateFactory(adminEnabled: false, adminToken: "short");
        using var offClient = off.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await offClient.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await offClient.GetAsync("/admin")).StatusCode);

        var failure = await Record.ExceptionAsync(async () =>
        {
            await using var broken = fixture.CreateFactory(adminEnabled: true, adminToken: "short");
            using var brokenClient = broken.CreateClient();
            await brokenClient.GetAsync("/health");
        });
        Assert.NotNull(failure);
        Assert.Contains("FIVEHEAD_ADMIN_TOKEN", Flatten(failure), StringComparison.Ordinal);
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        void Walk(Exception current)
        {
            messages.Add(current.Message);
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    Walk(inner);
                }
            }
            else if (current.InnerException is not null)
            {
                Walk(current.InnerException);
            }
        }

        Walk(exception);
        return string.Join(" ", messages);
    }

    private static async Task<JsonElement> CreateMatter(HttpClient client, string title, string owner)
    {
        var response = await client.PostAsJsonAsync("/matters", new
        {
            title,
            owner,
            metadata = new { links = new[] { "https://example.test/matter" } }
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
