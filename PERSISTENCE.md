# ComCross 持久化说明文档

本文档描述 ComCross 应用中所有持久化相关的业务和实现细节。

## 概述

ComCross 使用两种持久化机制：
1. **SQLite 数据库** (`AppDatabase`) - 用于结构化数据持久化
2. **JSON 文件** (`ConfigService`) - 用于配置和状态持久化

---

## SQLite 数据库持久化 (AppDatabase)

### 数据库位置
- **路径**: `%AppData%/ComCross/ComCross.db` (Windows) 或 `~/.config/ComCross/ComCross.db` (Linux)
- **实现类**: `src/Core/Services/AppDatabase.cs`

### 表结构

#### 1. notifications (通知中心)
存储应用通知历史记录。

```sql
CREATE TABLE IF NOT EXISTS notifications (
    id TEXT PRIMARY KEY,
    category INTEGER NOT NULL,
    message_key TEXT NOT NULL,
    message_args TEXT NOT NULL,
    level INTEGER NOT NULL,
    created_at TEXT NOT NULL,
    is_read INTEGER NOT NULL
);
```

**字段说明**:
- `id`: 通知唯一标识符 (GUID)
- `category`: 通知类别 (0=Storage, 1=Connection, 2=Export)
- `message_key`: i18n 消息键 (用于多语言显示)
- `message_args`: 消息参数 JSON 数组
- `level`: 通知级别 (0=Info, 1=Warning, 2=Error)
- `created_at`: 创建时间 (ISO 8601 格式)
- `is_read`: 是否已读 (0=未读, 1=已读)

**相关服务**: `NotificationService`

**操作方法**:
- `InsertNotificationAsync()` - 插入新通知
- `GetNotificationsAsync()` - 查询通知列表（支持时间过滤）
- `MarkAllNotificationsReadAsync()` - 标记所有通知为已读
- `MarkNotificationReadAsync(id)` - 标记指定通知为已读

**业务逻辑**:
- 通知按照设置中的保留天数自动过滤 (`Notifications.RetentionDays`)
- 支持按类别开关通知 (Storage/Connection/Export)
- 通知添加时会触发 `NotificationAdded` 事件

---

#### 2. log_files (日志文件索引)
存储日志文件的元数据和索引信息。

```sql
CREATE TABLE IF NOT EXISTS log_files (
    id TEXT PRIMARY KEY,
    session_id TEXT NOT NULL,
    session_name TEXT NOT NULL,
    file_path TEXT NOT NULL UNIQUE,
    start_time TEXT NOT NULL,
    end_time TEXT NOT NULL,
    size_bytes INTEGER NOT NULL
);
```

**字段说明**:
- `id`: 记录唯一标识符 (GUID)
- `session_id`: 关联的 session ID
- `session_name`: session 名称
- `file_path`: 日志文件完整路径 (UNIQUE约束)
- `start_time`: 日志开始时间 (ISO 8601)
- `end_time`: 日志结束时间 (ISO 8601)
- `size_bytes`: 文件大小（字节）

**相关服务**: `LogStorageService`

**操作方法**:
- `UpsertLogFileAsync(record)` - 插入或更新日志文件记录
- `RemoveLogFileAsync(filePath)` - 删除日志文件记录

**业务逻辑**:
- 每个session的日志按时间戳自动分片（单文件10MB上限）
- 支持总占用上限检查（默认256MB）
- 超限时可选自动删除最旧日志
- 日志文件名格式: `{session_name}_{start_timestamp}.txt`

---

#### 3. config_history (配置历史)
存储应用配置的历史版本。

```sql
CREATE TABLE IF NOT EXISTS config_history (
    id TEXT PRIMARY KEY,
    created_at TEXT NOT NULL,
    settings_json TEXT NOT NULL
);
```

**字段说明**:
- `id`: 历史记录唯一标识符 (GUID)
- `created_at`: 保存时间 (ISO 8601)
- `settings_json`: 完整配置 JSON (`AppSettings` 序列化)

**相关服务**: `SettingsService`

**操作方法**:
- `InsertConfigHistoryAsync(settingsJson)` - 插入配置历史记录

**业务逻辑**:
- 每次保存配置时自动记录历史版本
- 用于配置追溯和恢复（未来功能）
- 包含所有配置项：语言、日志、连接、显示、导出、插件等

---

## JSON 文件持久化 (ConfigService)

### 配置文件位置
- **路径**: `%AppData%/ComCross/` (Windows) 或 `~/.config/ComCross/` (Linux)
- **实现类**: `src/Core/Services/ConfigService.cs`

### 文件列表

#### 1. app-settings.json (应用配置)
存储应用的所有配置项。

**结构** (`AppSettings`):
```json
{
  "Language": "en-US",
  "FollowSystemLanguage": true,
  "AppLogs": {
    "Enabled": true,
    "Directory": "",
    "Format": "txt",
    "MinLevel": "Info"
  },
  "Logs": {
    "AutoSaveEnabled": true,
    "Directory": "",
    "MaxFileSizeMb": 10,
    "MaxTotalSizeMb": 256,
    "AutoDeleteEnabled": false
  },
  "Notifications": {
    "StorageAlertsEnabled": true,
    "ConnectionAlertsEnabled": true,
    "ExportAlertsEnabled": true,
    "RetentionDays": 7
  },
  "Connection": {
    "DefaultBaudRate": 115200,
    "DefaultEncoding": "UTF-8",
    "DefaultAddCr": true,
    "DefaultAddLf": true,
    "ExistingSessionBehavior": 0
  },
  "Display": {
    "MaxMessages": 10000,
    "AutoScroll": true,
    "TimestampFormat": "HH:mm:ss.fff"
  },
  "Export": {
    "DefaultFormat": "txt",
    "DefaultDirectory": "",
    "RangeMode": 0,
    "RangeCount": 1000
  },
  "Commands": {
    "GlobalCommands": [],
    "SessionCommands": {}
  },
  "Plugins": {
    "Enabled": {}
  }
}
```

**相关服务**: `SettingsService`

**操作方法**:
- `LoadAppSettingsAsync()` - 加载配置
- `SaveAppSettingsAsync(settings)` - 保存配置

**业务逻辑**:
- 启动时自动加载
- 保存时同时记录到 `config_history` 表
- 支持默认值填充（`EnsureDefaults()`）
- 配置变更时触发 `SettingsChanged` 事件

---

#### 2. workspace-state.json (工作区状态)
存储工作区的运行时状态，包括 session 列表和 UI 状态。

**结构** (`WorkspaceState`):
```json
{
  "WorkspaceId": "default",
  "Sessions": [
    {
      "Id": "session-guid",
      "Port": "/dev/ttyUSB0",
      "Name": "My Session",
      "Settings": {
        "BaudRate": 115200,
        "DataBits": 8,
        "Parity": 0,
        "StopBits": 1
      },
      "Connected": false,
      "Metrics": {
        "Rx": 1024,
        "Tx": 512
      }
    }
  ],
  "UiState": {
    "ActiveSessionId": "session-guid",
    "AutoScroll": true,
    "Filters": null,
    "HighlightRules": []
  },
  "SendHistory": []
}
```

**相关服务**: `WorkspaceService`

**操作方法**:
- `LoadWorkspaceStateAsync()` - 加载工作区状态
- `SaveWorkspaceStateAsync(state)` - 保存工作区状态
- `SaveCurrentStateAsync(sessions, activeSession, autoScroll)` - 保存当前状态

**业务逻辑**:
- 应用启动时恢复 session 列表（不自动重连）
- session 创建、断开、删除时自动保存
- 保存活动 session、UI 状态（自动滚动等）
- **注意**: Session 状态持久化，但连接不持久化（重启后需手动重连）

---

## 持久化触发时机

### 自动保存
1. **配置更改**: `SettingsService.SaveAsync()` 被调用时
   - 同时保存到 `app-settings.json` 和 `config_history` 表

2. **Session 状态变化**:
   - 创建新 session: `QuickConnectAsync()` → `SaveWorkspaceStateAsync()`
   - 断开连接: `DisconnectAsync()` → `SaveWorkspaceStateAsync()`
   - 删除 session: `DeleteSessionAsync()` → `SaveWorkspaceStateAsync()`

3. **通知添加**: `NotificationService.AddAsync()` 被调用时
   - 自动插入到 `notifications` 表

4. **日志文件更新**: `LogStorageService` 定期更新
   - 文件轮转时: `UpsertLogFileAsync()`
   - 文件删除时: `RemoveLogFileAsync()`

### 手动保存
- 用户点击设置中的"保存"按钮
- 导出操作完成后更新导出路径设置

---

## 数据迁移与兼容性

### 版本控制
- 当前版本: v0.3.1
- 数据库 schema 使用 `CREATE TABLE IF NOT EXISTS` 确保向前兼容
- JSON 配置使用可选字段和默认值确保兼容性

### 升级策略
1. 添加新表/字段不影响现有数据
2. 配置项使用默认值处理缺失情况
3. **未来版本可能需要**: 版本号字段和迁移脚本

---

## 持久化相关代码清单

### 核心服务
- `src/Core/Services/AppDatabase.cs` - SQLite 数据库操作
- `src/Core/Services/ConfigService.cs` - JSON 文件读写
- `src/Core/Services/SettingsService.cs` - 配置管理
- `src/Core/Services/WorkspaceService.cs` - 工作区状态管理
- `src/Core/Services/NotificationService.cs` - 通知持久化
- `src/Core/Services/LogStorageService.cs` - 日志文件索引

### 数据模型
- `src/Shared/Models/AppSettings.cs` - 应用配置模型
- `src/Core/Models/WorkspaceState.cs` - 工作区状态模型
- `src/Shared/Models/NotificationItem.cs` - 通知项模型
- `src/Shared/Models/LogFileRecord.cs` - 日志文件记录模型

### ViewModel 交互
- `src/Shell/ViewModels/MainWindowViewModel.cs` - 调用 `SaveWorkspaceStateAsync()`
- `src/Shell/ViewModels/SettingsViewModel.cs` - 调用 `SettingsService.SaveAsync()`

---

## 开发指南

### 添加新的持久化数据

**如果需要 SQLite 持久化**:
1. 在 `AppDatabase.InitializeAsync()` 中添加新表的 CREATE TABLE 语句
2. 在 `AppDatabase` 中添加对应的 CRUD 方法
3. 在相应的 Service 中调用这些方法
4. **更新本文档**，描述新表的结构和用途

**如果需要 JSON 持久化**:
1. 在 `AppSettings` 或创建新的模型类中添加字段
2. 在 `SettingsService` 或 `ConfigService` 中添加加载/保存逻辑
3. 确保有默认值处理
4. **更新本文档**，描述新配置项的结构

### 修改现有持久化

**修改表结构**:
1. 使用 ALTER TABLE 语句（保持向前兼容）
2. 或者创建新表并迁移数据
3. **必须更新本文档的表结构说明**

**修改配置结构**:
1. 添加新字段（使用默认值）
2. 标记废弃字段（保留以兼容旧版本）
3. **必须更新本文档的 JSON 结构示例**

---

## 注意事项

1. **并发安全**: 所有数据库操作使用 `await using` 确保连接正确释放
2. **异常处理**: 持久化失败不应导致应用崩溃，应记录日志
3. **性能**: 避免频繁保存，合并批量操作
4. **数据安全**: 敏感数据（如密码）应加密存储（当前版本未实现）
5. **备份**: 用户应定期备份 `%AppData%/ComCross/` 目录

---

**最后更新**: 2026-01-15  
**维护者**: 开发团队  
**版本**: v0.3.1
