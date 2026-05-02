# ComCross

> 文件提示：本文件可能由 AI 產生或輔助翻譯，僅用於協助理解與導覽。
> 如果本文件與英文 README 或正式規格文件衝突，請以英文來源文件為準。

ComCross 是一個跨平台嵌入式通訊工具箱，面向序列埠、TCP 與 UDP 工作流程。

## 目前狀態

ComCross 仍處於穩定相容期之前。為了修正架構邊界、執行時目錄、外掛配置
或封裝契約，專案仍允許破壞性變更，但這些變更必須明確記錄。

## 主要能力

- 序列埠、TCP、UDP 匯流排介面卡以隔離外掛形式交付。
- 工作階段與工作負載管理，使用持久化描述保存狀態。
- 可搜尋的 RX/TX 訊息流，支援受限的 frame attributes。
- 外掛設定頁與外掛提供的 UI 狀態。
- Shell 內建英文與簡體中文本地化。

## 開發快速開始

```bash
dotnet restore
dotnet build ComCross.sln --no-restore
dotnet run --project src/Shell/ComCross.Shell.csproj
dotnet test ComCross.sln --no-build
```

## 重要說明

- 未來面向使用者的入口計畫為 `ComCross.Startup`。
- Windows 公開版本初期可能使用自簽測試憑證。該憑證不會被 Windows 預設信任。
- 使用者在略過系統安全提示前，應先驗證發布校驗和與發布簽章。
- 官方外掛包簽章將與 Windows 程式碼簽章、發布產物簽章分離。

## 參考

- 根 README：[../../README.md](../../README.md)
- 規格索引：[../specs/00-Index.md](../specs/00-Index.md)
- Startup、實例身份與簽章設計：
  [../specs/12-Startup-Identity-Signing-Design.md](../specs/12-Startup-Identity-Signing-Design.md)
