# Shared state API

Postgres-backed matters for more than one agent. 5head still owns `SOUL.md`,
`MEMORY.md`, and `USER.md`. This process owns the shared queue. Agents do not
talk to Postgres. They talk to this API, usually through the optional tools
on the same `5head-mcp` process.

There is no vector database here. `vectorRef` is a pointer to one you already run.

## Why .NET

The rest of this repo is .NET 9, and CI is already `dotnet test`. ASP.NET Core
gives bearer auth, OpenAPI, and a test host. Npgsql can run
`FOR UPDATE SKIP LOCKED`. The API is a long-running home-lab service, so it
is not published as Native AOT. The stdio host that calls it is.

## Layout

| Path | Role |
|------|------|
| `src/FiveHead.State.Api` | HTTP API and SQL migrations |
| `src/FiveHead.State.Api/Admin` | Optional operator console. Static files embedded in the API. Off unless `FIVEHEAD_ADMIN_ENABLED=true` |
| `src/FiveHead.State.Client` | Typed HTTP client used by `5head-mcp` |
| `tests/FiveHead.State.Tests` | 409, claim exclusivity, owner rule, auth, admin console gating |
| `examples/` | LiteLLM callback, generic ASGI snippet, reconcile sweep |
| `docs/capture-pattern.md` | Proxy hook, explicit updates, agent backstop, reconcile sweep |
| `docker-compose.yml` (repo root) | Postgres 16 + the API. No host ports. This is what Coolify imports |
| `docker-compose.ports.yml` (repo root) | Opt-in host port for the API. Not auto-loaded |

## Run

From the repository root. The base compose file publishes no host ports.
Postgres stays on the compose network. The API listens on container port
8080 and applies migrations before it accepts traffic. To reach it from the
host, add `docker-compose.ports.yml` and set `FIVEHEAD_HOST_PORT` (no
default; Compose exits if it is unset). With no `.env`, the local defaults
are database user `state`, password `state`, and bearer token
`local-dev-token`.

```sh
FIVEHEAD_HOST_PORT=18080 docker compose -f docker-compose.yml -f docker-compose.ports.yml up --build -d
curl -fsS http://127.0.0.1:18080/health
FIVEHEAD_HOST_PORT=18080 docker compose -f docker-compose.yml -f docker-compose.ports.yml down
```

Copy `.env.example` to `.env` before using any other password or token.
Coolify setup, including which generated values to copy into
`FIVEHEAD_STATE_URL` and `FIVEHEAD_STATE_TOKEN`, is in the root README
section "Deploy on Coolify".

### Environment

| Variable | Used by | Meaning |
|----------|---------|---------|
| `STATE_DATABASE_URL` | API | `postgres://user:pass@host:5432/db` or an Npgsql keyword string |
| `STATE_AUTH_TOKEN` | API | Bearer token the API expects. At least 8 characters |
| `STATE_CLAIM_TTL_SECONDS` | API | Default claim lifetime (300) |
| `FIVEHEAD_ADMIN_ENABLED` | API | `true` serves `/admin`. Unset or any other value does not map those routes (404). Compose defaults this to `false` |
| `FIVEHEAD_ADMIN_TOKEN` | API | Optional second bearer for the console only. Unset reuses `STATE_AUTH_TOKEN`. Set a different value so agent tokens cannot call admin overrides. At least 8 characters when set |
| `FIVEHEAD_STATE_URL` | `5head-mcp` | Base URL of this API. Together with the token, turns the state tools on |
| `FIVEHEAD_STATE_TOKEN` | `5head-mcp` | Same secret as `STATE_AUTH_TOKEN` |
| `FIVEHEAD_STATE_AGENT` | `5head-mcp` | Name stored as `agent` / `claimed_by` (default `5head`) |
| `STATE_API_URL` | hooks | Base URL of this API |
| `STATE_AUTH_TOKEN` | hooks | Same secret, sent as the bearer token |
| `STATE_AGENT_NAME` | hooks | Name stored on events the proxy appends |
| `STATE_MATTER_ID` | LiteLLM hook | Fallback matter when request metadata has none |
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | Compose | Local overrides. Defaults `state` / `state` / `state` |
| `SERVICE_USER_POSTGRES` / `SERVICE_PASSWORD_64_POSTGRES` / `SERVICE_PASSWORD_64_FIVEHEAD` | Coolify | Generated credentials. They replace the local defaults |
| `FIVEHEAD_HOST_PORT` | `docker-compose.ports.yml` | Host port for the API when that file is passed explicitly. No default. Postgres has no host port |

The token is compared with SHA-256 and `CryptographicOperations.FixedTimeEquals`.
The token is the trust boundary. `agent` is attribution: anyone holding the
token can send any agent name. That matches a single tunnel secret shared by
your own agents. It is not per-agent authentication.

`.agent-memory/config.yaml` may set `stateApiUrl`, `stateAuthToken`, and
`stateAgent`. Environment variables win. Keep the token in the environment of
each MCP client so it is not committed with the repo.

### Cloudflare Tunnel

The cloud agent is not on the LAN. Run `cloudflared` on the home-lab host
with a tunnel token, and point the public hostname at the API. The base
compose file does not publish a host port, so either attach `cloudflared` to
the compose network and use `http://api:8080`, or publish a host port with
`docker-compose.ports.yml` and point the tunnel at
`http://localhost:${FIVEHEAD_HOST_PORT}`. Do not add Postgres to the ingress.
Clients send:

```http
Authorization: Bearer <STATE_AUTH_TOKEN>
```

Prefer a named tunnel over `cloudflared tunnel --url`, so the hostname stays
stable. Cloudflare Access in front of the hostname is optional extra; the
bearer token is what the API checks. Keep the token out of URLs and out of
git. `.env` is gitignored.

### 5head

5head is a local memory MCP by default. Shared State is an optional add-on:
those tools are registered only when `FIVEHEAD_STATE_URL` and
`FIVEHEAD_STATE_TOKEN` are both set (or the matching `config.yaml` keys).
See the README section "Local memory by default, shared state when you opt in".

## Admin console

Off by default. Set `FIVEHEAD_ADMIN_ENABLED=true` on the API process and open
`/admin`. The sign-in form posts the admin token once; the API sets an
HttpOnly `SameSite=Strict` cookie on path `/admin` for 12 hours (`Secure`
when the request is HTTPS). The page does not keep the token in
`localStorage` or `sessionStorage`. `DELETE /admin/session` clears the cookie.

`/admin/api` accepts that cookie or `Authorization: Bearer` with the admin
token. It does not accept the state token when `FIVEHEAD_ADMIN_TOKEN` is set
to something else. The public routes keep requiring `STATE_AUTH_TOKEN`.

Operator edits of status, owner, and next step still send `version`. A
mismatch is **409** with the current row, and nothing is written. The owner
rule on `PATCH /matters/{id}` is unchanged. The admin route may change status
or owner without being the owner, and `POST /admin/api/matters/{id}/force-release`
clears a claim held by any agent. Both write an append-only event as agent
`admin`.

The console shows shared matters only. `SOUL.md`, `MEMORY.md`, `USER.md`, and
the provenance database stay on each agent and are not browsable here.

```sh
curl -sS -X POST "$API/admin/session" \
  -H "Content-Type: application/json" \
  -d '{"token":"'"$ADMIN_TOKEN"'"}'

curl -sS -X PATCH "$API/admin/api/matters/MATTER_ID" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"version":1,"status":"blocked","owner":"ops","nextStep":"look at the claim"}'

curl -sS -X POST "$API/admin/api/matters/MATTER_ID/force-release" \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

`$ADMIN_TOKEN` is `FIVEHEAD_ADMIN_TOKEN`, or `STATE_AUTH_TOKEN` when that
variable is unset. These routes 404 while the console is disabled.

## HTTP API

OpenAPI: `GET /openapi/v1.json` (no auth). Health: `GET /health` (no auth).
Everything else requires the bearer token.

```sh
TOKEN=replace-with-a-long-random-token
API=http://localhost:18080

curl -sS "$API/health"

curl -sS -X POST "$API/matters" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"title":"Cut the tunnel","owner":"brandon","nextStep":"publish the hostname","metadata":{"links":["https://example.test"]},"vectorRef":"qdrant://memories/tunnel"}'

# Use the id and version from the response.
curl -sS -X PATCH "$API/matters/MATTER_ID" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"version":1,"agent":"brandon","status":"active"}'

curl -sS -X POST "$API/matters/MATTER_ID/events" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"agent":"hermes-local","kind":"note","summary":"proxy saw the turn","payload":{"model":"local"}}'

curl -sS -X POST "$API/claims/next" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"agent":"hermes-local","ttlSeconds":300}'

curl -sS -X POST "$API/matters/MATTER_ID/release" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"agent":"hermes-local"}'
```

`PATCH` succeeds only when `version` matches (`UPDATE ... WHERE id = $1 AND version = $2`).
A mismatch is **409** with the current row. The 5head `update_matter` tool
returns that as a tool error naming the current version. The caller re-reads
and retries. Only `owner` may change `status` or `owner` (otherwise **403**,
version unchanged). Events are insert-only; the database rejects `UPDATE` and
`DELETE`. Appending an event does not bump the matter version.

`claim_next` locks the next row that is not `done` or `cancelled` and whose
claim is empty or expired, with `FOR UPDATE SKIP LOCKED`. A second caller
gets a different row, or **204** when nothing is left. An active claim can
be released only by the claimant. An expired claim can be cleared by anyone,
and `claim_next` will take it.

## Tests

CI starts Postgres and sets `STATE_API_TEST_CONNECTION`. Locally:

```sh
export STATE_API_TEST_CONNECTION='Host=localhost;Port=5432;Username=state;Password=state;Database=postgres'
dotnet test tests/FiveHead.State.Tests
```

The fixture creates a throwaway database and drops it. Client and tool
registration tests live in `tests/FiveHead.Mcp.Tests` and use a fake HTTP
handler, so they do not need Postgres.

## Capture

See [docs/capture-pattern.md](../docs/capture-pattern.md). Short version: the
proxy hook logs every model response, agents call `update_matter` when the
plan changes, the system prompt tells them to do that before they finish a
turn, and `examples/reconcile.py` lists open matters that have gone quiet.
