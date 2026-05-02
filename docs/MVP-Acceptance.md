# ComCross v0.4 Acceptance

## Version Info

- Version: 0.4.0
- Date: 2026-05-02
- Status: Release preparation

## Acceptance Criteria

### 1. Architecture

- [x] `Shell` uses Avalonia MVVM and Shell-facing services.
- [x] `Core` owns orchestration, persistence, plugin runtime coordination, and message flow.
- [x] `PluginSdk` is the public plugin-facing API.
- [x] Built-in plugins use the same SDK contracts expected of external plugins.
- [x] Shell raw plugin-host IPC is removed from normal UI flows.
- [x] Remaining Shell static bridges are documented as controlled v0.4 exceptions.

### 2. Bus Plugins

- [x] Serial adapter connects with plugin-owned port scanning.
- [x] TCP client connects, disconnects, reconnects when allowed, and surfaces send failures.
- [x] TCP listener accepts clients as scoped child sessions.
- [x] UDP client sends and receives on a connected endpoint.
- [x] UDP listener receives datagrams on the listener session without creating child sessions.
- [x] UDP listener replies through plugin-produced transmit targets.

### 3. Sessions And Workspace

- [x] Workloads and session descriptors persist across restarts.
- [x] Sessions restore as disconnected descriptors, not live connections.
- [x] Plugin startup session-state initialization can patch metadata/storage.
- [x] Session deletion removes the descriptor and plugin-owned session storage.
- [x] v0.3 legacy session state is intentionally unsupported.

### 4. Messages

- [x] RX and TX frames enter the same render/search pipeline.
- [x] Frame attributes are normalized and bounded.
- [x] Attribute search and display work for plugin-produced facts.
- [x] Direction is represented by frame/message direction, not by attributes.
- [x] Send-result errors are shown to the user.

### 5. Shell UI

- [x] Quick-create and session list consume plugin-produced adapter/session facts.
- [x] Session detail reuses the common connection UI and view-model path.
- [x] Quick commands support editable user storage and localized defaults.
- [x] Search popups and icon-button hit targets follow the Shell UI rules.
- [x] English and Simplified Chinese resources are available.

### 6. Verification

Required before release:

```bash
dotnet build ComCross.sln --no-restore
dotnet test ComCross.sln --no-build
bash repo-tools/check-project-boundaries.sh
bash repo-tools/check-shell-i18n.sh
bash repo-tools/check-shell-i18n-keys.sh
git diff --check
```

## Known Deferred Work

- File-stream-backed message storage/display.
- Complete removal of remaining Shell static bridges.
- Plugin diagnostics and test tooling through explicit services.
- Protocol parsing cache and CPDL improvements.
- Installer/security hardening for permissions and plugin trust.

## Acceptance Conclusion

v0.4 is ready when the criteria above pass manual validation and the verification commands pass on the release candidate commit.
