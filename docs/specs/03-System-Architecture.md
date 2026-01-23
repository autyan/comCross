# System Architecture (MVP)

## High-Level Modules
- Shell: window, layout, theming, tool management.
- Core Services: device, message stream, config, event bus, logging.
- Workspace: sessions, state, and stream binding.
- Tools: modular UI panels around the workspace.
- Platform: platform-specific capabilities and integrations.
- Plugins: extend tools and provide bus/device capabilities.

## Core Buses
### EventBus
- Purpose: decouple modules (connection status, tool actions, system events).
- Requirements: stable API, low overhead, supports multiple subscribers.

### MessageStreamService
- Purpose: unify incoming data into per-session streams.
- Requirements: high throughput, paging, search, filtering, subscriptions.

## Data Flow
1) DeviceAdapter -> DeviceConnection -> MessageStreamService
2) MessageStreamService -> Workspace UI
3) Tools operate via WorkspaceContext and EventBus
4) ConfigService persists WorkState and Toolset

## Services
- DeviceService: enumerates ports and manages connections.
- SessionManager: creates and tracks sessions.
- MessageStreamService: buffering, filtering, querying, highlighting.
- ConfigService: save/load Toolset and WorkState.
- ToolManager: plugin discovery and lifecycle.
- LogService: internal diagnostics and errors.

## Core Interfaces (Conceptual)
- IDeviceAdapter
  - ListPorts()
  - Open(settings) -> IDeviceConnection
- IDeviceConnection
  - ReadAsync / WriteAsync / Close / Status
- IMessageStream
  - Append / Query / Subscribe / Clear
- IWorkspaceContext
  - ActiveSession / MessageStream / Metrics
- IToolModule
  - Metadata / CreateView(context) / OnActivate / OnDeactivate
- IEventBus
  - Publish / Subscribe
- IConfigStore
  - Load/Save Toolset and WorkState
