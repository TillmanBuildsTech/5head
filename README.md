# 5head-mcp

> Durable, curated, provenance-backed memory for LLM coding assistants.
> Plugs into Claude Code, GitHub Copilot CLI, Cursor, VS Code — any MCP-compatible client.

**5head is a local memory MCP.** That is the default, and it has zero
dependencies: the binary, three markdown files, and a local SQLite database.
With `FIVEHEAD_STATE_URL` and `FIVEHEAD_STATE_TOKEN` unset, it registers only
the memory tools, opens no network connection, and does not need Postgres.

**Shared State is an optional add-on** for multi-agent and team setups. It
stays off until both of those variables are set. The same binary then also
talks to the state API. An operator console for that API is a further
opt-in (`FIVEHEAD_ADMIN_ENABLED`); it is not part of the local memory server.

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

This fetches the matching binary for your OS/arch from the latest **stable**
release and installs it to `~/.5head/bin/5head-mcp` (no npm, no npmjs, nothing
else installed). You need Node.js — which you have if you use Claude Code,
Cursor, or any Node-based MCP client.

### Snapshot (pre-release) install

Every PR against `main` publishes its binaries as a rolling `snapshot`
pre-release. To try the latest unreleased build instead of the stable release:

```sh
export FIVEHEAD_MCP_RELEASE_TAG=snapshot
curl -sL https://github.com/TillmanBuildsTech/5head/releases/download/snapshot/install.js | node
```

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

Requires the .NET 10 SDK.

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

### Local memory by default, shared state when you opt in

The default client config above is the whole product: a local memory MCP.
Shared State is the optional add-on. The same binary serves shared matters
(`list_matters`, `get_matter`, `create_matter`, `update_matter`,
`append_event`, `claim_next`, `release_claim`) only when both of these are set:

| Variable | Meaning |
|----------|---------|
| `FIVEHEAD_STATE_URL` | Base URL of the state API (`docker-compose.yml`) |
| `FIVEHEAD_STATE_TOKEN` | Bearer token, the same value as the API's `STATE_AUTH_TOKEN` |
| `FIVEHEAD_STATE_AGENT` | Optional name stored on writes and claims. Default `5head` |

Leave both unset and startup matches the memory server from before this
add-on: the seven memory tools, the three resources, no HTTP client, and no
Postgres. Set exactly one of them and the process exits, so a half
configuration does not silently drop back to local-only.

`.agent-memory/config.yaml` may also set `stateApiUrl`, `stateAuthToken`, and
`stateAgent`. The environment wins. Prefer the token in the client `env` block
so it is not committed.

A home-lab Hermes agent talks to the API on the host port published by
`docker-compose.ports.yml` (the smoke test uses 18080). The cloud agent, off
the LAN, uses the Cloudflare Tunnel hostname. Both run `5head-mcp`. Only the
env block changes:

```json
{
  "mcpServers": {
    "memory": {
      "command": "5head-mcp",
      "env": {
        "FIVEHEAD_STATE_URL": "http://127.0.0.1:18080",
        "FIVEHEAD_STATE_TOKEN": "replace-with-a-long-random-token",
        "FIVEHEAD_STATE_AGENT": "hermes-lab"
      }
    }
  }
}
```

```json
{
  "mcpServers": {
    "memory": {
      "command": "5head-mcp",
      "env": {
        "FIVEHEAD_STATE_URL": "https://state.example.com",
        "FIVEHEAD_STATE_TOKEN": "replace-with-a-long-random-token",
        "FIVEHEAD_STATE_AGENT": "cloud"
      }
    }
  }
}
```

`update_matter` sends the version you last read. If another agent wrote first,
the tool returns an error that includes the current version and the current
row. Re-read with `get_matter` and retry. Setup for the API itself, the
tunnel, and curl examples are in [`deploy/README.md`](deploy/README.md).

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
stateApiUrl: http://127.0.0.1:18080
stateAgent: hermes-lab
```

`stateAuthToken` is accepted in that file too. Leave it unset and pass
`FIVEHEAD_STATE_TOKEN` in the MCP client env instead. The state keys are
optional; without a URL and a token the server stays memory-only.

`provenanceDbPath` may be absolute or relative to `.agent-memory/`.

If 5head cannot discover a repo-local memory root, it falls back to `~/.agent-memory/global/`.
That global path is an implementation fallback, not the primary V1 product surface.

---

## Tools

| Tool | Description |
|------|-------------|
| `read_memory` | Read SOUL, MEMORY, or USER file |
| `write_memory` | Append, replace, or remove content (`replace` is the V1 manual compaction path; checked against size limit + contradictions). Optional `expectedContentHash` fails the write if the file changed since it was read |
| `check_contradictions` | Dry-run conflict detection before a write |
| `list_facts` | List active/confirmed/uncertain/retracted facts |
| `confirm_fact` | Explicitly confirm a provenance row |
| `retract_fact` | Explicitly retract a provenance row |
| `get_provenance` | Full history for a subject+predicate pair |
| `list_matters`, `get_matter`, `create_matter`, `update_matter`, `append_event`, `claim_next`, `release_claim` | Shared matters. Present only when `FIVEHEAD_STATE_URL` and `FIVEHEAD_STATE_TOKEN` are set |

For V1, compaction is intentionally narrow: use an explicit `replace` write when a memory file needs cleanup or consolidation. 5head records the replacement in provenance and retracts facts from the prior file state when they no longer appear. It does not run automated compaction or background cleanup.

`read_memory` returns the file text plus a `contentHash` (SHA-256 of the UTF-8 bytes). Pass that hash back as `expectedContentHash` on the next `write_memory`. If another writer landed first, the call fails and the file is left as-is. Writes also take a per-file cross-process lock, and the file is replaced by a temp-file rename in the same step as the provenance transaction (the previous bytes are restored if the database write fails).

`remove` retracts a fact only when that fact is no longer in the file after the string removal. A remove string that does not match the file does not retract a fact that is still there. A partial removal that changes a fact line retracts the old fact and records the fact the file now contains.

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
5head-mcp (.NET 10, Native AOT)
    ├── Memory tools and resources
    ├── Contradiction Checker
    ├── Size Limit Guard
    ├── Fact Extractor
    ├── Storage Layer
    │       ├── .agent-memory/{SOUL,MEMORY,USER}.md  (git-tracked)
    │       └── ~/.agent-memory/cache/{hash}/provenance.db  (SQLite WAL)
    └── Shared State add-on (off unless FIVEHEAD_STATE_URL and FIVEHEAD_STATE_TOKEN are both set)
            │ HTTP, bearer token
            ▼
        FiveHead.State.Api  →  Postgres
            └── /admin console, off unless FIVEHEAD_ADMIN_ENABLED=true
```

---

## Distribution

### Release pipeline

- **Snapshot build** — every PR against `main` runs `ci.yml`: build + test +
  Native AOT for all platforms, and publishes the binaries as a rolling
  `snapshot` **pre-release** for trying unreleased builds.
- **Production release** — merging to `main` runs `release.yml`: it rebuilds
  the exact release binaries, publishes a GitHub Release `v<version>` (with
  the one-line installer and the platform binaries as assets, marked latest),
  then bumps the version (patch by default; `[release:minor]` /
  `[release:major]` in the merge message bumps that part) and opens a release
  PR carrying the bump back to `main` (main is PR-only, so the bot cannot push
  directly). The bump PR does **not** publish a GitHub Release. An empty
  stable release would become `releases/latest` and the installer URL would
  404. Merging the bump is skipped because its commit message is
  `chore(release): bump`.

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

Requirements: .NET 10 SDK.

---

## Deploy on Coolify

Import the repo as a Docker Compose resource with base directory `/` and
compose location [`docker-compose.yml`](docker-compose.yml). That file does
not publish a host port. The API only `expose`s container port 8080, and
Coolify's proxy routes the domain to that container port. Postgres has no
host port either. [`docker-compose.ports.yml`](docker-compose.ports.yml) is
for a local or direct port only. It is not named `docker-compose.override.yml`,
so Compose and Coolify leave it unloaded.

Coolify generates these and keeps them across deploys. Leave the local
defaults unset so the generated values win:

| Variable | What it becomes |
|----------|-----------------|
| `SERVICE_USER_POSTGRES` | Postgres user |
| `SERVICE_PASSWORD_64_POSTGRES` | Postgres password, shared with the API connection string |
| `SERVICE_PASSWORD_64_FIVEHEAD` | Bearer token. The API reads it as `STATE_AUTH_TOKEN` |
| `SERVICE_URL_API_8080` | Public URL. The proxy sends it to container port 8080 |

On the `api` service, set the domain to `https://5head-state.example.com:8080`.
The `:8080` tells Coolify's proxy to target container port 8080. No host port
is needed. Browsers still use 443. Do not give `postgres` a domain or a host
port. Its data is the named volume `state-postgres`.

The API process applies `src/FiveHead.State.Api/Migrations` before it listens.
`GET /health` does not need the token and pings Postgres, so a healthy check
means the migrations have finished.

Point each `5head-mcp` at that deployment. The token is the generated
`SERVICE_PASSWORD_64_FIVEHEAD` value, copied out of Coolify's environment
page. The URL is the public hostname without the container port:

```json
{
  "mcpServers": {
    "memory": {
      "command": "5head-mcp",
      "env": {
        "FIVEHEAD_STATE_URL": "https://5head-state.example.com",
        "FIVEHEAD_STATE_TOKEN": "the-SERVICE_PASSWORD_64_FIVEHEAD-value",
        "FIVEHEAD_STATE_AGENT": "cloud"
      }
    }
  }
}
```

The compose file does not bind a host port, so a lab agent on the Coolify
machine uses that same public hostname. Use a different `FIVEHEAD_STATE_AGENT`
per agent.

### Admin console

The API can serve a small operator console at `/admin`. It is **off by
default**. With the flag unset, `/admin` is not a route (HTTP 404), including
for clients that already hold the state bearer token. Local memory is
unchanged: this console is part of the optional shared-state API, not of
`5head-mcp`.

On the `api` service in Coolify, set:

| Variable | Meaning |
|----------|---------|
| `FIVEHEAD_ADMIN_ENABLED` | `true` to serve `/admin`. Anything else, including unset, leaves it off. `docker-compose.yml` passes this through and defaults it to `false`. |
| `FIVEHEAD_ADMIN_TOKEN` | Optional. When set (8+ characters), only this token can open the console or call `/admin/api`. Leave it unset to reuse `STATE_AUTH_TOKEN`. Set a **different** value when agents hold the state token, so a leaked agent token cannot force-release a claim or change another owner's status. Do not put this token in an agent's MCP env. |

Open `https://5head-state.example.com/admin` and paste the admin token into
the sign-in form. The browser exchanges it for an HttpOnly, `SameSite=Strict`
cookie scoped to `/admin` (12 hours, `Secure` on HTTPS). The page script
never stores the token. Signing out deletes the cookie. Scripts can send
`Authorization: Bearer <admin token>` to `/admin/api/...` instead of using
the form.

The console lists and filters matters, shows metadata and the append-only
event timeline, edits `status` / `owner` / `nextStep` with the same version
check as `PATCH /matters/{id}` (a stale version is HTTP 409 and the row is
left as-is), and can clear a claim another agent still holds. Those operator
writes are stored as events from the agent name `admin` (`admin.update`,
`admin.force_release`). The public matter routes are unchanged: a non-owner
still cannot change status or owner through `PATCH /matters/{id}`.

Memory browsing is out of scope. Per-agent memory lives in that agent's
`.agent-memory/` files and provenance database, not in Postgres. A later
viewer would have to run on the machine that holds those files; it should
not upload them into the shared state API.

### Local smoke test

From the repository root, with Docker Compose v2. No `.env` is required.
The local defaults are user `state`, password `state`, and token
`local-dev-token`. Those are for a private smoke test. Copy
[`.env.example`](.env.example) to `.env` to replace them.

`docker-compose.yml` publishes no host port. Add
[`docker-compose.ports.yml`](docker-compose.ports.yml) and set
`FIVEHEAD_HOST_PORT`. There is no default host port. Compose exits if the
variable is unset.

```sh
FIVEHEAD_HOST_PORT=18080 docker compose -f docker-compose.yml -f docker-compose.ports.yml up --build -d
curl -fsS http://127.0.0.1:18080/health
FIVEHEAD_HOST_PORT=18080 docker compose -f docker-compose.yml -f docker-compose.ports.yml down
```

`curl` prints `{"status":"ok"}`. Postgres is only reachable as the hostname
`postgres` on the compose network.

## Shared state

Optional add-on for multi-agent and team setups. The local memory MCP does
not start it and does not require it. Matters that more than one agent must
update live in Postgres behind the root compose file. API details,
curl examples, and the tunnel notes are in [`deploy/README.md`](deploy/README.md).
The API does not write `SOUL.md` / `MEMORY.md` / `USER.md`. The capture
pattern (proxy hook, explicit `update_matter`, agent-loop backstop,
reconcile sweep) is [`docs/capture-pattern.md`](docs/capture-pattern.md).

The optional admin console (`FIVEHEAD_ADMIN_ENABLED=true`, path `/admin`)
reads and updates this shared state only. It does not browse per-agent
memory files. See "Admin console" under Deploy on Coolify.

## License

MIT. See [LICENSE](LICENSE). Copyright 2026 TillmanBuildsTech / Brandon Tillman.
