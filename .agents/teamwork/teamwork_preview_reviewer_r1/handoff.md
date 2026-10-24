# Reviewer Round 1 Handoff Report: 連打・二重送信防止およびトースト通知機能の検証・修正

## 1. 概要 (Executive Summary)
先行実装（`teamwork_preview_implementer_r1`）に対する批判的レビューおよび検証を実施しました。
先行実装は一部のUI（AddItem）を中心に最低限のガードを実装していたものの、重大な設計欠陥・スコープ漏れ・並行処理バグ・未登録DIが存在していました。
本ラウンドにてこれらすべての問題を是正し、包括的な単体・結合テストを追加して全3,335件のテストが100%成功、0警告、0エラー、`dotnet format --diagnostics IDE0055` 完全準拠を達成しました。

---

## 2. 先行実装の欠陥と是正内容 (Issues Found & Fixed)

### 2.1 スコープの漏れ (Missing Requirements Coverage)
1. **リプライ処理 (`ItemReplyService`) の重複排除漏れ**:
   - **問題**: `ItemReplyService.AddItemReplyAsync` にサーバー側重複チェックが一切なく、並行・連打リクエストで同一リプライがDBに重複作成されていた。
   - **是正**: `ItemReplyService` に `IRequestDeduplicationService` を導入。30秒以内の同一親アイテム・同一本文・同一ユーザーのリプライを排他ロック & 重複判定し、`reply.IsDuplicate = true` を設定。
   - **API / UI 連携**: `ItemController.AddReply` で `X-Duplicate-Request` ヘッダーと `IsDuplicate` フラグを返却し、`ItemListApiClient.AddReplyAsync` および `ItemDetail.razor.cs` / `ItemSearchView.razor.cs` で重複トースト（Snackbar）を表示。

2. **引用処理 (`ItemQuoteService`) のサーバー重複排除漏れ**:
   - **問題**: 先行実装は `QuoteItemViewModel` にのみガードを置き、バックエンドの `ItemQuoteService.CreateQuoteItemAsync` は無防備であった。
   - **是正**: `ItemQuoteService` に `IRequestDeduplicationService` を注入し、実DBレベルでの二重引用作成を抑止。

3. **子タグ作成ダイアログ (`TagAddViewModel` / `TagAddDialog`) の連打抑止漏れ**:
   - **問題**: `AddTagPageViewModel`（単独ページ）のみ対応され、モーダルダイアログである `TagAddViewModel` での連打抑止が未実装だった。
   - **是正**: `TagAddViewModel` に `IsSubmitting` ガードと重複検知トースト表示を追加。

4. **Razor UI ボタンの無効化バインド漏れ**:
   - **問題**: C# ViewModel 側のガードのみで、UI 上の `<MudButton>` に `Disabled` がバインドされておらず、ブラウザ側の連続クリックイベントがキューされていた。
   - **是正**: `AddItem.razor`, `ItemEditDialog.razor`, `TagAddDialog.razor` の保存ボタンに `Disabled="@ViewModel.IsSubmitting"` をバインド。

### 2.2 並行処理・アーキテクチャの欠陥 (Concurrency & Architecture Bugs)
1. **`RequestDeduplicationService` のセマフォ破棄競合 (Race Condition & Dispose Exception)**:
   - **問題**: 先行実装の `CleanupLockIfIdle` は `CurrentCount == 1` のセマフォを即座に破棄・削除していたため、他スレッドが待機に入ろうとした瞬間にセマフォが Dispose される例外（`ObjectDisposedException`）や、排他が外れて異なるセマフォが作成される致命的欠陥が存在した。
   - **是正**: 参照カウント方式（`LockEntry`）に全面刷新。アクティブな待機者・使用者が 0 になったこと、および最小アイドル時間が経過したことをスレッドセーフに確認した上で安全に回収する設計に変更。

2. **`DoubleSubmissionGuard` の単一キー上書きバグ**:
   - **問題**: 先行実装は単一の `_lastCompletedKey` しか保持せず、5秒以内に別の操作を行うと直前の操作キーの重複ガードが消失していた。また `Complete()` を引数なしで呼ぶと直前のキーが記録されなかった。
   - **是正**: `Dictionary<string, DateTimeOffset>` による複数操作キーの時間枠管理に拡張し、`Complete()` 呼び出し時に `TryBeginSubmit` で指定されたキーへの自動フォールバックを実装。

3. **Blazor WebAssembly (`SRNSMudApp.Client`) の DI 登録漏れ**:
   - **問題**: `IDoubleSubmissionGuard` がサーバー側の DI にのみ登録され、WASM クライアント側（`SRNSMudApp.Client/Program.cs`）で登録されていなかったため、クライアント実行時に `InvalidOperationException` が発生するリスクがあった。
   - **是正**: `SRNSMudApp.Client/Program.cs` に `builder.Services.AddTransient<IDoubleSubmissionGuard, DoubleSubmissionGuard>();` を追加。

4. **コンパイラ・アナライザー警告 (IDE0028)**:
   - **問題**: `DoubleSubmissionGuard` のコレクション初期化で IDE0028 警告が発生していた。
   - **是正**: コレクション式 `[]` へのリファクタリングにより 0 警告を達成。

---

## 3. 追加・修正されたテスト (Tests Added / Updated)

| テストファイル | 内容 |
|---|---|
| `SRNSMudApp.Tests/Services/DoubleSubmissionGuardTests.cs` | 複数キー並行時間枠追跡、引数なし `Complete()` のフォールバックテスト |
| `SRNSMudApp.Tests/Services/RequestDeduplicationServiceTests.cs` | セマフォ参照カウント回収、キャンセル時安全解放、デッドロック回避テスト |
| `SRNSMudApp.Tests/Controllers/ItemControllerTests.cs` | `CreateItem` 冪等性キー検証、`AddReply` 重複レスポンス検証 |
| `SRNSMudApp.Tests/Components/Tag/TagAddViewModelTests.cs` | `TagAddViewModel` の連打抑止および Snackbar 通知テスト |
| `SRNSMudApp.Tests/Components/Item/QuoteItemViewModelTests.cs` | サーバー重複時の引用 ViewModel エラーハンドリングテスト |
| `SRNSMudApp.Tests/Components/Item/ItemDetailThreadTests.cs` | `ItemDetail` での重複リプライ送信時 Snackbar 表示テスト |
| `SRNSMudApp.Tests/ItemReplyServiceTests.cs` | Testcontainers (実 MSSQL) でのリプライ重複登録抑止テスト |
| `SRNSMudApp.Tests/ItemQuoteServiceTests.cs` | Testcontainers (実 MSSQL) での引用重複登録抑止テスト |

---

## 4. 検証実績 (Verification Results)

1. **ビルド検証 (`dotnet build`)**:
   - 警告: **0 件**
   - エラー: **0 件**
2. **フォーマット検証 (`dotnet format --diagnostics IDE0055 --verify-no-changes`)**:
   - 違反: **0 件 (Exit code 0)**
3. **テスト検証 (`dotnet test`)**:
   - 対象テスト（ガード・重複排除・関連UI・コントローラー・サービス）: **258件 全件合格**
   - ソリューション全単体・結合テストスイート: **3,335件 全件合格 (0 失敗, 0 スキップ)**
