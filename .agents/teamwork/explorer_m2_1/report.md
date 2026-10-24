# E2E Test Suite Optimization & Retirement Investigation Report

**Date**: 2026-10-23  
**Investigator**: explorer_m2_1 (Milestone 2 Explorer 1)  
**Subject**: Retirement of redundant heavy UI E2E tests (`InteractivePageDialogE2ETests.cs`, `ItemSplitE2ETests.cs`) and preservation of browser-essential Passkey E2E tests (`PasskeyLoginE2ETests.cs`).

---

## 1. Executive Summary

- **Redundancy Assessment**: 100% of the UI interaction and screen logic scenarios in `InteractivePageDialogE2ETests.cs` and `ItemSplitE2ETests.cs` are now fully covered by deterministic, millisecond-level bUnit component tests and pure unit tests in `SRNSMudApp.Tests`.
- **Passkey Preservation**: `SRNSMudApp.E2ETests/PasskeyLoginE2ETests.cs` and its supporting infrastructure (`SharedTestServerFixture.cs`, `WebAuthnTestHelpers.cs`, `CustomWebApplicationFactory.cs`, `DockerSocketSqlProxy.cs`, `ContainerLimiter.cs`) have **zero** dependencies on the redundant test files and remain completely intact.
- **Retirement Impact**: Deleting the two redundant test files (`InteractivePageDialogE2ETests.cs` and `ItemSplitE2ETests.cs`) requires no project file (`.csproj`) modifications (due to SDK-style automatic item globbing), leaves `SRNSMudApp.E2ETests` focused strictly on its core mission (WebAuthn CDP virtual authenticator), eliminates SignalR click-retry loops and multi-user login churn, and preserves clean compilation (0 warnings, 0 errors).

---

## 2. Detailed Inspection of Candidate E2E Test Files

### 2.1 `InteractivePageDialogE2ETests.cs` (143 lines)
- **Role**: Validated Blazor InteractiveServer (SignalR) UI button clicks, MudDialog popups, and tab switching via Playwright.
- **Test Methods**:
  1. `UserGroupList_ClickCreateButton_OpensDialog_AndCreatesGroup`:
     - Navigates to `/UserGroup/List` and verifies empty state.
     - Clicks "グループを作成" button to open "新規グループ作成" dialog (`UserGroupCreateEditDialog`).
     - Fills group name input and clicks "保存".
     - Asserts group name in list and success snackbar `グループ「{groupName}」を作成しました。`.
     - Clicks "メンバー" button on the created group to open `UserGroupMembersDialog`.
     - Verifies "新規メンバー追加" section is visible.
  2. `ContractManagement_SwitchesTabsInteractively_AndBountyBoardOpensCreateDialog`:
     - Navigates to `/Contract/ContractManagement` and verifies empty incoming contract alert.
     - Clicks "送信済み (Outbox)" tab and verifies empty outgoing contract alert.
     - Navigates to `/Bounty/BountyBoard` and clicks "タグ付けを依頼する" button.
     - Verifies `BountyCreateDialog` opens containing "依頼を作成" submit button.

### 2.2 `ItemSplitE2ETests.cs` (91 lines)
- **Role**: Validated item split button visibility based on ownership, unselected text warning snackbar, and SignalR UI behavior.
- **Test Method**:
  1. `ItemSplit_OwnerRendersDirectSplit_NonOwnerRendersSplitRequest_AndShowsWarningWhenNoTextSelected`:
     - User A (owner) views item: "直接分割" button (`button[title='選択テキストを別アイテムに分割']`) is visible; "分割リクエスト" button is not visible.
     - User A logs out; User B (non-owner) logs in.
     - User B views item: "分割リクエスト" button (`button[title='選択テキストを別アイテムに分割するリクエストを送信']`) is visible; "直接分割" button is not visible.
     - User B clicks "分割リクエスト" button without selecting text: warning snackbar "分割するテキストが選択されていません。" is displayed.

---

## 3. Comprehensive Scenario Verification Matrix (E2E vs bUnit/Unit)

| E2E File & Scenario | Exact Operation / Assertion | Replacement in `SRNSMudApp.Tests` | Status |
|---|---|---|---|
| `InteractivePageDialogE2ETests`: UserGroup Empty State | "グループがまだありません" visible | `Components/UserGroup/UserGroupListTests.cs`: `WhenGroupsEmpty_ClickingCreateGroupButton_OpensCreateDialogViaLauncher` | **Fully Covered** |
| `InteractivePageDialogE2ETests`: Open Group Create Dialog | Click "グループを作成" -> Dialog "新規グループ作成" opens | `Components/UserGroup/UserGroupListTests.cs`: `WhenGroupsEmpty_ClickingCreateGroupButton_OpensCreateDialogViaLauncher` & `ClickingHeaderCreateButton_OpensCreateDialogViaLauncher` | **Fully Covered** |
| `InteractivePageDialogE2ETests`: Input & Validate Group Name | Fill name -> "保存" button enabled | `Components/UserGroup/UserGroupCreateEditDialogTests.cs`: `WhenCreateMode_EmptyName_SaveButtonIsDisabled` & `WhenCreateMode_InputNameAndClickSave_CreatesGroupAndClosesDialog`<br>Unit: `UserGroupCreateEditViewModelTests.CanSave_ValidatesConditions` | **Fully Covered** |
| `InteractivePageDialogE2ETests`: Save Group & Show Snackbar | Click "保存" -> Snackbar `グループ「{name}」を作成しました。` | `Components/UserGroup/UserGroupCreateEditDialogTests.cs`: `WhenCreateMode_InputNameAndClickSave_CreatesGroupAndClosesDialog`<br>Unit: `UserGroupCreateEditViewModelTests.SaveAsync_CreateMode_CallsCreateUserGroupAsyncAndReturnsSuccess` | **Fully Covered** |
| `InteractivePageDialogE2ETests`: Open Members Dialog | Click "メンバー" button -> `UserGroupMembersDialog` "メンバー管理" opens | `Components/UserGroup/UserGroupListTests.cs`: `WhenGroupsExist_ClickingMembersButton_OpensMembersDialog` | **Fully Covered** |
| `InteractivePageDialogE2ETests`: Member Dialog UI Verification | "新規メンバー追加" section visible | `Components/UserGroup/UserGroupMembersDialogTests.cs`: `WhenOwner_DisplaysMemberList_AndAddMemberSection` (line 105: `Assert.Contains("新規メンバー追加", host.Markup)`), plus member add/remove interaction tests | **Fully Covered** |
| `InteractivePageDialogE2ETests`: ContractManagement Empty State | "現在、受信したコントラクトの提案はありません。" visible | `Components/Contract/ContractManagementTests.cs`: `WhenNoIncomingContracts_RendersEmptyAlert` | **Fully Covered** |
| `InteractivePageDialogE2ETests`: ContractManagement Tab Switch | Click "送信済み (Outbox)" tab -> "現在、送信したコントラクトの提案はありません。" visible | `Components/Contract/ContractManagementTests.cs`: `WhenSwitchedToOutboxTab_RendersEmptyOutboxAlert` (lines 155-177) | **Fully Covered** |
| `InteractivePageDialogE2ETests`: BountyBoard Open Dialog | Click "タグ付けを依頼する" -> `BountyCreateDialog` opens | `Components/Bounty/BountyBoardTests.cs`: `ClickingCreateBountyButton_OpensBountyCreateDialogViaLauncher_AndReloadsOnSuccess` (lines 65-99) | **Fully Covered** |
| `InteractivePageDialogE2ETests`: BountyCreateDialog Submit Button | Dialog displays "依頼を作成" button | `Components/Bounty/BountyCreateDialogTests.cs`: `Submit_WithItemAndTag_ExecutesCreateBountyCommand_AndClosesDialog` (lines 131-133: `host.FindAll("button").First(b => b.TextContent.Contains("依頼を作成"))`) | **Fully Covered** |
| `ItemSplitE2ETests`: Owner Split Button Visibility | Owner sees "選択テキストを別アイテムに分割" and NOT "選択テキストを別アイテムに分割するリクエストを送信" | `Components/UI/ItemCardSplitRequestTests.cs`: `ItemCard_WhenViewerIsOwner_RendersDirectSplitButton` (lines 110-132)<br>Unit: `Data/IOwnableUnitTests.cs`: `IsOwnedBy_EvaluatesOwnershipCorrectly` | **Fully Covered** |
| `ItemSplitE2ETests`: Non-Owner Split Button Visibility | Non-owner sees "選択テキストを別アイテムに分割するリクエストを送信" and NOT "選択テキストを別アイテムに分割" | `Components/UI/ItemCardSplitRequestTests.cs`: `ItemCard_WhenViewerIsNotOwner_RendersRequestSplitButton` (lines 84-107)<br>Unit: `Data/IOwnableUnitTests.cs`: `IsOwnedBy_EvaluatesOwnershipCorrectly` | **Fully Covered** |
| `ItemSplitE2ETests`: Empty Text Warning Snackbar | Click split button without selecting text -> Snackbar "分割するテキストが選択されていません。" | `Components/UI/ItemCardSplitRequestTests.cs`: `ItemCard_WhenSplitClickedWithEmptySelection_ShowsWarningSnackbar` (lines 887-943, parameterized for both owner and non-owner) | **Fully Covered** |

All scenarios are covered with equal or greater fidelity in `SRNSMudApp.Tests`.

---

## 4. Preservation of Browser-Essential Passkey E2E Tests

### 4.1 Test Scope & Integrity of `PasskeyLoginE2ETests.cs`
- **Method**: `Register_LoginPassword_CreatePasskey_LoginPasskey`
- **Essential Browser Capabilities Required**:
  - Chrome DevTools Protocol (`CDP`) session activation: `page.Context.NewCDPSessionAsync(page)`.
  - Virtual Authenticator registration via CDP: `WebAuthn.enable`, `WebAuthn.addVirtualAuthenticator` (protocol CTAP2, transport USB, resident key, user verification).
  - WebAuthn browser API execution (`navigator.credentials.create`, `navigator.credentials.get`).
  - Real HTTP cookie generation, secure transport, and authentication middleware challenge.
- **Infrastructure Dependency Tree**:
  - `PasskeyLoginE2ETests.cs` -> `SharedTestServerFixture.cs` (Kestrel server & MSSQL Testcontainer single-instance lifecycle).
  - `PasskeyLoginE2ETests.cs` -> `WebAuthnTestHelpers.cs` (CDP Virtual Authenticator attach/remove, mock Google login).
  - `PasskeyLoginE2ETests.cs` -> `CustomWebApplicationFactory.cs` (Custom factory).
- **Independence Check**:
  - Zero imports or references to `InteractivePageDialogE2ETests` or `ItemSplitE2ETests`.
  - Zero shared static state or test sequencing dependencies.
  - `SharedTestServerFixture` was specifically authored to allow single-class execution without overhead.

---

## 5. Build and Project Integrity Analysis

1. **`SRNSMudApp.E2ETests.csproj`**:
   - Uses SDK-style implicit globbing: `<Project Sdk="Microsoft.NET.Sdk">`.
   - No explicit `<Compile Include="..." />` items for the candidate files.
   - Deleting `InteractivePageDialogE2ETests.cs` and `ItemSplitE2ETests.cs` leaves `.csproj` completely untouched and valid.
2. **`CustomWebApplicationFactory.cs` Helper Cleanliness**:
   - Method `public async Task<int> CreateItemAsync(string ownerUserName, string content)` was exclusively called by `ItemSplitE2ETests.cs:42`.
   - Retaining `CreateItemAsync` produces 0 compiler warnings/errors.
   - Alternatively, it can be kept as a utility or removed during the implementation cleanup.
3. **Execution Time Verification**:
   - `SRNSMudApp.Tests`: 3,209 passed, 0 failed, 0 skipped, completed in 44 seconds.
   - `SRNSMudApp.E2ETests`: Compiles with 0 warnings, 0 errors.

---

## 6. Recommended Retirement Plan

### Step-by-Step Execution Plan (For Implementer / Worker):
1. **Delete redundant E2E test files**:
   - `rm SRNSMudApp.E2ETests/InteractivePageDialogE2ETests.cs`
   - `rm SRNSMudApp.E2ETests/ItemSplitE2ETests.cs`
2. **Documentation update**:
   - Update `SRNSMudApp.E2ETests/README.md` to reflect that `InteractivePageDialogE2ETests` and `ItemSplitE2ETests` have been migrated to `SRNSMudApp.Tests`, keeping `PasskeyLoginE2ETests.cs` as the designated sole E2E test class.
3. **Verification**:
   - Run `dotnet build SRNSMudApp.E2ETests/SRNSMudApp.E2ETests.csproj --no-incremental` -> Expect 0 warnings, 0 errors.
   - Run `dotnet format --diagnostics IDE0055 --verify-no-changes` -> Expect clean formatting.
   - Run `dotnet test SRNSMudApp.Tests/SRNSMudApp.Tests.csproj` -> Expect 3,209/3,209 passed.
