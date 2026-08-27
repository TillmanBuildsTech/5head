# V1 Cut Line

## Goal

Ship the smallest version of 5head that proves the core product thesis:

local, curated, provenance-backed memory for MCP-compatible LLM workflows.

## Must Ship In V1

### Core Memory Surfaces

- `.agent-memory/SOUL.md`
- `.agent-memory/MEMORY.md`
- `.agent-memory/USER.md`
- Repo-first root selection when a repo boundary is present
- Enforced size limits for each file
- Stable read and write behavior for each file

### Core Tools

- `read_memory`
- `write_memory`
- `check_contradictions`
- `list_facts`
- `confirm_fact`
- `retract_fact`
- `get_provenance`

### Provenance and Fact Lifecycle

- Local SQLite provenance store
- Fact recording for writes
- Fact status support for at least active, confirmed, uncertain, and retracted states
- Confirm and retract flows
- History lookup for a fact or fact family

### Memory Safety

- Contradiction detection before write
- Rejection of conflicting active facts unless explicitly retracted or superseded
- Staleness handling so old facts do not remain permanently trusted
- Manual `replace` as the only V1 compaction path for active markdown

### Distribution

- A working packaged executable flow
- Verified npm wrapper plus native binary flow, not just metadata
- A documented MCP config flow for multiple clients
- Proven compatibility with at least two or three real MCP clients
- The proof must clearly separate tested launch paths from client-doc-based assumptions

### Scope Clarification

- A user-level global fallback path is acceptable as an implementation detail when no repo-local root exists.
- Global memory should not be positioned as a polished V1 feature.

### Developer Confidence

- Automated tests for the core memory write flow
- Automated tests for contradiction behavior
- Automated tests for provenance lifecycle behavior
- Automated tests for file limit enforcement

## Nice To Have In V1 If Cheap

- Cleaner onboarding examples for common clients
- Better error messages around contradictions and size limits
- Monorepo-aware root selection if it fits naturally into the current design
- Minimal config file support where already aligned with the implementation

### V1 Config Decision

- Config is real in V1, but intentionally minimal.
- The only supported file is repo-local `.agent-memory/config.yaml`.
- The only supported fields are `stalenessThresholdDays` and `provenanceDbPath` because they are the only config inputs used by the V1 runtime.
- Per-package modes, size-limit overrides, and broader config surfaces are deferred until they are needed by shipped behavior.

## Explicitly Out Of Scope For V1

- Auto-editing MCP config files during install
- Full install automation across all clients and harnesses
- A polished global memory vault product
- Automated memory compaction jobs, heuristics, or background cleanup systems
- Obsidian-specific sync or PARA orchestration beyond basic file compatibility
- Search UX beyond the core provenance and fact lookup flows
- Embeddings, reranking, or semantic retrieval systems
- Hosted sync or cloud account requirements
- Analytics dashboards and admin panels

## Product Risks To Watch

### 1. Too Much Scope In Ingestion

Supporting every LLM harness is important, but trying to solve every integration path in v1 can delay the core product.

Guardrail:

- Ship MCP-first and prove value before widening integration methods.

### 2. Memory Bloat

If active memory becomes a dump, the product loses its core advantage.

Guardrail:

- Preserve hard limits, manual `replace`, and explicit retraction flows.

### 3. Provenance Without Usability

If provenance exists but agents and operators cannot use it to resolve conflicts, it is just extra storage.

Guardrail:

- Keep confirm, retract, and history retrieval as first-class behaviors.

### 4. Repo Memory Versus Global Memory

Trying to ship both as fully realized products at once may blur the initial value proposition.

Guardrail:

- Treat repo memory as v1.
- Treat global vault memory as the next layer.

## V1 Release Decision

V1 is ready when:

1. The core tools are stable.
2. The three-file memory model is genuinely useful in real sessions.
3. Provenance and contradiction handling are reliable.
4. The setup burden is acceptable for early adopters, even if not yet automated.
