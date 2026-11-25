using FiveHead.State.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var apiUrl = Environment.GetEnvironmentVariable("STATE_API_URL");
var token = Environment.GetEnvironmentVariable("STATE_AUTH_TOKEN");
var agent = Environment.GetEnvironmentVariable("STATE_AGENT_NAME");
if (string.IsNullOrWhiteSpace(agent))
{
    agent = "state-mcp";
}

if (string.IsNullOrWhiteSpace(apiUrl) || string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("STATE_API_URL and STATE_AUTH_TOKEN are required.");
    return 1;
}

void AddClient(IServiceCollection services)
{
    services.AddSingleton(StateApiClient.CreateHttp(apiUrl, token));
    services.AddSingleton(sp => new StateApiClient(sp.GetRequiredService<HttpClient>(), agent));
}

var transport = Environment.GetEnvironmentVariable("STATE_MCP_TRANSPORT");
if (string.Equals(transport, "http", StringComparison.OrdinalIgnoreCase))
{
    var web = WebApplication.CreateBuilder(args);
    web.Logging.ClearProviders();
    web.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    AddClient(web.Services);
    web.Services.AddMcpServer().WithHttpTransport().WithTools<StateMcpTools>();
    var app = web.Build();
    app.MapMcp();
    await app.RunAsync();
    return 0;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
AddClient(builder.Services);
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<StateMcpTools>();
await builder.Build().RunAsync();
return 0;
