# ComCross - Cross-Platform Serial Toolbox

A modern, modular embedded development toolbox designed for serial communication and device debugging.

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)
![Platform](https://img.shields.io/badge/platform-Linux%20%7C%20Windows-lightgrey.svg)

## ✨ Features

- 🚀 **High-Performance Message Stream** - Supports high-frequency data reception and real-time display
- 🔌 **Modular Tool System** - Extensible plugin architecture
- 💾 **State Persistence** - Automatic workspace and session state saving
- 🎨 **Modern UI** - Cross-platform dark theme interface based on Avalonia
- 📊 **Multi-Session Support** - Manage multiple serial port connections simultaneously
- 🔍 **Powerful Search** - Supports keyword and regular expression search
- 🌐 **Internationalization** - Built-in i18n support (English, 简体中文)

## 🎯 MVP Goals

### Completed (v0.1) ✅

- ✅ Serial device enumeration and connection
- ✅ Real-time message reception and display
- ✅ Multi-session management
- ✅ Message search and filtering
- ✅ RX/TX statistics
- ✅ Workspace state persistence
- ✅ Unit test coverage
- ✅ Internationalization support

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
- **简体中文** (zh-CN) - Simplified Chinese

### Adding New Languages

1. Create a new JSON file in `src/Core/Resources/Localization/`:
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

1. Add the key to all language files in `src/Core/Resources/Localization/`
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

- **.NET**: 10.0
- **UI Framework**: Avalonia 11.2.2
- **Architecture**: MVVM + Service Layer
- **Testing**: xUnit
- **Serialization**: System.Text.Json
- **Localization**: JSON-based i18n

## 📋 Roadmap

### v0.1 (MVP) - ✅ Completed
- Basic architecture and core services
- Serial port support
- Basic UI and message stream
- State persistence
- Internationalization support

### v0.2 - Planned
- Complete send tool (HEX mode, history)
- Data export functionality
- 高级过滤和高亮规则

### v0.3 - 计划中
- 插件动态加载
- 脚本支持
- Windows安装包

## 🤝 贡献

欢迎贡献！请查看我们的贡献指南。

## 📄 许可证

本项目采用 MIT 许可证。查看 [LICENSE](LICENSE) 文件了解详情。

---

**当前状态**: MVP已完成，所有核心功能已实现并通过测试。

