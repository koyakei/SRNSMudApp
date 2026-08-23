# dotnet format 運用ポリシー

## 背景

本リポジトリでは、`SRNSMudApp.Data.Tag` / `SRNSMudApp.Data.Item` と同名の名前空間
(`Components.Tag` / `Components.Item`) が存在するため、C# の名前解決規則上、
**using エイリアスを名前空間宣言の内側に置くパターン**を採用している。

例:

```csharp
namespace SRNSMudApp.Components.UI;

// 兄弟名前空間 ...Tag が同名型と解決されるため、エイリアスは名前空間の内側に置く
using Tag = SRNSMudApp.Data.Tag;
```

## ルール

- `dotnet format` を実行する場合は **必ず `--diagnostics IDE0055` を指定すること**。
- 診断子を指定しないフル実行は **禁止**。IDE0001 (名前の簡略化) / IDE0008 (var スタイル) の
  コードフィクサーが上記エイリアス行を削除・展開し、17 ファイル規模の破損
  (CS0118 'Tag' is a namespace エラー多発) が発生した実績がある (2026-08)。
- `--verify-no-changes` による検証時も `--diagnostics IDE0055` を付与すること。
  付与しない場合、IMPORTS (using 順序) チェックがエイリアス配置と競合して誤検出する。
