using System.Text.Json;

namespace FiveHead.State.Api;

public static class AdminEndpoints
{
    public static void Map(WebApplication app, string adminToken)
    {
        // Touch the embedded files at startup so a missing asset fails boot.
        _ = AdminAssets.Html;

        var admin = app.MapGroup("/admin").ExcludeFromDescription();

        admin.MapGet("", () => Page());
        admin.MapGet("/app.css", () => Results.Content(AdminAssets.Css, "text/css; charset=utf-8"));
        admin.MapGet("/app.js", () => Results.Content(AdminAssets.Script, "text/javascript; charset=utf-8"));

        admin.MapPost("/session", async (HttpContext context, CancellationToken ct) =>
        {
            var body = await ReadBodyAsync(context.Request, ct);
            if (body is null)
            {
                return ApiResults.BadJson();
            }

            if (!TryRequiredString(body.Value, "token", out var token) || !BearerToken.Matches(token, adminToken))
            {
                return ApiResults.Unauthorized();
            }

            AdminSession.Set(context, adminToken);
            return Results.NoContent();
        });

        admin.MapDelete("/session", (HttpContext context) =>
        {
            AdminSession.Clear(context);
            return Results.NoContent();
        });

        admin.MapGet("/api/overview", async (MatterRepository store, CancellationToken ct) =>
        {
            var overview = await store.GetOverviewAsync(ct);
            return Results.Ok(new
            {
                total = overview.Total,
                byStatus = overview.ByStatus.Select(row => new { status = row.Status, count = row.Count }),
                activeClaims = overview.ActiveClaims,
                staleClaims = overview.StaleClaims,
                claims = overview.Claims.Select(row => new
                {
                    id = row.Id,
                    title = row.Title,
                    owner = row.Owner,
                    status = row.Status,
                    claimedBy = row.ClaimedBy,
                    claimExpiresAt = row.ClaimExpiresAt,
                    active = row.Active,
                    version = row.Version
                })
            });
        });

        admin.MapGet("/api/matters", async (string? status, string? owner, int? limit, MatterRepository store, CancellationToken ct) =>
        {
            var rows = await store.ListAsync(status, owner, Math.Clamp(limit ?? 100, 1, 200), ct);
            return Results.Ok(rows.Select(row => row.ToResponse()));
        });

        admin.MapGet("/api/matters/{id:guid}", async (Guid id, MatterRepository store, CancellationToken ct) =>
        {
            var matter = await store.GetAsync(id, ct);
            if (matter is null)
            {
                return Results.NotFound(new { error = "not_found", message = $"Matter {id} does not exist." });
            }

            var events = await store.ListEventsAsync(id, ct);
            return Results.Ok(matter.ToResponse(events));
        });

        admin.MapPatch("/api/matters/{id:guid}", async (Guid id, HttpRequest request, MatterRepository store, CancellationToken ct) =>
        {
            var body = await ReadBodyAsync(request, ct);
            if (body is null)
            {
                return ApiResults.BadJson();
            }

            if (!body.Value.TryGetProperty("version", out var versionElement) || !versionElement.TryGetInt64(out var version))
            {
                return Results.BadRequest(new { error = "bad_request", message = "version is required." });
            }

            var hasStatus = body.Value.TryGetProperty("status", out var statusElement);
            var hasOwner = body.Value.TryGetProperty("owner", out var ownerElement);
            var hasNext = body.Value.TryGetProperty("nextStep", out var nextElement);
            if (!hasStatus && !hasOwner && !hasNext)
            {
                return Results.BadRequest(new { error = "bad_request", message = "status, owner, or nextStep is required." });
            }

            if (hasStatus && string.IsNullOrWhiteSpace(Text(statusElement)))
            {
                return Results.BadRequest(new { error = "bad_request", message = "status cannot be empty." });
            }

            if (hasOwner && string.IsNullOrWhiteSpace(Text(ownerElement)))
            {
                return Results.BadRequest(new { error = "bad_request", message = "owner cannot be empty." });
            }

            return ApiResults.From(await store.AdminUpdateAsync(
                id,
                version,
                hasStatus,
                Text(statusElement),
                hasOwner,
                Text(ownerElement),
                hasNext,
                EmptyToNull(Text(nextElement)),
                ct));
        });

        admin.MapPost("/api/matters/{id:guid}/force-release", async (Guid id, MatterRepository store, CancellationToken ct) =>
            ApiResults.From(await store.ForceReleaseAsync(id, ct)));
    }

    private static IResult Page() => Results.Content(AdminAssets.Html, "text/html; charset=utf-8");

    private static async Task<JsonElement?> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(request.Body, cancellationToken: ct);
            return body.ValueKind == JsonValueKind.Object ? body : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryRequiredString(JsonElement body, string name, out string? value)
    {
        value = null;
        if (!body.TryGetProperty(name, out var element))
        {
            return false;
        }

        value = Text(element);
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string? Text(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.ToString()
        };

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
