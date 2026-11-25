# Capture pattern

5head's curated files stay small on purpose. They are the wrong place to store
every model turn. Shared matters are the log that several agents can agree on.
Nothing here writes `SOUL.md`, `MEMORY.md`, or `USER.md`.

Four layers, from automatic to deliberate:

## 1. Proxy hook on every turn

The local LLM proxy sees every completion, including turns where the agent
forgot to call a tool. `hooks/litellm_matter_callback.py` is a LiteLLM custom
logger. `hooks/http_middleware_snippet.py` is the same idea for a generic ASGI
proxy.

The hook POSTs one append-only event:

- `kind`: `model_response`
- `summary`: a short slice of the assistant text
- `payload`: model name and a truncated copy of the text

It does not change matter status, owner, or version. A missed hook must not
fail the user's request; both samples swallow network errors.

Pass the matter id in LiteLLM request metadata as `matter_id`, or set
`STATE_MATTER_ID` when a process is working a single matter. The cloud agent
reaches this hook only if the proxy itself is on the home LAN. The hook then
calls the state API on localhost. The cloud agent does not need a path to
Postgres.

## 2. Explicit `update_matter`

When an agent actually changes the plan, it calls the MCP tool (or the HTTP
API) with the version it last read:

```json
{ "version": 3, "agent": "hermes-local", "status": "blocked", "nextStep": "wait for tunnel" }
```

Only the matter's owner may change `status` or `owner`. Anyone authenticated
may change `nextStep`, `metadata`, or `vectorRef`, and anyone may
`append_event`. A stale version is HTTP 409 with the current row. The agent
re-reads and retries. It does not write blind.

`vectorRef` is a pointer such as `qdrant://memories/abc`. This service does
not store embeddings.

## 3. Agent-loop backstop

The system prompt for each Hermes agent, and for the cloud agent, should say:

- At the start of a task, `get_matter` or `claim_next`.
- Before finishing a turn that changed the plan, `update_matter` with the
  version from the last read.
- If the tool returns a conflict, re-read once and retry once.
- `release_claim` when the task is done or handed off.

The proxy hook still records the turn if the model skips the tool. The
backstop is what keeps status and next step honest.

## 4. Periodic reconciliation sweep

`scripts/reconcile.py` lists `open` matters whose newest event is older than
`STATE_RECONCILE_MINUTES` (default 30). Run it from cron on the home-lab host.
It only prints JSON. A human or a follow-up agent decides whether to append a
`reconcile` event, release a stuck claim, or update `nextStep`.

Claims expire on their own (`claim_expires_at`). `claim_next` will take an
expired claim. The sweep is for matters that went quiet, not for lock recovery.

## What not to do

- Do not point the hook at 5head `write_memory`. Curated files would fill up
  with transcripts and the contradiction checker would fight the log.
- Do not let two writers both `UPDATE` a matter without the version check.
- Do not expose Postgres through the tunnel. Publish the API only, with the
  bearer token.
