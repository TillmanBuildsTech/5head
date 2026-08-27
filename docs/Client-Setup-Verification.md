# Client Setup Verification

This note closes checklist item 9 by documenting real MCP client setups against the transport and packaging that this repo actually ships.

## What is repo-proven

- `5head-mcp` is a local stdio MCP server. The host wires `WithStdioServerTransport()` in `src/FiveHead.Mcp.Host/McpServer.cs`.
- The development entrypoint is proven by `tests/FiveHead.Mcp.Tests/StdioMcpIntegrationTests.cs`, which launches the server with `dotnet <assembly>` and exercises tools and resources end to end.
- Native AOT release artifacts are built in CI for `linux-x64`, `linux-arm64`, `win-x64`, `osx-arm64`, and `osx-x64` in `.github/workflows/ci.yml`.
- The server keeps stdout clean for MCP JSON-RPC and sends logs to stderr in `src/FiveHead.Mcp.Host/Program.cs`.

## What is not fully proven in this checkout

- The repo contains npm packaging metadata and a wrapper, but the wrapper path is not covered by the automated test suite.
- The npm installer now resolves the GitHub repository from package metadata and supports `FIVEHEAD_MCP_LOCAL_BINARY` and `FIVEHEAD_MCP_RELEASE_REPO`, but this repo still does not exercise `npx 5head-mcp` end to end in CI.
- The client configurations below are aligned to each client's published config format, but they are not exercised by this repo's automated test suite.

## Recommended launch paths

Use one of these command styles in client configs:

### 1. Published binary

Best fit for early adopters once release artifacts are attached.

```json
{
  "command": "/absolute/path/to/5head-mcp",
  "args": []
}
```

### 2. Local checkout with .NET

Best fit for local development and for the flow already covered by tests.

```json
{
  "command": "dotnet",
  "args": [
    "/absolute/path/to/src/FiveHead.Mcp.Host/bin/Debug/net9.0/5head-mcp.dll"
  ]
}
```

If you have not built yet, run `dotnet build` first.

## Real client setups

### Claude Code

Status: documented against current Claude Code MCP docs. Compatible with the repo's stdio transport. Not exercised in CI.

Project-scoped setup with a published binary:

```bash
claude mcp add --transport stdio --scope project memory -- /absolute/path/to/5head-mcp
```

Local checkout setup with the repo-proven dev flow:

```bash
claude mcp add --transport stdio --scope project memory -- \
  dotnet /absolute/path/to/src/FiveHead.Mcp.Host/bin/Debug/net9.0/5head-mcp.dll
```

Equivalent `.mcp.json` shape:

```json
{
  "mcpServers": {
    "memory": {
      "command": "/absolute/path/to/5head-mcp",
      "args": []
    }
  }
}
```

Caveats:

- Claude Code distinguishes local, project, and user scopes. For team sharing, use `--scope project` so a `.mcp.json` file is written.
- On Windows, Claude Code documents using `cmd /c` when the server command is `npx`. That caveat should not apply to the native `5head-mcp.exe` path.
- This repo currently exposes stdio only, so the Claude Code config must use stdio rather than HTTP or SSE.

### Claude Desktop

Status: documented against the Model Context Protocol quickstart for Claude Desktop. Compatible with the repo's stdio transport. Not exercised in CI.

Config file shape:

```json
{
  "mcpServers": {
    "memory": {
      "command": "/absolute/path/to/5head-mcp",
      "args": []
    }
  }
}
```

Dev checkout variant:

```json
{
  "mcpServers": {
    "memory": {
      "command": "dotnet",
      "args": [
        "/absolute/path/to/src/FiveHead.Mcp.Host/bin/Debug/net9.0/5head-mcp.dll"
      ]
    }
  }
}
```

Common config locations from the MCP quickstart:

- macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`
- Windows: `%APPDATA%\\Claude\\claude_desktop_config.json`

Caveats:

- Claude Desktop needs a full restart after config changes.
- Because `5head-mcp` uses the current working directory to locate repo memory, launch it from the target repo context when possible. A project-local client config is the least surprising option.
- The same stdout rule applies here: any wrapper script must not print to stdout before the server starts.

### VS Code

Status: documented against current VS Code MCP docs. Compatible with the repo's stdio transport. Not exercised in CI.

Workspace config in `.vscode/mcp.json`:

```json
{
  "servers": {
    "memory": {
      "command": "/absolute/path/to/5head-mcp",
      "args": []
    }
  }
}
```

Dev checkout variant:

```json
{
  "servers": {
    "memory": {
      "command": "dotnet",
      "args": [
        "/absolute/path/to/src/FiveHead.Mcp.Host/bin/Debug/net9.0/5head-mcp.dll"
      ]
    }
  }
}
```

Caveats:

- VS Code uses `servers`, not `mcpServers`, in `mcp.json`.
- Workspace config is the right default if you want the server to run in the same repo context as the code you are editing.
- VS Code may prompt for trust before starting a workspace MCP server.

## Reality check for packaging claims

Today the strongest supported claims are:

- local `dotnet` launch works and is covered by tests
- release binaries are built in CI and match the documented command shape
- stdio client configs for Claude Code, Claude Desktop, and VS Code are straightforward and consistent with each client's published format

Claims that should stay out of the README for now:

- `npx 5head-mcp` is verified end to end
- client configs are auto-installed
- HTTP or SSE transports are supported
