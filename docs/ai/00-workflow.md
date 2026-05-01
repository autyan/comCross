# Workflow Rules

## Before Editing

- Read `AGENTS.md` first, then read the task-specific rule files.
- Confirm target, non-targets, and acceptance criteria when the task is broad, risky, or changes contracts.
- Check existing implementation first. Prefer repository patterns over new abstractions.
- If the user asks for analysis, a plan, or discussion only, do not edit files.
- If the task is part of a commit-by-commit sequence, keep the scope small enough to review and verify independently.

## During Implementation

- Keep edits closely scoped to the request.
- Do not add unrelated cleanup, formatting churn, dependency changes, or broad rewrites.
- Preserve dirty work that you did not make.
- Use structured APIs or parsers over ad hoc string manipulation when available.
- Add abstractions only when they reduce real coupling, duplication, or complexity in the current codebase.
- Avoid placeholder implementations, fake persistence, or TODO comments as delivered behavior.

## Blocking Decisions

Ask or explicitly state the assumed decision before proceeding when the work affects:

- Plugin manifest structure or semantics.
- `PluginSdk` public API, serialized types, or enum semantics.
- Core/Host/Plugin IPC, event, or shared-memory protocols.
- Workspace, config, database, or other persisted structures.
- Runtime entry points, publish layout, plugin directory layout, or data directory rules.
- Security, permissions, capability IDs, plugin IDs, or privilege semantics.

## Verification

- Run the narrowest useful verification for the change.
- Prefer `dotnet build ComCross.sln --no-restore` for code changes.
- Run guardrails relevant to the touched area.
- Report known unrelated failures separately from failures introduced by the current scope.

## Deliverables

- State what changed and where.
- State what verification was run.
- Mention residual risk or follow-up only when useful.
- For user-accepted commit flow, do not commit until the user explicitly approves the verified scope.
