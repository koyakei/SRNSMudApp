# Handoff Report: 連打・二重送信防止およびトースト通知機能の実装

## 1. 概要 (Executive Summary)
アイテム投稿ボタンやデータ更新操作の連打・二重送信を防止し、即時トースト通知（MudBlazor Snackbar による "既にその投稿は送信されました"）およびサーバー側での重複登録抑止・重複検知レスポンスを実装完了しました。

すべてのテストがグリーン（100%成功）、ビルド警告0件・エラー0件、`dotnet format --diagnostics IDE0055` 違反0件を達成しています。

---

## 2. 実装方針とアーキテクチャ (Architectural Design)

### 2.1 クライアント / UI 層
1. **`IDoubleSubmissionGuard` / `DoubleSubmissionGuard` (`SRNSMudApp.Client/Services/`)**:
   - `System.Threading.Lock` によるスレッドセーフな排他制御。
   - `TryBeginSubmit(operationKey)`: 処理中フラグ (`_isSubmitting == 1`) または直近5秒以内の同一操作キーによる再試行を即座にブロック (`false` を返却)。
   - `Complete(operationKey)`: 正常完了時に直近完了時刻とキーを記録し、送信フラグを解除。
   - `Reset()`: 例外・エラー時に直近重複ウィンドウを作らずに即座に再試行可能にする。
   - 定数 `DuplicateSubmissionMessage = "既にその投稿は送信されました"`。

2. **UI 操作ハンドラーおよび ViewModel 連携**:
   - `AddItemViewModel`:
     - 投稿本文、対象ユーザー、プライベート設定等からハッシュ/キーを生成し、`IDoubleSubmissionGuard` による多重実行を防止。
     - 戻り値として `SaveItemResult(bool Success, string? ErrorMessage, bool IsDuplicate = false)` を返却。
     - レガシーコードとの互換性のため、`var (success, errorMessage) = await sut.SaveItemAsync(...)` への暗黙変換およびタプル分解を完全にサポート。
   - `AddItem.razor.cs`:
     - `ViewModel.IsSubmitting` 時の連打クリック、および `result.IsDuplicate` 時に `Snackbar.Add(DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info)` を即座に表示。
   - `QuoteItemViewModel` / `QuoteItemDialog.razor.cs`:
     - 引用投稿の連打・重複送信を防止し、重複時に Info Snackbar で通知。
   - `ItemEditViewModel` / `ItemEditDialog.razor.cs`:
     - アイテム編集時の連打・重複保存を防止。
   - `AddTagPageViewModel` / `AddTag.razor.cs`:
     - タグ作成時の連打・重複送信を防止。
   - `ItemSearchView.razor.cs`:
     - `_isAddingItem` ガードと、サーバーから `dto.IsDuplicate == true` が返却された場合の即時トースト通知を追加。

### 2.2 サーバー / バックエンド層
1. **`IRequestDeduplicationService` / `RequestDeduplicationService` (`SRNSMudApp/Services/`)**:
   - 同一キー（所有者ID + 本文ハッシュ + 親/引用ID等）に対するインフライト要求の `SemaphoreSlim` による並行排他ロック。
   - `IMemoryCache` を利用した30秒間の直近登録結果のキャッシュと高速判定。
2. **`ItemCardDataProvider.CreateItemAsync` (`SRNSMudApp/Services/`)**:
   - 重複リクエスト検知時に、重複したアイテムエンティティ、タグ関連付け、タグ重み台帳 (`TagWeightLedger`) の不要な再作成を行わず、既存アイテムの ID をセットして `item.IsDuplicate = true` を設定。
   - 既存のすべてのテスト（3300件以上）が期待する `Task CreateItemAsync(Item, ...)` のシグネチャを一切壊さず後方互換性を100%担保。
3. **`ItemController.CreateItem` (`SRNSMudApp/Controllers/`)**:
   - `item.IsDuplicate == true` の場合、レスポンスヘッダー `X-Duplicate-Request: true` を付与し、`ItemDto { IsDuplicate = true }` を返却（HTTP 200 OK）。
4. **API クライアント (`ItemListApiClient.cs`)**:
   - レスポンスヘッダーまたは JSON の `IsDuplicate` フラグを検知し、呼び出し元に安全に重複情報を伝達。

---

## 3. 変更・追加ファイル一覧 (Modified & Added Files)

| ファイルパス | 変更種別 | 内容 |
|---|---|---|
| `SRNSMudApp.Client/Services/DoubleSubmissionGuard.cs` | 新規追加 | クライアント二重送信防止ガード |
| `SRNSMudApp/Components/Item/SaveItemResult.cs` | 新規追加 | タプル互換性を持つ投稿結果レコード構造体 |
| `SRNSMudApp/Services/IRequestDeduplicationService.cs` | 新規追加 | サーバー側リクエスト重複排除インターフェース |
| `SRNSMudApp/Services/RequestDeduplicationService.cs` | 新規追加 | SemaphoreSlim + MemoryCache による重複排除実装 |
| `SRNSMudApp.Client/Models/Api/ItemListRequest.cs` | 変更 | `CreateItemRequest` に `IdempotencyKey` を追加 |
| `SRNSMudApp.Client/Models/Api/ItemListResponse.cs` | 変更 | `ItemDto` に `IsDuplicate` プロパティを追加 |
| `SRNSMudApp.Client/Services/ItemListApiClient.cs` | 変更 | `X-Duplicate-Request` ヘッダー検知と `IsDuplicate` 反映 |
| `SRNSMudApp.Client/Components/ItemSearchView.razor.cs` | 変更 | 連打抑止フラグおよび重複時トースト通知 |
| `SRNSMudApp/Data/Item.cs` | 変更 | `[NotMapped] public bool IsDuplicate { get; set; }` 追加 |
| `SRNSMudApp/Services/ItemCardDataProvider.cs` | 変更 | 重複排除ロックと既存アイテム検知 |
| `SRNSMudApp/Controllers/ItemController.cs` | 変更 | 重複時のヘッダー付与と Dto 返却 |
| `SRNSMudApp/Components/Item/AddItemViewModel.cs` | 変更 | ガード適用、`SaveItemResult` 返却、キー計算 |
| `SRNSMudApp/Components/Item/AddItem.razor.cs` | 変更 | 連打・重複検知時の Snackbar 通知 |
| `SRNSMudApp/Components/Item/QuoteItemViewModel.cs` | 変更 | ガード適用と重複エラーハンドリング |
| `SRNSMudApp/Components/Item/QuoteItemDialog.razor.cs` | 変更 | 重複検知時の Snackbar 通知 |
| `SRNSMudApp/Components/Item/ItemEditViewModel.cs` | 変更 | ガード適用 |
| `SRNSMudApp/Components/Item/ItemEditDialog.razor.cs` | 変更 | 重複検知時の Snackbar 通知 |
| `SRNSMudApp/Components/Tag/AddTagPageViewModel.cs` | 変更 | ガード適用 |
| `SRNSMudApp/Components/Tag/AddTag.razor.cs` | 変更 | 重複検知時の Snackbar 通知 |
| `SRNSMudApp/Extensions/ServiceCollectionExtensions.cs` | 変更 | DI 登録 (`IRequestDeduplicationService`, `IDoubleSubmissionGuard`) |
| `SRNSMudApp.Tests/Services/DoubleSubmissionGuardTests.cs` | 新規追加 | ガードの単体テスト（並行・時間窓・キー別・リセット等 7件） |
| `SRNSMudApp.Tests/Services/RequestDeduplicationServiceTests.cs` | 新規追加 | 重複排除サービスの単体テスト（7件） |
| `SRNSMudApp.Tests/Components/Item/AddItemViewModelTests.cs` | 変更 | 並行/連打抑止およびサーバー重複時のテスト追加 |
| `SRNSMudApp.Tests/Components/Item/QuoteItemViewModelTests.cs` | 変更 | 引用投稿の連打/重複抑止テスト追加 |
| `SRNSMudApp.Tests/Components/Item/AddItemTests.cs` | 変更 | bUnit による Snackbar 通知テスト追加 |
| `SRNSMudApp.Tests/Controllers/ItemControllerTests.cs` | 変更 | コントローラーの重複ヘッダー・Dto テスト追加 |
| `SRNSMudApp.Tests/Services/ItemCardDataProviderTests.cs` | 変更 | Testcontainers (MSSQL) による実DB重複排除テスト追加 |
| `SRNSMudApp.Tests/Architecture/ServiceCollectionExtensionsTests.cs` | 変更 | DI 登録の検証アサーション追加 |

---

## 4. 検証結果 (Verification Record)

- **ビルド検証 (`dotnet build`)**:
  - 全4プロジェクト (`SRNSMudApp.Client`, `SRNSMudApp`, `SRNSMudApp.Tests`, `SRNSMudApp.E2ETests`)
  - **結果: 0 エラー, 0 警告**
- **コードフォーマット検証 (`dotnet format --diagnostics IDE0055 --verify-no-changes`)**:
  - **結果: 違反 0 件 (終了コード 0)**
- **テスト実行結果 (`dotnet test`)**:
  - `DoubleSubmissionGuardTests` & `RequestDeduplicationServiceTests`: 14件全件合格
  - `AddItemViewModelTests`, `QuoteItemViewModelTests`, `AddItemTests`, `ItemControllerTests`, `ServiceCollectionExtensionsTests`: 137件全件合格
  - `ItemCardDataProviderTests` (Testcontainers 実 MSSQL): 10件全件合格 (実DB重複排除含む)
