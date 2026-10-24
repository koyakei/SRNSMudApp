# Handoff Report: Target Components & Screen Logic Survey

## 1. Observation

- **UserGroup Dialogs & Pages**:
  - `UserGroupList.razor` (lines 18-21, 38-41, 89-100) and `UserGroupList.razor.cs` (lines 45-88):
    Injected: `UserGroupListViewModel`, `IDialogLauncher`, `ISnackbar`.
    Opens `UserGroupCreateEditDialog` ("新規グループ作成", "グループを編集") and `UserGroupMembersDialog` ("メンバー管理").
  - `UserGroupCreateEditDialog.razor` (lines 1-24) and `.razor.cs` (lines 20-33, 44-67):
    Parameters: `Group` (UserGroup?), `CurrentUserId` (string). Cascading: `MudDialog` (IMudDialogInstance).
    Injected: `UserGroupCreateEditViewModel`, `ISnackbar`.
    Calls `ViewModel.SaveAsync()`, displays snackbar `"グループ「...」を作成しました。"` or `"グループ「...」を更新しました。"`.
  - `UserGroupMembersDialog.razor` (lines 5-80) and `.razor.cs` (lines 20-33, 44-77):
    Parameters: `Group` (UserGroup), `CurrentUserId` (string). Cascading: `MudDialog` (IMudDialogInstance).
    Injected: `UserGroupMembersViewModel`, `ISnackbar`.
    Condition `@if (ViewModel.IsOwner)` controls visibility of "新規メンバー追加" section.
  - Existing tests:
    `UserGroupCreateEditDialogTests.cs` (lines 59-193, 4 bUnit tests).
    `UserGroupMembersDialogTests.cs` (lines 64-254, 4 bUnit tests).
    `UserGroupListTests.cs` (lines 75-248, 6 bUnit tests).
    `UserGroupCreateEditViewModelTests.cs` (10 pure unit tests).
    `UserGroupMembersViewModelTests.cs` (14 pure unit tests).
    `UserGroupListViewModelTests.cs` (12 pure unit tests).

- **Home Page & Tabs**:
  - `Home.razor` (lines 1-89): Contains `Virtualize<TimelineFeedGroup>`, desktop `AddItem`, mobile `MudFab` for `AddItemDialog`.
    **There are NO `MudTabs` or tab panels in `Home.razor`.**
  - `InteractivePageDialogE2ETests.cs` (lines 95-118):
    The tab test `ContractManagement_SwitchesTabsInteractively_AndBountyBoardOpensCreateDialog` tests `/Contract/ContractManagement`.
  - `ContractManagement.razor` (lines 19-108):
    Contains `<MudTabs>` with `<MudTabPanel Text="受信トレイ (Inbox)">` and `<MudTabPanel Text="送信済み (Outbox)">`.
    Text: `"現在、受信したコントラクトの提案はありません。"` and `"現在、送信したコントラクトの提案はありません。"`.
  - `ContractManagementTests.cs` (lines 64-153): Tests empty incoming contracts, accept button, reject button. Does NOT currently test switching to "送信済み (Outbox)" tab.

- **ItemSplit & Permission Checks**:
  - `ItemCard.razor` (lines 50-64):
    `@if (((IOwnable)Item).IsOwnedBy(CurrentUserId))`
    Owner renders Direct Split button: `title="選択テキストを別アイテムに分割"`, `data-testid="direct-split-button-{Item.Id}"`.
    Non-owner renders Split Request button: `title="選択テキストを別アイテムに分割するリクエストを送信"`, `data-testid="request-split-button-{Item.Id}"`.
  - `ItemCardSplitCoordinator.cs` (lines 66-70, 133-137):
    `if (string.IsNullOrWhiteSpace(selectedText)) { _ = _snackbar.Add("分割するテキストが選択されていません。", Severity.Warning); return ...; }`
  - Existing tests:
    `ItemCardSplitRequestTests.cs` (lines 83-132, 230-273): Verifies button rendering and click delegation to coordinator using mocked `IItemCardSplitCoordinator`.
    `ItemCardSplitCoordinatorTests.cs` (lines 74-85, 1110-1122): Pure unit tests verifying `"分割するテキストが選択されていません。"` warning snackbar for empty selection.

---

## 2. Logic Chain

1. **UserGroup E2E Parity**:
   - `InteractivePageDialogE2ETests.UserGroupList_ClickCreateButton_OpensDialog_AndCreatesGroup` verifies: (a) empty state "グループがまだありません", (b) clicking "グループを作成", (c) filling "グループ名" in `UserGroupCreateEditDialog` and saving, (d) verifying group and snackbar in list, (e) clicking "メンバー" and verifying `UserGroupMembersDialog` displays "新規メンバー追加".
   - `UserGroupListTests.cs` already verifies (a), (b), and (e). `UserGroupCreateEditDialogTests.cs` already verifies (c). `UserGroupMembersDialogTests.cs` already verifies (e).
   - `UserGroupCreateEditViewModelTests.cs`, `UserGroupMembersViewModelTests.cs`, and `UserGroupListViewModelTests.cs` verify all underlying state transitions and authorization checks in milliseconds.
   - Therefore, the UserGroup E2E flow is already fully backed by existing bUnit and unit tests.

2. **Tab Switching Parity**:
   - The user request mentions "Home タブ切り替え", but `Home.razor` has no tabs.
   - In `InteractivePageDialogE2ETests.cs`, the tab interaction is `ContractManagement_SwitchesTabsInteractively`, which tests `ContractManagement.razor` tabs (`受信トレイ (Inbox)` vs `送信済み (Outbox)`).
   - `ContractManagementTests.cs` currently tests the Inbox, but lacks a bUnit test clicking the Outbox tab and verifying `"現在、送信したコントラクトの提案はありません。"`.
   - Adding a tab-switching test to `ContractManagementTests.cs` provides 100% component-level parity for the E2E tab scenario.

3. **ItemSplit Parity**:
   - `ItemSplitE2ETests.cs` verifies: (a) owner renders direct split button & non-owner renders split request button, (b) clicking split request with empty text selection triggers `"分割するテキストが選択されていません。"`.
   - `ItemCardSplitRequestTests.cs` covers (a).
   - `ItemCardSplitCoordinatorTests.cs` covers (b) as a pure unit test.
   - Adding 1 bUnit integration test on `ItemCard` (using real `ItemCardSplitCoordinator` registered via `AddMockSrnsServices()`) to verify the warning snackbar on button click with unselected text will complete 100% bUnit parity.

---

## 3. Caveats

- In `SRNSMudApp.Client`, there are client-side WASM components (`UserGroupView.razor`, `Pages/UserGroupPage.razor`, `Pages/ContractManagementPage.razor`). These were inspected; they communicate with HTTP controllers rather than `IDialogLauncher` or direct ViewModels. The Playwright E2E tests target Blazor Server InteractiveServer routes (`/UserGroup/List`, `/Contract/ContractManagement`, `/Item/ItemList`), so the migration targets are strictly in `SRNSMudApp` and tested in `SRNSMudApp.Tests`.
- No code was modified during this survey.

---

## 4. Conclusion

- All target components, dialogs, ViewModels, coordinators, and domain services have been mapped with exact paths, line numbers, DI registrations, parameters, and callbacks.
- The separation between UI rendering concerns (bUnit targets) and screen control / permission logic (pure unit test targets) is clean and already largely realized in the codebase.
- The E2E tests `InteractivePageDialogE2ETests.cs` and `ItemSplitE2ETests.cs` are almost entirely redundant with existing bUnit and unit tests. Only two minor test additions are recommended:
  1. Add a tab-switching test to `ContractManagementTests.cs` (switching to Outbox tab).
  2. Add a bUnit integration test in `ItemCardSplitRequestTests.cs` (or dedicated test) verifying the warning snackbar when split request is clicked with empty selection.
- Detailed findings and architecture maps are recorded in `/Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/explorer_survey_targets/report.md`.

---

## 5. Verification Method

- Inspect report: `/Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/explorer_survey_targets/report.md`
- Inspect components:
  - `SRNSMudApp/Components/UserGroup/UserGroupList.razor`
  - `SRNSMudApp/Components/UserGroup/UserGroupCreateEditDialog.razor`
  - `SRNSMudApp/Components/UserGroup/UserGroupMembersDialog.razor`
  - `SRNSMudApp/Components/Contract/ContractManagement.razor`
  - `SRNSMudApp/Components/UI/ItemCard.razor`
- Inspect existing tests:
  - `SRNSMudApp.Tests/Components/UserGroup/UserGroupCreateEditDialogTests.cs`
  - `SRNSMudApp.Tests/Components/UserGroup/UserGroupMembersDialogTests.cs`
  - `SRNSMudApp.Tests/Components/UserGroup/UserGroupListTests.cs`
  - `SRNSMudApp.Tests/Components/Contract/ContractManagementTests.cs`
  - `SRNSMudApp.Tests/Components/UI/ItemCardSplitRequestTests.cs`
  - `SRNSMudApp.Tests/Services/ItemCardSplitCoordinatorTests.cs`
