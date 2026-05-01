# Architecture And Boundary Rules

## Project Responsibilities

- `src/Platform`: platform capabilities only. It must not reference other in-repository projects.
- `src/PluginSdk`: public plugin SDK and protocol-facing types only. It must not reference other in-repository projects.
- `src/Shared`: shared contracts, events, models, and small pure helpers. It must not own business workflows.
- `src/Core`: business orchestration, persistence, plugin runtime coordination, workspace/session coordination.
- `src/Shell`: UI and interaction. It must not own business rules.
- `src/PluginHost`, `src/SessionHost`, `src/ExtensionHost`: host process boundaries and host-side runtime behavior.
- `src/Plugins/*`: built-in plugins that use the public SDK and host contracts.

## Dependency Direction

- Keep `Platform` and `PluginSdk` independent.
- Keep Shell dependencies pointed at stable use-case services or domain services, not scattered Core internals.
- Do not move orchestration into `Shared` to avoid dependency direction problems.
- If a new responsibility clearly does not fit an existing project, prefer a new project or domain boundary over turning `Shared`, `Core`, or `Shell` into a catch-all.

## Naming Style

Use names that match existing repository language:

- `Service`: business capability, use-case entry point, or host-side helper.
- `Coordinator`: cross-service domain orchestration.
- `Manager`: runtime state or lifecycle manager.
- `Factory`: object creation.
- `HostService`: Shell-side helper that provides host behavior for plugin UI or runtime integration.

Avoid naming classes after design patterns, such as `Facade`, unless the repository already uses that term consistently. The rule concept can still be "facade-like"; the class name should fit the codebase.

## Core Organization

- Prefer domain grouping over a flat `Services` bucket when adding new code.
- Existing domains include workspace/session lifecycle, plugin runtime, host IPC, messaging, shared memory, localization, configuration, and logging.
- When reducing coupling, first introduce a narrow service boundary, then move behavior behind it in small, reviewable steps.

## Shell/Core Boundary

- Shell ViewModels may bind UI state and commands, but should not learn low-level Core details such as dispatcher payload shape, plugin-host message names, or workspace persistence mechanics.
- Put Shell-facing use cases into Shell services when they adapt Core services for UI workflows.
- Put domain orchestration into Core services or coordinators when the behavior is not UI-specific.
