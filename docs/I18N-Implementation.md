# Internationalization Implementation

## Overview

ComCross uses a Core-owned localization service and Shell binding helper interfaces for user-visible UI text.

Supported cultures:

- `en-US` as the built-in fallback.
- `zh-CN` loaded from the embedded JSON resource.

Any unsupported culture falls back to English.

## Runtime Components

- `ILocalizationService` (`src/Shared/Services/ILocalizationService.cs`)
  - `GetString(key, args)`
  - `SetCulture(cultureCode)`
  - `CurrentCulture`
  - `AvailableCultures`
  - `LanguageChanged`

- `LocalizationService` (`src/Core/Services/LocalizationService.cs`)
  - Loads hardcoded English defaults.
  - Loads additional cultures from embedded JSON.
  - Emits language-change notifications.

- `ILocalizationStrings` / `LocalizationStrings`
  - Provides indexer-style XAML and view-model access through `L[key]`.

- Plugin localization
  - Plugins can provide manifest `i18n` dictionaries.
  - Shell/Core can notify plugins with `plugin.language.changed`.

`LocalizedStringsViewModel` is deprecated. New Shell code should use `L[key]` or `ILocalizationService.GetString(...)`.

## Resource File

Additional localization resources are embedded in:

```text
src/Assets/Resources/Localization/strings.json
```

The JSON contains non-English cultures. English is maintained in `LocalizationService` so i18n key guardrails can validate Shell references against the canonical fallback dictionary.

Example:

```json
{
  "defaultCulture": "en-US",
  "cultures": {
    "zh-CN": {
      "app.title": "ComCross - 串口工具箱"
    }
  }
}
```

## Adding Shell Copy

1. Add the English key/value to `LocalizationService`.
2. Add translations to `strings.json` for supported non-English cultures.
3. Use `L[key]` in XAML or `Localization.GetString(key)` in view models/services.
4. Run:

```bash
bash repo-tools/check-shell-i18n.sh
bash repo-tools/check-shell-i18n-keys.sh
```

## Adding Plugin Copy

Plugin-owned text should usually live in the plugin manifest `i18n` section and be referenced by plugin schemas or plugin UI state.

Plugins should provide English strings and may provide localized strings for supported cultures.

## Language Resolution

At startup:

1. App settings are loaded.
2. If `FollowSystemLanguage` is enabled, Core resolves from the system UI culture.
3. If the exact culture is unavailable, Core falls back to the matching neutral language where supported.
4. If no supported culture is found, Core uses `en-US`.

Language changes refresh Shell bindings and notify plugins.

## Guardrails

Shell code must not introduce user-visible raw strings. The guardrails check:

- likely raw strings in `src/Shell/**/*.cs`
- Shell localization keys referenced from code/XAML against the English fallback dictionary

These checks are required for UI-copy changes.
