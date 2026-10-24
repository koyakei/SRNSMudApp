# E2E Test Execution and Fixtures Investigation Report

## Executive Summary
This investigation analyzed the E2E test suite in `SRNSMudApp.E2ETests` with a focus on `PasskeyLoginE2ETests.cs`, `SharedTestServerFixture.cs`, and `WebAuthnTestHelpers.cs`.
Key conclusions:
1. **PasskeyLoginE2ETests is completely independent** of `InteractivePageDialogE2ETests` and `ItemSplitE2ETests`. There are no shared data, state, helper dependencies, or execution order requirements.
2. **InteractivePageDialogE2ETests and ItemSplitE2ETests can be safely retired** without breaking `PasskeyLoginE2ETests` or the test infrastructure.
3. **Execution characteristics**: The suite runs under NUnit 4 + Playwright 1.48 + Testcontainers MSSQL. Full suite (4 tests) takes ~25s; running `PasskeyLoginE2ETests` alone takes ~10s (including MSSQL container start + Kestrel launch). Docker daemon access is required.

---

## 1. Inspection of E2E Fixtures and Passkey Tests

### 1.1 `PasskeyLoginE2ETests.cs`
- **Location**: `SRNSMudApp.E2ETests/PasskeyLoginE2ETests.cs`
- **Class Structure**:
  ```csharp
  [TestFixture]
  public class PasskeyLoginE2ETests : PageTest
  ```
- **Lifecycle & Setup**:
  - Inherits from `Microsoft.Playwright.NUnit.PageTest`, which automatically provides an isolated Playwright browser context and `Page` for each test.
  - In `[OneTimeSetUp]`, attaches to `SharedTestServerFixture.Factory` and `SharedTestServerFixture.ServerAddress`.
- **Test Scenario (`Register_LoginPassword_CreatePasskey_LoginPasskey`)**:
  1. Activates Chrome DevTools Protocol (CDP) virtual WebAuthn authenticator via `WebAuthnTestHelpers.EnableVirtualAuthenticatorAsync(Page)`.
  2. Generates a unique user identity with `Guid.NewGuid().ToString("N")[..8]` (`testuser_<id>@example.com`).
  3. Authenticates via `WebAuthnTestHelpers.LoginWithMockGoogleAsync(Page, _serverAddress!, email)`.
  4. Navigates to `/Account/Manage/Passkeys` and clicks `"Add a new passkey"`.
  5. Waits for redirect to `**/Account/Manage/RenamePasskey/**` (validating WebAuthn registration credential creation).
  6. Logs out via the `"Logout"` button.
  7. Re-authenticates via `WebAuthnTestHelpers.LoginWithMockGoogleAsync(...)` to ensure passkey login lifecycle works.
  8. Cleans up virtual authenticator with `WebAuthnTestHelpers.RemoveVirtualAuthenticatorAsync(cdp, authenticatorId)`.
- **E2E Justification**: Real browser CDP virtual authenticator and HTTP authentication cookie pipeline are strictly required. Cannot be replaced by bUnit.

### 1.2 `SharedTestServerFixture.cs`
- **Location**: `SRNSMudApp.E2ETests/SharedTestServerFixture.cs`
- **Class Structure**:
  ```csharp
  [SetUpFixture]
  public class SharedTestServerFixture
  ```
- **Lifecycle & Responsibilities**:
  - Assembly-level NUnit fixture shared across all test classes in `SRNSMudApp.E2ETests`.
  - `[System.Runtime.CompilerServices.ModuleInitializer]` invokes `SanitizeBrowserEnvironment()`:
    - Checks the `BROWSER` environment variable. If set to an executable path or non-standard value (common in VS Code / Linux environments), resets it to `null` to prevent `PlaywrightSettingsProvider` throwing `ArgumentException`. Only `chromium`, `firefox`, and `webkit` are allowed.
  - `EnsureInitialized()` (thread-safe lock on `s_lock`):
    - Sets dummy Google ClientId: `Environment.SetEnvironmentVariable("Authentication__Google__ClientId", "1234567890-dummy.apps.googleusercontent.com")`.
    - Creates and initializes single `CustomWebApplicationFactory` instance (`factory.EnsureServer()`).
    - Spins up single Testcontainers MSSQL container (`mcr.microsoft.com/mssql/server:2019-CU18-ubuntu-20.04`) and binds Kestrel on loopback port (`http://localhost:<dynamic_port>`).
  - `[OneTimeTearDown]`:
    - Disposes `CustomWebApplicationFactory` (stops Kestrel, cleans up SQL proxy, stops and removes MSSQL container).
    - Clears Google ClientId environment variable.

### 1.3 `WebAuthnTestHelpers.cs`
- **Location**: `SRNSMudApp.E2ETests/WebAuthnTestHelpers.cs`
- **Helper Methods**:
  - `EnableVirtualAuthenticatorAsync(IPage page)`:
    - Initiates CDP session: `await page.Context.NewCDPSessionAsync(page)`.
    - Sends `WebAuthn.enable`.
    - Adds virtual authenticator with CTAP2 protocol, USB transport, resident keys, and verified user.
    - Returns `(ICDPSession Cdp, JsonElement? AuthenticatorId)`.
  - `RemoveVirtualAuthenticatorAsync(ICDPSession cdp, JsonElement? authenticatorId)`:
    - Sends `WebAuthn.removeVirtualAuthenticator` with ID.
  - `LoginWithMockGoogleAsync(IPage page, string serverAddress, string email)`:
    - Clears cookies via `page.Context.ClearCookiesAsync()`.
    - Navigates to `/auth/callback?provider=Google&code=mock-{userName}` with commit wait.
    - Waits for redirect to root `{serverAddress}/`.
    - Verifies `"Logout"` button is visible.

---

## 2. Independence of `PasskeyLoginE2ETests`

We conducted a thorough dependency analysis between `PasskeyLoginE2ETests` and the candidate retirement tests (`InteractivePageDialogE2ETests`, `ItemSplitE2ETests`):

| Aspect | `PasskeyLoginE2ETests` | `InteractivePageDialogE2ETests` | `ItemSplitE2ETests` | Independence Confirmed |
|---|---|---|---|---|
| **Class Hierarchy** | Inherits `PageTest` | Inherits `PageTest` | Inherits `PageTest` | **Yes** (independent NUnit fixtures) |
| **Shared Fixture** | Reads `SharedTestServerFixture` | Reads `SharedTestServerFixture` | Reads `SharedTestServerFixture` | **Yes** (`SharedTestServerFixture` has no test-specific logic) |
| **User Identity** | `testuser_<guid8>@example.com` | `e2e_ug_<guid8>@...`, `e2e_cm_<guid8>@...` | `e2e_split_a_*`, `e2e_split_b_*` | **Yes** (zero user collision) |
| **Entities / Data** | Identity Passkeys only | UserGroups, Contracts, Bounties | Items (`CreateItemAsync`) | **Yes** (no shared entities) |
| **Execution Order** | No order dependencies | No order dependencies | No order dependencies | **Yes** (NUnit runs test classes independently) |
| **Isolated Execution** | **PASS** (1/1 passed in 10s) | N/A | N/A | **Yes** (verified via `--filter`) |

**Conclusion on Independence**:
`PasskeyLoginE2ETests` has **zero coupling** to `InteractivePageDialogE2ETests` or `ItemSplitE2ETests`.
Retiring `InteractivePageDialogE2ETests.cs` and `ItemSplitE2ETests.cs` from `SRNSMudApp.E2ETests` will cause no compilation or runtime regressions in `PasskeyLoginE2ETests`.

---

## 3. Test Command and Execution Characteristics

### 3.1 Test Framework and Runner Architecture
- **Framework**: .NET 11.0, C# 15 preview language version.
- **Test Runner**: NUnit 4.3.2 with `NUnit3TestAdapter` 5.0.0 and `Microsoft.NET.Test.Sdk` 17.14.1 (Standard VSTest protocol).
- **E2E Tooling**: `Microsoft.Playwright.NUnit` 1.48.0 (Chromium headless by default).
- **Database Backend**: `Testcontainers.MsSql` 3.8.0.
  - Accompanied by `DockerSocketSqlProxy.cs` (for sandboxed / network-separated socket tunneling) and `ContainerLimiter.cs` (locking max 4 concurrent containers).

### 3.2 Exact Execution Commands

```bash
# 1. Run full E2E test suite
dotnet test SRNSMudApp.E2ETests/SRNSMudApp.E2ETests.csproj

# 2. Run without rebuilding (faster iteration)
dotnet test SRNSMudApp.E2ETests/SRNSMudApp.E2ETests.csproj --no-build

# 3. Run only PasskeyLoginE2ETests (isolated filter)
dotnet test SRNSMudApp.E2ETests/SRNSMudApp.E2ETests.csproj --filter "FullyQualifiedName~PasskeyLoginE2ETests"

# 4. Verbose logging (if diagnosing issues)
dotnet test SRNSMudApp.E2ETests/SRNSMudApp.E2ETests.csproj --logger "console;verbosity=normal"
```

### 3.3 Execution Characteristics and Baseline Measurements

Measured on host machine (`mac`, .NET 11.0.100-rc.1):

1. **Docker Environment Requirement**:
   - Docker daemon must be running.
   - If running inside sandboxed agent environments, `BypassSandbox: true` is necessary to access `unix:///Users/keisukekoyanagi/.docker/run/docker.sock`. Direct sandboxed calls fail with `permission denied`.
2. **Startup Time Breakdown**:
   - MSBuild build & restore: ~1-2 seconds.
   - Testcontainers MSSQL container launch & readiness check: ~6-8 seconds.
   - Kestrel web host initialization + DB migration: ~1-2 seconds.
   - Total one-time fixed startup overhead per run: **~8-10 seconds**.
3. **Execution Timing Comparison**:
   - **Full current suite (4 tests across 3 classes)**:
     - Tests: `ContractManagement_SwitchesTabsInteractively...`, `UserGroupList_ClickCreateButton...`, `ItemSplit_OwnerRenders...`, `Register_LoginPassword_CreatePasskey...`
     - Duration: **25 seconds** (Passed: 4, Failed: 0).
   - **Target state (only `PasskeyLoginE2ETests`)**:
     - Tests: `Register_LoginPassword_CreatePasskey_LoginPasskey`
     - Duration: **10 seconds** (Passed: 1, Failed: 0).
   - **Optimization Impact**:
     - Retiring the redundant UI E2E tests saves **15 seconds** (~60% reduction in E2E test execution time).
     - Furthermore, the retired scenarios are already executed in `SRNSMudApp.Tests` via bUnit in **milliseconds**, eliminating SignalR UI click latency and retry loops.

---

## 4. Recommendations for Milestone 2 Implementation

1. **Retiring Redundant E2E Tests**:
   - Remove or deprecate `SRNSMudApp.E2ETests/InteractivePageDialogE2ETests.cs` and `SRNSMudApp.E2ETests/ItemSplitE2ETests.cs`.
   - Update `SRNSMudApp.E2ETests/README.md` to document the retirement into bUnit and unit test equivalents.
2. **Keeping Helper / Infrastructure Files**:
   - Retain `SharedTestServerFixture.cs`, `CustomWebApplicationFactory.cs`, `WebAuthnTestHelpers.cs`, `ContainerLimiter.cs`, and `DockerSocketSqlProxy.cs`.
3. **Verification Command**:
   - Run `dotnet test SRNSMudApp.E2ETests/SRNSMudApp.E2ETests.csproj` to confirm 1/1 tests pass cleanly in ~10 seconds.
