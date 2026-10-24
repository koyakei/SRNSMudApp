# Project: E2E to bUnit / Unit Test Migration and Optimization

## Architecture
- **Framework**: .NET 11.0, ASP.NET Core Blazor Web App (InteractiveServer & WASM), MudBlazor
- **Component Test Framework**: bUnit + xUnit (`BunitTestSetup`, `MudDialogProvider`, `MudSnackbarProvider`)
- **Unit Test Framework**: xUnit + NSubstitute / Moq for ViewModels, Coordinators, Services
- **E2E Test Framework**: Microsoft.Playwright + Testcontainers MSSQL + WebAuthn CDP virtual authenticator

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | BUNIT-UG-01 | UserGroup dialog bUnit test verification & member add interaction test in UserGroupMembersDialogTests | M1 | survey_targets & survey_e2e |
| 2 | BUNIT-TAB-01 | ContractManagement MudTabs Outbox tab switching bUnit test in ContractManagementTests | M1 | survey_targets & survey_e2e |
| 3 | BUNIT-SPLIT-01 | ItemCard unselected warning snackbar integration bUnit test in ItemCardSplitRequestTests | M1 | survey_targets & survey_e2e |
| 4 | UNIT-LOGIC-01 | Pure unit tests coverage verification for ownership, permissions, and conditions (R2) | M1 | survey_targets & survey_e2e |
| 5 | E2E-OPT-01 | Refactor/retire redundant UI E2E tests (InteractivePageDialogE2ETests, ItemSplitE2ETests) | M2 | survey_e2e & user request |
| 6 | E2E-PASSKEY-01 | Retain and verify browser-essential Passkey E2E tests (PasskeyLoginE2ETests) | M2 | survey_e2e & user request |
| 7 | VERIF-ALL-01 | Full verification: build --no-incremental, format IDE0055, and all test suites pass | M2 | parent dispatch & acceptance criteria |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | bUnit Component & Screen Logic Tests | BUNIT-UG-01, BUNIT-TAB-01, BUNIT-SPLIT-01, UNIT-LOGIC-01 | none | DONE |
| 2 | E2E Suite Optimization & Full Verification | E2E-OPT-01, E2E-PASSKEY-01, VERIF-ALL-01 | M1 | DONE |

## Interface Contracts
### bUnit Test Suite
- `SRNSMudApp.Tests/Components/Contract/ContractManagementTests.cs`:
  - `WhenSwitchedToOutboxTab_RendersEmptyOutboxAlert`: simulates clicking "送信済み (Outbox)" tab and asserts empty alert text.
- `SRNSMudApp.Tests/Components/UI/ItemCardSplitRequestTests.cs`:
  - `ItemCard_WhenSplitClickedWithEmptySelection_ShowsWarningSnackbar`: verifies warning snackbar "分割するテキストが選択されていません。" via coordinator integration.
- `SRNSMudApp.Tests/Components/UserGroup/UserGroupMembersDialogTests.cs`:
  - Member addition UI interaction test when clicking add button.
- `SRNSMudApp.Tests/Components/UserGroup/UserGroupMembersViewModelTests.cs`:
  - `CanAddMember` condition evaluations.
- `SRNSMudApp.Tests/Data/IOwnableUnitTests.cs`:
  - `IOwnable.IsOwnedBy` unit tests.

### E2E Test Suite
- `SRNSMudApp.E2ETests/PasskeyLoginE2ETests.cs`:
  - Preserved as primary E2E scenario validating real browser WebAuthn CDP virtual authenticator and auth cookies.
- Redundant dialog and split UI E2E tests retired from `SRNSMudApp.E2ETests`.

## Code Layout
- `SRNSMudApp.Tests/Components/` - bUnit component tests
- `SRNSMudApp.Tests/Services/` - ViewModel, Coordinator, Service unit tests
- `SRNSMudApp.E2ETests/` - Playwright E2E tests
