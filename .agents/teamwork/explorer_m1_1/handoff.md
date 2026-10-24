# Handoff Report: MudTabs Tab Switching Test Design

## 1. Observation

1. **`SRNSMudApp/Components/Contract/ContractManagement.razor` (lines 19–25 and lines 65–70)**:
   - Line 19: `<MudTabs Elevation="2" Rounded="true" ApplyEffectsToContainer="true" Class="mt-4">`
   - Lines 20–25: `<MudTabPanel Text="受信トレイ (Inbox)">` contains:
     `<MudAlert Severity="Severity.Info">現在、受信したコントラクトの提案はありません。</MudAlert>` when `!loaded.IncomingContracts.Any()`.
   - Lines 65–70: `<MudTabPanel Text="送信済み (Outbox)">` contains:
     `<MudAlert Severity="Severity.Info">現在、送信したコントラクトの提案はありません。</MudAlert>` when `!loaded.OutgoingContracts.Any()`.
   - MudTabs has no `KeepPanelsAlive="true"`; inactive panels are unmounted by default.

2. **`SRNSMudApp.Tests/Components/Contract/ContractManagementTests.cs` (lines 29–78)**:
   - Sets up `_contractDataMock`, `_contractServiceMock`, `_snackbarMock`, `_authContext` (`CurrentUserId = "test-contract-user"`), and renders `MudPopoverProvider`.
   - Line 64: Existing test `WhenNoIncomingContracts_RendersEmptyAlert()` sets:
     ```csharp
     _contractDataMock
         .Setup(d => d.GetContractsAsync(CurrentUserId))
         .ReturnsAsync(new ContractManagementPageData([], []));
     ```
     and asserts `Assert.Contains("現在、受信したコントラクトの提案はありません。", cut.Markup);`.
   - Tests `WhenIncomingContractsExist_ClickingAcceptButton_CallsServiceAndShowsSnackbar` and `WhenIncomingContractsExist_ClickingRejectButton_CallsServiceAndShowsSnackbar` use synchronous `public void` and `button.Click()`.

3. **`SRNSMudApp.Tests/Components/Contract/DuplicateTaggingRequestCancelTests.cs` (lines 92–93)**:
   - Uses DOM click on `.mud-tab` to switch tabs on `ContractManagement`:
     ```csharp
     host.WaitForState(() => host.Markup.Contains("送信済み"));
     host.FindAll(".mud-tab").First(t => t.TextContent.Contains("送信済み")).Click();
     ```
   - Running `dotnet test SRNSMudApp.Tests/SRNSMudApp.Tests.csproj --filter "FullyQualifiedName~DuplicateTaggingRequestCancelTests"` executes 2 tests in 392ms and passes cleanly.

4. **`SRNSMudApp.Tests/Components/Contract/ProposeContractDialogTests.cs` (lines 182–183) & `ItemDetailDeepLinkTests.cs` (lines 96–97)**:
   - Uses component API `ActivatePanelAsync(1)`:
     ```csharp
     IRenderedComponent<MudTabs> tabs = cut.FindComponent<MudTabs>();
     await cut.InvokeAsync(() => tabs.Instance.ActivatePanelAsync(1));
     ```

5. **`SRNSMudApp.E2ETests/InteractivePageDialogE2ETests.cs` (lines 88–119)**:
   - Lines 100–118: Locates `Page.GetByRole(AriaRole.Tab, new() { Name = "送信済み (Outbox)" })` and clicks with up to 8 retry attempts and 500ms timeouts awaiting `"現在、送信したコントラクトの提案はありません。"`.

---

## 2. Logic Chain

1. From Observation 1, `ContractManagement.razor` only renders the active panel's children because `KeepPanelsAlive` is omitted. Therefore, when `CurrentUserId` has zero contracts (`[]`, `[]`), the initial render contains the Inbox empty alert (`"現在、受信したコントラクトの提案はありません。"`) and does not contain the Outbox empty alert (`"現在、送信したコントラクトの提案はありません。"`).
2. From Observation 2, `ContractManagementTests` already configures the necessary DI services (`IContractManagementDataProvider`, `ITaggingContractService`, `ISnackbar`, `MudPopoverProvider`, `AuthenticationState`). A new test method only needs to configure `_contractDataMock.Setup(d => d.GetContractsAsync(CurrentUserId)).ReturnsAsync(new ContractManagementPageData([], []));`. No other mock setups are needed since no contract actions or snackbars are invoked.
3. From Observation 3 and 4, there are two established patterns in the codebase for MudTabs switching in bUnit:
   - DOM click on `.mud-tab`: `cut.FindAll(".mud-tab").First(t => t.TextContent.Contains("送信済み (Outbox)")).Click();`
   - Component API: `tabs.Instance.ActivatePanelAsync(1)`
4. Direct DOM click is superior as the primary recommendation because:
   - It directly exercises the event listener on the rendered HTML tab element, exactly replicating the user interaction previously checked in Playwright E2E (Observation 5).
   - It matches the synchronous `public void` signature and structure of existing tests in `ContractManagementTests.cs` (Observation 2) and `DuplicateTaggingRequestCancelTests.cs` (Observation 3).
5. Before clicking the tab, asserting `Assert.DoesNotContain("現在、送信したコントラクトの提案はありません。", cut.Markup);` proves that the Outbox empty alert was not previously visible. After clicking, asserting `cut.WaitForAssertion(() => Assert.Contains("現在、送信したコントラクトの提案はありません。", cut.Markup));` reliably confirms that tab activation triggered the rendering of the Outbox empty alert.

---

## 3. Caveats

- MudBlazor tab animation/sliding uses JS interop internally; this is already accommodated by `_ctx.JSInterop.Mode = JSRuntimeMode.Loose;` in `ContractManagementTests` constructor.
- If MudBlazor's internal markup changes in a future major version from `.mud-tab` to a different class, the DOM selector would need updating. The alternative component API method (`tabs.Instance.ActivatePanelAsync(1)`) was also documented in `report.md` as a fallback.

---

## 4. Conclusion

The exact test method `WhenSwitchedToOutboxTab_RendersEmptyOutboxAlert` should be added to `SRNSMudApp.Tests/Components/Contract/ContractManagementTests.cs`:

```csharp
    [Fact]
    public void WhenSwitchedToOutboxTab_RendersEmptyOutboxAlert()
    {
        // Arrange
        _contractDataMock
            .Setup(d => d.GetContractsAsync(CurrentUserId))
            .ReturnsAsync(new ContractManagementPageData([], []));

        IRenderedComponent<ContractManagement> cut = _ctx.Render<ContractManagement>();

        Assert.Contains("現在、受信したコントラクトの提案はありません。", cut.Markup);
        Assert.DoesNotContain("現在、送信したコントラクトの提案はありません。", cut.Markup);

        // Act: 「送信済み (Outbox)」タブヘッダーをクリックしてタブを切り替え
        var outboxTab = cut.FindAll(".mud-tab").First(t => t.TextContent.Contains("送信済み (Outbox)"));
        outboxTab.Click();

        // Assert: 送信トレイ（Outbox）の空メッセージが表示されていることを検証
        cut.WaitForAssertion(() =>
            Assert.Contains("現在、送信したコントラクトの提案はありません。", cut.Markup));
    }
```

Detailed design and alternate implementations are documented in:
`/Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/explorer_m1_1/report.md`.

---

## 5. Verification Method

To verify after implementation:
1. Run the test target:
   ```bash
   dotnet test SRNSMudApp.Tests/SRNSMudApp.Tests.csproj --filter "FullyQualifiedName~ContractManagementTests"
   ```
   Expected: 4 passed tests (3 existing + 1 new `WhenSwitchedToOutboxTab_RendersEmptyOutboxAlert`), runtime <500ms.
2. Invalidation conditions:
   - If `Assert.DoesNotContain("現在、送信したコントラクトの提案はありません。", cut.Markup)` fails before clicking, `KeepPanelsAlive` was unexpectedly enabled or panel rendered early.
   - If `cut.WaitForAssertion` fails after clicking, the `.mud-tab` click event did not fire the panel activation.
