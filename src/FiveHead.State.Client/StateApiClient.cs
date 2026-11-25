using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace FiveHead.State.Client;

/// <summary>
/// Typed HTTP client for the shared-state API. JSON uses the source-generated
/// <see cref="StateJsonContext"/> so the Native AOT host can trim reflection.
/// </summary>
public sealed class StateApiClient
{
    private readonly HttpClient _http;
    private readonly string _agent;

    public StateApiClient(HttpClient http, string agent)
    {
        _http = http;
        _agent = string.IsNullOrWhiteSpace(agent) ? "5head" : agent;
    }

    public string Agent => _agent;

    public static HttpClient CreateHttpClient(string baseUrl, string token)
    {
        var root = baseUrl.Trim();
        if (!root.EndsWith('/'))
        {
            root += "/";
        }

        var http = new HttpClient
        {
            BaseAddress = new Uri(root, UriKind.Absolute)
        };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

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

        var path = "matters" + (query.Count == 0 ? "" : "?" + string.Join('&', query));
        return SendEmptyAsync(HttpMethod.Get, path, ct);
    }

    public Task<string> GetMatterAsync(string id, CancellationToken ct) =>
        SendEmptyAsync(HttpMethod.Get, "matters/" + Uri.EscapeDataString(id), ct);

    public Task<string> CreateMatterAsync(
        string title,
        string owner,
        string? status,
        string? nextStep,
        string? metadataJson,
        string? vectorRef,
        CancellationToken ct)
    {
        var body = new CreateMatterRequest
        {
            Title = title,
            Owner = owner,
            Status = BlankToNull(status),
            NextStep = BlankToNull(nextStep),
            VectorRef = BlankToNull(vectorRef),
            Metadata = ParseObject(metadataJson, "metadataJson")
        };
        return SendAsync(HttpMethod.Post, "matters", body, StateJsonContext.Default.CreateMatterRequest, ct);
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
        var body = new UpdateMatterRequest
        {
            Version = version,
            Agent = PickAgent(agent),
            Title = BlankToNull(title),
            Status = BlankToNull(status),
            Owner = BlankToNull(owner),
            NextStep = BlankToNull(nextStep),
            VectorRef = BlankToNull(vectorRef),
            Metadata = ParseObject(metadataJson, "metadataJson")
        };
        return SendAsync(
            HttpMethod.Patch,
            "matters/" + Uri.EscapeDataString(id),
            body,
            StateJsonContext.Default.UpdateMatterRequest,
            ct);
    }

    public Task<string> AppendEventAsync(string id, string kind, string summary, string? payloadJson, string? agent, CancellationToken ct)
    {
        var body = new AppendEventRequest
        {
            Agent = PickAgent(agent),
            Kind = kind,
            Summary = summary,
            Payload = ParseObject(payloadJson, "payloadJson")
        };
        return SendAsync(
            HttpMethod.Post,
            "matters/" + Uri.EscapeDataString(id) + "/events",
            body,
            StateJsonContext.Default.AppendEventRequest,
            ct);
    }

    public async Task<string> ClaimNextAsync(int ttlSeconds, string? agent, CancellationToken ct)
    {
        var body = new ClaimNextRequest
        {
            Agent = PickAgent(agent),
            TtlSeconds = ttlSeconds
        };
        var response = await SendRawAsync(HttpMethod.Post, "claims/next", body, StateJsonContext.Default.ClaimNextRequest, ct);
        if (response.Status == HttpStatusCode.NoContent)
        {
            return """{"claimed":false}""";
        }

        EnsureSuccess(response);
        return response.Body;
    }

    public Task<string> ReleaseClaimAsync(string id, string? agent, CancellationToken ct)
    {
        var body = new ReleaseClaimRequest { Agent = PickAgent(agent) };
        return SendAsync(
            HttpMethod.Post,
            "matters/" + Uri.EscapeDataString(id) + "/release",
            body,
            StateJsonContext.Default.ReleaseClaimRequest,
            ct);
    }

    private Task<string> SendEmptyAsync(HttpMethod method, string path, CancellationToken ct) =>
        SendAsync<object>(method, path, null, null, ct);

    private async Task<string> SendAsync<T>(HttpMethod method, string path, T? body, JsonTypeInfo<T>? typeInfo, CancellationToken ct)
    {
        var response = await SendRawAsync(method, path, body, typeInfo, ct);
        EnsureSuccess(response);
        return response.Body;
    }

    private async Task<(HttpStatusCode Status, string Body)> SendRawAsync<T>(
        HttpMethod method,
        string path,
        T? body,
        JsonTypeInfo<T>? typeInfo,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null && typeInfo is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, typeInfo), Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        return (response.StatusCode, text);
    }

    private static void EnsureSuccess((HttpStatusCode Status, string Body) response)
    {
        if (response.Status == HttpStatusCode.Conflict)
        {
            throw StateConflictException.FromBody(response.Body);
        }

        if ((int)response.Status is < 200 or >= 300)
        {
            throw new StateApiException(
                response.Status,
                response.Body,
                $"State API returned {(int)response.Status}: {response.Body}");
        }
    }

    private string PickAgent(string? agent) => string.IsNullOrWhiteSpace(agent) ? _agent : agent.Trim();

    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static JsonElement? ParseObject(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        JsonElement element;
        try
        {
            element = JsonSerializer.Deserialize(json, StateJsonContext.Default.JsonElement);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"{name} must be a JSON object.", name, ex);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"{name} must be a JSON object.", name);
        }

        return element;
    }
}
