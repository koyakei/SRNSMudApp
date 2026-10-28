# Original User Request

## Approved Task Prompt — 2026-10-28T08:40:43Z

ソリューション内の全C#コードベース（プロダクションコードおよびテストプロジェクトを含む）を対象に、レガシーな命令型コードのモダン化、最新C#機能の適用、アナライザー警告の解消を行い、元のコードとの論理的等価性と可読性を維持した上でビルドおよびテストをパスさせる。

Integrity mode: development

## Requirements

### R1. コードベース全体の解析とモダン化
ソリューション内の全C#プロジェクト（`SRNSMudApp`、`SRNSMudApp.Client`、`SRNSMudApp.Tests`、`SRNSMudApp.E2ETests` 等）を解析し、レガシーな命令型コードをモダン化する。
- 宣言的な可読性を向上させるため、安全にLINQ式等へ変換できる `for` や `foreach` ループを検討・適用する。
- パターンマッチング、switch式、target-typed `new()` などの最新C#機能を適用する。
- リファクタリング後のコードが高い可読性を維持し、元のコードと論理的に等価であることを保証する。

### R2. 静的解析警告の解消とフォーマット制約
- Roslynator や IDE/コンパイラのアナライザー警告を解消する。
- `dotnet format` を実行する場合は必ず `--diagnostics IDE0055` を指定し、引数なしのフル実行は避けること。

### R3. ビルドおよびテストによる動作検証
- リファクタリング後にソリューション全体のビルドおよび既存テストスイートを実行し、問題なくパスすることを確認する。

## Verification Resources
- ビルド検証: `dotnet build`
- テスト検証: ソリューション内のテストスイート（`SRNSMudApp.Tests`, `SRNSMudApp.E2ETests`）を `dotnet test` により実行

## Acceptance Criteria

### コードモダン化と論理的等価性
- [ ] 安全にLINQ等へ変換可能なループ（for / foreach）がモダン化されていること
- [ ] パターンマッチング、switch式、target-typed `new()` 等のモダンC#機能が適用されていること
- [ ] リファクタリング後のコードが高い可読性を維持し、元のコードと論理的に等価であること

### 静的解析と警告解消
- [ ] 対象プロジェクトにおいて Roslynator およびコンパイラ/IDEのアナライザー警告が解消されていること
- [ ] コード整形が行われる場合、`dotnet format` に `--diagnostics IDE0055` が指定されていること（引数なしのフル実行が行われていないこと）

### ビルドおよびテスト
- [ ] `dotnet build` によるソリューションのビルドが成功すること
- [ ] `dotnet test` により、既存のテストスイート（Tests, E2ETests）がすべてパスすること
