# Internationalization (i18n) Implementation

## Overview

ComCross now includes a complete internationalization system that allows the application to support multiple languages. The system is JSON-based, extensible, and follows .NET localization best practices.

## Architecture

### Components

1. **LocalizationService** (`src/Core/Services/LocalizationService.cs`)
   - Core service for loading and retrieving localized strings
   - Supports dynamic culture switching
   - Automatic creation of default translation files
   - JSON-based resource loading

2. **ILocalizationService** (`src/Shared/Services/ILocalizationService.cs`)
   - Interface defining the localization contract
   - Methods: `GetString(key)`, `SetCulture(culture)`, `GetCurrentCulture()`

3. **LocalizedStringsViewModel** (`src/Shell/ViewModels/LocalizedStringsViewModel.cs`)
   - View model wrapper for XAML data binding
   - Exposes all localized strings as properties
   - Supports property change notifications for culture switching

4. **LocaleCultureInfo** (`src/Shared/Services/ILocalizationService.cs`)
   - Custom culture information record
   - Used to avoid naming conflicts with System.Globalization.CultureInfo

### Resource Files

Localization resources are stored as JSON files in `src/Core/Resources/Localization/`:

```
src/Core/Resources/Localization/
├── strings.en-US.json    # English (Default)
└── strings.zh-CN.json    # Simplified Chinese
```

## Supported Languages

| Language | Culture Code | Status |
|----------|-------------|---------|
| English | en-US | ✅ Default |
| Simplified Chinese | zh-CN | ✅ Complete |

## Translation Keys

The system uses dot-notation for hierarchical organization:

### Application Level
- `app.title` - Application title

### Menu
- `menu.connect` - Connect button
- `menu.disconnect` - Disconnect button
- `menu.clear` - Clear button
- `menu.export` - Export button

### Connect Dialog
- `dialog.connect.title` - Dialog title
- `dialog.connect.port` - Port label
- `dialog.connect.baudrate` - Baud rate label
- `dialog.connect.sessionname` - Session name label
- `dialog.connect.cancel` - Cancel button
- `dialog.connect.connect` - Connect button

### Sidebar
- `sidebar.devices` - Devices section title
- `sidebar.sessions` - Sessions section title

### Message Stream
- `stream.search.placeholder` - Search box placeholder
- `stream.metrics.rx` - RX metrics label
- `stream.metrics.tx` - TX metrics label
- `stream.metrics.lines` - Lines metrics label

### Tool Dock
- `tool.send` - Send tab title
- `tool.filter` - Filter tab title
- `tool.highlight` - Highlight tab title
- `tool.export` - Export tab title
- `tool.send.quickcommands` - Quick commands section
- `tool.send.message` - Message section
- `tool.send.hexmode` - HEX mode checkbox
- `tool.send.addcr` - Add CR checkbox
- `tool.send.addlf` - Add LF checkbox
- `tool.send.button` - Send button
- `tool.send.cmd.*` - Command labels

### Status Bar
- `status.ready` - Ready status message

## Usage Guide

### For Developers

#### Adding a New Translation Key

1. Add the key-value pair to all JSON files in `src/Core/Resources/Localization/`:

**strings.en-US.json:**
```json
{
  "my.new.key": "My New Text"
}
```

**strings.zh-CN.json:**
```json
{
  "my.new.key": "我的新文本"
}
```

2. Add a property to `LocalizedStringsViewModel.cs`:
```csharp
public string MyNewKey => _localization.GetString("my.new.key");
```

3. Use in XAML:
```xml
<TextBlock Text="{Binding LocalizedStrings.MyNewKey}" />
```

#### Adding a New Language

1. Create a new JSON file: `strings.{culture}.json`
   - Example: `strings.ja-JP.json` (Japanese)
   - Example: `strings.de-DE.json` (German)

2. Copy the structure from `strings.en-US.json`

3. Translate all values:
```json
{
  "app.title": "コムクロス",
  "menu.connect": "接続",
  ...
}
```

4. The language will be automatically loaded based on system culture

#### Switching Languages Programmatically

```csharp
// In MainWindowViewModel or other service
var localizationService = new LocalizationService();
localizationService.SetCulture(new LocaleCultureInfo("zh-CN"));

// Refresh the UI
LocalizedStrings.RefreshStrings();
```

### For Translators

Translation files use simple JSON format:

```json
{
  "key": "Translated text",
  "another.key": "Another translated text"
}
```

**Guidelines:**
- Keep keys in English (dot-notation)
- Translate only the values
- Maintain consistent terminology
- Test with actual UI to ensure text fits
- Consider context (button labels vs. descriptions)

## Technical Details

### Initialization

The `LocalizationService` is initialized in `MainWindowViewModel`:

```csharp
public MainWindowViewModel()
{
    _localization = new LocalizationService();
    LocalizedStrings = new LocalizedStringsViewModel(_localization);
    // ...
}
```

### Default Behavior

1. System culture is detected automatically
2. If matching translation file exists, it's loaded
3. If not found, falls back to `en-US`
4. Missing keys return the key itself (for debugging)

### File Structure

JSON files must be embedded resources with build action `EmbeddedResource`:

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Localization\strings.en-US.json" />
  <EmbeddedResource Include="Resources\Localization\strings.zh-CN.json" />
</ItemGroup>
```

### Performance Considerations

- Translation strings are loaded once at startup
- Dictionary lookup is O(1)
- No runtime file I/O after initialization
- Culture switching requires UI refresh

## Testing

### Manual Testing

1. **Test Default Language (English):**
   ```bash
   dotnet run --project src/Shell/ComCross.Shell.csproj
   ```

2. **Test Chinese:**
   ```bash
   LANG=zh_CN.UTF-8 dotnet run --project src/Shell/ComCross.Shell.csproj
   ```

3. **Verify All Keys:**
   - Check all menus
   - Open connect dialog
   - Verify sidebar labels
   - Check tool dock tabs
   - Verify status bar

### Automated Testing

Currently, localization has no unit tests. Recommended tests:

```csharp
[Fact]
public void LocalizationService_LoadsEnglishByDefault()
{
    var service = new LocalizationService();
    var title = service.GetString("app.title");
    Assert.Equal("ComCross", title);
}

[Fact]
public void LocalizationService_SwitchesToChineseCorrectly()
{
    var service = new LocalizationService();
    service.SetCulture(new LocaleCultureInfo("zh-CN"));
    var title = service.GetString("app.title");
    Assert.Equal("串口工具箱", title);
}
```

## Future Enhancements

1. **UI Language Selector**
   - Add dropdown menu to switch languages at runtime
   - Save preference to configuration

2. **Pluralization Support**
   - Handle singular/plural forms
   - Example: "1 device" vs "2 devices"

3. **Date/Time Formatting**
   - Culture-specific date formats
   - Time zone handling

4. **Number Formatting**
   - Decimal separators
   - Thousand separators

5. **Right-to-Left (RTL) Support**
   - Arabic, Hebrew support
   - UI mirroring

6. **Fallback Chain**
   - en-US → en → invariant
   - zh-CN → zh-TW → en-US

7. **Translation Validation**
   - Detect missing keys
   - Detect unused keys
   - Check format string consistency

## References

- [.NET Globalization](https://docs.microsoft.com/en-us/dotnet/core/extensions/globalization)
- [Avalonia Localization](https://docs.avaloniaui.net/docs/basics/user-interface/assets#localization)
- [JSON Format Specification](https://www.json.org/)

## Contributors

To contribute translations:
1. Fork the repository
2. Add/update translation files
3. Test with your system locale
4. Submit a pull request

## License

Localization resources are part of ComCross and follow the same MIT license.
