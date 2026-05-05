# Shell And UI Rules

## MVVM

- `src/Shell` follows MVVM: one View maps to one ViewModel.
- View code-behind should be limited to UI event bridging that cannot reasonably be expressed as binding.
- Business decisions, session lifecycle logic, plugin action payloads, and validation rules should not live in code-behind.
- Large ViewModels should be reduced by extracting use-case services and item ViewModels before doing broad UI rewrites.

## Dialogs And UI Services

- Common dialogs and message entry points must be centralized.
- Do not directly `new` shared dialogs, windows, or ViewModels at scattered call sites.
- Prefer named workflow services such as `ConnectDialogService` or `SessionRenameDialogService` over exposing a raw object factory through a static service.
- Composition root initialization may wire static compatibility bridges, but those bridges should expose narrow workflows, not general Service Locator access.

## Service Locator

- Avoid Service Locator outside the composition root.
- If a static bridge is retained for Avalonia integration, keep its exposed surface narrow and document the boundary in code or in the relevant AI rule file.
- Do not add new global service access for convenience.

## User-Visible Text And I18n

- New or changed user-visible text must use i18n keys.
- The scope includes Shell XAML/C#, plugin manifests, plugin UI schemas, plugin titles, buttons, tooltips, and prompts.
- Default required cultures are `en-US` and `zh-CN`.
- Shell must not introduce raw UI strings. Use i18n keys.
- Logs, diagnostics, and internal exception messages may be English. Use `// i18n-ignore` only for true non-UI literals or known false positives.
- Existing i18n violations should not be expanded during unrelated tasks. Track them for a dedicated cleanup scope.

## UI Behavior

- Keep interaction workflows ergonomic and predictable.
- Avoid broad visual redesigns inside architecture or service-boundary commits.
- When changing UI workflow services, preserve existing ViewModel public properties and command behavior unless the task explicitly changes UI behavior.
- Icon buttons must use a single interactive outer control that owns size, hit testing, hover, pressed, disabled, focus, and tooltip states. The visible icon must be passive content inside that control.
- Visual feedback must match the clickable region: never make a small `Path` or nested visual look like the control while the actual clickable area is a different or smaller XAML layer.
- Related icon actions in the same toolbar/row must use the same component structure, sizing, tooltip pattern, and disabled-state expression. Compact icon buttons should use at least a 28px hit area unless an existing component style defines a larger one.
- Icon+text buttons must use a fixed-size icon slot and a fixed text line box instead of relying on `StackPanel` baseline behavior or ad hoc per-button transforms. Icons in these buttons should use explicit geometry bounds inside the slot, not unknown cropped geometry stretched to fit. Hover/pressed feedback for prominent floating buttons should use border, shadow, or color changes; do not use opacity changes that make the control look disabled.
- Composite input controls must have one visual frame. If an outer container owns the background, border, radius, hover, or focus state, the inner `TextBox` must be transparent and borderless, including focused state, so Avalonia's default input chrome does not create a nested box.
- Popup-backed pickers must treat the popup as a separate interaction layer. Do not decide whether a popup click is "inside" by walking ancestors from the parent control, because `Popup` content lives under a separate popup root. Prefer `Popup.IsLightDismissEnabled` for outside-click dismissal, handle item actions inside the popup explicitly, and synchronize the ViewModel search/open state from the popup close event.

## Message Viewer

- Message viewer data loading must be independent of display mode. `LiveSpool` and `Archive` both use the same streaming/window query path; display mode may rebuild rendering only, not change paging, search semantics, or the current data window.
- Message viewer search is viewer state. Search input changes should trigger debounced search, clearing the input exits search mode, and search navigation controls belong inside the message viewer surface rather than inside the search input row.
- Structured search syntax should stay compact for v0.6 scope: support direction and attributes without adding separate toolbar controls for each condition. Do not add regex, hex search, decoded-field search, indexing, or cross-session search unless a later scope explicitly asks for it.
- `Detailed` display mode uses the structured per-frame item renderer. It does not need mouse text selection in the current scope.
- `Plain` and `Slim` display modes use an aggregate text renderer so users can select text across messages. `Plain` renders a continuous plain text stream. `Slim` renders plain text payloads too, but inserts frame boundaries at `TX` / `RX` message boundaries so each frame remains distinguishable.
