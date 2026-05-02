# ComCross

> ドキュメント注意: この文書は AI によって生成または翻訳支援されている
> 可能性があります。理解と案内を目的とした参考資料です。英語の README
> または正式仕様と矛盾する場合は、英語の原文を優先してください。

ComCross は、シリアル、TCP、UDP のワークフロー向けのクロスプラット
フォーム組み込み通信ツールボックスです。

## 状態

ComCross はまだ安定互換期間に入っていません。アーキテクチャ境界、実行時
ディレクトリ、プラグイン配置、パッケージング契約を修正するための破壊的
変更は許可されます。ただし、その変更は文書化する必要があります。

## 主な機能

- シリアル、TCP、UDP バスアダプターを分離プラグインとして提供。
- セッションとワークロードを永続化された記述子で管理。
- 制限付きフレーム属性を持つ検索可能な RX/TX メッセージストリーム。
- プラグイン設定ページとプラグイン生成 UI 状態。
- Shell は英語と簡体字中国語のローカライズを内蔵。

## 開発クイックスタート

```bash
dotnet restore
dotnet build ComCross.sln --no-restore
dotnet run --project src/Shell/ComCross.Shell.csproj
dotnet test ComCross.sln --no-build
```

## 重要事項

- 将来のユーザー向けエントリポイントは `ComCross.Startup` になる予定です。
- Windows 公開リリースでは初期段階で自己署名テスト証明書を使う可能性が
  あります。この証明書は Windows に既定では信頼されません。
- OS の警告を回避する前に、リリースのチェックサムと署名を検証してください。
- 公式プラグインパッケージ署名は、Windows コード署名およびリリース成果物
  署名とは別に扱われます。

## 参照

- ルート README：[../../README.md](../../README.md)
- 仕様索引：[../specs/00-Index.md](../specs/00-Index.md)
- Startup、インスタンス ID、署名設計：
  [../specs/12-Startup-Identity-Signing-Design.md](../specs/12-Startup-Identity-Signing-Design.md)
