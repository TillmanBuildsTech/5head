# Roadmap

## Direction

5head should expand in layers without losing its local-first, high-signal memory model.

The roadmap is:

1. make repo memory work well
2. make setup easy
3. expand into a global knowledge layer
4. improve memory quality and retrieval over time

## Phase 1: Repo Memory

Objective:

- Ship the smallest trustworthy memory product for MCP-compatible workflows.

Deliverables:

- Three constrained markdown files
- Local provenance DB
- Contradiction checks
- Confirm and retract flows
- Staleness handling
- Core MCP tools

Why this phase matters:

- It is easy to understand.
- It is easy to test.
- It is easy to trust.
- It gives immediate value to coding workflows.

## Phase 2: Install And Integration Automation

Objective:

- Reduce setup friction without changing the underlying product model.

Deliverables:

- Better install flow
- Detection of common client config locations
- Optional guided config or patch generation for client setup
- Stronger packaging across supported platforms

Why this phase matters:

- Distribution improves once the product already works.
- This lowers adoption cost for less technical users.

## Phase 3: Global Vault Layer

Objective:

- Expand from repo-local memory into a longer-lived personal knowledge layer.

Deliverables:

- Vault-level memory structure compatible with local markdown workflows
- Obsidian-friendly organization
- Cross-project summaries and carry-forward memory
- Clear boundary between repo memory and global memory

Why this phase matters:

- Users want memory across planning, building, refactoring, and general life or work context.
- The vault becomes the long-lived aggregation layer while repo memory remains the active working set.

## Phase 4: Memory Quality Automation

Objective:

- Improve the sharpness of memory without bloating the active files.

Deliverables:

- Better compaction strategies
- Automated stale fact review flows
- Smarter conflict grouping and supersession handling
- Heuristics for promoting, demoting, and pruning memory

Why this phase matters:

- Quality of memory is the product moat.
- Retrieval speed and trust depend on disciplined memory management.

## Phase 5: Broader Harness Coverage

Objective:

- Reach more clients and workflows while keeping MCP as the clean core integration model.

Deliverables:

- More tested client setups
- Better harness-specific onboarding
- Optional adapters for workflows that do not expose MCP cleanly

Why this phase matters:

- The long-term product should be present wherever people think, build, and iterate with LLMs.

## Product Principles For All Phases

- Local first by default
- Human-readable memory surfaces
- Machine-verifiable provenance
- Sharp memory over infinite memory
- Minimal hidden state
- Portable across tools and time

## Near-Term Execution Priority

If there is tension between scope and polish, prioritize:

1. reliable repo memory
2. reliable provenance
3. reliable contradiction handling
4. simpler onboarding
5. broader integrations

This keeps the product centered on trust and usefulness rather than feature count.
