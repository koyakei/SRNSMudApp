# SRNSWebApp

Blazor Server + MudBlazor 製のタグベースSNSアプリケーション（SRNSMudApp）。

## テスト戦略

テストは2プロジェクトに役割分担されている。新しくテストを書く際は以下の判断基準に従うこと。

| プロジェクト | フレームワーク | 役割 |
|---|---|---|
| `SRNSMudApp.Tests` | xUnit + bUnit | コンポーネントテスト／サービステスト。**新機能追加時は基本的にここに書く** |
| `SRNSMudApp.E2ETests` | Playwright (NUnit) | E2Eテスト。実ブラウザAPI依存など、他の手段で代替できないものだけに限定する |

※ 旧 `SRNSMudApp.ComponentTests` プロジェクトは Phase 1 のテスト統合時に削除済み
（bUnit テストはすべて `SRNSMudApp.Tests` に集約）。

### 新しいテストを書く際の判断基準

1. **ブラウザのネイティブAPIに依存するか？**
   - IntersectionObserver / WebAuthn(CDP) / 実JSグローバル関数 / 実SignalR接続 /
     Cookie発行を伴う認証リダイレクト → **E2E (`SRNSMudApp.E2ETests`)**
2. **Blazorのレンダリング結果とDB状態の検証だけで済むか？**
   - → **コンポーネントテスト (`SRNSMudApp.Tests/Components/...`)**
3. 純粋なロジック（類似度計算、JSON生成、状態同期など）
   - UIから切り出して**サービステスト (`SRNSMudApp.Tests`) で直接検証する**

### 実行方法

```bash
# 軽量（毎日の開発用・約2秒）
dotnet test SRNSMudApp.Tests

# 重い（Testcontainers で MSSQL コンテナを起動。アセンブリで1回のみ・約1分）
dotnet test SRNSMudApp.E2ETests
```

E2Eテストの詳細な棚卸し（何が・なぜ残っているか）は
[SRNSMudApp.E2ETests/README.md](SRNSMudApp.E2ETests/README.md) を参照。

## CI について

現時点で `.github/workflows/` 配下にCIパイプラインは存在しない。
CIを追加する場合の目安:

- `dotnet test SRNSMudApp.Tests`: 数秒。全PRで必ず実行。
- `dotnet test SRNSMudApp.E2ETests`: Testcontainers(Docker) が必要。
  共有フィクスチャによりMSSQLコンテナ起動はアセンブリで1回のみ（実績: 約50秒）。
  残存テスト数が少ないため、単一ジョブでの逐次実行で十分。
