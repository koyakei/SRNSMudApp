# E2Eテスト移行に向けた対象コンポーネント・画面ロジック調査レポート

## 1. 調査の概要と背景

Playwright による重量級 E2E テスト（`InteractivePageDialogE2ETests.cs`, `ItemSplitE2ETests.cs`）で検証されている UI インタラクションおよび画面ロジックを、高速かつ決定的な bUnit コンポーネントテストおよび純粋単体テスト（ViewModel / Coordinator）へ移行・最適化するための基盤調査を実施した。

本調査では、以下の4つの主要領域について、コンポーネント構造、パラメータ、DI、イベントコールバック、状態管理、権限判定ロジック、および既存テストの網羅状況を精緻に特定した。

1. **UserGroup ダイアログ群**（グループ作成・編集ダイアログ、メンバー管理ダイアログ、一覧画面）
2. **Home 画面およびタブ実装**（Home 画面の実態調査と E2E タブ切り替えの真の対象）
3. **ItemSplit コンポーネント・ダイアログ**（直接分割、分割リクエスト、所有者判定、未選択警告スナックバー）
4. **画面ロジックと権限判定（R2）**（UI レンダリング責務と純粋ロジック責務の明確な分離）

---

## 2. UserGroup ダイアログ群の構造と実装詳細

### 2.1 コンポーネント一覧と階層構造

| コンポーネント | ファイルパス | 種別 | ルート / 用途 |
|---|---|---|---|
| `UserGroupList` | `SRNSMudApp/Components/UserGroup/UserGroupList.razor`<br>`SRNSMudApp/Components/UserGroup/UserGroupList.razor.cs` | ページ (Blazor Server) | `@page "/UserGroup/List"`<br>所属グループ一覧、作成・編集・メンバーダイアログ起動 |
| `UserGroupCreateEditDialog` | `SRNSMudApp/Components/UserGroup/UserGroupCreateEditDialog.razor`<br>`SRNSMudApp/Components/UserGroup/UserGroupCreateEditDialog.razor.cs` | MudDialog | グループ新規作成および既存グループの名称・説明編集 |
| `UserGroupMembersDialog` | `SRNSMudApp/Components/UserGroup/UserGroupMembersDialog.razor`<br>`SRNSMudApp/Components/UserGroup/UserGroupMembersDialog.razor.cs` | MudDialog | グループメンバー一覧、メンバー検索追加、メンバー削除 |
| `UserGroupMembers` | `SRNSMudApp/Components/UserGroup/UserGroupMembers.razor`<br>`SRNSMudApp/Components/UserGroup/UserGroupMembers.razor.cs` | ページ (Blazor Server) | `@page "/UserGroup/Members/{GroupId:int}"`<br>メンバー管理のページ版 |
| `UserGroupView` | `SRNSMudApp.Client/Components/UserGroupView.razor`<br>`SRNSMudApp.Client/Components/UserGroupView.razor.cs` | WASM コンポーネント | APIクライアント経由の WASM 専用コンポーネント |

### 2.2 各コンポーネントの詳細定義

#### (1) `UserGroupCreateEditDialog`
- **パラメータ / カスケードパラメータ**:
  ```csharp
  [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;
  [Parameter] public UserGroup? Group { get; set; } // null: 新規作成モード, 非null: 編集モード
  [Parameter] public string CurrentUserId { get; set; } = null!;
  ```
- **注入サービス (DI)**:
  - `[Inject] private UserGroupCreateEditViewModel ViewModel { get; set; } = null!;`
  - `[Inject] private ISnackbar Snackbar { get; set; } = null!;`
- **ライフサイクル / イベントコールバック**:
  - `OnInitialized()`: `ViewModel.Initialize(Group, CurrentUserId);`
  - キャンセル: `MudDialog.Cancel()`
  - 保存: `SaveAsync()` -> `ViewModel.SaveAsync()` 実行後、成功時に `MudDialog.Close(DialogResult.Ok(...))` および Snackbar 表示。
- **UI バインディング**:
  - `MudTextField` @bind-Value="ViewModel.Name" (Label="グループ名")
  - `MudTextField` @bind-Value="ViewModel.Description" (Label="説明")
  - 保存ボタン Disabled="@(!ViewModel.CanSave)"

#### (2) `UserGroupMembersDialog`
- **パラメータ / カスケードパラメータ**:
  ```csharp
  [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;
  [Parameter] public UserGroup Group { get; set; } = null!;
  [Parameter] public string CurrentUserId { get; set; } = null!;
  ```
- **注入サービス (DI)**:
  - `[Inject] private UserGroupMembersViewModel ViewModel { get; set; } = null!;`
  - `[Inject] private ISnackbar Snackbar { get; set; } = null!;`
- **ライフサイクル / イベントコールバック**:
  - `OnInitializedAsync()`: `await ViewModel.InitializeAsync(Group, CurrentUserId);`
  - メンバー検索: `SearchUsersAsync(string? value, CancellationToken ct)` -> `ViewModel.SearchUsersAsync(value, ct)`
  - メンバー追加: `AddMemberAsync()` -> `ViewModel.AddMemberAsync()`
  - メンバー削除: `RemoveMemberAsync(string userId)` -> `ViewModel.RemoveMemberAsync(userId)`
  - 閉じる: `MudDialog.Close(DialogResult.Ok(true))`
- **権限制御**:
  - `@if (ViewModel.IsOwner)`: 新規メンバー追加セクション (`MudAutocomplete` + 追加ボタン) を表示。
  - `@if (ViewModel.IsOwner && member.UserId != Group.OwnerId)`: メンバー削除アイコンボタンを表示（オーナー本人は削除不可）。

#### (3) `UserGroupList`
- **注入サービス (DI)**:
  - `[Inject] private UserGroupListViewModel ViewModel { get; set; } = null!;`
  - `[Inject] private IDialogLauncher DialogLauncher { get; set; } = null!;`
  - `[Inject] private ISnackbar Snackbar { get; set; } = null!;`
  - `[CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }`
- **ダイアログ起動連携**:
  - `OpenCreateDialogAsync()`: `DialogLauncher.ShowAsync<UserGroupCreateEditDialog>("新規グループ作成", ViewModel.CreateDialogParameters())`
  - `OpenEditDialogAsync(group)`: `DialogLauncher.ShowAsync<UserGroupCreateEditDialog>("グループを編集", ViewModel.EditDialogParameters(group))`
  - `OpenMembersDialogAsync(group)`: `DialogLauncher.ShowAsync<UserGroupMembersDialog>("メンバー管理", ViewModel.MembersDialogParameters(group))`

### 2.3 ViewModel 実装と状態管理

1. **`UserGroupCreateEditViewModel`** (`SRNSMudApp/Components/UserGroup/UserGroupCreateEditViewModel.cs`)
   - 責務: グループ作成・更新の入力検証とデータ永続化のオーケストレーション。
   - 依存: `IUserGroupDataProvider`
   - プロパティ: `Name`, `Description`, `IsSaving`, `IsEditMode`, `CanSave`
   - メソッド: `Initialize(group, currentUserId)`, `Task<Result<UserGroupEntity>> SaveAsync(ct)`

2. **`UserGroupMembersViewModel`** (`SRNSMudApp/Components/UserGroup/UserGroupMembersViewModel.cs`)
   - 責務: メンバー一覧取得・整列（オーナー先頭ソート）、メンバー候補検索（既存メンバー除外）、追加・削除権限判定。
   - 依存: `IUserGroupDataProvider`, `IUserDataProvider`
   - プロパティ: `Group`, `CurrentUserId`, `Members`, `SelectedUser`, `IsLoading`, `IsAdding`, `IsRemoving`, `IsOwner`, `CanAddMember`
   - メソッド: `InitializeAsync(...)`, `SearchUsersAsync(...)`, `AddMemberAsync(...)`, `RemoveMemberAsync(...)`

3. **`UserGroupListViewModel`** (`SRNSMudApp/Components/UserGroup/UserGroupListViewModel.cs`)
   - 責務: 所属グループ一覧取得、削除、ダイアログ起動用 `DialogParameters` の生成。
   - 依存: `IUserGroupDataProvider`
   - プロパティ: `Groups`, `CurrentUserId`, `IsLoading`
   - メソッド: `LoadGroupsAsync(...)`, `DeleteGroupAsync(...)`, `IsOwner(...)`, `CreateDialogParameters()`, `EditDialogParameters(...)`, `MembersDialogParameters(...)`

---

## 3. Home 画面およびタブ実装の実態調査

### 3.1 Home 画面 (`Home.razor`) の現状

- **ファイル**: `SRNSMudApp/Components/Pages/Home.razor`, `Home.razor.cs`, `HomeViewModel.cs`
- **重大な発見事項**:
  - **`Home.razor` にはタブ (`MudTabs` / タブパネル) は存在しない。**
  - 現在の `Home.razor` は、ログインユーザーがフォローしているタグのタイムライン (`Virtualize<TimelineFeedGroup>`) のみを単一の縦スクロールフィードとして描画している。
  - PC表示時は上部に `AddItem`（アイテム追加フォーム）、モバイル表示時は右下に `MudFab`（アイテム追加ダイアログ起動ボタン）を配置している。

### 3.2 E2E テストで検証されている「タブ切り替え」の真の対象

`ORIGINAL_REQUEST.md` に記載されていた「Home タブ切り替え」という記述について、実 E2E テストコード（`InteractivePageDialogE2ETests.cs`）を詳細に精査した結果、以下の事実が判明した。

1. **実 E2E テストでタブ切り替えを検証している箇所**:
   - テストメソッド: `ContractManagement_SwitchesTabsInteractively_AndBountyBoardOpensCreateDialog`
   - 対象 URL: `/Contract/ContractManagement`
   - 対象コンポーネント: `SRNSMudApp/Components/Contract/ContractManagement.razor`
   - 検証内容:
     ```csharp
     await Page.GotoAsync($"{_serverAddress}/Contract/ContractManagement");
     // 受信トレイの空メッセージ確認
     await Expect(Page.GetByText("現在、受信したコントラクトの提案はありません。")).ToBeVisibleAsync();

     // 「送信済み (Outbox)」タブをクリック
     var outboxTab = Page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "送信済み (Outbox)" });
     await outboxTab.ClickAsync();

     // 送信済みの空メッセージ確認
     await Expect(Page.GetByText("現在、送信したコントラクトの提案はありません。")).ToBeVisibleAsync();
     ```
2. **`ContractManagement.razor` のタブ構造**:
   - `<MudTabs Elevation="2" Rounded="true" ApplyEffectsToContainer="true" Class="mt-4">`
     - `<MudTabPanel Text="受信トレイ (Inbox)">`: 受信した提案一覧または「現在、受信したコントラクトの提案はありません。」
     - `<MudTabPanel Text="送信済み (Outbox)">`: 送信した提案一覧または「現在、送信したコントラクトの提案はありません。」
3. **結論**:
   - E2E テストの移植対象である「タブ切り替え」の真の対象は `ContractManagement.razor` の `MudTabs`（Inbox / Outbox 切り替え）である。
   - `Home.razor` 自体はタブを持たないため、Home 側の bUnit テストは既存の PC 用 AddItem 表示/コールバック (`HomeDesktopTests.cs`) およびモバイル用 FAB 表示/ダイアログ起動 (`HomeMobileTests.cs`) で既に十分にカバーされている。

---

## 4. ItemSplit コンポーネント・ダイアログの実装詳細

### 4.1 コンポーネント構造と処理の流れ

アイテム分割は、UI コンポーネント（`ItemCard.razor`）、調停役（`ItemCardSplitCoordinator.cs`）、およびドメインサービス（`ItemSplitService.cs`）の3層に綺麗に責務分離されている。

```
[ItemCard.razor] (UI層: ボタン表示出し分け・クリック検知)
       │
       ▼
[ItemCardSplitCoordinator] (調停層: JS選択テキスト取得・スナックバー・モーダル起動)
       │
       ▼
[ItemSplitService] (ドメイン層: DB更新・リンク置換・トランザクション)
```

### 4.2 所有者 vs 非所有者のボタン出し分け位置

`ItemCard.razor` (50〜64行目):
```razor
@if (((IOwnable)Item).IsOwnedBy(CurrentUserId))
{
    @* 所有者: 直接分割ボタン *@
    <MudIconButton Icon="@Icons.Material.Filled.VerticalSplit" Size="Size.Small"
                   Color="Color.Secondary" OnClick="SplitSelectionAsync" title="選択テキストを別アイテムに分割"
                   data-testid="@($"direct-split-button-{Item.Id}")" />
    <MudIconButton Icon="@Icons.Material.Filled.Edit" ... />
    <MudIconButton Icon="@Icons.Material.Filled.Delete" ... />
}
else
{
    @* 非所有者: 分割リクエストボタン *@
    <MudIconButton Icon="@Icons.Material.Filled.VerticalSplit" Size="Size.Small"
                   Color="Color.Secondary" OnClick="RequestSplitSelectionAsync"
                   title="選択テキストを別アイテムに分割するリクエストを送信" data-testid="@($"request-split-button-{Item.Id}")" />
    <MudIconButton Icon="@Icons.Material.Outlined.Report" ... />
}
```

- **判定式**: `((IOwnable)Item).IsOwnedBy(CurrentUserId)` (実質 `Item.OwnerId == CurrentUserId`)
- **所有者表示**: `title="選択テキストを別アイテムに分割"` (`data-testid="direct-split-button-{id}"`)
- **非所有者表示**: `title="選択テキストを別アイテムに分割するリクエストを送信"` (`data-testid="request-split-button-{id}"`)

### 4.3 未選択警告スナックバーの発生トリガーと位置

未選択時の警告スナックバー表示は、`ItemCardSplitCoordinator.cs` に集約されている。

1. **直接分割 (`SplitSelectionAsync`)**:
   - `ItemCardSplitCoordinator.cs` (66〜70行目):
     ```csharp
     string? selectedText = await GetSelectedTextAsync();
     if (string.IsNullOrWhiteSpace(selectedText))
     {
         _ = _snackbar.Add("分割するテキストが選択されていません。", Severity.Warning);
         return false;
     }
     ```
2. **分割リクエスト (`RequestSplitSelectionAsync`)**:
   - `ItemCardSplitCoordinator.cs` (133〜137行目):
     ```csharp
     string? selectedText = await GetSelectedTextAsync();
     if (string.IsNullOrWhiteSpace(selectedText))
     {
         _ = _snackbar.Add("分割するテキストが選択されていません。", Severity.Warning);
         return null;
     }
     ```
3. **JS 連携メカニズム**:
   - `GetSelectedTextAsync()` が `IJSRuntime.InvokeAsync<string>("selectionHelper.getSelectedText")` を呼び出す。
   - ブラウザ上でテキストが未選択の場合、`""` (空文字) または空白が返却され、`string.IsNullOrWhiteSpace` が真となって警告スナックバー `分割するテキストが選択されていません。` が発火する。

---

## 5. 画面ロジックと権限判定（R2）の分析

### 5.1 責務の分離マトリクス

| 判定・操作 | 担当クラス | レイヤ | テスト手法 |
|---|---|---|---|
| アイテム所有者によるボタン切り替え | `ItemCard.razor` | UI (Markup) | **bUnit コンポーネントテスト** (`ItemCardSplitRequestTests.cs`) |
| 未選択テキスト時の警告スナックバー | `ItemCardSplitCoordinator.cs` | Coordinator (Service) | **純粋単体テスト** (`ItemCardSplitCoordinatorTests.cs`) または bUnit 統合 |
| 所有者自身への分割リクエスト禁止 | `ItemCardSplitCoordinator.cs`<br>`ItemSplitService.cs` | Coordinator<br>Domain Service | **純粋単体テスト** (`ItemCardSplitCoordinatorTests.cs`, `ItemSplitServiceTests.cs`) |
| 非所有者の直接分割禁止 | `ItemCardSplitCoordinator.cs`<br>`ItemSplitService.cs` | Coordinator<br>Domain Service | **純粋単体テスト** (`ItemCardSplitCoordinatorTests.cs`, `ItemSplitServiceTests.cs`) |
| グループ作成・編集の入力バリデーション (`CanSave`) | `UserGroupCreateEditViewModel.cs` | ViewModel | **純粋単体テスト** (`UserGroupCreateEditViewModelTests.cs`) |
| グループオーナー判定・メンバー追加権限 | `UserGroupMembersViewModel.cs` | ViewModel | **純粋単体テスト** (`UserGroupMembersViewModelTests.cs`) |
| メンバー追加時の既存メンバー除外フィルタ | `UserGroupMembersViewModel.cs` | ViewModel | **純粋単体テスト** (`UserGroupMembersViewModelTests.cs`) |
| コントラクト取消可能判定 (`CanCancelContract`) | `ContractManagementViewModel.cs` | ViewModel | **純粋単体テスト** (`ContractManagementViewModelTests.cs`) |
| コントラクトタブ切り替え (Inbox / Outbox) | `ContractManagement.razor` | UI (MudTabs) | **bUnit コンポーネントテスト** (`ContractManagementTests.cs`) |

---

## 6. 既存テスト資産の網羅状況と差分分析

### 6.1 既存テスト資産の状況

| 対象領域 | 既存 bUnit テスト | 既存 純粋単体テスト (ViewModel/Coordinator) |
|---|---|---|
| **UserGroup 作成・編集ダイアログ** | `UserGroupCreateEditDialogTests.cs`<br>・空文字時の保存無効化<br>・入力保存とダイアログ終了<br>・編集モード初期値と更新<br>・キャンセル動作 | `UserGroupCreateEditViewModelTests.cs`<br>・`CanSave` の全条件テスト<br>・未認証/空文字エラー返却<br>・作成/更新の Provider 呼び出し検証 |
| **UserGroup メンバー管理ダイアログ** | `UserGroupMembersDialogTests.cs`<br>・オーナー時のメンバー一覧と追加欄表示<br>・非オーナー時の追加欄・削除ボタン非表示<br>・メンバー削除クリックとスナックバー通知<br>・ダイアログ終了動作 | `UserGroupMembersViewModelTests.cs`<br>・オーナー先頭ソート<br>・既存メンバー除外検索<br>・オーナー以外の追加/削除拒否<br>・Provider 失敗時のエラー返却 |
| **UserGroup 一覧画面** | `UserGroupListTests.cs`<br>・空状態からの「グループを作成」クリック<br>・ヘッダー「新規グループ作成」クリック<br>・未認証時のダイアログ非起動<br>・「メンバー」クリック時のメンバーダイアログ起動<br>・「編集」「削除」クリック動作 | `UserGroupListViewModelTests.cs`<br>・グループ取得・削除<br>・オーナー判定<br>・各ダイアログ用パラメータ生成 |
| **Home 画面** | `HomeDesktopTests.cs` (AddItem 表示/コールバック)<br>`HomeMobileTests.cs` (FAB 表示/ダイアログ起動) | `HomeViewModelTests.cs`<br>・認証状態判定<br>・システムタグ確保ロジック<br>・タイムライン取得ページネーション |
| **ContractManagement (タブ切り替え)** | `ContractManagementTests.cs`<br>・受信トレイ空状態表示<br>・承認/拒否ボタンクリック | `ContractManagementViewModelTests.cs`<br>・ステータス表示文字/色判定<br>・取消可能判定 |
| **ItemSplit (ボタン・警告)** | `ItemCardSplitRequestTests.cs`<br>・所有者: 直接分割ボタン表示<br>・非所有者: 分割リクエストボタン表示<br>・各ボタンクリック時の Coordinator 呼び出し | `ItemCardSplitCoordinatorTests.cs`<br>・テキスト未選択時の警告スナックバー (`RequestSplitSelectionAsync_WhenNoTextSelected_ShowsWarningSnackbarAndReturnsNull`)<br>・直接分割時の未選択警告スナックバー<br>・所有者ガード、本文不一致エラー |

### 6.2 不足しているテスト（ギャップ）と推奨対応

1. **ContractManagement のタブ切り替え bUnit テスト（優先度：高）**:
   - `InteractivePageDialogE2ETests.cs` の `ContractManagement_SwitchesTabsInteractively` は実ブラウザで「送信済み (Outbox)」タブをクリックし、「現在、送信したコントラクトの提案はありません。」が表示されることを検証している。
   - `ContractManagementTests.cs` に、`MudTabs` の2番目のタブ（Outbox）をクリックして送信済みパネルの内容が表示されることを検証する bUnit テストを追加する。
2. **ItemCard の未選択警告スナックバー bUnit 統合テスト（優先度：中）**:
   - `ItemCardSplitRequestTests.cs` では `IItemCardSplitCoordinator` をモックしているため、ボタンクリックから Coordinator の未選択警告スナックバー発火までを一連で検証する bUnit テストがまだない（Coordinator 単体テストとしては `ItemCardSplitCoordinatorTests.cs` に存在）。
   - `AddMockSrnsServices()`（実 Coordinator 登録）を使用した bUnit テストを1件追加し、テキスト未選択状態で分割リクエストボタンをクリックした際に `ISnackbar` に「分割するテキストが選択されていません。」が追加されることを検証することで、E2E と完全同等のカバレッジをミリ秒で達成できる。
3. **UserGroup メンバー追加 UI インタラクション（優先度：低 / 補強）**:
   - `UserGroupMembersDialogTests.cs` では削除ボタンクリックは検証されているが、Autocomplete 選択後の「追加」ボタンクリックの bUnit インタラクションを追加するとさらに強固になる（ViewModel 単体テストでは既に検証済み）。

---

## 7. まとめと実装者への推奨アクション

1. **UserGroup 領域**:
   - 既に `UserGroupCreateEditDialogTests`, `UserGroupMembersDialogTests`, `UserGroupListTests`, および各 ViewModelTests が極めて高密度に実装されている。
   - 既存テストが全てパスすることを確認すれば、E2E の `UserGroupList_ClickCreateButton_OpensDialog_AndCreatesGroup` は安全に責務縮小または整理可能。
2. **Home / ContractManagement タブ切り替え領域**:
   - Home 画面にタブは存在しないことを確認。
   - `ContractManagementTests.cs` にタブ切り替えテスト（Inbox -> Outbox）を 1 テスト追加することで、E2E の `ContractManagement_SwitchesTabsInteractively` を完全代替可能。
3. **ItemSplit 領域**:
   - 所有者／非所有者のボタン表示切り替えは `ItemCardSplitRequestTests.cs` で既に網羅。
   - 未選択警告スナックバーは `ItemCardSplitCoordinatorTests.cs` で既に網羅。
   - コンポーネント統合テストを 1 件補強することで、E2E の `ItemSplitE2ETests` を完全代替可能。
4. **E2E テストスイートの最適化**:
   - ブラウザが真に必須なシナリオ（`PasskeyLoginE2ETests.cs` の WebAuthn 仮想オーセンティケーターなど）に E2E の責務を限定し、UI 操作・ダイアログ・タブ切り替えをミリ秒単位の bUnit / 単体テストに集約することで、テスト実行速度を劇的に向上できる。
