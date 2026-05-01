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
