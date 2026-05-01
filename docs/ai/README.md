# AI Rules Index

`AGENTS.md` is the single repository entry point for AI coding agents. This directory contains the detailed rule files referenced by that entry point.

Read only the files relevant to the current task, but always keep the root workflow and hard rules in mind.

## Files

- `00-workflow.md`: collaboration process, scope control, implementation discipline, and verification.
- `01-architecture-boundaries.md`: project responsibilities, dependency direction, naming, and placement.
- `02-shell-ui.md`: Shell MVVM, UI services, dialog workflows, Service Locator constraints, and i18n.
- `03-contracts-persistence.md`: explicit contract changes, persisted state, migrations, and compatibility.
- `04-plugins-hosts-ipc.md`: plugin/host/capability boundaries, IPC, shared memory, and publish layout.
- `05-guardrails-verification.md`: repository guardrail scripts and verification expectations.
- `06-git-collaboration.md`: commits, dirty worktrees, user acceptance, and release-sensitive operations.
