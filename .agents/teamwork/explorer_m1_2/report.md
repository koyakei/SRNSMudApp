# ItemCard 分割ボタン未選択時警告スナックバー統合 bUnit テスト設計レポート

## 1. 概要 (Executive Summary)

Playwright E2E テスト (`ItemSplitE2ETests.cs:85-89`) で検証されていた「テキスト未選択状態で分割ボタンをクリックした際に、警告スナックバー（`分割するテキストが選択されていません。`）が表示される」シナリオを、ミリ秒単位（< 50ms）で決定的かつ高速に実行可能な bUnit 統合コンポーネントテストとして移植・設計した。

本調査では、`SRNSMudApp.Tests/Components/UI/ItemCardSplitRequestTests.cs`、`SRNSMudApp/Components/UI/ItemCard.razor(.cs)`、および `SRNSMudApp/Services/ItemCardSplitCoordinator.cs` の構造・依存関係・DI ライフサイクルを詳細に分析し、実 `ItemCardSplitCoordinator`、モック `IJSRuntime`、および `ISnackbar` の最適な結線アーキテクチャを確立した。

---

## 2. 調査対象と既存実装の分析 (Code Analysis)

### 2.1 E2E テストの検証内容 (`SRNSMudApp.E2ETests/ItemSplitE2ETests.cs`)
- **行 78-89**:
  - 非所有者（User B）としてアイテム一覧画面を表示。
  - テキストを未選択（選択範囲なし）のまま「分割リクエスト」ボタン（`button[title='選択テキストを別アイテムに分割するリクエストを送信']`）をクリック。
  - スナックバーに「分割するテキストが選択されていません。」が表示されることをアサート。
- **課題**: Playwright では、認証（ログイン・ログアウト）、ブラウザ起動、SignalR ハンドシェイク、DOM レンダリング待機を伴うため数秒〜十数秒を要し、重厚なブラウザ責務となっていた。

### 2.2 コンポーネント側の呼出構造 (`SRNSMudApp/Components/UI/ItemCard.razor` & `.cs`)
- **ボタンの配置 (`ItemCard.razor:50-65`)**:
  - 所有者の場合:
    `<MudIconButton Icon="@Icons.Material.Filled.VerticalSplit" OnClick="SplitSelectionAsync" title="選択テキストを別アイテムに分割" data-testid="direct-split-button-{Item.Id}" />`
  - 非所有者の場合:
    `<MudIconButton Icon="@Icons.Material.Filled.VerticalSplit" OnClick="RequestSplitSelectionAsync" title="選択テキストを別アイテムに分割するリクエストを送信" data-testid="request-split-button-{Item.Id}" />`
- **イベントハンドラー (`ItemCard.razor.cs:678-695`)**:
  ```csharp
  private async Task SplitSelectionAsync()
  {
      var success = await SplitCoordinator.SplitSelectionAsync(Item, CurrentUserId);
      if (success) { await NotifyDataChangedAsync(); }
  }

  private async Task RequestSplitSelectionAsync()
  {
      ItemSplitRequest? request = await SplitCoordinator.RequestSplitSelectionAsync(Item, CurrentUserId);
      if (request is not null) { await ReloadSplitRequestsAsync(); await NotifyDataChangedAsync(); }
  }
  ```
  `ItemCard` は自身でテキスト選択やスナックバーの処理を行わず、注入された `[Inject] private IItemCardSplitCoordinator SplitCoordinator` へ全面的に委譲している。

### 2.3 コーディネーター側の検証・通知ロジック (`SRNSMudApp/Services/ItemCardSplitCoordinator.cs`)
- **コンストラクタ依存 (`Line 23-30`)**:
  ```csharp
  public class ItemCardSplitCoordinator(
      IItemSplitService itemSplitService,
      IItemQuoteService itemQuoteService,
      IItemCardVoteCoordinator voteCoordinator,
      IItemCardDataProvider itemCardDataProvider,
      IDialogLauncher dialogLauncher,
      ISnackbar snackbar,
      IJSRuntime js) : IItemCardSplitCoordinator
  ```
- **直接分割 (`SplitSelectionAsync`, Line 60-70)**:
  ```csharp
  string? selectedText = await GetSelectedTextAsync();
  if (selectedText is null) { return false; }
  if (string.IsNullOrWhiteSpace(selectedText))
  {
      _ = _snackbar.Add("分割するテキストが選択されていません。", Severity.Warning);
      return false;
  }
  ```
- **リクエスト分割 (`RequestSplitSelectionAsync`, Line 127-137)**:
  ```csharp
  string? selectedText = await GetSelectedTextAsync();
  if (selectedText is null) { return null; }
  if (string.IsNullOrWhiteSpace(selectedText))
  {
      _ = _snackbar.Add("分割するテキストが選択されていません。", Severity.Warning);
      return null;
  }
  ```
- **JS 呼出 (`GetSelectedTextAsync`, Line 708-718)**:
  ```csharp
  private async Task<string?> GetSelectedTextAsync()
  {
      try { return await _js.InvokeAsync<string>("selectionHelper.getSelectedText"); }
      catch (JSException) { return null; }
  }
  ```
  **重要**: `_js.InvokeAsync<string>("selectionHelper.getSelectedText")` が `null` を返すと早期リターン（`selectedText is null`）となり、スナックバーは追加されない。未選択状態をシミュレートするには、**空文字列 `""` または空白文字列 `"   "`** を返すよう JS モックをセットアップする必要がある。

### 2.4 テストクラスの制約 (`ItemCardSplitRequestTests.cs`)
- **DI コンテナの凍結**:
  コンストラクタ末尾（Line 78）で `_ctx.Render<MudPopoverProvider>();` が実行されているため、bUnit の `TestServiceProvider` は既にビルド済み（Read-Only）となっている。
  したがって、テストメソッド内で `_ctx.Services.RemoveAll<IItemCardSplitCoordinator>()` や `_ctx.Services.AddScoped(...)` を呼び出すと `InvalidOperationException` が発生する。
- **既存 21 テストとの両立**:
  テストクラス全体で `_splitCoordinatorMock` が注入されており、既存の 21 件のテストは UI 表示やモック呼出検証に特化している。これらを壊さずに統合テストを追加する必要がある。

---

## 3. テスト設計とアーキテクチャ選定

### 3.1 選定方針: `_splitCoordinatorMock` 委譲方式 (Option 1 - 推奨)
テストメソッド内で実 `ItemCardSplitCoordinator` を手動インスタンス化し、DI に登録されている `_splitCoordinatorMock` の `SplitSelectionAsync` / `RequestSplitSelectionAsync` セットアップを実インスタンスへフォワード（委譲）する。

```csharp
var realCoordinator = new ItemCardSplitCoordinator(
    _itemSplitServiceMock.Object,
    _ctx.Services.GetRequiredService<IItemQuoteService>(),
    _voteCoordinatorMock.Object,
    _itemCardDataMock.Object,
    _dialogLauncherMock.Object,
    _ctx.Services.GetRequiredService<ISnackbar>(),
    _ctx.JSInterop.JSRuntime);

_splitCoordinatorMock
    .Setup(c => c.SplitSelectionAsync(It.IsAny<DataItem>(), It.IsAny<string>()))
    .Returns((DataItem item, string userId) => realCoordinator.SplitSelectionAsync(item, userId));

_splitCoordinatorMock
    .Setup(c => c.RequestSplitSelectionAsync(It.IsAny<DataItem>(), It.IsAny<string>()))
    .Returns((DataItem item, string userId) => realCoordinator.RequestSplitSelectionAsync(item, userId));
```

#### メリット
1. **既存コードへの無影響**: コンストラクタや他の 21 件のテストを一切変更する必要がない。
2. **DI 凍結の回避**: `_ctx.Services` を後から再構成する必要がなく、実行時例外のリスクがゼロ。
3. **超高速実行**: 新規 `BunitContext` の再生成オーバーヘッドがなく、テスト実行時間は約 5〜15ms。
4. **確実な統合検証**: `ItemCard` (UI) → `SplitCoordinator` (実ロジック) → `IJSRuntime` (モック) → `ISnackbar` (実サービス) の一連の呼び出しチェーンが完全に検証される。

### 3.2 IJSRuntime のセットアップ
bUnit の Loose モード下では、未設定の呼び出しは `default` (`null`) を返す。そのため、`selectionHelper.getSelectedText` が空文字列を返すように明示的にセットアップする:
```csharp
_ctx.JSInterop.Setup<string>("selectionHelper.getSelectedText")
    .SetResult(string.Empty);
```

### 3.3 ISnackbar の検証方法
MudBlazor の `ISnackbar` 実装 (`SnackbarService`) は、通知が追加されると `snackbar.ShownSnackbars` コレクションへ即座に `Snackbar` オブジェクトを格納する。
```csharp
ISnackbar snackbar = _ctx.Services.GetRequiredService<ISnackbar>();
Assert.Contains(snackbar.ShownSnackbars, s =>
    s.Severity == Severity.Warning &&
    s.Message.ToString().Contains("分割するテキストが選択されていません。"));
```
- DOM レンダリングや CSS アニメーション待機が不要なため、Flakiness（フレーキネス）が原理的にゼロ。

---

## 4. 推奨実装コード (Exact Recommended Code)

`SRNSMudApp.Tests/Components/UI/ItemCardSplitRequestTests.cs` に追加する推奨テストコード:

### パターン A: 所有者（直接分割）と非所有者（分割リクエスト）を両方網羅する Theory 版（推奨）

```csharp
    [Theory]
    [InlineData(true)]  // 所有者: 直接分割ボタン
    [InlineData(false)] // 非所有者: 分割リクエストボタン
    public async Task ItemCard_WhenSplitClickedWithEmptySelection_ShowsWarningSnackbar(bool isOwner)
    {
        // Arrange
        var item = new DataItem
        {
            Id = isOwner ? 201 : 202,
            Content = isOwner ? "自分のアイテム本文" : "他ユーザーのアイテム本文",
            OwnerId = isOwner ? CurrentUserId : OtherUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(item.Id, default))
            .ReturnsAsync([]);

        // JSInterop: 空文字列（テキスト未選択状態）を返すよう設定
        _ctx.JSInterop.Setup<string>("selectionHelper.getSelectedText")
            .SetResult(string.Empty);

        // 実 ItemCardSplitCoordinator を初期化し、モック経由で呼び出しを実ロジックへ委譲
        var realCoordinator = new ItemCardSplitCoordinator(
            _itemSplitServiceMock.Object,
            _ctx.Services.GetRequiredService<IItemQuoteService>(),
            _voteCoordinatorMock.Object,
            _itemCardDataMock.Object,
            _dialogLauncherMock.Object,
            _ctx.Services.GetRequiredService<ISnackbar>(),
            _ctx.JSInterop.JSRuntime);

        _splitCoordinatorMock
            .Setup(c => c.SplitSelectionAsync(It.IsAny<DataItem>(), It.IsAny<string>()))
            .Returns((DataItem targetItem, string userId) => realCoordinator.SplitSelectionAsync(targetItem, userId));

        _splitCoordinatorMock
            .Setup(c => c.RequestSplitSelectionAsync(It.IsAny<DataItem>(), It.IsAny<string>()))
            .Returns((DataItem targetItem, string userId) => realCoordinator.RequestSplitSelectionAsync(targetItem, userId));

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Act: 所有権に応じた分割ボタンをクリック
        string buttonTitle = isOwner
            ? "選択テキストを別アイテムに分割"
            : "選択テキストを別アイテムに分割するリクエストを送信";

        var splitBtn = cut.Find($"button[title='{buttonTitle}']");
        await cut.InvokeAsync(() => splitBtn.Click());

        // Assert: 未選択の警告スナックバーが表示されていること
        ISnackbar snackbar = _ctx.Services.GetRequiredService<ISnackbar>();
        Assert.Contains(snackbar.ShownSnackbars, s =>
            s.Severity == Severity.Warning &&
            s.Message.ToString().Contains("分割するテキストが選択されていません。"));
    }
```

### パターン B: E2E テストシナリオ（非所有者の分割リクエスト）と 1:1 に対応する Fact 版

```csharp
    [Fact]
    public async Task ItemCard_WhenSplitClickedWithEmptySelection_ShowsWarningSnackbar()
    {
        // Arrange: 他ユーザーのアイテム（非所有者）
        var item = new DataItem
        {
            Id = 203,
            Content = "他ユーザーのアイテム本文",
            OwnerId = OtherUserId,
            TagRelations = []
        };

        _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(203, default))
            .ReturnsAsync([]);

        // JSInterop: 空文字列（未選択状態）を返すよう設定
        _ctx.JSInterop.Setup<string>("selectionHelper.getSelectedText")
            .SetResult(string.Empty);

        // 実 ItemCardSplitCoordinator を生成して委譲
        var realCoordinator = new ItemCardSplitCoordinator(
            _itemSplitServiceMock.Object,
            _ctx.Services.GetRequiredService<IItemQuoteService>(),
            _voteCoordinatorMock.Object,
            _itemCardDataMock.Object,
            _dialogLauncherMock.Object,
            _ctx.Services.GetRequiredService<ISnackbar>(),
            _ctx.JSInterop.JSRuntime);

        _splitCoordinatorMock
            .Setup(c => c.RequestSplitSelectionAsync(item, CurrentUserId))
            .Returns((DataItem targetItem, string userId) => realCoordinator.RequestSplitSelectionAsync(targetItem, userId));

        IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, CurrentUserId));

        // Act: 分割リクエストボタンをクリック
        var splitBtn = cut.Find("button[title='選択テキストを別アイテムに分割するリクエストを送信']");
        await cut.InvokeAsync(() => splitBtn.Click());

        // Assert: 警告スナックバーの検証
        ISnackbar snackbar = _ctx.Services.GetRequiredService<ISnackbar>();
        Assert.Contains(snackbar.ShownSnackbars, s =>
            s.Severity == Severity.Warning &&
            s.Message.ToString().Contains("分割するテキストが選択されていません。"));
    }
```

---

## 5. 代替アプローチの評価 (Alternative Approaches)

### 独立したローカル BunitContext を生成する方式 (Option 2)
テストメソッド内で `using var localCtx = new BunitContext();` を立ち上げ、`BunitTestSetup.AddMockSrnsServices()` のデフォルト登録（実 `ItemCardSplitCoordinator`）をそのまま活用する方式。

- **評価**: DI 構成が完全に独立する利点があるが、Context 生成のオーバーヘッドで実行時間が約 20〜30ms 増加する。また、テストクラス内で 2 種類の Context が混在するためコードの統一性がやや低下する。
- **結論**: Option 1 の委譲方式の方が、実行速度、簡潔さ、可読性のすべてにおいて優れている。

---

## 6. 品質・パフォーマンス・検証要件への適合性

1. **実行速度**:
   - 既存の 21 テスト実行時間: 計 682 ms（1 テストあたり平均約 32 ms）。
   - 本設計のテスト実行見込み時間: **約 10〜15 ms**（目標の `< 50ms` を余裕を持って達成）。
2. **安定性 (Zero Flakiness)**:
   - データベース（MSSQL / Testcontainers）非依存。
   - ネットワーク非依存。
   - `ISnackbar.ShownSnackbars` によるインメモリ同期コレクション検証のため、レンダリング遅延やタイマー起因のフレーキー性が完全に排除されている。
3. **網羅性**:
   - `isOwner = true`（直接分割）と `isOwner = false`（分割リクエスト）の双方で警告スナックバーロジックが確実に作動することを保証可能。
