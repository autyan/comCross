# Plugins

Place non-core tool plugins here. Each plugin should be a DLL with an embedded
manifest resource named `ComCross.Plugin.Manifest.json`.

## BusAdapter plugins: schema + shared memory

BusAdapter 插件需要遵循两条硬约束：

1) **Schema-driven UI**：插件必须提供 capability 描述（含参数 schema），主进程据此生成配置 UI，并通过统一指令驱动连接。
2) **Shared memory negotiation**：插件必须显式声明共享内存申请范围，并支持在允许时进行扩容与写入切换。

补充（v0.4 验收口径）：

- 系统允许丢数据（不做“消费速度 ≥ 生产速度”的硬控制），但插件必须上报 Drop 通知，且通知需限频（首次立即；之后两次间隔至少 5s；聚合汇总）。

### Capability descriptors (schema-driven)

- 插件实现 `IPluginCapabilityProvider` 并返回 capabilities（例如 `serial` / `tcp-client` / `udp`）。
- 每个 capability 可包含：
	- `JsonSchema`：参数 JSON Schema
	- `UiSchema`：UI 描述（推荐，ConnectDialog 将优先使用它渲染表单）
	- `DefaultParametersJson`：默认参数（可选）

### Plugin UI / State / i18n（v0.4 规范）

原则：**主程序只做动作执行与状态传输**，不应硬编码具体业务字段。

插件需要提供三类声明：

1) **UI 描述（UiSchema）**：声明 UI 元素、字段控件类型、字段与 state 的映射、以及可触发的 actions。
2) **State 描述（get-ui-state）**：插件维护动态状态（例如端口列表、推荐默认值），并通过 `ui-state-invalidated` 触发主程序刷新。
3) **UI i18n**：插件提供自己的 key-value 文案，并由主程序注册到核心 i18n 服务。

#### 1) UiSchema 约定（JSON）

当前主程序支持的一个最小结构（建议按需扩展，但要保持向后兼容）：

```json
{
	"titleKey": "your.plugin.connect.title",
	"fields": [
		{
			"name": "port",
			"control": "select",
			"labelKey": "your.plugin.connect.port",
			"optionsStatePath": "ports",
			"defaultStatePath": "defaultParameters.port",
			"enumFromSchema": false,
			"required": true
		}
	],
	"actions": [
		{
			"id": "connect",
			"labelKey": "your.plugin.connect.action.connect",
			"kind": "host",
			"hostAction": "comcross.session.connect",
			"extraParameters": { "adapter": "serial" }
		}
	]
}
```

字段含义：

- `fields[].name`：参数字段名（与 `JsonSchema.properties` 对齐）
- `fields[].control`：目前支持 `text` / `number` / `select`
- `fields[].optionsStatePath`：从 UI state 中取选项数组（例如 `ports`）
- `fields[].defaultStatePath`：从 UI state 中取默认值（例如 `defaultParameters.port`）
- `fields[].enumFromSchema=true`：选项从 `JsonSchema.properties[name].enum` 推导
- `actions[].kind`：`host` 表示执行主程序动作；`plugin` 表示走插件 connect（保留兼容）

#### 2) UI State 约定（get-ui-state）

插件应返回可序列化 JSON（`PluginUiStateSnapshot.State`）。对 ConnectDialog 的推荐约定：

- `ports`: string[]（或其他动态选项集合）
- `defaultParameters`: object（字段默认值，字段名与参数 schema 对齐）

当状态变化（例如端口列表变化）时，插件应触发：

- `UiStateInvalidated(capabilityId, sessionId=null, viewId="connect-dialog", reason=...)`

#### 3) 插件 i18n 注册

插件在 `ComCross.Plugin.Manifest.json` 里提供可选字段 `i18n`：

```json
"i18n": {
	"en-US": { "your.plugin.key": "value" },
	"zh-CN": { "your.plugin.key": "中文" }
}
```

强制规则：

- key 必须带插件 domain 前缀：`{pluginId}.`（例如 `serial.adapter.xxx`），以避免冲突。
- 主程序注册时：
	- **默认不覆盖**已存在 key
	- 检测到重复 key 会发出 Notification（让用户知情，并可联系插件作者修复）

主进程与插件的统一沟通方式（概念层）：

- `GetCapabilities()`：获取 capabilities + schema + `SharedMemoryRequest`
- `Connect(sessionId, capabilityId, parameters)`：建立连接并开始写共享内存
- `Disconnect(sessionId)`：断开连接
- `RequestSegmentUpgrade(sessionId, requestedBytes)`：可选扩容申请

### Shared memory requests and writer switching

- capability 可声明 `SharedMemoryRequest`：`MinBytes` / `PreferredBytes` / `MaxBytes`。
- 插件可以在运行时尝试申请更大的 segment；主程序决定是否允许。
- 插件负责完成写入切换；PluginSdk 提供默认 wrapper：`SwitchableSharedMemoryWriter`。

默认推荐值（用于插件填写 request 的参考，最终以插件自身能力为准）：

- Min=512KiB，Preferred=2MiB，Max=16MiB

跨进程共享内存（验收：方案 B，跨平台统一语义）：

- Unix：主进程通过 Unix Domain Socket 传递 FD（`SCM_RIGHTS`），插件侧以 FD 映射共享内存
- Windows：主进程使用 Named shared memory + ACL，插件侧按 name 打开映射
- 上层统一为 `SharedMemoryDescriptor`（包含 `mode` 与 `sizeBytes`），插件只应映射自己的 session 段

可选的小缓存补救（插件可不采用）：

- 当写入失败（共享内存满）时，可将帧放入一个有界队列并稍后重试
- 无论是否启用缓存，只要发生丢数据，都必须触发 Drop 通知（限频规则见上）

> 设计说明与主进程职责边界见：`/dev-docs/architecture/插件与主进程通信-共享内存与会话生命周期.md`（内部文档）。
