using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FiveHead.State.Mcp;

public sealed class StateApiException(HttpStatusCode status, string body)
    : Exception($"State API returned {(int)status}: {body}")
{
    public HttpStatusCode Status { get; } = status;
    public string Body { get; } = body;
}

/// <summary>Thin HTTP client. The API is the source of truth; this process does not touch Postgres.</summary>
public sealed class StateApiClient(HttpClient http, string agent)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Agent { get; } = agent;

    public Task<string> ListMattersAsync(string? status, string? owner, CancellationToken ct)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query.Add("status=" + Uri.EscapeDataString(status));
        }

        if (!string.IsNullOrWhiteSpace(owner))
        {
            query.Add("owner=" + Uri.EscapeDataString(owner));
        }

        var path = "/matters" + (query.Count == 0 ? "" : "?" + string.Join('&', query));
        return SendAsync(HttpMethod.Get, path, null, ct);
    }

    public Task<string> GetMatterAsync(string id, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, $"/matters/{Uri.EscapeDataString(id)}", null, ct);

    public Task<string> CreateMatterAsync(string title, string owner, string? status, string? nextStep, string? metadataJson, string? vectorRef, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["owner"] = owner
        };
        AddIfPresent(body, "status", status);
        AddIfPresent(body, "nextStep", nextStep);
        AddIfPresent(body, "vectorRef", vectorRef);
        if (!string.IsNullOrWhiteSpace(metadataJson))
        {
            body["metadata"] = JsonSerializer.Deserialize<JsonElement>(metadataJson);
        }

        return SendAsync(HttpMethod.Post, "/matters", body, ct);
    }

    public Task<string> UpdateMatterAsync(
        string id,
        long version,
        string? title,
        string? status,
        string? owner,
        string? nextStep,
        string? vectorRef,
        string? metadataJson,
        string? agent,
        CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["version"] = version,
            ["agent"] = string.IsNullOrWhiteSpace(agent) ? Agent : agent
        };
        AddIfPresent(body, "title", title);
        AddIfPresent(body, "status", status);
        AddIfPresent(body, "owner", owner);
        AddIfPresent(body, "nextStep", nextStep);
        AddIfPresent(body, "vectorRef", vectorRef);
        if (!string.IsNullOrWhiteSpace(metadataJson))
        {
            body["metadata"] = JsonSerializer.Deserialize<JsonElement>(metadataJson);
        }

        return SendAsync(HttpMethod.Patch, $"/matters/{Uri.EscapeDataString(id)}", body, ct);
    }

    public Task<string> AppendEventAsync(string id, string kind, string summary, string? payloadJson, string? agent, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["agent"] = string.IsNullOrWhiteSpace(agent) ? Agent : agent,
            ["kind"] = kind,
            ["summary"] = summary
        };
        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            body["payload"] = JsonSerializer.Deserialize<JsonElement>(payloadJson);
        }

        return SendAsync(HttpMethod.Post, $"/matters/{Uri.EscapeDataString(id)}/events", body, ct);
    }

    public async Task<string> ClaimNextAsync(int ttlSeconds, string? agent, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["agent"] = string.IsNullOrWhiteSpace(agent) ? Agent : agent,
            ["ttlSeconds"] = ttlSeconds
        };
        var response = await SendRawAsync(HttpMethod.Post, "/claims/next", body, ct);
        if (response.Status == HttpStatusCode.NoContent)
        {
            return """{"claimed":false}""";
        }

        return response.Body;
    }

    public Task<string> ReleaseClaimAsync(string id, string? agent, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["agent"] = string.IsNullOrWhiteSpace(agent) ? Agent : agent
        };
        return SendAsync(HttpMethod.Post, $"/matters/{Uri.EscapeDataString(id)}/release", body, ct);
    }

    private async Task<string> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var response = await SendRawAsync(method, path, body, ct);
        return response.Body;
    }

    private async Task<(HttpStatusCode Status, string Body)> SendRawAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        }

        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return (response.StatusCode, text);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new StateApiException(response.StatusCode, text);
        }

        return (response.StatusCode, text);
    }

    private static void AddIfPresent(Dictionary<string, object?> body, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            body[name] = value;
        }
    }

    public static HttpClient CreateHttp(string baseUrl, string token)
    {
        var http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/")
        };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }
}
