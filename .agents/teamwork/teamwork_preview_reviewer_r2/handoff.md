# Reviewer R2 Handoff Report: Adversarial Review & QA Remediation

## Executive Summary
Round 2 adversarial review evaluated the double submission prevention implementation across client-side (Blazor WASM & Server UI components/ViewModels) and server-side (`RequestDeduplicationService` and controllers).
While Round 1 established the baseline infrastructure (`DoubleSubmissionGuard`, `IRequestDeduplicationService`, and primary item create flows), probing uncovered 11 edge cases and unhandled submission paths in secondary dialogues, WASM components, parent-child dialog bindings, and concurrency cleanup. All 11 issues were remediated, verified with new unit tests, and the full test suite (3,339 tests) and code formatting passed with 0 errors and 0 warnings.

---

## 1. Defects Identified & Remediated

### Defect 1: WASM `ItemSearchView.razor` Reply Button Unprotected
- **Input**: User clicks "返信" submit button rapidly in WASM mode.
- **Expected**: Reply submit button is disabled (`Disabled="@_isSubmittingReplyMap.GetValueOrDefault(item.Id)"`), and duplicate attempts show toast `Snackbar.Add("既にその投稿は送信されました", Severity.Info)`.
- **Actual**: No `Disabled` binding existed on the MudButton in `ItemSearchView.razor`.
- **Root Cause**: Omission of disabled state binding in the razor markup.
- **Fix**: Bound `Disabled` and added in-flight check with snackbar feedback.

### Defect 2: WASM `ItemSearchView.razor` & `.cs` Edit Save Button In-flight State Missing
- **Input**: User clicks "保存" in inline/dialog edit in WASM view.
- **Expected**: Edit save button is disabled while saving and concurrent submissions are rejected with duplicate snackbar.
- **Actual**: `SaveEditAsync` had no in-flight item tracking; button remained active and allowed concurrent API requests.
- **Root Cause**: `_savingEditItemId` field and guards were missing.
- **Fix**: Added `_savingEditItemId` tracking, `Disabled` binding, and duplicate toast guard.

### Defect 3: `SplitRequestWithQuoteDialog.razor.cs` Unchecked Submission & Ignored Duplicate Quote Response
- **Input**: User clicks Submit repeatedly or backend returns `IsDuplicate = true` on `ItemQuoteService.CreateQuoteItemAsync`.
- **Expected**: `_isSubmitting` blocks second click; if `createdQuote.IsDuplicate` is true, displays duplicate toast and aborts subsequent reply comment creation.
- **Actual**: `SubmitAsync` lacked an initial `_isSubmitting` check, and `createdQuote.IsDuplicate` was ignored, creating duplicate reply comments regardless.
- **Root Cause**: Incomplete guard in dialog code-behind.
- **Fix**: Added `_isSubmitting` guard at entry and handled `createdQuote.IsDuplicate` with duplicate toast notification and graceful exit.

### Defect 4: `TagEditViewModel.cs` & `TagEditDialog` Lacked Double Submission Guard
- **Input**: User clicks Save button in `TagEditDialog` repeatedly.
- **Expected**: `TagEditViewModel` uses `IDoubleSubmissionGuard` to block duplicate saves, disables the Save button, and displays duplicate toast on duplicate attempts.
- **Actual**: `TagEditViewModel` had no guard; `TagEditDialog` Save button was never disabled.
- **Root Cause**: Double-submission guard was omitted during initial tag management refactoring.
- **Fix**: Injected `IDoubleSubmissionGuard` into `TagEditViewModel`, bound dialog button `Disabled="@ViewModel.IsSubmitting"`, and added unit tests.

### Defect 5: `AddTagPageViewModel.cs` Disconnected `IsSubmitting` Property
- **Input**: Rapid submission on `AddTagPageViewModel.AddTagAsync()`.
- **Expected**: `IsSubmitting` reflects `_submissionGuard.IsSubmitting` immediately.
- **Actual**: `IsSubmitting` was an independent boolean field set only after `FindUserByIdAsync` completed, opening a race window.
- **Root Cause**: Property was not delegated directly to `_submissionGuard.IsSubmitting`.
- **Fix**: Redirected `IsSubmitting` property getter directly to `_submissionGuard.IsSubmitting`.

### Defect 6: `ItemEditViewModel.cs` Constructor DI Seam & Double Submission Coverage
- **Input**: Multi-click on edit item form submit button.
- **Expected**: `ItemEditViewModel` supports DI constructor injection of `IDoubleSubmissionGuard` and rejects duplicate saves with toast feedback.
- **Actual**: Lacked constructor injection seam and lacked double submission unit tests.
- **Root Cause**: Constructor only instantiated a default guard internally without test seam.
- **Fix**: Added constructor accepting `IDoubleSubmissionGuard`, integrated duplicate feedback, and added test coverage.

### Defect 7: `AddItemDialog.razor` External Footer Submit Button Disabled State
- **Input**: User clicks external "投稿する" button on `AddItemDialog`.
- **Expected**: External submit button is disabled while `<AddItem>` child component is submitting.
- **Actual**: External button in `AddItemDialog` remained enabled throughout submission because it was disconnected from child state.
- **Root Cause**: Missing `EventCallback<bool> IsSubmittingChanged` on `AddItem.razor`.
- **Fix**: Added `IsSubmittingChanged` EventCallback in `AddItem.razor.cs`, hooked into `AddItemDialog`, and bound `Disabled="@_isSubmitting"`.

### Defect 8: `UserGroupCreateEditViewModel.cs` & Dialog Unchecked Double Clicks
- **Input**: Rapid clicking on Save in `UserGroupCreateEditDialog`.
- **Expected**: Second click blocked with duplicate snackbar message; button disabled during save.
- **Actual**: `SaveAsync()` allowed multiple concurrent DB writes.
- **Root Cause**: Missing submission guard in ViewModel and Dialog.
- **Fix**: Added `IsSaving` guard in `UserGroupCreateEditViewModel`, duplicate snackbar feedback, and unit tests.

### Defect 9: `ReportContentDialog.razor.cs` Missing Pre-submission Guard Check
- **Input**: Rapid keyboard submit (Enter key) on report content dialog.
- **Expected**: `SubmitAsync` aborts if `ViewModel.IsSubmitting` is already true.
- **Actual**: `SubmitAsync` lacked an entry guard check.
- **Root Cause**: Relied only on button disable state without code-behind guard.
- **Fix**: Added `if (ViewModel.IsSubmitting) return;` check in `SubmitAsync`.

### Defect 10: `DoubleSubmissionGuard.cs` Key Normalization and State Retention
- **Input**: Operation key with whitespace or repeated checks after completion.
- **Expected**: Keys trimmed and normalized; completed operation records the last key.
- **Actual**: Raw keys used; key cleared during completion without retention.
- **Root Cause**: Missing trim normalization and missing state capture in `Complete()`.
- **Fix**: Added trimming and retention in `DoubleSubmissionGuard.cs`.

### Defect 11: `RequestDeduplicationService.cs` Race Condition in Cleanup
- **Input**: Concurrent request completions and cache expiration in backend deduplication service.
- **Expected**: Only the exact matching `KeyValuePair<string, LockEntry>` is removed from `_activeRequests`.
- **Actual**: `_activeRequests.TryRemove(key, out _)` was called unconditionally, which could delete a newer lock entry for the same key.
- **Root Cause**: Non-atomic entry removal.
- **Fix**: Replaced with `((ICollection<KeyValuePair<string, LockEntry>>)_activeRequests).Remove(new KeyValuePair<string, LockEntry>(key, entry))` for atomic instance removal.

---

## 2. Modified & Added Files
- `SRNSMudApp.Client/Services/DoubleSubmissionGuard.cs`
- `SRNSMudApp/Services/RequestDeduplicationService.cs`
- `SRNSMudApp.Client/Components/ItemSearchView.razor`
- `SRNSMudApp.Client/Components/ItemSearchView.razor.cs`
- `SRNSMudApp/Components/UI/SplitRequestWithQuoteDialog.razor.cs`
- `SRNSMudApp/Components/Tag/TagEditViewModel.cs`
- `SRNSMudApp/Components/Tag/TagEditDialog.razor`
- `SRNSMudApp/Components/Tag/TagEditDialog.razor.cs`
- `SRNSMudApp/Components/Tag/AddTagPageViewModel.cs`
- `SRNSMudApp/Components/Item/ItemEditViewModel.cs`
- `SRNSMudApp/Components/Item/AddItem.razor.cs`
- `SRNSMudApp/Components/Item/AddItemDialog.razor`
- `SRNSMudApp/Components/Item/AddItemDialog.razor.cs`
- `SRNSMudApp/Components/UserGroup/UserGroupCreateEditViewModel.cs`
- `SRNSMudApp/Components/UserGroup/UserGroupCreateEditDialog.razor.cs`
- `SRNSMudApp/Components/UI/ReportContentDialog.razor.cs`
- `SRNSMudApp.Tests/Components/Item/ItemEditViewModelTests.cs`
- `SRNSMudApp.Tests/Components/Tag/TagEditViewModelTests.cs`
- `SRNSMudApp.Tests/Components/UserGroup/UserGroupCreateEditViewModelTests.cs`
- `SRNSMudApp.Tests/Services/DoubleSubmissionGuardTests.cs`

---

## 3. Verification Record
- **Deep Verification (Ran Actual Tests)**:
  - `dotnet test SRNSMudApp.Tests/SRNSMudApp.Tests.csproj`
    - Result: `Passed! - Failed: 0, Passed: 3339, Skipped: 0, Total: 3339, Duration: 25 s`
  - `dotnet format --diagnostics IDE0055 --verify-no-changes`
    - Result: Clean exit code 0, 0 formatting errors or warnings.
  - `dotnet build`
    - Result: 0 compilation errors, 0 compiler warnings.
- **Shallow Verification**:
  - Code review across all modified Blazor components for lifecycle issues, event callbacks, and unneeded `StateHasChanged()` calls.
- **Unverified Aspects**:
  - Live browser end-to-end user clicking with Playwright/Selenium across physical devices (unit and bUnit tests verified component logic and bindings).

---

## 4. Known Issues & Remaining Risks
- **Minor Robustness Risk**: While `DoubleSubmissionGuard` and `RequestDeduplicationService` protect in-memory concurrency, multi-node horizontal scaling would require a distributed lock (e.g., Redis distributed lock) for cross-server API deduplication. For single-server Blazor Server / WebApp architecture, in-memory concurrency primitives are robust.
