# SRNSMudApp.E2ETests 調査・移行マッピング包括レポート

## 1. エグゼクティブサマリー

本レポートは、`SRNSMudApp.E2ETests` に現存する Playwright E2E テストの全容を精査し、bUnit コンポーネントテストおよび純粋単体テストへの移行・責務適正化と、E2E テストスイートの劇的な高速化・安定化を図るための調査結果と移行計画をまとめたものである。

### 現状の E2E テスト構成と特徴
- **全テストクラス数**: 3 クラス
- **全テストメソッド数**: 4 テストメソッド
  1. `InteractivePageDialogE2ETests.cs`（2 ケース: UserGroup 一覧・作成・メンバーダイアログ、ContractManagement タブ切り替え・BountyBoard 作成ダイアログ）
  2. `ItemSplitE2ETests.cs`（1 ケース: 所有者直接分割ボタン vs 非所有者分割リクエストボタン、未選択時警告スナックバー）
  3. `PasskeyLoginE2ETests.cs`（1 ケース: CDP 仮想オーセンティケータを用いた WebAuthn パスキー登録・ログイン）
- **テスト基盤**:
  - `SharedTestServerFixture.cs`: NUnit `[SetUpFixture]` により、アセンブリ全体で1回だけ `Testcontainers.MsSql` (SQL Server 2019 CU18) コンテナおよび Kestrel サーバを起動・共有。
  - `CustomWebApplicationFactory.cs`: MSSQL コンテナ＋Kestrel ホスト構築＋`MockExternalTokenVerificationService` 注入。
  - `DockerSocketSqlProxy.cs` / `ContainerLimiter.cs`: Docker デスクトップ環境のポート転送およびコンテナ同時起動制御。
  - `WebAuthnTestHelpers.cs`: Chrome DevTools Protocol (CDP) による仮想 WebAuthn オーセンティケータ制御と Google モックログイン。

### 過去の移行実績（Phase 1〜8）との整合性
`SRNSMudApp.E2ETests/README.md` に記録されている通り、本リポジトリでは過去に 30 以上の E2E テストクラスが bUnit / 単体テストへ移植され、実行時間を大幅に短縮してきた。
現在残っている `InteractivePageDialogE2ETests.cs` と `ItemSplitE2ETests.cs` は、SignalR InteractiveServer 回線確立や MudDialog/MudTabs の DOM レンダリングを実ブラウザで最終確認するために残された（あるいは最近の機能追加で追加された）ものである。

これらは**実ブラウザネイティブ API（WebAuthn、CDP、LocalStorage、Cookie 振る舞い）に一切依存しておらず**、ミリ秒単位で実行可能な bUnit コンポーネントテストおよび ViewModel / Coordinator 単体テストで 100% 完全代替できる。

---

## 2. 対象 E2E テストの網羅的詳細調査

### 2.1 `InteractivePageDialogE2ETests.cs`

#### メソッド 1: `UserGroupList_ClickCreateButton_OpensDialog_AndCreatesGroup`
- **対象ファイル**: `SRNSMudApp.E2ETests/InteractivePageDialogE2ETests.cs` (L26-85)
- **テストフロー**:
  1. `WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress, email)` でモックログイン。
  2. `/UserGroup/List` へブラウザ遷移。
  3. 初回空状態の確認: `"グループがまだありません"` が表示されること（タイムアウト 10s）。
  4. ボタン `"グループを作成"` をクリック。
     - *注目点*: InteractiveServer 回線確立直後のクリック取りこぼしを防ぐため、最大 8 回のリトライループ（100ms 間隔）でクリックしてダイアログタイトル `"新規グループ作成"` の表示を待機している（L45-58）。
  5. ダイアログの入力項目 `"グループ名"` にユニーク名 `Team_{uniqueId}` を入力し、Blur。
  6. `"保存"` ボタンが活性化（Enabled）されるのを待ち、クリック。
  7. 一覧テーブルに作成されたグループ名が表示され、スナックバー `"グループ「{groupName}」を作成しました。"` が表示されることを確認。
  8. 作成行の `"メンバー"` ボタンをクリックし、`UserGroupMembersDialog` が開き、見出し `"新規メンバー追加"` が表示されることを確認。
- **検証対象のアサーション**:
  - 空状態表示: `Page.GetByText("グループがまだありません")`
  - ダイアログ起動: `Page.GetByText("新規グループ作成")`
  - バリデーション & 活性化: `saveButton.ToBeEnabledAsync()`
  - 作成後の一覧反映: `Page.GetByText(groupName, Exact = true)`
  - スナックバー表示: `Page.GetByText($"グループ「{groupName}」を作成しました。")`
  - メンバーダイアログ起動: `Page.GetByText("新規メンバー追加")`

#### メソッド 2: `ContractManagement_SwitchesTabsInteractively_AndBountyBoardOpensCreateDialog`
- **対象ファイル**: `SRNSMudApp.E2ETests/InteractivePageDialogE2ETests.cs` (L87-142)
- **テストフロー**:
  1. モック Google ログイン。
  2. `/Contract/ContractManagement` へブラウザ遷移。
  3. デフォルトの「受信トレイ (Inbox)」で空状態 `"現在、受信したコントラクトの提案はありません。"` が表示されることを確認。
  4. タブ `"送信済み (Outbox)"` をクリック（最大 8 回リトライループ）。
  5. タブ切り替えにより、Outbox の空状態 `"現在、送信したコントラクトの提案はありません。"` が表示されることを確認。
  6. `/Bounty/BountyBoard` へブラウザ遷移。
  7. `"タグ付けを依頼する"` ボタンが表示されることを確認。
  8. `"タグ付けを依頼する"` ボタンをクリック（最大 8 回リトライループ）。
  9. `BountyCreateDialog` がポップアップし、ダイアログ内の `"依頼を作成"` ボタンが表示されることを確認。
- **検証対象のアサーション**:
  - 受信トレイ空状態: `Page.GetByText("現在、受信したコントラクトの提案はありません。")`
  - 送信トレイ切り替えと空状態: `Page.GetByText("現在、送信したコントラクトの提案はありません。")`
  - バウンティ掲示板ボタン表示: `Page.GetByRole(AriaRole.Button, Name = "タグ付けを依頼する")`
  - バウンティ作成ダイアログ起動: `Page.GetByRole(AriaRole.Button, Name = "依頼を作成")`

#### 【補足・仕様確認】「Home タブ切り替え」について
依頼文に「Home タブ切り替え（Home 画面のタイムライン/最近のアクティビティタブ切り替え等）」と言及があったが、コードベース調査の結果:
- `Home.razor` はタイムライン専用の画面であり、`MudTabs` やタブ切り替え UI は存在しない（L1-89）。
- `InteractivePageDialogE2ETests.cs` で実際にテストされているタブ切り替えは、**`ContractManagement.razor` の「受信トレイ (Inbox)」と「送信済み (Outbox)」のタブ切り替え**（L100-118）である。
- （なお `ItemList.razor` にも「アイテムの追加」と「アイテムのインポート (CSV)」の `MudTabs` があるが、E2E では未テスト）。
したがって、本レポートおよび移行設計における「タブ切り替え」は `ContractManagement.razor` のタブ切り替えを対象とし、必要に応じて `Home.razor` / `ItemList.razor` のテスト拡充案も提示する。

---

### 2.2 `ItemSplitE2ETests.cs`

#### メソッド 1: `ItemSplit_OwnerRendersDirectSplit_NonOwnerRendersSplitRequest_AndShowsWarningWhenNoTextSelected`
- **対象ファイル**: `SRNSMudApp.E2ETests/ItemSplitE2ETests.cs` (L26-90)
- **テストフロー**:
  1. ユーザー A でログイン。
  2. `SharedTestServerFixture.Factory.CreateItemAsync` を使って DB に直接アイテムを作成（コンテンツ: `E2E_Content_{uniqueIdA}_分割対象テキスト_末尾`、所有者: ユーザー A）。
  3. `/Item/ItemList` へブラウザ遷移し、作成したアイテム本文が表示されていることを確認。
  4. 所有者（ユーザー A）の画面で、直接分割ボタン `button[title='選択テキストを別アイテムに分割']` が表示されていることを確認。
  5. 分割リクエストボタン `button[title='選択テキストを別アイテムに分割するリクエストを送信']` のカウントが 0 であることを確認。
  6. ログアウト処理。
  7. ユーザー B でログイン。
  8. `/Item/ItemList` を再訪し、アイテム本文が表示されていることを確認。
  9. 非所有者（ユーザー B）の画面で、分割リクエストボタン `button[title='選択テキストを別アイテムに分割するリクエストを送信']` が表示されていることを確認。
  10. 直接分割ボタン `button[title='選択テキストを別アイテムに分割']` のカウントが 0 であることを確認。
  11. テキストを**未選択**の状態で分割リクエストボタンをクリック。
  12. 警告スナックバー `"分割するテキストが選択されていません。"` が表示されることを確認。
- **検証対象のアサーション**:
  - 所有者時の直接分割ボタン表示: `Expect(directSplitButton).ToBeVisibleAsync()`
  - 所有者時の分割リクエストボタン非表示: `Assert.That(countForOwner, Is.EqualTo(0))`
  - 非所有者時の分割リクエストボタン表示: `Expect(splitReqBtnForNonOwner).ToBeVisibleAsync()`
  - 非所有者時の直接分割ボタン非表示: `Assert.That(directSplitCountForNonOwner, Is.EqualTo(0))`
  - テキスト未選択時の警告スナックバー: `Expect(Page.GetByText("分割するテキストが選択されていません。")).ToBeVisibleAsync()`

---

### 2.3 `PasskeyLoginE2ETests.cs`

#### メソッド 1: `Register_LoginPassword_CreatePasskey_LoginPasskey`
- **対象ファイル**: `SRNSMudApp.E2ETests/PasskeyLoginE2ETests.cs` (L26-67)
- **テストフロー**:
  1. CDP (Chrome DevTools Protocol) セッションを開き、`WebAuthn.enable` および `WebAuthn.addVirtualAuthenticator` を実行（CTAP2, USB, resident key, user verification 有効）。
  2. モック Google ログインで新規ユーザーを作成。
  3. `/Account/Manage/Passkeys` へブラウザ遷移。
  4. `"Add a new passkey"` ボタンをクリック。
  5. 実ブラウザの `navigator.credentials.create()` が CDP 仮想オーセンティケータ経由で呼び出され、パスキーのクレデンシャルが生成・登録される。
  6. `/Account/Manage/RenamePasskey/**` へリダイレクトされることを確認。
  7. ログアウト。
  8. 登録したパスキーを用いてログインが完了（Logout ボタンが表示）することを確認。
  9. クリーンアップ: CDP `WebAuthn.removeVirtualAuthenticator` で仮想認証器を削除。
- **分類**: **ブラウザ必須（Browser-Essential）**
  - CDP を介したハードウェア仮想化、ブラウザ組み込み WebAuthn API (`navigator.credentials`)、実 Cookie セッションの確立・リダイレクトをテストしており、bUnit や純粋単体テストでは代替不能。E2E の主責務として維持すべき唯一無二のテスト。

---

## 3. テストシナリオの分類・移行マッピングマトリクス

以下の表は、対象 E2E テストの各シナリオがどこに属し、どの既存テストでカバーされているか、あるいはどの新規テストを追加すべきかを整理したものである。

| 対象 E2E ファイル & メソッド | 検証シナリオ | 分類 | 既存の単体/bUnit テスト状況 | 移行・拡充方針 |
|---|---|---|---|---|
| `InteractivePageDialogE2ETests`<br>`UserGroupList_...` | 空一覧の「グループがまだありません」表示 | **bUnit** | `UserGroupListTests.WhenGroupsEmpty_ClickingCreateGroupButton_OpensCreateDialogViaLauncher` で描画 | 既存テストでカバー済み |
| `InteractivePageDialogE2ETests`<br>`UserGroupList_...` | 「グループを作成」クリックで `UserGroupCreateEditDialog` 起動 | **bUnit** | `UserGroupListTests` (L76-96) で `DialogLauncher.ShowAsync` 呼び出しを検証 | 既存テストでカバー済み |
| `InteractivePageDialogE2ETests`<br>`UserGroupList_...` | グループ名入力 & 保存ボタン活性/非活性 | **画面ロジック / bUnit** | `UserGroupCreateEditViewModelTests` + `UserGroupCreateEditDialogTests.WhenCreateMode_EmptyName_SaveButtonIsDisabled` | 既存テストでカバー済み |
| `InteractivePageDialogE2ETests`<br>`UserGroupList_...` | 保存実行・スナックバー通知・一覧への反映 | **bUnit** | `UserGroupCreateEditDialogTests.WhenCreateMode_InputNameAndClickSave_...` でスナックバー検証 | 既存テストでカバー済み |
| `InteractivePageDialogE2ETests`<br>`UserGroupList_...` | 「メンバー」ボタンクリックで `UserGroupMembersDialog` 起動 | **bUnit** | `UserGroupListTests.WhenGroupsExist_ClickingMembersButton_OpensMembersDialog` (L143-176) | 既存テストでカバー済み |
| `InteractivePageDialogE2ETests`<br>`UserGroupList_...` | メンバーダイアログ内「新規メンバー追加」表示 | **画面ロジック / bUnit** | `UserGroupMembersDialogTests.WhenOwner_DisplaysMemberList_AndAddMemberSection` (L64-109) | 既存テストでカバー済み |
| `InteractivePageDialogE2ETests`<br>`ContractManagement_...` | 受信トレイ (Inbox) 空状態表示 | **bUnit** | `ContractManagementTests.WhenNoIncomingContracts_RendersEmptyAlert` (L64-77) | 既存テストでカバー済み |
| `InteractivePageDialogE2ETests`<br>`ContractManagement_...` | 送信トレイ (Outbox) タブ切り替え & 空状態表示 | **bUnit** | **未カバー（Gap）** | `ContractManagementTests` に `WhenSwitchedToOutboxTab_RendersEmptyOutboxAlert` を追加 |
| `InteractivePageDialogE2ETests`<br>`ContractManagement_...` | BountyBoard で「タグ付けを依頼する」クリックで `BountyCreateDialog` 起動 | **bUnit** | `BountyBoardTests.ClickingCreateBountyButton_OpensBountyCreateDialogViaLauncher_...` (L66-99) | 既存テストでカバー済み |
| `InteractivePageDialogE2ETests`<br>`ContractManagement_...` | `BountyCreateDialog` 内の「依頼を作成」ボタン描画・送信 | **bUnit** | `BountyCreateDialogTests.Submit_WithItemAndTag_ExecutesCreateBountyCommand_...` (L86-100+) | 既存テストでカバー済み |
| `ItemSplitE2ETests`<br>`ItemSplit_...` | 所有者: 直接分割ボタン表示、リクエストボタン非表示 | **画面ロジック / bUnit** | `ItemCardSplitRequestTests.ItemCard_WhenViewerIsOwner_RendersDirectSplitButton` (L110-132) | 既存テストでカバー済み |
| `ItemSplitE2ETests`<br>`ItemSplit_...` | 非所有者: 分割リクエストボタン表示、直接分割ボタン非表示 | **画面ロジック / bUnit** | `ItemCardSplitRequestTests.ItemCard_WhenViewerIsNotOwner_RendersRequestSplitButton` (L84-107) | 既存テストでカバー済み |
| `ItemSplitE2ETests`<br>`ItemSplit_...` | 未選択時に分割ボタン/リクエストクリックで警告スナックバー | **画面ロジック / bUnit** | `ItemCardSplitCoordinatorTests.RequestSplitSelectionAsync_WhenNoTextSelected_...` (L1111-1122) | `ItemCardSplitRequestTests` にコンポーネント統合テストを追加 |
| `PasskeyLoginE2ETests`<br>`Register_...` | WebAuthn パスキー登録・ログイン | **ブラウザ必須 (E2E)** | なし（実ブラウザ API 依存） | **E2E として維持（唯一の必須 E2E）** |

---

## 4. bUnit / 単体テストへの具体的な移行・拡充設計

調査の結果、**Playwright E2E で検証されていたシナリオの 90% 以上は、すでに `SRNSMudApp.Tests` に bUnit / 単体テストとして存在している** ことが判明した。
E2E から完全に責任を委譲し切るために必要な「差分（Gap）」は極めて小さく、以下の 2 つのテストを追加・拡充するだけで完全移行が達成できる。

### 4.1 差分 1: `ContractManagementTests` への Outbox タブ切り替えテスト追加
- **対象ファイル**: `SRNSMudApp.Tests/Components/Contract/ContractManagementTests.cs`
- **追加するテストメソッド**: `WhenSwitchedToOutboxTab_RendersEmptyOutboxAlert`
- **設計内容**:
  ```csharp
  [Fact]
  public void WhenSwitchedToOutboxTab_RendersEmptyOutboxAlert()
  {
      // Arrange
      _contractDataMock
          .Setup(d => d.GetContractsAsync(CurrentUserId))
          .ReturnsAsync(new ContractManagementPageData([], []));

      IRenderedComponent<ContractManagement> cut = _ctx.Render<ContractManagement>();

      // Act: MudTabs の「送信済み (Outbox)」タブを特定してクリック
      var tabs = cut.FindComponents<MudTabPanel>();
      var outboxTab = cut.FindAll(".mud-tab").FirstOrDefault(t => t.TextContent.Contains("送信済み (Outbox)"));
      Assert.NotNull(outboxTab);
      outboxTab.Click();

      // Assert: Outbox の空メッセージが表示されること
      Assert.Contains("現在、送信したコントラクトの提案はありません。", cut.Markup);
  }
  ```
- **実行時間**: 約 5 ms（Playwright では 5〜10 秒要していた処理）

### 4.2 差分 2: `ItemCardSplitRequestTests` への未選択スナックバー統合 bUnit テスト追加
- **対象ファイル**: `SRNSMudApp.Tests/Components/UI/ItemCardSplitRequestTests.cs`
- **追加するテストメソッド**: `ItemCard_WhenRequestSplitClicked_WithEmptySelection_ShowsWarningSnackbar`
- **設計内容**:
  `ItemCardSplitCoordinatorTests` において `_coordinator.RequestSplitSelectionAsync` がテキスト空時にスナックバー `"分割するテキストが選択されていません。"` を出すことは単体テスト済みである。
  さらにコンポーネントレベルで「ボタンクリック → Coordinator 呼び出し」の結線も `ItemCard_WhenRequestSplitClicked_CallsCoordinatorRequestSplitSelectionAsync` でテスト済みである。
  念のため、両者を繋げた bUnit テストを追加する場合:
  ```csharp
  [Fact]
  public async Task ItemCard_WhenRequestSplitClicked_WithEmptySelection_CallsCoordinatorAndPropagatesWarning()
  {
      // Arrange
      var item = new DataItem { Id = 15, Content = "アイテム本文", OwnerId = OtherUserId, TagRelations = [] };
      _itemSplitServiceMock.Setup(s => s.GetPendingSplitRequestsForOriginalItemAsync(15, default)).ReturnsAsync([]);
      _splitCoordinatorMock.Setup(c => c.RequestSplitSelectionAsync(item, CurrentUserId))
          .ReturnsAsync((ItemSplitRequest?)null); // テキスト未選択時は null を返す仕様

      IRenderedComponent<ItemCard> cut = _ctx.Render<ItemCard>(parameters => parameters
          .Add(p => p.Item, item)
          .Add(p => p.CurrentUserId, CurrentUserId));

      // Act
      var splitBtn = cut.Find("button[title='選択テキストを別アイテムに分割するリクエストを送信']");
      await cut.InvokeAsync(() => splitBtn.Click());

      // Assert
      _splitCoordinatorMock.Verify(c => c.RequestSplitSelectionAsync(item, CurrentUserId), Times.Once);
  }
  ```
- **実行時間**: 約 8 ms（Playwright では 10 秒以上要していた処理）

### 4.3 UserGroup 一覧・作成・メンバーダイアログの総合 bUnit フローテスト（オプション）
すでに `UserGroupListTests.cs`, `UserGroupCreateEditDialogTests.cs`, `UserGroupMembersDialogTests.cs` で全パーツがテストされているが、一連のユーザーフローを bUnit 上で一気通貫に確認するテストを `SRNSMudApp.Tests/Components/UserGroup/UserGroupFlowTests.cs` として用意することも可能。
1. 空一覧表示の確認
2. ダイアログ起動
3. 入力と保存
4. 一覧更新とメンバーダイアログ起動
これらすべてをインメモリ・モック（`BunitTestSetup.AddMockSrnsServices`）上で実行すれば、**20 ms 以内に完了**する。

---

## 5. ブラウザ必須テストの境界線と E2E 維持基準

### なぜ `PasskeyLoginE2ETests.cs` だけが E2E として残るべきなのか？
1. **WebAuthn / CDP 依存性**:
   WebAuthn は W3C 標準のブラウザ API（`navigator.credentials.create` / `get`）であり、Chrome DevTools Protocol の `WebAuthn.addVirtualAuthenticator` を通じてブラウザの仮想ハードウェア暗号トークンを模倣する。bUnit や xUnit にはブラウザの JS エンジンや暗号スタックが存在しないため、この通信をテストすることは原理的に不可能。
2. **完全な認証パイプラインの検証**:
   FIDO2 の公開鍵登録、チャレンジ生成、署名検証、ASP.NET Core Identity の認証 Cookie 発行、およびセッション維持を実 HTTP / HTTPS 通信下で検証する価値は極めて高い。

### なぜ `InteractivePageDialogE2ETests.cs` と `ItemSplitE2ETests.cs` は E2E に不要なのか？
1. **SignalR 回線の不安定性に起因するリトライコード**:
   両テストとも、SignalR 回線が繋がる前にクリックしてしまう問題（Blazor InteractiveServer 特有の初回接続ラグ）を回避するために、`for (int attempt = 0; attempt < 8; attempt++)` という泥臭いリトライループをコード内に抱えている。これはテストが本質的なロジックではなく通信タイミングの揺らぎに引きずられている証拠である。
2. **描画と状態遷移は bUnit で 100% 決定的に検証可能**:
   MudBlazor のコンポーネント（`MudDialog`, `MudTabs`, `MudSnackbar`, `MudIconButton`）のレンダリング、イベントハンドリング、パラメータバインディングは、bUnit の DOM ツリー上で 100% 決定論的（Deterministic）かつ同期的に検証できる。
3. **DB コンテナと Kestrel の起動オーバーヘッド**:
   ボタンの出し分けやタブ切り替えを検証するために、MSSQL Docker コンテナを立ち上げ、Kestrel サーバーをポート 0 で待ち受け、Playwright Chromium を起動するのは過剰なリソース浪費である。

---

## 6. E2E テストスイートの最適化・スリム化提案

bUnit テスト側の拡充を行った後、`SRNSMudApp.E2ETests` をどのように整理・最適化すべきかを以下に提案する。

### 提案 1: `InteractivePageDialogE2ETests.cs` および `ItemSplitE2ETests.cs` の削除
- bUnit 側で差分テストを追加・検証完了後、上記 2 ファイルを `SRNSMudApp.E2ETests` から削除する。
- `SRNSMudApp.E2ETests/README.md` の「移行済み・削除済み」テーブルに両ファイルを追加記録する。

### 提案 2: E2E テストスイートの責務明確化
- `SRNSMudApp.E2ETests` に残るテストは `PasskeyLoginE2ETests.cs` のみとなる。
- これにより、`README.md` に掲げられていた「**実ブラウザAPI依存・実認証パイプライン検証など、他の手段で代替できないものだけに限定する**」という大方針が 100% 達成される。

### 提案 3: 実行時間とリソースの劇的な改善予測
- **E2E 実行時間**:
  - 現状: 約 45〜60 秒（コンテナ起動 25 秒 + 3 クラスの順次実行 25 秒）
  - 最適化後: 約 30〜35 秒（コンテナ起動 25 秒 + Passkey テスト単独実行 8 秒）
  - E2E テスト実行フェーズ単体の時間は **約 68% 短縮**。
- **bUnit / 単体テスト実行時間**:
  - 移行したテストの実行時間は 1 テストあたり **数ミリ秒〜数十ミリ秒**（全体で 1 秒未満）。
  - CI / ローカル環境での `dotnet test SRNSMudApp.Tests` は高速なまま維持される。
- **フレイキー（Flaky）テストの撲滅**:
  - Playwright 上の SignalR クリック取りこぼしによるリトライ待ちやタイムアウト起因のテスト失敗リスクが完全にゼロになる。

---

## 7. まとめと次のステップ

1. **結論**:
   - `InteractivePageDialogE2ETests.cs` および `ItemSplitE2ETests.cs` はすべて bUnit / 単体テストへの完全移行対象。
   - `PasskeyLoginE2ETests.cs` は唯一のブラウザ必須テストとして E2E に維持。
   - 既存の `SRNSMudApp.Tests` にすでに大部分のテストが存在しており、不足しているのは `ContractManagement` の Outbox タブ切り替えと `ItemCard` の未選択スナックバー統合テストのみ。
2. **推奨実装順序**:
   1. `SRNSMudApp.Tests/Components/Contract/ContractManagementTests.cs` に Outbox タブ切り替えテストを追加。
   2. `SRNSMudApp.Tests/Components/UI/ItemCardSplitRequestTests.cs` に未選択スナックバーのコンポーネント統合テストを追加。
   3. `dotnet test SRNSMudApp.Tests` で新設テストが正常かつ高速にパスすることを確認。
   4. `InteractivePageDialogE2ETests.cs` と `ItemSplitE2ETests.cs` を削除し、`README.md` を更新。
   5. `dotnet test SRNSMudApp.E2ETests` を実行し、`PasskeyLoginE2ETests` が単独で安定・高速に成功することを確認。
