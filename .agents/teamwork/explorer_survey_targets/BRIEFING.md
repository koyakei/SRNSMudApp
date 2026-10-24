# BRIEFING — 2026-10-24T06:24:00Z

## Mission
Investigate target Razor components, dialogs, pages, ViewModels, and services (UserGroup dialogs, Home tabs, ItemSplit owner/request/warning, screen logic/permissions) and existing E2E/unit tests to provide detailed report for bUnit and unit test migration.

## 🔒 My Identity
- Archetype: explorer
- Roles: investigation, synthesis
- Working directory: /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/explorer_survey_targets
- Original parent: 8417fc9a-1777-4cd7-aa91-1012c46c67e3
- Milestone: Survey Target Components & Existing Tests

## 🔒 Key Constraints
- Read-only investigation — do NOT implement
- Do not modify any code
- Produce detailed report in report.md and handoff.md

## Current Parent
- Conversation ID: 8417fc9a-1777-4cd7-aa91-1012c46c67e3
- Updated: 2026-10-24T06:24:00Z

## Investigation State
- **Explored paths**:
  - `SRNSMudApp.E2ETests/InteractivePageDialogE2ETests.cs`
  - `SRNSMudApp.E2ETests/ItemSplitE2ETests.cs`
  - `SRNSMudApp/Components/UserGroup/` (`UserGroupList`, `UserGroupCreateEditDialog`, `UserGroupMembersDialog`, `UserGroupMembers`, ViewModels)
  - `SRNSMudApp/Components/Pages/Home.razor`, `Home.razor.cs`, `HomeViewModel.cs`
  - `SRNSMudApp/Components/Contract/ContractManagement.razor`, `ContractManagement.razor.cs`
  - `SRNSMudApp/Components/UI/ItemCard.razor`, `ItemCard.razor.cs`, `ItemCardSplitCoordinator.cs`
  - `SRNSMudApp.Tests/Components/` and `SRNSMudApp.Tests/Services/`
- **Key findings**:
  - UserGroup dialogs (`UserGroupCreateEditDialog`, `UserGroupMembersDialog`) and list (`UserGroupList`) already have comprehensive bUnit and pure unit tests.
  - Home page (`Home.razor`) DOES NOT have any tabs. The tab switching in `InteractivePageDialogE2ETests.cs` actually tests `/Contract/ContractManagement` (`ContractManagement.razor`) switching between "受信トレイ (Inbox)" and "送信済み (Outbox)".
  - ItemSplit owner vs non-owner button toggling is implemented in `ItemCard.razor` line 50 via `((IOwnable)Item).IsOwnedBy(CurrentUserId)`. Direct split title: `選択テキストを別アイテムに分割`, request split title: `選択テキストを別アイテムに分割するリクエストを送信`.
  - Unselected text warning snackbar (`分割するテキストが選択されていません。`) is emitted in `ItemCardSplitCoordinator.cs` (lines 68, 135). Pure unit test in `ItemCardSplitCoordinatorTests.cs` verifies it; component test in `ItemCardSplitRequestTests.cs` mocks coordinator, leaving a minor integration gap for bUnit.
- **Unexplored areas**: None for target survey scope.

## Key Decisions Made
- Documented exact file paths, line numbers, DI, parameters, and callbacks in `report.md`.
- Clarified the discrepancy regarding Home tabs vs ContractManagement tabs so subsequent implementers don't get confused.
- Synthesized testing gaps and exact recommendations.

## Artifact Index
- /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/explorer_survey_targets/DISPATCH.md — Dispatch log
- /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/explorer_survey_targets/BRIEFING.md — Persistent context & state
- /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/explorer_survey_targets/report.md — Comprehensive survey report (deliverable)
- /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/explorer_survey_targets/handoff.md — 5-component handoff report
