# 5head-mcp

> Durable, curated, provenance-backed memory for LLM coding assistants.
> Plugs into Claude Code, GitHub Copilot CLI, Cursor, VS Code — any MCP-compatible client.

## Why another memory MCP?

- **Curated markdown, not event logs.** Three files (`SOUL.md` / `MEMORY.md` / `USER.md`) with enforced character limits force the agent to distill signal from noise instead of growing an unbounded dump.
- **Per-repo and git-versioned.** Memory lives in `.agent-memory/` alongside your code, travels with branches, is reviewable in PRs, and survives machine wipes — no cloud account required.
- **Provenance with confirm/retract.** Every write is tracked in SQLite with timestamps and source attribution. Conflicting facts are rejected until the old one is explicitly retracted; operators can confirm or retract facts at any time.

---

## Install

No `git clone`, no package manager, no registry. 5head ships pre-built native
binaries for macOS, Linux, and Windows on every [GitHub Release](../../releases),
and the one-line installer downloads the right one for your machine.

### One-line install (recommended)

```sh
curl -sL https://github.com/TillmanBuildsTech/5head/releases/latest/download/install.js | node
```

This fetches the matching binary for your OS/arch from the latest release and
installs it to `~/.5head/bin/5head-mcp` (no npm, no npmjs, nothing else
installed). You need Node.js — which you have if you use Claude Code, Cursor,
or any Node-based MCP client.

### Manual download (no Node)

Grab the binary for your platform from the [latest release](../../releases)
and drop it somewhere on your PATH:

```sh
# macOS Apple Silicon
curl -L -o 5head-mcp \
  https://github.com/TillmanBuildsTech/5head/releases/latest/download/5head-mcp-osx-arm64
chmod +x 5head-mcp

# Linux x64
curl -L -o 5head-mcp \
  https://github.com/TillmanBuildsTech/5head/releases/latest/download/5head-mcp-linux-x64
chmod +x 5head-mcp
```

Windows: download `5head-mcp-win-x64.exe`.

### Build from source (developers)

```sh
git clone https://github.com/TillmanBuildsTech/5head
cd 5head
dotnet build
dotnet run --project src/FiveHead.Mcp.Host
```

Requires the .NET 9 SDK.

> Homebrew, Chocolatey, and `dotnet tool` packaging are planned but not shipped yet.

---

## Configure your MCP client

Point any MCP client at the installed binary's path (default after the
one-line install is `~/.5head/bin/5head-mcp`):

```json
{
  "mcpServers": {
    "memory": {
      "command": "/home/YOU/.5head/bin/5head-mcp",
      "args": []
    }
  }
}
```

Or put the binary on your PATH and use the bare name:

```json
{
  "mcpServers": {
    "memory": {
      "command": "5head-mcp",
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

### Release pipeline

- **Snapshot build** — every PR against `main` runs `ci.yml`: build + test +
  Native AOT for all platforms, with the binaries attached to the PR as
  workflow artifacts for manual snapshot testing.
- **Production release** — merging that PR to `main` runs `release.yml`: it
  rebuilds the exact release binaries, publishes a GitHub Release `v<version>`
  (with the one-line installer and the platform binaries as assets), then bumps
  the version (patch by default; `[release:minor]` / `[release:major]` in the
  merge message bumps that part) and opens a release PR carrying the bump back
  to `main` (main is PR-only, so the bot cannot push directly).

There is **no npm package and no registry** — distribution is entirely through
GitHub Releases. Every release carries:

| Asset | Purpose |
|-------|---------|
| `install.js` | One-line installer (`curl … \| node`) |
| `5head-mcp-osx-arm64` | macOS Apple Silicon binary |
| `5head-mcp-osx-x64` | macOS Intel binary |
| `5head-mcp-linux-x64` | Linux x64 binary |
| `5head-mcp-linux-arm64` | Linux arm64 binary |
| `5head-mcp-win-x64.exe` | Windows x64 binary |

`scripts/install.js` in this repo is the installer: it's self-contained, detects
the platform, downloads the matching binary from the release, and installs it
to `~/.5head/bin/`. `scripts/version.js` bumps the `<Version>` in the csproj
files during the release.

Release assumption: GitHub Releases must publish the exact filenames listed in
the table above for the one-line installer and the manual-download commands to
succeed.

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
