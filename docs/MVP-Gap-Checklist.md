# MVP gap checklist

Updated to match the tree as of the shared-state change. Older copies of this
file described work that had already landed (structured tool payloads, config
loading, contradiction and remove tests, a release workflow).

## Shipped

- Three repo-local memory files with enforced limits (`MemoryFileStore`, `MemoryLimits`)
- Read, append, replace, and remove through `MemoryService`
- Fact extraction from markdown-style lines
- SQLite provenance with confirm, retract, list, and history
- Derived statuses: `active`, `confirmed`, `uncertain`, `retracted`
- Contradiction checks that block conflicting `active` and `confirmed` facts. `uncertain` facts stay visible and do not block writes (`MemoryServiceUncertainFactTests`)
- MCP tools: `read_memory`, `write_memory`, `check_contradictions`, `list_facts`, `confirm_fact`, `retract_fact`, `get_provenance`
- Structured payloads for contradictions, fact lists, provenance history, and the read/write content hash
- MCP resources: `memory://soul`, `memory://memory`, `memory://user`
- Minimal `.agent-memory/config.yaml` loader (`stalenessThresholdDays`, `provenanceDbPath` only). Dapper and YamlDotNet are not used
- Repo-first root selection, with `~/.agent-memory/global/` only as an unmarketed fallback
- Manual `replace` as the only compaction path
- Registry-free GitHub Release installer (`scripts/install.js`) and Native AOT publish for the five RIDs
- Write path: per-file cross-process locks, temp-file rename, expected content hash, provenance transaction with file restore on failure, SQLite `busy_timeout`, errors for unknown and already-retracted ids
- `remove` keeps the file and the extracted facts together: a fact is retracted when it is absent from the file after the removal, not merely because the remove string parsed as that fact
- Shared matter service in `state-api/` (separate process, Postgres). It is not part of the curated-memory server

## Still open

- `ci.yml` stdout-silence job is still a placeholder. It does not yet pipe an initialize request
- Real MCP client setups (Claude Code, Cursor, Copilot) are documented, not exercised in CI
- `docs/Packaging-Verification.md` still describes the removed npm wrapper. The README is the packaging contract
- A crash between the file rename and the provenance commit can still leave one write unrecorded. Exceptions roll the file back; a killed process cannot
- `expectedContentHash` is optional so existing clients keep working. Callers that omit it are serialized by the lock but a `replace` computed from a stale read can still overwrite if the caller does not send the hash
- The state API uses one shared bearer token. Agent names are self-asserted. There are no per-agent credentials
- The state service does not embed or query a vector database. `vectorRef` is only a pointer

## Not in this product

- Automated compaction, embeddings, or a hosted memory account
- Using the shared-state event log as a substitute for `SOUL.md` / `MEMORY.md` / `USER.md`
