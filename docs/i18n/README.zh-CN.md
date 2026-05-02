# ComCross

> 文档提示：本文档可能由 AI 生成或辅助翻译，仅用于帮助理解和快速导航。
> 如果本文档与英文 README 或正式规格文档存在冲突，请以英文源文档为准。

ComCross 是一个跨平台嵌入式通信工具箱，面向串口、TCP 和 UDP 工作流。

## 当前状态

ComCross 仍处于稳定兼容期之前。为了修正架构边界、运行时目录、插件布局
或打包契约，项目仍允许破坏性变更，但这些变更必须明确写入文档。

## 主要能力

- 串口、TCP、UDP 总线适配器以隔离插件形式交付。
- 会话和工作负载管理，使用持久化描述符保存状态。
- 可搜索的 RX/TX 消息流，支持受限的帧属性。
- 插件设置页和插件提供的 UI 状态。
- Shell 内置英文和简体中文本地化。

## 开发快速开始

```bash
dotnet restore
dotnet build ComCross.sln --no-restore
dotnet run --project src/Shell/ComCross.Shell.csproj
dotnet test ComCross.sln --no-build
```

## 重要说明

- 未来面向用户的入口计划是 `ComCross.Startup`。
- Windows 公开版本初期可能使用自签名测试证书。该证书不会被 Windows 默认信任。
- 用户在绕过系统安全提示之前，应先验证发布校验和和发布签名。
- 官方插件包签名将与 Windows 代码签名、发布产物签名分离。

## 参考

- 根 README：[../../README.md](../../README.md)
- 规格索引：[../specs/00-Index.md](../specs/00-Index.md)
- Startup、实例身份和签名设计：
  [../specs/12-Startup-Identity-Signing-Design.md](../specs/12-Startup-Identity-Signing-Design.md)
