# 5head MVP

## Product Thesis

5head is a local-first MCP memory system for LLM workflows.

Its job is to capture durable knowledge from conversations, planning, building, and refactoring, distill that knowledge into a small set of high-signal markdown files, and back every fact with provenance so stale or conflicting memory can be confirmed, retracted, or manually compacted over time.

This is not a raw event log. The product value is sharp memory, not long memory.

## Primary User

The primary user is any person using an MCP-compatible LLM client who wants durable local memory without giving up inspectability, portability, or control.

Initial target users:

- Claude Code users
- Claude Desktop users
- Cursor users
- Copilot CLI and other MCP-capable coding users

## Core Workflow

1. A user works in an MCP-compatible LLM client.
2. The client calls 5head tools to read and write memory.
3. 5head stores human-facing summaries in local markdown files.
4. 5head stores machine-facing provenance and fact history in SQLite.
5. Conflicting facts are blocked until older facts are retracted or superseded.
6. Stale memory is downgraded, retracted, or manually compacted so the active memory stays fast and useful.

## MVP Features

### 1. MCP-First Integration

5head must work through MCP so one integration model can support many clients.

Required outcome:

- A user can wire 5head into multiple MCP-compatible LLM clients with config changes only.

### 2. Three Curated Memory Files

5head exposes exactly three primary memory surfaces:

- `SOUL.md`: stable identity, principles, and persistent behavioral guidance
- `MEMORY.md`: project facts, conventions, architecture notes, and active learnings
- `USER.md`: operator preferences, style, context, and persistent user-specific facts

These files are the main UX surface for both humans and agents.

Required properties:

- Local files
- Easy to inspect manually
- Small enough to stay high-signal
- Stable enough to be reused across sessions

### 3. Local-First Durable Storage

5head stores memory locally by default.

Storage layers:

- Markdown files as the human-readable source of active memory
- SQLite as the machine-readable source of provenance and fact history

Required outcome:

- Memory remains private, local, portable, and resilient to machine or session resets.

### 4. Provenance for Every Fact

Every meaningful write must be traceable.

Required provenance support:

- Timestamp
- Source kind
- File target
- Fact status
- Confirmation and retraction history

This is what makes the system trustworthy instead of becoming an unreviewable AI note dump.

### 5. Contradiction Detection

5head must detect conflicting facts before they land in active memory.

Required behavior:

- A candidate write can be checked before execution.
- Contradictory active or confirmed facts are rejected unless prior facts are explicitly retracted or superseded.
- Contradictory uncertain facts remain visible in listings and provenance history, but do not block new writes.

### 6. Active Forgetting

The system must keep memory sharp over time.

Required behavior:

- Old knowledge is not allowed to accumulate forever in active memory.
- Facts can become uncertain or stale.
- Facts can be retracted.
- Active markdown can be manually compacted with `replace` when it becomes noisy.

Engineering stance for MVP:

- V1 compaction means an explicit `replace` write to the target memory file.
- The system records the replacement and retracts superseded facts that disappear from the new content.
- V1 does not include automated compaction loops, summarizers, or background cleanup jobs.

The principle is not blind deletion. The principle is that active memory should remain accurate and fast.

### 7. Per-Repo Memory as the Initial Product Surface

The first shipping surface should be repo memory.

Required behavior:

- Memory is rooted in the current project or repo.
- Memory can be reviewed in context.
- Memory travels with the project when appropriate.

This keeps the initial product grounded, testable, and easy to trust.

## Recommended Storage Model

- Repo-local active memory at `.agent-memory/`
- Local provenance DB in a user cache directory
- Future global vault support layered on top rather than replacing repo-local memory

## Repo-First Scope Clarification

- MVP behavior is repo-first when a repo boundary can be found.
- A user-level global path may exist as a fallback when no repo-local root can be inferred.
- That fallback should stay low-friction in the implementation, but global memory is not a marketed V1 feature.

## Non-Goals For MVP

These are valuable, but they are not required for the first meaningful release:

- Auto-installing or auto-editing every MCP client config
- Full cross-client bootstrap automation
- Rich dashboard UX
- Embeddings-based semantic recall
- Global life-memory as a polished first-release product
- Deep Obsidian PARA automation beyond a simple local vault-compatible layout

## Success Criteria

The MVP is successful if:

1. A user can connect 5head to real MCP clients and keep memory across sessions.
2. The three markdown files remain concise, readable, and useful.
3. Conflicting memory is detectably safer than naive append-only notes.
4. Provenance makes memory auditable and retractable.
5. The system remains local-first and understandable without hidden cloud state.

## Product Positioning

5head is not just note-taking for LLMs.

It is:

- curated local memory
- with provenance
- and active forgetting

That is the core differentiation.
