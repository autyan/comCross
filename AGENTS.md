# ComCross AI Collaboration Guide

This is the repository-level entry point for AI coding agents. Read this file before changing code, then read the referenced `docs/ai/*` files that match the task scope.

`.ai-rules.md` is currently retained for compatibility. For new work, use this `AGENTS.md` workflow and the `docs/ai/*` rule files as the active rule system.

## Project Shape

- Product: ComCross, a cross-platform embedded communication toolbox.
- Shell: `src/Shell/`, Avalonia desktop UI using MVVM.
- Core: `src/Core/`, business orchestration, persistence, plugin runtime coordination, workspace/session services.
- Shared: `src/Shared/`, shared contracts, events, models, and small pure helpers.
- Plugin SDK: `src/PluginSdk/`, public plugin API and protocol-facing types.
- Platform: `src/Platform/`, platform capabilities only.
- Hosts: `src/PluginHost/`, `src/SessionHost/`, `src/ExtensionHost/`.
- Built-in plugins: `src/Plugins/*`.
- Guardrails and repository scripts: `repo-tools/`.

## Required Workflow

1. Identify the affected scope before editing.
2. Read this file, then read the relevant `docs/ai/*` files listed below.
3. Keep changes scoped. Do not mix unrelated refactors, formatting churn, dependency changes, or opportunistic cleanups.
4. Preserve user changes. Never revert work you did not make unless explicitly asked.
5. For broad or risky work, propose a scope first. If the user says "先给方案", "只分析", "先讨论", or similar, do not edit files.
6. For commit-driven work, complete one coherent scope per commit and wait for user acceptance before committing unless the user explicitly says otherwise.

## Hard Rules

- Do not silently change repository boundaries or cross-process contracts.
- Do not deliver mock, placeholder, or TODO-based implementations for real capabilities.
- Do not add user-visible raw strings. Use i18n keys and update the required resources when the task touches UI copy.
- Do not use Service Locator outside the composition root unless the reason and boundary are explicit.
- Do not change runtime entry points, publish layout, plugin directory layout, or data directory rules unless the task explicitly targets those contracts.
- Do not put new responsibilities into `Shared`, `Core`, or `Shell` when the responsibility clearly needs a narrower project or domain.

## Required References

Choose the detailed rule files by task scope:

- General workflow, scope control, commit flow, and verification:
  `docs/ai/00-workflow.md`
- Module boundaries, naming, dependency direction, and placement:
  `docs/ai/01-architecture-boundaries.md`
- Shell MVVM, UI services, dialogs, and i18n rules:
  `docs/ai/02-shell-ui.md`
- Contract and persistence changes:
  `docs/ai/03-contracts-persistence.md`
- Plugin, host, capability, IPC, and shared-memory work:
  `docs/ai/04-plugins-hosts-ipc.md`
- Guardrails and verification commands:
  `docs/ai/05-guardrails-verification.md`
- Git collaboration, commits, dirty worktrees, and release-sensitive work:
  `docs/ai/06-git-collaboration.md`

If a task touches multiple scopes, apply all relevant files. If the scope is unclear, state the assumed scope before editing.

## Traceability Requirement

At the start of a non-trivial task, state which rule files are being used and why. Keep it concise.

Example:

```text
Using AGENTS.md, docs/ai/01-architecture-boundaries.md, and docs/ai/02-shell-ui.md because this changes Shell/Core boundaries.
```

For small direct tasks, a short statement is enough:

```text
Using AGENTS.md and docs/ai/06-git-collaboration.md.
```

If implementation scope changes during the task, mention the newly relevant rule file before editing that area.

## Verification Defaults

- Build: `dotnet build ComCross.sln --no-restore`
- Project boundary guardrail: `bash repo-tools/check-project-boundaries.sh`
- Shell i18n raw-string guardrail: `bash repo-tools/check-shell-i18n.sh`
- Shell i18n key guardrail: `bash repo-tools/check-shell-i18n-keys.sh`

Run the narrowest useful verification for the change. If a relevant verification cannot be run, report why.

## Interaction Contract

Useful prompts:

```text
Use AGENTS.md. First provide a plan, do not edit files.
```

```text
Use AGENTS.md. Implement one commit scope, verify locally, and wait for my acceptance before committing.
```

```text
Use AGENTS.md. This touches plugin IPC; apply docs/ai/04-plugins-hosts-ipc.md.
```
