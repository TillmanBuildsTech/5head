using System.Net;
using System.Net.Http.Headers;
using FiveHead.State.Mcp;

namespace FiveHead.State.Tests;

public sealed class StateApiClientTests
{
    [Fact]
    public async Task UpdateMatter_SendsTheBearerToken_AndSurfaces409()
    {
        var handler = new StubHandler
        {
            Status = HttpStatusCode.Conflict,
            Body = """{"error":"version_conflict","matter":{"version":4}}"""
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://state.local/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        var tools = new StateMcpTools(new StateApiClient(http, "hermes"));

        var ex = await Assert.ThrowsAsync<StateApiException>(() =>
            tools.update_matter("matter-1", 3, status: "done"));

        Assert.Equal(HttpStatusCode.Conflict, ex.Status);
        Assert.Contains("\"version\":4", ex.Body, StringComparison.Ordinal);
        Assert.Equal("Bearer", handler.Scheme);
        Assert.Equal("test-token", handler.Parameter);
        Assert.Contains("/matters/matter-1", handler.Path, StringComparison.Ordinal);
        Assert.Contains("\"version\":3", handler.Content, StringComparison.Ordinal);
        Assert.Contains("\"agent\":\"hermes\"", handler.Content, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"done\"", handler.Content, StringComparison.Ordinal);
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
            Path = request.RequestUri?.PathAndQuery ?? "";
            Content = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(Body)
            };
        }
    }
}
