## 2026-10-23T21:33:27Z
You are worker_m1.
Your working directory is: /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/worker_m1

MANDATORY FIRST STEP: Read /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/ORIGINAL_REQUEST.md completely (especially under ## 2026-10-23T21:13:45Z).
Also read:
- Project Plan: /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/orchestrator_4/PROJECT.md
- Explorer 1 Report (ContractManagement bUnit test): /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/explorer_m1_1/report.md
- Explorer 2 Report (ItemCard split snackbar bUnit test): /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/explorer_m1_2/report.md
- Explorer 3 Report (UserGroup dialogs & pure unit tests): /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/explorer_m1_3/report.md

DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_preview_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Scope & File Ownership:
You have exclusive write ownership over:
1. `SRNSMudApp.Tests/Components/Contract/ContractManagementTests.cs`
2. `SRNSMudApp.Tests/Components/UI/ItemCardSplitRequestTests.cs`
3. `SRNSMudApp.Tests/Components/UserGroup/UserGroupMembersDialogTests.cs`
4. `SRNSMudApp.Tests/Components/UserGroup/UserGroupMembersViewModelTests.cs`
5. `SRNSMudApp.Tests/Data/IOwnableUnitTests.cs` (create new)

Implementation Tasks:
1. In `SRNSMudApp.Tests/Components/Contract/ContractManagementTests.cs`:
   Implement `WhenSwitchedToOutboxTab_RendersEmptyOutboxAlert` following explorer 1's design. Assert that before switching, outbox message is not present, click "送信済み (Outbox)" tab, and assert "現在、送信したコントラクトの提案はありません。" is displayed.
2. In `SRNSMudApp.Tests/Components/UI/ItemCardSplitRequestTests.cs`:
   Implement `ItemCard_WhenSplitClickedWithEmptySelection_ShowsWarningSnackbar` following explorer 2's design. Wire real `ItemCardSplitCoordinator` with JSInterop returning empty string, trigger split button click, and assert that the warning snackbar "分割するテキストが選択されていません。" is added.
3. In `SRNSMudApp.Tests/Components/UserGroup/UserGroupMembersDialogTests.cs`:
   Implement member addition UI interaction tests (`WhenOwner_SelectsUserAndClicksAdd_CallsAddMemberAndShowsSnackbar_AndUpdatesList`, `WhenOwner_AddMemberFails_ShowsErrorSnackbar`) following explorer 3's design.
4. In `SRNSMudApp.Tests/Components/UserGroup/UserGroupMembersViewModelTests.cs`:
   Add `CanAddMember` condition unit tests following explorer 3's design.
5. In `SRNSMudApp.Tests/Data/IOwnableUnitTests.cs`:
   Create pure unit tests for `IOwnable.IsOwnedBy` predicate covering match, mismatch, null, empty user ID.

Verification Requirements:
- Run `dotnet build SRNSMudApp.Tests/SRNSMudApp.Tests.csproj` (must compile cleanly, 0 errors, 0 warnings).
- Run `dotnet test SRNSMudApp.Tests/SRNSMudApp.Tests.csproj` (all tests must pass).
- Run `dotnet format --diagnostics IDE0055 --verify-no-changes` to ensure zero style issues.

Write your changes and test outcomes to `/Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/worker_m1/handoff.md`.
When finished, send a completion message back.
