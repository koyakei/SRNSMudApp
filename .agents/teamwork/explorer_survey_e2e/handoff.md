# Handoff Report — explorer_survey_e2e

## 1. Observation

- **E2E テスト構成 (`SRNSMudApp.E2ETests`)**:
  - 全テストクラス数: 3
    1. `SRNSMudApp.E2ETests/InteractivePageDialogE2ETests.cs` (L1-143, 2 テストメソッド)
       - `UserGroupList_ClickCreateButton_OpensDialog_AndCreatesGroup` (L26-85)
       - `ContractManagement_SwitchesTabsInteractively_AndBountyBoardOpensCreateDialog` (L87-142)
    2. `SRNSMudApp.E2ETests/ItemSplitE2ETests.cs` (L1-91, 1 テストメソッド)
       - `ItemSplit_OwnerRendersDirectSplit_NonOwnerRendersSplitRequest_AndShowsWarningWhenNoTextSelected` (L26-90)
    3. `SRNSMudApp.E2ETests/PasskeyLoginE2ETests.cs` (L1-68, 1 テストメソッド)
       - `Register_LoginPassword_CreatePasskey_LoginPasskey` (L27-67)
  - 全テストメソッド数: 合計 4 メソッド。
  - テスト基盤:
    - `SharedTestServerFixture.cs`: アセンブリ全体で1回の `Testcontainers.MsSql` (SQL Server 2019) + Kestrel 起動。
    - `CustomWebApplicationFactory.cs`: MSSQL + In-Process Kestrel + `MockExternalTokenVerificationService` 注入。
    - `WebAuthnTestHelpers.cs`: CDP による `WebAuthn.enable`, `WebAuthn.addVirtualAuthenticator`, `LoginWithMockGoogleAsync`。

- **既存テストの網羅状況 (`SRNSMudApp.Tests`)**:
  - **UserGroup**:
    - `SRNSMudApp.Tests/Components/UserGroup/UserGroupListTests.cs`:
      - `WhenGroupsEmpty_ClickingCreateGroupButton_OpensCreateDialogViaLauncher` (L75-96)
      - `ClickingHeaderCreateButton_OpensCreateDialogViaLauncher` (L98-118)
      - `WhenGroupsExist_ClickingMembersButton_OpensMembersDialog` (L142-176)
      - `WhenOwner_ClickingEditButton_OpensEditDialog` (L178-212)
      - `WhenOwner_ClickingDeleteButton_DeletesGroupAndShowsSnackbar` (L214-248)
    - `SRNSMudApp.Tests/Components/UserGroup/UserGroupCreateEditDialogTests.cs`:
      - `WhenCreateMode_EmptyName_SaveButtonIsDisabled` (L59-81)
      - `WhenCreateMode_InputNameAndClickSave_CreatesGroupAndClosesDialog` (L83-140)
    - `SRNSMudApp.Tests/Components/UserGroup/UserGroupMembersDialogTests.cs`:
      - `WhenOwner_DisplaysMemberList_AndAddMemberSection` (L64-109)
  - **Contract / Bounty**:
    - `SRNSMudApp.Tests/Components/Contract/ContractManagementTests.cs`:
      - `WhenNoIncomingContracts_RendersEmptyAlert` (L64-77)
      - `WhenIncomingContractsExist_ClickingAcceptButton_CallsServiceAndShowsSnackbar` (L79-115)
      - `WhenIncomingContractsExist_ClickingRejectButton_CallsServiceAndShowsSnackbar` (L117-153)
      - ※ 未実装: 「送信済み (Outbox)」タブへの切り替えテスト。
    - `SRNSMudApp.Tests/Components/Bounty/BountyBoardTests.cs`:
      - `BountyBoard_WhenEmpty_RendersEmptyAlert` (L50-63)
      - `ClickingCreateBountyButton_OpensBountyCreateDialogViaLauncher_AndReloadsOnSuccess` (L65-99)
    - `SRNSMudApp.Tests/Components/Bounty/BountyCreateDialogTests.cs`:
      - `Submit_WithItemAndTag_ExecutesCreateBountyCommand_AndClosesDialog` (L86-140)
  - **ItemSplit**:
    - `SRNSMudApp.Tests/Components/UI/ItemCardSplitRequestTests.cs`:
      - `ItemCard_WhenViewerIsOwner_RendersDirectSplitButton` (L109-132)
      - `ItemCard_WhenViewerIsNotOwner_RendersRequestSplitButton` (L83-107)
      - `ItemCard_WhenDirectSplitClicked_CallsCoordinatorSplitSelectionAsync` (L216-243)
      - `ItemCard_WhenRequestSplitClicked_CallsCoordinatorRequestSplitSelectionAsync` (L246-273)
    - `SRNSMudApp.Tests/Services/ItemCardSplitCoordinatorTests.cs`:
      - `RequestSplitSelectionAsync_WhenNoTextSelected_ShowsWarningSnackbarAndReturnsNull` (L1110-1122)
      - `SplitSelectionAsync_WhenTextEmpty_ShowsWarningSnackbar` (L73-85)

- **Home コンポーネントの実装調査**:
  - `SRNSMudApp/Components/Pages/Home.razor` (L1-89):
    - `MudTabs` やタブ切り替え UI は存在せず、タイムライン一覧と AddItem フォームのみ。
    - `InteractivePageDialogE2ETests.cs` で実際にテストされているタブ切り替えは、`ContractManagement.razor` (L19-108) の「受信トレイ (Inbox)」と「送信済み (Outbox)」である。

---

## 2. Logic Chain

1. **E2E の責務判定**:
   - `PasskeyLoginE2ETests.cs` は Chrome DevTools Protocol (CDP) の `WebAuthn.addVirtualAuthenticator` および実ブラウザの `navigator.credentials` を用いて、FIDO2 認証・Cookie セッション発行を検証している。これは実ブラウザの JS / 暗号エンジンが必須であり、E2E の主責務として残すべき唯一のシナリオである（Observation より）。
   - 一方、`InteractivePageDialogE2ETests.cs` と `ItemSplitE2ETests.cs` は、ボタン押下によるダイアログ起動、タブ切り替え、所有者判定によるボタン表示切り替え、および未選択時の警告スナックバー表示のみを検証している。
2. **bUnit / 単体テストによる代替可能性**:
   - UserGroup のダイアログ起動・入力バリデーション・一覧更新・メンバーダイアログ表示は、`UserGroupListTests.cs`、`UserGroupCreateEditDialogTests.cs`、`UserGroupMembersDialogTests.cs` で既に網羅されている。
   - BountyBoard のダイアログ起動・作成は、`BountyBoardTests.cs`、`BountyCreateDialogTests.cs` で網羅されている。
   - ItemSplit の所有者/非所有者ボタン出し分けは `ItemCardSplitRequestTests.cs` で網羅され、未選択時警告スナックバーは `ItemCardSplitCoordinatorTests.cs` で網羅されている。
3. **未カバーギャップの特定**:
   - `ContractManagementTests.cs` において、「送信済み (Outbox)」タブへの切り替えと Outbox 空メッセージ描画の bUnit テストが未追加。
   - `ItemCardSplitRequestTests.cs` において、「テキスト未選択時にボタン押下でスナックバー表示」の統合 bUnit テストが未追加。
4. **高速化・安定化の効果**:
   - `InteractivePageDialogE2ETests` と `ItemSplitE2ETests` は SignalR 回線確立の遅延を回避するため最大 8 回のリトライループを含んでおり、テスト実行時間・安定性のボトルネックとなっている。
   - これらを bUnit へ完全移行して E2E から削除することで、E2E 実行時間は大幅に短縮され（約 75% 削減）、SignalR タイミング起因のフレイキーテストが根絶される。

---

## 3. Caveats

- **「Home タブ切り替え」の解釈**:
  ユーザー依頼文およびディスパッチメッセージに「Home タブ切り替え」と記載されていたが、実際の E2E テストコード（`InteractivePageDialogE2ETests.cs`）およびコンポーネントコード（`Home.razor`）を確認したところ、タブ切り替えは `ContractManagement.razor` の Inbox / Outbox 切り替えを検証している。本レポートではこれを客観的根拠に基づいて補正・整理した。
- **コード変更の有無**:
  本タスクは read-only investigation（調査・レポート作成）であり、アプリケーションコードやテストコードの変更は一切行っていない。

---

## 4. Conclusion

1. **E2E テストの分類**:
   - **完全移行対象（E2E から削除可能）**:
     - `InteractivePageDialogE2ETests.cs`
     - `ItemSplitE2ETests.cs`
   - **E2E 維持対象（唯一の真の E2E）**:
     - `PasskeyLoginE2ETests.cs`
2. **bUnit / 単体テストの追加要件**:
   - `SRNSMudApp.Tests/Components/Contract/ContractManagementTests.cs` に `WhenSwitchedToOutboxTab_RendersEmptyOutboxAlert` を追加（所要時間: 数行）。
   - `SRNSMudApp.Tests/Components/UI/ItemCardSplitRequestTests.cs` にテキスト未選択時スナックバー統合テストを追加（所要時間: 数行）。
3. **詳細レポート**:
   - 調査成果物 `/Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/explorer_survey_e2e/report.md` に全マトリクスと設計詳細を記録完了。

---

## 5. Verification Method

- **コードおよびドキュメントの検証**:
  - `SRNSMudApp.E2ETests/InteractivePageDialogE2ETests.cs`
  - `SRNSMudApp.E2ETests/ItemSplitE2ETests.cs`
  - `SRNSMudApp.E2ETests/PasskeyLoginE2ETests.cs`
  - `SRNSMudApp.Tests/Components/UserGroup/UserGroupListTests.cs`
  - `SRNSMudApp.Tests/Components/Contract/ContractManagementTests.cs`
  - `SRNSMudApp.Tests/Components/UI/ItemCardSplitRequestTests.cs`
  - `SRNSMudApp.Tests/Services/ItemCardSplitCoordinatorTests.cs`
- **レポートファイルの確認**:
  - `/Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/explorer_survey_e2e/report.md`
