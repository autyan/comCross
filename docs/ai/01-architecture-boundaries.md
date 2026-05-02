# Architecture And Boundary Rules

## Project Responsibilities

- `src/Platform`: platform capabilities only. It must not reference other in-repository projects.
- `src/PluginSdk`: public plugin SDK and protocol-facing types only. It must not reference other in-repository projects.
- `src/Shared`: shared contracts, events, models, and small pure helpers. It must not own business workflows.
- `src/Core`: business orchestration, persistence, plugin runtime coordination, workspace/session coordination.
- `src/Startup`: minimal GUI launcher and user-facing entry point. Keep dependencies small; do not move Core workflows into Startup.
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

## Producer/Consumer Boundary

ComCross has an explicit producer/consumer boundary between the main program and bus plugins.

- Bus plugins are producers of domain facts: session metadata, reconnect policy, session topology, managed resources, UI schema, UI state, state patches, and plugin-owned connection semantics.
- Core and Shell are consumers of those facts. They provide lifecycle orchestration, persistence, host coordination, generic UI rendering, and user workflow glue.
- Core and Shell must not infer bus-domain behavior from plugin-private parameters, capability-specific payload shapes, or hardcoded plugin ids.
- Core and Shell may route by stable platform identifiers such as plugin id or capability id only when performing generic platform dispatch, not when deciding bus-domain meaning.
- If Shell needs to display or operate on a bus-domain fact, first require the plugin to produce that fact through a contract. Do not parse private parameters in Shell as a shortcut.
- If Core needs durable session facts, persist the plugin-produced public metadata or patches. Do not reconstruct those facts later from plugin-private storage.

Review smell: `Core` or `Shell` code that branches on a bus plugin id/capability id to decide listener/client semantics, parent-child topology, reconnectability, display icon, business labels, or parameter migration is presumed wrong unless it is clearly generic routing.

## Async Boundary Rules

- Use `async/await` only when a method crosses a real asynchronous boundary:
  file, database, network, IPC, process, stream, cancellable wait, timer, or UI
  dispatcher work.
- Do not keep `async` on pure in-memory operations such as collection refresh,
  ViewModel property synchronization, synchronous resource release, or logging.
- If an interface, override, lifecycle contract, or public call chain must
  return `Task` but the current implementation is synchronous, return
  `Task.CompletedTask` or `Task.FromResult(...)` directly. Do not add
  `await Task.CompletedTask`, `await Task.Yield()`, or fake background work to
  silence compiler warnings.
- If a method is currently synchronous but is part of a broader async lifecycle
  where future I/O is plausible, keeping the `Task` contract is acceptable; the
  implementation should still avoid an `async` state machine until it actually
  awaits something.
- Platform-specific APIs must declare their platform boundary explicitly with
  supported-platform attributes or platform-specific project/file structure so
  release builds do not hide CA1416 warnings.
