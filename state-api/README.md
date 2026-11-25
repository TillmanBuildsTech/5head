# Shared state

A small HTTP service that sits **beside** 5head. 5head still owns the three
curated markdown files. This service owns matters that more than one agent
must update without lost writes: the cloud agent off the LAN, and the Hermes
agents on the home lab.

Postgres is the source of truth. The API is the only process that talks to it.
A thin MCP wrapper calls the API. There is no vector database here; `vectorRef`
is a pointer to one you already run.

## Why .NET

The rest of this repo is .NET 9, and CI is already `dotnet test`. ASP.NET
Core gives bearer auth, OpenAPI, and a test host. Npgsql can run
`FOR UPDATE SKIP LOCKED`. The MCP wrapper uses the same ModelContextProtocol
package as `5head-mcp`. The service is a long-running home-lab API, not a
Native AOT stdio binary, so it does not follow 5head's trimming constraints.

## Layout

| Path | Role |
|------|------|
| `FiveHead.State.Api` | HTTP API and SQL migrations |
| `FiveHead.State.Mcp` | MCP tools that call the API (`stdio`, or streamable HTTP) |
| `FiveHead.State.Tests` | 409, claim exclusivity, owner rule, auth |
| `hooks/` | LiteLLM callback and a generic ASGI snippet |
| `docs/capture-pattern.md` | Proxy hook, explicit updates, agent backstop, reconcile sweep |
| `docker-compose.yml` | Postgres 16 + the API |

## Run

```sh
cd state-api
cp .env.example .env
# edit STATE_AUTH_TOKEN; openssl rand -hex 32
docker compose up --build
```

The API listens on `http://localhost:8080`. Postgres is on `localhost:5432`
for local debugging. Do not publish Postgres through the tunnel.

### Environment

| Variable | Used by | Meaning |
|----------|---------|---------|
| `STATE_DATABASE_URL` | API | `postgres://user:pass@host:5432/db` or an Npgsql keyword string |
| `STATE_AUTH_TOKEN` | API, MCP, hooks | Bearer token. At least 8 characters; use a long random value |
| `STATE_CLAIM_TTL_SECONDS` | API | Default claim lifetime (300) |
| `STATE_API_URL` | MCP, hooks | Base URL of the API |
| `STATE_AGENT_NAME` | MCP, hooks | Name stored as `agent` / `claimed_by` |
| `STATE_MATTER_ID` | LiteLLM hook | Fallback matter when request metadata has none |
| `STATE_MCP_TRANSPORT` | MCP | `stdio` (default) or `http` |
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | Compose | Database bootstrap. Dev defaults only |

The token is compared with SHA-256 and `CryptographicOperations.FixedTimeEquals`,
so a short guess does not bail out on length. The token is the trust boundary.
`agent` is attribution: anyone holding the token can send any agent name.
That matches a single tunnel secret shared by your own agents. It is not
per-agent authentication.

### Cloudflare Tunnel

The cloud agent is not on the LAN. Run `cloudflared` on the home-lab host
with a tunnel token, and point the public hostname at `http://localhost:8080`
(or the compose service). Do not add Postgres to the ingress. Clients send:

```http
Authorization: Bearer <STATE_AUTH_TOKEN>
```

Prefer a named tunnel over `cloudflared tunnel --url`, so the hostname stays
stable. Cloudflare Access in front of the hostname is optional extra; the
bearer token is what the API checks. Keep the token out of URLs and out of
git. `.env` is gitignored.

### MCP wrapper

Local Hermes agents:

```sh
export STATE_API_URL=http://localhost:8080
export STATE_AUTH_TOKEN=...   # same value as the API
export STATE_AGENT_NAME=hermes-local
dotnet run --project state-api/FiveHead.State.Mcp
```

That process speaks MCP over stdio. Point the client at `5head-state-mcp`.
Stdout is the MCP channel.

For a remote MCP client that can open HTTP, set `STATE_MCP_TRANSPORT=http`.
The wrapper then serves the ModelContextProtocol streamable HTTP endpoint and
still calls the API for every tool. The cloud agent can instead call the REST
API directly if that is simpler than running a second hop.

Tools: `list_matters`, `get_matter`, `create_matter`, `update_matter`,
`append_event`, `claim_next`, `release_claim`. `create_matter` is included
because the others have nothing to update until a row exists.

## HTTP API

OpenAPI: `GET /openapi/v1.json` (no auth). Health: `GET /health` (no auth).
Everything else requires the bearer token.

```sh
TOKEN=replace-with-a-long-random-token
API=http://localhost:8080

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
A mismatch is **409** with the current row. The caller re-reads and retries.
Only `owner` may change `status` or `owner` (otherwise **403**, version unchanged).
Events are insert-only; the database rejects `UPDATE` and `DELETE`. Appending
an event does not bump the matter version.

`claim_next` locks the next row that is not `done` or `cancelled` and whose
claim is empty or expired, with `FOR UPDATE SKIP LOCKED`. A second caller
gets a different row, or **204** when nothing is left. An active claim can
be released only by the claimant. An expired claim can be cleared by anyone,
and `claim_next` will take it.

## Tests

CI starts Postgres and sets `STATE_API_TEST_CONNECTION`. Locally, with the
dev role from this README:

```sh
export STATE_API_TEST_CONNECTION='Host=localhost;Port=5432;Username=state;Password=state;Database=postgres'
dotnet test state-api/FiveHead.State.Tests
```

The fixture creates a throwaway database and drops it.

## Capture

See [docs/capture-pattern.md](docs/capture-pattern.md). Short version: the
proxy hook logs every model response, agents call `update_matter` when the
plan changes, the system prompt tells them to do that before they finish a
turn, and `scripts/reconcile.py` lists open matters that have gone quiet.
