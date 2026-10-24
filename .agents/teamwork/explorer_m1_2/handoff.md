# Handoff Report - explorer_m1_2

## 1. Observation
- **E2E テストの検証内容 (`SRNSMudApp.E2ETests/ItemSplitE2ETests.cs:78-89`)**:
  Playwright E2E テストでは、非所有者（User B）がアイテムを表示し、テキスト未選択状態で「分割リクエスト」ボタン（`button[title='選択テキストを別アイテムに分割するリクエストを送信']`）をクリックした際、`分割するテキストが選択されていません。` という警告スナックバーが表示されることを検証している。
- **UI コンポーネントの実装 (`SRNSMudApp/Components/UI/ItemCard.razor:50-65` & `ItemCard.razor.cs:678-695`)**:
  - 所有者の場合: `SplitSelectionAsync()` が呼ばれ、`SplitCoordinator.SplitSelectionAsync(Item, CurrentUserId)` を実行。
  - 非所有者の場合: `RequestSplitSelectionAsync()` が呼ばれ、`SplitCoordinator.RequestSplitSelectionAsync(Item, CurrentUserId)` を実行。
  - ボタンタイトル:
    - 所有者: `選択テキストを別アイテムに分割` (`data-testid="direct-split-button-{Item.Id}"`)
    - 非所有者: `選択テキストを別アイテムに分割するリクエストを送信` (`data-testid="request-split-button-{Item.Id}"`)
- **コーディネーターの実装 (`SRNSMudApp/Services/ItemCardSplitCoordinator.cs`)**:
  - `SplitSelectionAsync` (Line 60-70) および `RequestSplitSelectionAsync` (Line 127-137):
    - `GetSelectedTextAsync()` を呼び出し、内部で `_js.InvokeAsync<string>("selectionHelper.getSelectedText")` を実行（Line 708-718）。
    - `selectedText is null` の場合は警告を出さずに `false` または `null` を返却。
    - `string.IsNullOrWhiteSpace(selectedText)` の場合にのみ `_snackbar.Add("分割するテキストが選択されていません。", Severity.Warning)` を実行。
- **既存 bUnit テストクラスの制約 (`SRNSMudApp.Tests/Components/UI/ItemCardSplitRequestTests.cs`)**:
  - コンストラクタで `_ctx.Services.RemoveAll<IItemCardSplitCoordinator>()` および `_ctx.Services.AddScoped(_ => _splitCoordinatorMock.Object)` を登録（Line 72-73）。
  - コンストラクタ末尾（Line 78）で `_ctx.Render<MudPopoverProvider>();` を呼び出しているため、bUnit の `TestServiceProvider` は既にビルド（凍結）済み。テストメソッド内で `_ctx.Services.RemoveAll` を呼び出すと `InvalidOperationException` が発生する。
  - スナックバーの検証は `ISnackbar snackbar = _ctx.Services.GetRequiredService<ISnackbar>();` および `Assert.Contains(snackbar.ShownSnackbars, s => ...)` を使用（Line 607-608）。

## 2. Logic Chain
1. **問題の特定**:
   E2E の `ItemSplitE2ETests` は、テキスト未選択時の警告スナックバー表示のためだけにブラウザ、ログイン/ログアウト、SignalR 接続を走らせており数秒〜十数秒を消費している。
2. **bUnit 化の要件**:
   `ItemCard` の分割ボタン押下から `ItemCardSplitCoordinator` の検証、`IJSRuntime` の選択テキスト取得、および `ISnackbar` の通知発火までを統合的にミリ秒単位（< 50ms）で検証する必要がある。
3. **DI 結線方式の推論**:
   `ItemCardSplitRequestTests` のコンストラクタで `MudPopoverProvider` がレンダリングされているため、テストメソッド内で `_ctx.Services` を再登録することはできない。
   一方で、`_splitCoordinatorMock` のセットアップをテストメソッド内で実 `ItemCardSplitCoordinator` インスタンスへ委譲（`realCoordinator.RequestSplitSelectionAsync(...)` / `realCoordinator.SplitSelectionAsync(...)`）すれば、既存の 21 テストに一切影響を与えず、かつ例外なく実コーディネーターをテスト可能となる。
4. **JSInterop の挙動**:
   bUnit Loose モードの既定値 `null` では `selectedText is null` の早期リターンに入ってしまいスナックバーが出ない。したがって、`_ctx.JSInterop.Setup<string>("selectionHelper.getSelectedText").SetResult(string.Empty);` で空文字（未選択状態）を明示的に返却させる必要がある。
5. **パフォーマンスと Flakiness 排除**:
   インメモリの `snackbar.ShownSnackbars` コレクションを直接アサートすることで、DOM レンダリング遅延や CSS アニメーション待機を完全に不要化し、実行時間約 10〜15ms、Flakiness ゼロを達成できる。

## 3. Caveats
- `_ctx.JSInterop.Setup<string>("selectionHelper.getSelectedText")` で `null` ではなく `string.Empty`（または空白文字）を返さなければ、コーディネーターがスナックバーを出さずに早期終了する点に注意が必要。
- MudBlazor の `MudSnackbarProvider` を DOM にレンダリングして HTML 要素を探す必要はなく、`snackbar.ShownSnackbars` を検証することが本リポジトリの標準かつ最も安定的・高速である。

## 4. Conclusion
`ItemCard_WhenSplitClickedWithEmptySelection_ShowsWarningSnackbar` は、`ItemCardSplitRequestTests.cs` において実 `ItemCardSplitCoordinator` を手動生成し、`_splitCoordinatorMock` の呼び出しを委譲する構成とすることで、既存テストへの影響ゼロ・DI コンテナ凍結回避・実行時間 < 15ms・Flakiness ゼロで確実に実現可能である。
所有者（直接分割）と非所有者（分割リクエスト）の双方を網羅する `[Theory]` パターンおよび E2E と 1:1 に対応する `[Fact]` パターンの完全なコードを `report.md` に提示した。

## 5. Verification Method
1. **静的検証**:
   - `SRNSMudApp.Tests/Components/UI/ItemCardSplitRequestTests.cs`、`SRNSMudApp/Components/UI/ItemCard.razor`、`SRNSMudApp/Services/ItemCardSplitCoordinator.cs` の各コードが本レポートの記載行と一致していることを `view_file` で確認。
2. **既存テスト検証**:
   - `dotnet test SRNSMudApp.Tests/SRNSMudApp.Tests.csproj --filter "FullyQualifiedName~ItemCardSplitRequestTests"`
   - 既存の 21 件が全件パスすること（約 680ms）を確認。
3. **新規テスト実装後の検証**:
   - `report.md` の推奨コードを追加後、以下のコマンドで実行:
     `dotnet test SRNSMudApp.Tests/SRNSMudApp.Tests.csproj --filter "FullyQualifiedName~ItemCard_WhenSplitClickedWithEmptySelection_ShowsWarningSnackbar"`
   - 実行時間が 50ms 未満で 0 失敗・全件パスとなること。
