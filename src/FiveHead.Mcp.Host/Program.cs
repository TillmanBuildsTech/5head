// 5head-mcp — durable curated memory for LLM coding assistants
// IMPORTANT: stdout is the MCP channel. ALL logging must go to stderr only.

using FiveHead.Mcp.Host;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose)
    .CreateLogger();

try
{
    await McpServer.RunAsync(args);
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Unhandled exception in 5head-mcp");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
