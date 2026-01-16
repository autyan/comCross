# ComCross - Cross-Platform Serial Toolbox

A modern, modular embedded development toolbox designed for serial communication and device debugging.

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)
![Platform](https://img.shields.io/badge/platform-Linux%20%7C%20Windows-lightgrey.svg)
![Version](https://img.shields.io/badge/version-v0.3.0-blue.svg)

## ✨ Features

- 🚀 **High-Performance Message Stream** - Supports high-frequency data reception and real-time display
- 🔌 **Modular Tool System** - Extensible plugin architecture
- 💾 **State Persistence** - Automatic workspace and session state saving
- 🎨 **Modern UI** - Cross-platform dark theme interface based on Avalonia
- 📊 **Multi-Session Support** - Manage multiple serial port connections simultaneously
- 🔍 **Powerful Search** - Supports keyword and regular expression search
- 🌐 **Internationalization** - Built-in i18n support (English, Simplified Chinese)

## 🎯 Release Goals

### Completed (v0.3.x) ✅

- ✅ Serial device enumeration and connection
- ✅ Real-time message reception and display
- ✅ Multi-session management
- ✅ Message search and filtering
- ✅ RX/TX statistics
- ✅ Workspace state persistence
- ✅ Unit test coverage
- ✅ Internationalization support
- ✅ Plugin system with process isolation
- ✅ Official plugins (Stats, Protocol, Flow)
- ✅ Plugin manager UI
- ✅ Command system with hotkeys and import/export
- ✅ Export tooling with range selection

### 🗺️ Roadmap to v1.0

ComCross is on a journey to become the **essential toolbox for embedded developers**. See our detailed roadmap:

- 📖 [**v1.0 Roadmap Summary**](docs/V1.0-Roadmap-Summary.md) - Quick overview
- 📋 [**Full Roadmap Document**](docs/specs/12-V1.0-Roadmap.md) - Detailed planning

**Timeline**: v0.3 (Now) → v0.4 (Q1'26) → v0.5 (Q2'26) → v0.6 (Q3'26) → v1.0 (Q1'27)

**v1.0 Vision**:
- 🔌 Multi-bus support (Serial/TCP/UDP/CAN/I2C/SPI)
- 🧩 10+ official plugins
- 🤖 JavaScript scripting engine
- 🔥 Plugin hot-swapping
- 📚 Complete documentation
- 🌍 Thriving community

## 🚀 Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Linux or Windows operating system

### Build and Run

```bash
# Clone the repository
git clone https://github.com/yourusername/comCross.git
cd comCross

# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run the application
dotnet run --project src/Shell/ComCross.Shell.csproj

# Run tests
dotnet test
```

## 🏗️ Project Structure

```
ComCross/
├── src/
│   ├── Shared/          # Shared models and interfaces
│   ├── Core/            # Core services (EventBus, MessageStream, DeviceService, LocalizationService)
│   ├── Adapters/        # Platform adapters (Serial)
│   ├── Tools/           # Pluggable tool modules
│   └── Shell/           # Avalonia UI main application
├── tests/               # Unit tests
├── docs/
│   └── specs/           # Product and system specifications
└── assets/              # Visual assets
```

## 🌐 Internationalization

ComCross supports multiple languages through a JSON-based localization system.

### Supported Languages

- **English** (en-US) - Default
- **Simplified Chinese** (zh-CN) - Simplified Chinese

### Adding New Languages

1. Create a new JSON file in `src/Assets/Resources/Localization/`:
   ```
   strings.{culture}.json
   ```
   Example: `strings.ja-JP.json` for Japanese

2. Copy the structure from `strings.en-US.json` and translate the values:
   ```json
   {
     "app.title": "Your Translation",
     "menu.connect": "Your Translation",
     ...
   }
   ```

3. The language will be automatically loaded based on system culture or can be set programmatically:
   ```csharp
   localizationService.SetCulture(new LocaleCultureInfo("ja-JP"));
   ```

### Adding New Translation Keys

1. Add the key to all language files in `src/Assets/Resources/Localization/`
2. Add a corresponding property to `LocalizedStringsViewModel.cs` if needed:
   ```csharp
   public string MyNewKey => _localization.GetString("my.new.key");
   ```
3. Use in XAML:
   ```xml
   <TextBlock Text="{Binding LocalizedStrings.MyNewKey}" />
   ```

## 📖 Documentation

- [MVP Acceptance Document](docs/MVP-Acceptance.md)
- [Development Summary](docs/Development-Summary.md)
- [UI/UX Specification](docs/specs/06-UI-UX-Spec.md)
- [MVP Scope](docs/specs/02-MVP-Scope.md)
- [System Architecture](docs/specs/03-System-Architecture.md)
- [Plugin System](docs/specs/04-Plugin-System.md)
- [Workspace State](docs/specs/05-Workspace-State.md)

## 🛠️ Technology Stack

- **.NET SDK**: 10.0
- **Runtime baseline**: .NET 8 LTS
- **UI Framework**: Avalonia 11.2.2
- **Architecture**: MVVM + Service Layer
- **Testing**: xUnit
- **Serialization**: System.Text.Json
- **Localization**: JSON-based i18n

## 📋 Roadmap

### v0.3.0 - ✅ Completed
- Plugin system with process isolation
- Official plugins (Stats, Protocol, Flow)
- Command system and export tooling
- Packaging scripts for release artifacts

### v0.3.x - Planned
- MSI/DEB/RPM installers
- Advanced filter and highlight rules

## 🤝 Contributing

Contributions are welcome. Please see the contribution guidelines.

## 📄 License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.

---

**Current status**: MVP completed, core features implemented and tested.

## ✅ Runtime Baseline Commitment

ComCross release packages target the .NET 8 LTS runtime baseline. We commit to
keeping this baseline stable and will only change it when strictly necessary.
Any breaking change will be announced ahead of time.
