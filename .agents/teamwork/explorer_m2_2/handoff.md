# Handoff Report — E2E Test Execution & Fixture Investigation

## 1. Observation

1. **`PasskeyLoginE2ETests.cs`** (`SRNSMudApp.E2ETests/PasskeyLoginE2ETests.cs:12-68`):
   - Defined as `[TestFixture] public class PasskeyLoginE2ETests : PageTest`.
   - In `[OneTimeSetUp]` (lines 15-20), assigns `_factory = SharedTestServerFixture.Factory` and `_serverAddress = SharedTestServerFixture.ServerAddress`.
   - Test `Register_LoginPassword_CreatePasskey_LoginPasskey` (lines 26-67) calls:
     - `WebAuthnTestHelpers.EnableVirtualAuthenticatorAsync(Page)` (lines 31-32)
     - `WebAuthnTestHelpers.LoginWithMockGoogleAsync(...)` with unique user `$"testuser_{uniqueId}@example.com"` (lines 35-39, line 63)
     - Navigates to `/Account/Manage/Passkeys` (line 42)
     - `WebAuthnTestHelpers.RemoveVirtualAuthenticatorAsync(cdp, authenticatorId)` (line 66)
   - Does NOT reference `InteractivePageDialogE2ETests`, `ItemSplitE2ETests`, or any entities created by them.

2. **`SharedTestServerFixture.cs`** (`SRNSMudApp.E2ETests/SharedTestServerFixture.cs:19-114`):
   - Assembly-level fixture marked `[SetUpFixture]`.
   - `[System.Runtime.CompilerServices.ModuleInitializer]` runs `SanitizeBrowserEnvironment()` (lines 22-46) to reset non-standard `BROWSER` environment variables.
   - Sets `Authentication__Google__ClientId` to `"1234567890-dummy.apps.googleusercontent.com"` (lines 85-86).
   - Starts a single `CustomWebApplicationFactory` with MSSQL Testcontainer (`factory.EnsureServer()`, lines 88-94).
   - Contains no references to any individual test classes.

3. **`WebAuthnTestHelpers.cs`** (`SRNSMudApp.E2ETests/WebAuthnTestHelpers.cs:16-72`):
   - Provides `EnableVirtualAuthenticatorAsync`, `RemoveVirtualAuthenticatorAsync`, and `LoginWithMockGoogleAsync`.
   - Independent static helper methods using Playwright CDP session.

4. **Candidate Retirement Files**:
   - `InteractivePageDialogE2ETests.cs` (`SRNSMudApp.E2ETests/InteractivePageDialogE2ETests.cs:14-142`):
     - Contains tests `UserGroupList_ClickCreateButton_OpensDialog_AndCreatesGroup` and `ContractManagement_SwitchesTabsInteractively_AndBountyBoardOpensCreateDialog`.
   - `ItemSplitE2ETests.cs` (`SRNSMudApp.E2ETests/ItemSplitE2ETests.cs:15-90`):
     - Contains test `ItemSplit_OwnerRendersDirectSplit_NonOwnerRendersSplitRequest_AndShowsWarningWhenNoTextSelected`.

5. **Test Discovery & Execution Tool Commands and Results**:
   - `dotnet test SRNSMudApp.E2ETests/SRNSMudApp.E2ETests.csproj --list-tests`:
     ```
     The following Tests are available:
         ContractManagement_SwitchesTabsInteractively_AndBountyBoardOpensCreateDialog
         UserGroupList_ClickCreateButton_OpensDialog_AndCreatesGroup
         ItemSplit_OwnerRendersDirectSplit_NonOwnerRendersSplitRequest_AndShowsWarningWhenNoTextSelected
         Register_LoginPassword_CreatePasskey_LoginPasskey
     ```
   - Isolated execution of `PasskeyLoginE2ETests`:
     Command: `dotnet test SRNSMudApp.E2ETests/SRNSMudApp.E2ETests.csproj --filter "FullyQualifiedName~PasskeyLoginE2ETests"`
     Result:
     ```
     Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 10 s - SRNSMudApp.E2ETests.dll (net11.0)
     ```
   - Current full suite execution:
     Command: `dotnet test SRNSMudApp.E2ETests/SRNSMudApp.E2ETests.csproj`
     Result:
     ```
     Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 25 s - SRNSMudApp.E2ETests.dll (net11.0)
     ```
   - Environment observation: Running commands against Docker inside sandbox requires socket access (`BypassSandbox: true` in agent tooling, or direct host execution).

## 2. Logic Chain

1. From Observation 1, `PasskeyLoginE2ETests` interacts only with `/Account/Manage/Passkeys` and `/auth/callback`, uses its own unique GUID-based user email (`testuser_<id>@example.com`), and references only `SharedTestServerFixture` and `WebAuthnTestHelpers`.
2. From Observation 4, `InteractivePageDialogE2ETests` and `ItemSplitE2ETests` interact with `/UserGroup/List`, `/Contract/ContractManagement`, `/Bounty/BountyBoard`, and `/Item/ItemList`, using their own separate GUID-based user emails (`e2e_ug_*`, `e2e_cm_*`, `e2e_split_*`).
3. From Observation 2, `SharedTestServerFixture` provides only the host web application factory and MSSQL container; it has no dependencies on any specific test cases.
4. From Observation 5, executing `PasskeyLoginE2ETests` alone via `--filter` passed with 0 errors in 10 seconds without running `InteractivePageDialogE2ETests` or `ItemSplitE2ETests`.
5. Therefore, `PasskeyLoginE2ETests` has no functional, data, or execution-order dependency on `InteractivePageDialogE2ETests` or `ItemSplitE2ETests`.
6. Therefore, retiring `InteractivePageDialogE2ETests.cs` and `ItemSplitE2ETests.cs` is safe and will reduce the E2E suite execution time from 25s to ~10s.

## 3. Caveats

- **Docker daemon requirement**: Running `dotnet test SRNSMudApp.E2ETests` requires an active Docker daemon.
- **Headless mode**: Tests execute headless by default. Any change to display settings requires ensuring Playwright browser binaries are present.
- **Sequential execution**: NUnit parallelism is intentionally not enabled in `SharedTestServerFixture` to prevent database collisions across tests sharing the single MSSQL instance.

## 4. Conclusion

1. **Independence Confirmed**: `PasskeyLoginE2ETests` is fully isolated and decoupled from `InteractivePageDialogE2ETests` and `ItemSplitE2ETests`.
2. **Safe Retirement**: `InteractivePageDialogE2ETests.cs` and `ItemSplitE2ETests.cs` can be retired in Milestone 2 without affecting `PasskeyLoginE2ETests.cs` or the shared test infrastructure.
3. **Execution Command**: `dotnet test SRNSMudApp.E2ETests/SRNSMudApp.E2ETests.csproj` is the canonical command, passing in 10s once retired (down from 25s).

## 5. Verification Method

To independently verify:
1. Run the isolated test:
   ```bash
   dotnet test SRNSMudApp.E2ETests/SRNSMudApp.E2ETests.csproj --filter "FullyQualifiedName~PasskeyLoginE2ETests"
   ```
   Confirm 1/1 tests pass in ~10 seconds.
2. Invalidation condition: Any failure in `PasskeyLoginE2ETests` when `InteractivePageDialogE2ETests` and `ItemSplitE2ETests` are deleted or excluded would invalidate this conclusion.
