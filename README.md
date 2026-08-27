# 5head-mcp

> Durable, curated, provenance-backed memory for LLM coding assistants.
> Plugs into Claude Code, GitHub Copilot CLI, Cursor, VS Code — any MCP-compatible client.

## Why another memory MCP?

- **Curated markdown, not event logs.** Three files (`SOUL.md` / `MEMORY.md` / `USER.md`) with enforced character limits force the agent to distill signal from noise instead of growing an unbounded dump.
- **Per-repo and git-versioned.** Memory lives in `.agent-memory/` alongside your code, travels with branches, is reviewable in PRs, and survives machine wipes — no cloud account required.
- **Provenance with confirm/retract.** Every write is tracked in SQLite with timestamps and source attribution. Conflicting facts are rejected until the old one is explicitly retracted; operators can confirm or retract facts at any time.

---

## Quick start

Use one of the entrypoints this repo actually ships today:

```sh
# dev flow from a local checkout
dotnet run --project src/FiveHead.Mcp.Host
```

Then add to your MCP client config:

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

`5head-mcp` speaks MCP over stdio only. Keep stdout reserved for MCP traffic.

Client-specific examples and verification notes live in `docs/Client-Setup-Verification.md`.

---

## Memory files

| File | Purpose | Limit |
|------|---------|-------|
| `SOUL.md` | Stable agent identity and principles | 1,500 chars |
| `MEMORY.md` | Learned project facts and conventions | 2,200 chars |
| `USER.md` | Operator profile summary | 1,375 chars |

Files live at `.agent-memory/` in your repo root and are committed to git.
Provenance DB lives at `~/.agent-memory/cache/{repo-hash}/provenance.db` (local, not shared).

Optional V1 config lives at `.agent-memory/config.yaml` and supports only these fields:

```yaml
stalenessThresholdDays: 30
provenanceDbPath: .cache/provenance.db
```

`provenanceDbPath` may be absolute or relative to `.agent-memory/`.

If 5head cannot discover a repo-local memory root, it falls back to `~/.agent-memory/global/`.
That global path is an implementation fallback, not the primary V1 product surface.

---

## Tools

| Tool | Description |
|------|-------------|
| `read_memory` | Read SOUL, MEMORY, or USER file |
| `write_memory` | Append, replace, or remove content (`replace` is the V1 manual compaction path; checked against size limit + contradictions) |
| `check_contradictions` | Dry-run conflict detection before a write |
| `list_facts` | List active/confirmed/uncertain/retracted facts |
| `confirm_fact` | Explicitly confirm a provenance row |
| `retract_fact` | Explicitly retract a provenance row |
| `get_provenance` | Full history for a subject+predicate pair |

For V1, compaction is intentionally narrow: use an explicit `replace` write when a memory file needs cleanup or consolidation. 5head records the replacement in provenance and retracts facts from the prior file state when they no longer appear. It does not run automated compaction or background cleanup.

---

## Resources

| URI | Content |
|-----|---------|
| `memory://soul` | SOUL.md content |
| `memory://memory` | MEMORY.md content |
| `memory://user` | USER.md content |

---

## Architecture

```
MCP Client (Claude Code / Copilot / Cursor)
    │ MCP JSON-RPC / stdio
    ▼
5head-mcp (.NET 9, Native AOT)
    ├── Tools Layer
    ├── Resources Layer
    ├── Contradiction Checker
    ├── Size Limit Guard
    ├── Fact Extractor
    └── Storage Layer
            ├── .agent-memory/{SOUL,MEMORY,USER}.md  (git-tracked)
            └── ~/.agent-memory/cache/{hash}/provenance.db  (SQLite WAL)
```

---

## Distribution

Pre-built binaries are attached to every [GitHub Release](../../releases):

| Platform | Binary |
|----------|--------|
| macOS arm64 | `5head-mcp-osx-arm64` |
| macOS x64 | `5head-mcp-osx-x64` |
| Linux x64 | `5head-mcp-linux-x64` |
| Linux arm64 | `5head-mcp-linux-arm64` |
| Windows x64 | `5head-mcp-win-x64.exe` |

The npm package installs a small Node wrapper at `bin/run.js`. On install, `npm/scripts/install.js` either:

- copies a locally published binary from `FIVEHEAD_MCP_LOCAL_BINARY`, or
- downloads the matching release asset from `https://github.com/TillmanBuildsTech/5head/releases/download/v<version>/...`

The native binary is stored inside the installed package directory as `bin/5head-mcp` or `bin/5head-mcp.exe`, and the wrapper executes it over stdio.
The published npm tarball should contain the wrapper and installer only, not a prebundled native executable.

Local packaging proof:

```sh
dotnet publish src/FiveHead.Mcp.Host/FiveHead.Mcp.Host.csproj -c Release -r osx-arm64 --self-contained true /p:PublishAot=true -o ./publish/osx-arm64
FIVEHEAD_MCP_LOCAL_BINARY="$PWD/publish/osx-arm64/5head-mcp" npm install ./npm
./node_modules/.bin/5head-mcp
```

Release assumption: GitHub Releases must publish the exact filenames listed in the table above for the npm installer to succeed.

---

## Development

```sh
dotnet build
dotnet test
```

Requirements: .NET 9 SDK.

---

## License

MIT
