# MVP Gap Checklist

This checklist reflects the current codebase, not just the product docs. It is meant to guide the next implementation session.

## Already Implemented

- Three repo-local memory files with enforced limits via `MemoryFileStore` and `MemoryLimits`
- Read, write, append, replace, and remove flows via `MemoryService`
- Fact extraction from markdown-style lines
- SQLite provenance store with confirm, retract, list, and history support
- Derived fact statuses: `active`, `confirmed`, `uncertain`, `retracted`
- Contradiction checking that blocks conflicting `active` and `confirmed` facts
- MCP tools for:
  - `read_memory`
  - `write_memory`
  - `check_contradictions`
  - `list_facts`
  - `confirm_fact`
  - `retract_fact`
  - `get_provenance`
- MCP resources for:
  - `memory://soul`
  - `memory://memory`
  - `memory://user`
- Passing unit and stdio integration tests via `dotnet test`

## Main Gaps To Close

### 1. Return richer MCP payloads

Current tool responses are mostly summary strings like `count=...` and `provenanceId=...`.

Next work:

- Return structured content for `check_contradictions`
- Return structured content for `list_facts`
- Return structured content for `get_provenance`
- Keep readable text summaries, but include machine-usable payloads too

### 2. Harden contradiction behavior with tests

Current behavior looks reasonable, but it needs sharper test coverage.

Next work:

- Test same-file `replace` with changed facts
- Test cross-file contradiction detection
- Test same-file replace that should supersede prior facts cleanly
- Confirm intended behavior when candidate facts conflict with facts in other memory files

### 3. Harden `remove` behavior with tests

Current `remove` behavior is string replacement plus fact-based provenance retraction.

Next work:

- Test remove no-op behavior
- Test partial content removal
- Test duplicate fact lines
- Test formatting variation in removed markdown lines

### 4. Make `uncertain` blocking behavior explicit

Right now only `active` and `confirmed` facts block contradictory writes. `uncertain` facts do not.

Next work:

- Decide whether this is the intended MVP rule
- Add tests that lock the rule in

Recommended MVP choice:

- `uncertain` facts should not block writes, but should remain visible through listing and provenance history

### 5. Decide whether config is real in V1

`AgentMemoryConfig` exists, but host wiring currently uses default config only.

Fields not clearly wired yet:

- `PerPackageMode`
- `ProvenanceDbPath`
- `SizeLimitOverrides`
- loading from `.agent-memory/config.yaml`

Next work:

- Either implement minimal config loading for values actually needed in V1
- Or explicitly defer config loading and keep defaults only for V1

Recommended MVP choice:

- Keep config minimal, only implement it if needed for staleness threshold or DB path override

### 6. Reconcile repo-local MVP scope with global fallback

`MemoryRootLocator` currently falls back to `~/.agent-memory/global` when no repo root is found.

Next work:

- Decide whether V1 should require repo context
- Or keep repo-first behavior with global fallback for convenience

Recommended MVP choice:

- Keep repo-first behavior if it does not complicate the code, but do not market global memory as a V1 feature

### 7. Define compaction narrowly for V1

Staleness exists. Automated compaction does not really exist yet.

Next work:

- Decide whether manual `replace` is the only MVP compaction primitive
- Avoid building large automated compaction systems in V1 unless there is a very small, obvious implementation

Recommended MVP choice:

- Treat manual `replace` as the compaction path for V1

### 8. Prove packaging end-to-end

The repo has npm packaging metadata, but packaging proof is still incomplete.

Next work:

- Verify local binary run flow
- Verify npm wrapper flow
- Verify expected release artifact assumptions
- Confirm install docs match reality

### 9. Prove 2-3 real MCP client setups

There is stdio integration coverage, but V1 still needs proof with real clients.

Next work:

- Test and document at least 2-3 real MCP-compatible clients
- Add setup examples that match the actual shipped package flow

## Recommended Next-Session Order

1. Upgrade MCP tool responses to include structured payloads
2. Add edge-case tests for contradiction and remove behavior
3. Make the repo-scope decision explicit
4. Make the config decision explicit
5. Define compaction narrowly and keep it small
6. Verify packaging end-to-end
7. Prove 2-3 client setups and document them

## Fastest Credible V1 Path

If speed matters most, prioritize in this order:

1. richer MCP outputs
2. edge-case tests
3. packaging proof
4. client proof
5. config loading only if still needed

## Key Files Reviewed

- `src/FiveHead.Mcp.Core/Memory/MemoryService.cs`
- `src/FiveHead.Mcp.Core/Storage/MemoryFileStore.cs`
- `src/FiveHead.Mcp.Core/Storage/SqliteProvenanceStore.cs`
- `src/FiveHead.Mcp.Host/MemoryMcpTools.cs`
- `src/FiveHead.Mcp.Host/MemoryMcpResources.cs`
- `src/FiveHead.Mcp.Host/McpServer.cs`
- `tests/FiveHead.Mcp.Tests/MemoryServiceTests.cs`
- `tests/FiveHead.Mcp.Tests/MemoryMcpSurfaceTests.cs`
- `tests/FiveHead.Mcp.Tests/StdioMcpIntegrationTests.cs`
