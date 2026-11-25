using System.Text.Json;
using FiveHead.State.Api;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var databaseUrl = builder.Configuration["STATE_DATABASE_URL"];
var authToken = builder.Configuration["STATE_AUTH_TOKEN"];
if (string.IsNullOrWhiteSpace(databaseUrl))
{
    throw new InvalidOperationException("STATE_DATABASE_URL is required.");
}

if (string.IsNullOrWhiteSpace(authToken) || authToken.Length < 8)
{
    throw new InvalidOperationException("STATE_AUTH_TOKEN is required and must be at least 8 characters.");
}

var claimTtl = int.TryParse(builder.Configuration["STATE_CLAIM_TTL_SECONDS"], out var configuredTtl)
    ? Math.Clamp(configuredTtl, 1, 86_400)
    : 300;

builder.Services.AddOpenApi();
builder.Services.AddSingleton(NpgsqlDataSource.Create(ConnectionStrings.ToNpgsql(databaseUrl)));
builder.Services.AddSingleton<MatterRepository>();

var app = builder.Build();
await MigrationRunner.ApplyAsync(app.Services.GetRequiredService<NpgsqlDataSource>());

app.MapOpenApi();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/health")
        || context.Request.Path.StartsWithSegments("/openapi"))
    {
        await next();
        return;
    }

    var header = context.Request.Headers.Authorization.ToString();
    var presented = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? header["Bearer ".Length..].Trim()
        : string.Empty;
    if (!BearerToken.Matches(presented, authToken))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "unauthorized", message = "Bearer token was missing or rejected." });
        return;
    }

    await next();
});

app.MapGet("/health", async (MatterRepository store, CancellationToken ct) =>
{
    var ok = await store.PingAsync(ct);
    return ok
        ? Results.Ok(new { status = "ok" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
})
.WithName("health")
.WithTags("ops")
.WithSummary("Liveness and database ping. Unauthenticated.");

app.MapGet("/matters", async (string? status, string? owner, int? limit, MatterRepository store, CancellationToken ct) =>
{
    var rows = await store.ListAsync(status, owner, Math.Clamp(limit ?? 100, 1, 200), ct);
    return Results.Ok(rows.Select(row => row.ToResponse()));
})
.WithName("list_matters")
.WithTags("matters");

app.MapPost("/matters", async (HttpRequest request, MatterRepository store, CancellationToken ct) =>
{
    var body = await ReadBodyAsync(request, ct);
    if (body is null)
    {
        return BadJson();
    }

    if (!TryRequiredString(body.Value, "title", out var title)
        || !TryRequiredString(body.Value, "owner", out var owner))
    {
        return Results.BadRequest(new { error = "bad_request", message = "title and owner are required." });
    }

    var status = TryOptionalString(body.Value, "status", out var parsedStatus) && !string.IsNullOrWhiteSpace(parsedStatus)
        ? parsedStatus!
        : "open";
    TryOptionalString(body.Value, "nextStep", out var nextStep);
    var metadata = TryMetadata(body.Value, "metadata", out var metadataJson, out var metadataError);
    if (!metadata)
    {
        return Results.BadRequest(new { error = "bad_request", message = metadataError });
    }

    TryOptionalString(body.Value, "vectorRef", out var vectorRef);
    var matter = await store.CreateAsync(Guid.NewGuid(), title!, owner!, status, EmptyToNull(nextStep), metadataJson ?? "{}", EmptyToNull(vectorRef), ct);
    return Results.Created($"/matters/{matter.Id}", matter.ToResponse());
})
.WithName("create_matter")
.WithTags("matters");

app.MapGet("/matters/{id:guid}", async (Guid id, MatterRepository store, CancellationToken ct) =>
{
    var matter = await store.GetAsync(id, ct);
    if (matter is null)
    {
        return Results.NotFound(new { error = "not_found", message = $"Matter {id} does not exist." });
    }

    var events = await store.ListEventsAsync(id, ct);
    return Results.Ok(matter.ToResponse(events));
})
.WithName("get_matter")
.WithTags("matters");

app.MapPatch("/matters/{id:guid}", async (Guid id, HttpRequest request, MatterRepository store, CancellationToken ct) =>
{
    var body = await ReadBodyAsync(request, ct);
    if (body is null)
    {
        return BadJson();
    }

    if (!body.Value.TryGetProperty("version", out var versionElement) || !versionElement.TryGetInt64(out var version))
    {
        return Results.BadRequest(new { error = "bad_request", message = "version is required." });
    }

    if (!TryRequiredString(body.Value, "agent", out var agent))
    {
        return Results.BadRequest(new { error = "bad_request", message = "agent is required." });
    }

    var hasTitle = body.Value.TryGetProperty("title", out var titleElement);
    var hasStatus = body.Value.TryGetProperty("status", out var statusElement);
    var hasOwner = body.Value.TryGetProperty("owner", out var ownerElement);
    var hasNext = body.Value.TryGetProperty("nextStep", out var nextElement);
    var hasVector = body.Value.TryGetProperty("vectorRef", out var vectorElement);
    var hasMetadata = body.Value.TryGetProperty("metadata", out _);
    string? metadataJson = null;
    if (hasMetadata && !TryMetadata(body.Value, "metadata", out metadataJson, out var metadataError))
    {
        return Results.BadRequest(new { error = "bad_request", message = metadataError });
    }

    if (hasTitle && string.IsNullOrWhiteSpace(Text(titleElement)))
    {
        return Results.BadRequest(new { error = "bad_request", message = "title cannot be empty." });
    }

    if (hasStatus && string.IsNullOrWhiteSpace(Text(statusElement)))
    {
        return Results.BadRequest(new { error = "bad_request", message = "status cannot be empty." });
    }

    if (hasOwner && string.IsNullOrWhiteSpace(Text(ownerElement)))
    {
        return Results.BadRequest(new { error = "bad_request", message = "owner cannot be empty." });
    }

    var result = await store.UpdateAsync(id, new MatterPatch(
        version,
        agent!,
        hasTitle,
        Text(titleElement),
        hasStatus,
        Text(statusElement),
        hasOwner,
        Text(ownerElement),
        hasNext,
        EmptyToNull(Text(nextElement)),
        hasMetadata,
        metadataJson,
        hasVector,
        EmptyToNull(Text(vectorElement))), ct);
    return ToResult(result);
})
.WithName("update_matter")
.WithTags("matters")
.WithSummary("Version-checked update. Only the owner may change status or owner. Conflict is HTTP 409 with the current row.");

app.MapPost("/matters/{id:guid}/events", async (Guid id, HttpRequest request, MatterRepository store, CancellationToken ct) =>
{
    var body = await ReadBodyAsync(request, ct);
    if (body is null)
    {
        return BadJson();
    }

    if (!TryRequiredString(body.Value, "agent", out var agent)
        || !TryRequiredString(body.Value, "kind", out var kind)
        || !TryRequiredString(body.Value, "summary", out var summary))
    {
        return Results.BadRequest(new { error = "bad_request", message = "agent, kind, and summary are required." });
    }

    var payload = "{}";
    if (body.Value.TryGetProperty("payload", out _))
    {
        if (!TryMetadata(body.Value, "payload", out var parsed, out var error))
        {
            return Results.BadRequest(new { error = "bad_request", message = error });
        }

        payload = parsed ?? "{}";
    }

    var created = await store.AppendEventAsync(id, agent!, kind!, summary!, payload, ct);
    return created is null
        ? Results.NotFound(new { error = "not_found", message = $"Matter {id} does not exist." })
        : Results.Created($"/matters/{id}/events/{created.Id}", created.ToResponse());
})
.WithName("append_event")
.WithTags("events")
.WithSummary("Append-only. Any authenticated agent may append. Does not change the matter version.");

app.MapPost("/claims/next", async (HttpRequest request, MatterRepository store, CancellationToken ct) =>
{
    var body = await ReadBodyAsync(request, ct);
    if (body is null)
    {
        return BadJson();
    }

    if (!TryRequiredString(body.Value, "agent", out var agent))
    {
        return Results.BadRequest(new { error = "bad_request", message = "agent is required." });
    }

    var ttl = claimTtl;
    if (body.Value.TryGetProperty("ttlSeconds", out var ttlElement) && ttlElement.TryGetInt32(out var requested))
    {
        ttl = Math.Clamp(requested, 1, 86_400);
    }

    var claimed = await store.ClaimNextAsync(agent!, ttl, ct);
    return claimed is null ? Results.NoContent() : Results.Ok(claimed.ToResponse());
})
.WithName("claim_next")
.WithTags("claims")
.WithSummary("Claims the next available matter with FOR UPDATE SKIP LOCKED. Expired claims are available again.");

app.MapPost("/matters/{id:guid}/release", async (Guid id, HttpRequest request, MatterRepository store, CancellationToken ct) =>
{
    var body = await ReadBodyAsync(request, ct);
    if (body is null)
    {
        return BadJson();
    }

    if (!TryRequiredString(body.Value, "agent", out var agent))
    {
        return Results.BadRequest(new { error = "bad_request", message = "agent is required." });
    }

    return ToResult(await store.ReleaseAsync(id, agent!, ct));
})
.WithName("release_claim")
.WithTags("claims");

app.Run();

static IResult ToResult(MutationResult result) => result.Kind switch
{
    MutationKind.Ok => Results.Ok(result.Matter!.ToResponse()),
    MutationKind.NotFound => Results.NotFound(new { error = "not_found", message = result.Message }),
    MutationKind.Conflict => Results.Json(
        new { error = string.IsNullOrEmpty(result.Code) ? "conflict" : result.Code, message = result.Message, matter = result.Matter!.ToResponse() },
        statusCode: StatusCodes.Status409Conflict),
    MutationKind.Forbidden => Results.Json(
        new { error = "forbidden", message = result.Message, matter = result.Matter!.ToResponse() },
        statusCode: StatusCodes.Status403Forbidden),
    _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
};

static IResult BadJson() =>
    Results.BadRequest(new { error = "bad_request", message = "Body must be a JSON object." });

static async Task<JsonElement?> ReadBodyAsync(HttpRequest request, CancellationToken ct)
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

static bool TryRequiredString(JsonElement body, string name, out string? value)
{
    value = null;
    if (!body.TryGetProperty(name, out var element))
    {
        return false;
    }

    value = Text(element);
    return !string.IsNullOrWhiteSpace(value);
}

static bool TryOptionalString(JsonElement body, string name, out string? value)
{
    value = null;
    if (!body.TryGetProperty(name, out var element))
    {
        return false;
    }

    value = Text(element);
    return true;
}

static bool TryMetadata(JsonElement body, string name, out string? json, out string? error)
{
    json = null;
    error = null;
    if (!body.TryGetProperty(name, out var element) || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
    {
        json = "{}";
        return true;
    }

    if (element.ValueKind != JsonValueKind.Object)
    {
        error = $"{name} must be a JSON object.";
        return false;
    }

    json = element.GetRawText();
    return true;
}

static string? Text(JsonElement element) =>
    element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => element.ToString()
    };

static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

public partial class Program;
