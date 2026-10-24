# Handoff Report: Exhaustive Survey of Razor Components & Pages

**Agent**: Survey Explorer 2 (`explorer_survey_comp_pages`)  
**Scope**: All Razor Components, Pages, and related code-behinds across `SRNSMudApp/Components/` and `SRNSMudApp.Client/`  
**Date**: 2026-10-14  

---

## 1. Observation

### 1.1 Scope and File Catalog
We performed a systematic filesystem and syntax scan across all active Razor components and pages in both server and client projects.
- **Total component/page files**: 277 files (.razor, .razor.cs)
- **Total related C# files in component directories (including ViewModels & UI Services)**: 396 files
- **Baseline Build Verification**:
  - `dotnet build SRNSMudApp.sln -m:1 /nr:false`
  - Result: `Build succeeded. 0 Warning(s) 0 Error(s). Time Elapsed 00:00:02.47`
- **Baseline Formatting Verification**:
  - `dotnet format --diagnostics IDE0055 --verify-no-changes`
  - Result: `0 violations` (exit code 0)

#### File Inventory by Directory
| Directory Path | File Count | Key Components & Files |
|---|---|---|
| `SRNSMudApp/Components/Pages/` | 16 | `TagDiagramPage.razor.cs` (880 lines), `NotificationsPage.razor.cs` (235 lines), `RightAssetOverview.razor.cs` (67 lines), `Home.razor.cs` (67 lines), `AuthCallback.razor`, `Error.razor`, `NotFound.razor` |
| `SRNSMudApp/Components/UI/` | 60 | `ItemCard.razor.cs` (743 lines), `TagCard.razor.cs` (338 lines), `ResourceList.razor.cs` (164 lines), `ItemCardContent.razor.cs` (157 lines), `ReactionCommentDialog.razor.cs` (113 lines), `NotificationBadge.razor.cs` (66 lines) |
| `SRNSMudApp/Components/Tag/` | 82 | `TagDetail.razor.cs` (407 lines), `TagTree.razor.cs` (399 lines), `ItemTagChip.razor.cs` (311 lines), `TagTable.razor.cs` (193 lines), `TagAddDialog.razor.cs` (120 lines), `ItemTagTable.razor.cs` (118 lines), `ItemTagChipList.razor` (100 lines), `TagTreePopoverContent.razor.cs` (91 lines) |
| `SRNSMudApp/Components/Item/` | 42 | `AddItem.razor.cs` (446 lines), `ItemDetail.razor.cs` (280 lines), `ItemList.razor.cs` (134 lines), `ItemEditDialog.razor.cs` (86 lines), `QuoteItemDialog.razor.cs` (65 lines) |
| `SRNSMudApp/Components/Admin/` | 12 | `TagManagement.razor.cs` (58 lines), `InvitationManager.razor.cs` (76 lines), `ReportManager.razor.cs` (112 lines) |
| `SRNSMudApp/Components/Bounty/` | 9 | `BountyBoard.razor.cs` (74 lines), `BountyCreateDialog.razor.cs` (89 lines), `FulfillBountyDialog.razor.cs` (88 lines) |
| `SRNSMudApp/Components/Contract/` | 7 | `ContractManagement.razor.cs` (84 lines), `ProposeContractDialog.razor.cs` (72 lines) |
| `SRNSMudApp/Components/PublicOffer/` | 9 | `PublicOfferBoard.razor.cs` (92 lines), `TriggerPublicOfferDialog.razor.cs` (101 lines), `CreatePublicOfferDialog.razor.cs` (79 lines) |
| `SRNSMudApp/Components/UserGroup/` | 11 | `UserGroupList.razor.cs` (105 lines), `UserGroupMembers.razor.cs` (85 lines), `UserGroupCreateEditDialog.razor.cs` (69 lines), `UserGroupMembersDialog.razor.cs` (83 lines) |
| `SRNSMudApp/Components/User/` | 13 | `UserDetail.razor.cs` (158 lines), `MakeMeAdmin.razor.cs` (39 lines), `UserManagement.razor.cs` (32 lines), `UserSearch.razor.cs` (24 lines) |
| `SRNSMudApp/Components/Layout/` | 5 | `NavMenu.razor.cs` (73 lines), `MainLayout.razor`, `ReconnectModal.razor` |
| `SRNSMudApp/Components/Diagram/` | 27 | `TagDiagramCanvas.razor` (98 lines), `AttachTagToEdgeDialog.razor.cs` (70 lines), `CreateEdgeDialog.razor.cs` (60 lines) |
| `SRNSMudApp/Components/Account/` | 46 | `Passkeys.razor` (178 lines), `PasskeySubmit.razor` (40 lines), `TwoFactorAuthentication.razor` (85 lines), Account Pages & Shared |
| `SRNSMudApp.Client/Components/` | 12 | `ItemSearchView.razor.cs` (707 lines), `TagTreeView.razor.cs` (340 lines), `UserGroupView.razor.cs` (315 lines), `TagDiagramView.razor.cs` (276 lines), `Tag/TagProposalsTab.razor` |
| `SRNSMudApp.Client/Pages/` | 25 | `TagDetailPage.razor` (905 lines), `ItemDetailPage.razor` (793 lines), `RightAssetOverviewPage.razor` (695 lines), `ContractManagementPage.razor` (497 lines), `PublicOfferBoardPage.razor` (353 lines), `ReportManagementPage.razor` (314 lines), `TagManagementPage.razor` (298 lines), `TagListPage.razor` (280 lines), `BountyBoardPage.razor` (278 lines), `UserGroupMembersPage.razor` (250 lines) |

---

### 1.2 Architectural Compliance Observations
1. **Direct DbContext Access**: **0 occurrences**. In all 277 components and pages, neither `ApplicationDbContext` nor `IDbContextFactory` is injected. All data access flows through ViewModels or Data Providers.
2. **IDialogService Usage**: **0 direct occurrences**. `IDialogService` is completely wrapped by `IDialogLauncher` across the application. Components consistently inject `IDialogLauncher`.
3. **Blocking Synchronous I/O**: **0 occurrences**. No `.GetAwaiter().GetResult()` or `.Wait()` calls exist in component code. All I/O operations are asynchronously awaited.
4. **Event Subscriptions & Resource Disposal**:
   - 13 component and ViewModel event subscriptions were identified:
     - `TagDiagramCanvas.razor:38-39`: `Diagram.SelectionChanged`, `Diagram.PointerClick` -> unsubscribed in `Dispose()` (lines 92-93)
     - `ItemList.razor.cs:38-39`: `ViewModel.StateChanged`, `ViewModel.QueryStateChanged` -> unsubscribed in `Dispose()` (lines 123-124)
     - `ItemListViewModel.cs:26`: `TagSearch.FiltersChanged` -> unsubscribed in `Dispose()` (line 89)
     - `NavMenu.razor.cs:33-34`: `ViewModel.StateChanged`, `NavigationManager.LocationChanged` -> unsubscribed in `Dispose()` (lines 68-70)
     - `NavMenuViewModel.cs:27`: `_notificationService.NotificationsChanged` -> unsubscribed in `Dispose()` (line 120)
     - `NotificationBadge.razor.cs:43-44`: `NavigationManager.LocationChanged`, `ViewModel.StateChanged` -> unsubscribed in `Dispose(bool)` (lines 72-73)
     - `NotificationBadgeViewModel.cs:23`: `_notificationService.NotificationsChanged` -> unsubscribed in `Dispose()` (line 85)
     - `ReactionCommentDialog.razor.cs:36-37`: `ViewModel.StateChanged`, `ViewModel.Completed` -> unsubscribed in `Dispose(bool)` (lines 95-97)
   - Every single event subscription is properly cleaned up in `Dispose()` or `DisposeAsync()`.
5. **Lifecycle & Thread Marshalling**:
   - Background tasks and asynchronous events correctly marshal UI updates via `await InvokeAsync(StateHasChanged)` or `_ = InvokeAsync(StateHasChanged)` (`AddItem.razor.cs`, `NavMenu.razor.cs`, `Home.razor.cs`, `ItemTagTable.razor.cs`).
   - **Lifecycle Anomaly Found**: `ResourceList.razor.cs:130` defines `private async void UpdateFocusUrl()`. It is invoked directly by UI selection methods (`SelectTag`, `SelectItem`) rather than as an `EventHandler` delegate. This is an untracked `async void` fire-and-forget method that bypasses task error propagation.

---

### 1.3 Modern C# & Declarative LINQ Scan Metrics
- **Switch statements identified**: 59 statements across 34 files.
  - ~42 statements represent repetitive handling of discriminated unions (`Result<T>` with `case Success<T>:` / `case Failure failure:` / `default: break;`).
  - ~5 statements represent single-case switch blocks used merely for null checking or single pattern matching.
  - ~4 statements represent 20+ line switch statements handling UI notifications (`TagCardActionResult` or `TagDeleteOperationResult`).
- **Imperative foreach loops identified in active C# logic**: 14 loops across 7 files.
  - 0 loops in `.razor` `@code` blocks (all loops are in `.razor.cs` code-behinds or ViewModels).
  - 3 loops in Razor templates perform `.Take(3)` on URL collections (pure rendering).
- **Null-checking idioms**: 149 occurrences of `!= null` and 77 occurrences of `== null`.
- **Target-typed new() / Collection expressions []**:
  - `[]` has begun being used in newer code (e.g. `IReadOnlyList<Tag> AllTags = []`), but older code still uses `new List<...>()`, `new HashSet<...>()`, and verbose DTO initializations `new CreateItemRequest(...)`.

---

## 2. Logic Chain

From the observations above, we establish the following analytical steps:

1. **Step 1: Declarative LINQ vs Imperative Mutation**
   - *Observation*: In `TagDiagramPage.razor.cs:52-70`, `GetEffectiveItemIds` allocates a list, conditionally adds `QueryItemId`, splits a string with bitwise options, runs an imperative `foreach` loop with `int.TryParse`, checks `!result.Contains(id)`, and returns the accumulated list.
   - *Reasoning*: This imperative sequence can be expressed as a single, readable, immutably chained declarative LINQ pipeline with `Where`, `Select`, `Distinct`, and modern C# 12 collection expressions (`[.. sequence]`). This eliminates mutable accumulator state and reduces line count from 19 lines to 10 lines with identical semantic behavior and no performance penalty for small UI queries.
   - *Observation*: In `ItemTagChipList.razor:12-32`, deleted tags from `HighlightEvents` are filtered and added to `allTags` using nested `switch` statements (`switch (HighlightEvents) { case not null: foreach (...) { switch (ev.FollowedTag) { case not null when ...: allTags.Add(...); } } }`).
   - *Reasoning*: Using nested switch statements with `case not null` inside a Blazor Razor view block is an awkward anti-pattern. Replacing it with `if (HighlightEvents is not null) allTags.AddRange(HighlightEvents.Where(...).Select(...));` replaces 20 lines of nested branching with a clean 7-line declarative pipeline.
   - *Observation*: In `AddItem.razor.cs:225-232` and `TagTreePopoverContent.razor.cs:41-48`, items are added to a `HashSet<int>` via `foreach (var id in ids) set.Add(id);`.
   - *Reasoning*: `HashSet<T>.UnionWith(ids)` is specifically optimized for bulk addition, avoids manual iteration, and expresses the mathematical intent directly.

2. **Step 2: Discriminated Union / Result Pattern Matching & Switch Expressions**
   - *Observation*: In `Admin/TagManagement.razor.cs`, `Admin/InvitationManager.razor.cs`, `Contract/ContractManagement.razor.cs`, `Tag/TagDetail.razor.cs`, `PublicOffer/PublicOfferBoard.razor.cs`, and `UserGroup/UserGroupMembers.razor.cs`, 40+ methods follow this exact pattern:
     ```csharp
     var result = await ViewModel.SomeActionAsync();
     switch (result)
     {
         case Success<T> [success]:
             Snackbar.Add("Success message", Severity.Success);
             break;
         case Failure failure:
             Snackbar.Add(failure.ErrorMessage, Severity.Error);
             break;
         default:
             break;
     }
     ```
   - *Reasoning*: Because `Result<T>` is a C# union type (`public readonly union Result<T>(Success<T>, Failure);`), the only two possible states are `Success<T>` and `Failure`. The `default: break;` is dead code. Furthermore, in cases where the switch statement merely computes a message and severity for `Snackbar.Add`, converting this to a switch expression deconstructing into a tuple `var (msg, sev) = result switch { Success<T> => (...), Failure f => (...) }; Snackbar.Add(msg, sev);` reduces boilerplate by >50%, improves readability, and makes the UI notification mapping declarative.

3. **Step 3: Elimination of Single-Case Switch Statements**
   - *Observation*: In `PasskeySubmit.razor:32-37`, `ItemCard.razor.cs:533-539`, `ItemTagTable.razor.cs:61-68`, `ItemTagTable.razor.cs:76-85`, and `TagTree.razor.cs:297-309`, switch statements with exactly one `case` and a `default: break;` are used for simple null checks, pattern checks, or dialog cancellation checks.
   - *Reasoning*: A switch statement with a single case is an anti-idiom in C#. Replacing:
     - `switch (currentToken) { case not null: requestTokenValue = currentToken.Value; break; }` with `requestTokenValue = AntiforgeryStateProvider.GetAntiforgeryToken()?.Value;`
     - `switch (result) { case { Canceled: false }: ... break; }` with `if (result is { Canceled: false }) { ... }`
     - `switch (TagSearchQuery.Parse(s)) { case IncompleteSearch: ... break; }` with `if (TagSearchQuery.Parse(s) is IncompleteSearch) { ... }`
     dramatically improves readability and aligns with Roslyn style guidelines.

4. **Step 4: Modern Pattern Matching (`is null or []`, tuple deconstruction, property patterns)**
   - *Observation*: In `TagTree.razor.cs:213-221`, checking for null or empty `selectedIds` is written as:
     ```csharp
     switch (selectedIds)
     {
         case null:
         case { Count: 0 }:
             _ = Snackbar.Add("削除するタグが選択されていません。", Severity.Info);
             return;
         default:
             break;
     }
     ```
   - *Reasoning*: C# 12 list patterns combined with the `or` pattern allow this to be expressed cleanly as `if (selectedIds is null or [])`.
   - *Observation*: In `TagDiagramPage.razor.cs:667`, unpacking dialog result is written as `if (result is not null && !result.Canceled && result.Data is ValueTuple<int, int> edgeData)` followed by `edgeData.Item1` and `edgeData.Item2`.
   - *Reasoning*: Using property and positional patterns together: `if (result is { Canceled: false, Data: (int sourceId, int targetId) })` unpacks the tuple into named variables directly at the condition site, eliminating null checks, type casts, and positional indexing (`Item1`, `Item2`).

---

## 3. Specific Candidate Blocks: Before & After Proposals

### Candidate 1: `SRNSMudApp/Components/Pages/TagDiagramPage.razor.cs` (Lines 52-70)
**Opportunity**: Declarative LINQ & Collection Expressions (`GetEffectiveItemIds`)

*Before*:
```csharp
    private List<int> GetEffectiveItemIds()
    {
        var result = new List<int>();
        if (QueryItemId.HasValue)
        {
            result.Add(QueryItemId.Value);
        }
        if (!string.IsNullOrWhiteSpace(QueryItemIds))
        {
            foreach (var part in QueryItemIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out int id) && !result.Contains(id))
                {
                    result.Add(id);
                }
            }
        }
        return result;
    }
```

*After*:
```csharp
    private List<int> GetEffectiveItemIds()
    {
        IEnumerable<int> initial = QueryItemId is { } singleId ? [singleId] : [];
        if (string.IsNullOrWhiteSpace(QueryItemIds))
        {
            return [.. initial];
        }

        return [.. initial
            .Concat(QueryItemIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(part => int.TryParse(part, out _))
                .Select(int.Parse))
            .Distinct()];
    }
```
*Rationale*: Replaces imperative list mutation and `Contains` lookups with an immutable declarative LINQ query, target-typed collection expressions `[]`, and pattern matching `is { }`.

---

### Candidate 2: `SRNSMudApp/Components/Pages/TagDiagramPage.razor.cs` (Lines 667-683)
**Opportunity**: Property Pattern Matching with Tuple Deconstruction

*Before*:
```csharp
        if (result is not null && !result.Canceled && result.Data is ValueTuple<int, int> edgeData)
        {
            Result<TagEdge> createResult = await ViewModel.CreateEdgeAsync(edgeData.Item1, edgeData.Item2);
            switch (createResult)
            {
                case Success<TagEdge> s:
                    Snackbar.Add("Edge を作成しました。", Severity.Success);
                    await ReloadDiagramAsync();
                    ViewModel.SelectedEdge = ViewModel.Edges.FirstOrDefault(e => e.Id == s.Value.Id);
                    break;
                case Failure f:
                    Snackbar.Add($"Edge 作成に失敗しました: {f.ErrorMessage}", Severity.Error);
                    break;
                default:
                    break;
            }
        }
```

*After*:
```csharp
        if (result is { Canceled: false, Data: (int sourceId, int targetId) })
        {
            Result<TagEdge> createResult = await ViewModel.CreateEdgeAsync(sourceId, targetId);
            if (createResult is Success<TagEdge> s)
            {
                Snackbar.Add("Edge を作成しました。", Severity.Success);
                await ReloadDiagramAsync();
                ViewModel.SelectedEdge = ViewModel.Edges.FirstOrDefault(e => e.Id == s.Value.Id);
            }
            else if (createResult is Failure f)
            {
                Snackbar.Add($"Edge 作成に失敗しました: {f.ErrorMessage}", Severity.Error);
            }
        }
```
*Rationale*: Eliminates verbose null/canceled checks and `ValueTuple<int, int>` type casting; variables `sourceId` and `targetId` are deconstructed directly in the pattern guard. Eliminates dead `default: break;`.

---

### Candidate 3: `SRNSMudApp/Components/Tag/ItemTagChipList.razor` (Lines 12-32)
**Opportunity**: Replace Nested Switch & Foreach with Declarative LINQ & AddRange

*Before*:
```csharp
    // 削除されたタグが TimelineEvent に含まれている場合は、仮想的な TagRelation としてリストに追加して表示する
    switch (HighlightEvents)
    {
        case not null:
            foreach (var ev in HighlightEvents.Where(e => e.EventType == "Delete"))
            {
                switch (ev.FollowedTag)
                {
                    case not null when allTags.All(t => t.TagId != ev.FollowedTagId):
                        allTags.Add(new TagRelation
                        {
                            TagId = ev.FollowedTagId,
                            Tag = ev.FollowedTag,
                            Weight = ev.PreviousWeight,
                            OwnerId = ev.OwnerId // fake owner
                        });
                        break;
                }
            }
            break;
    }
```

*After*:
```csharp
    // 削除されたタグが TimelineEvent に含まれている場合は、仮想的な TagRelation としてリストに追加して表示する
    if (HighlightEvents is not null)
    {
        var deletedTags = HighlightEvents
            .Where(e => e.EventType == "Delete" && e.FollowedTag is not null && allTags.All(t => t.TagId != e.FollowedTagId))
            .Select(ev => new TagRelation
            {
                TagId = ev.FollowedTagId,
                Tag = ev.FollowedTag!,
                Weight = ev.PreviousWeight,
                OwnerId = ev.OwnerId // fake owner
            });

        allTags.AddRange(deletedTags);
    }
```
*Rationale*: Eliminates 20 lines of nested switch statements in Razor template code, replacing it with a clean declarative query pipeline and bulk `AddRange`.

---

### Candidate 4: `SRNSMudApp/Components/Pages/NotificationsPage.razor.cs` (Lines 70-101)
*(Also applicable to `TagTable.razor.cs:101-130` and `TagTree.razor.cs:250-280`)*
**Opportunity**: Switch Expression & Property Pattern for UI Action Results

*Before*:
```csharp
    private void ApplyResult(TagCardActionResult result)
    {
        switch (result.Type)
        {
            case TagCardActionResultType.Warning:
                if (result.Message != null)
                {
                    _ = Snackbar.Add(result.Message, Severity.Warning);
                }
                break;
            case TagCardActionResultType.Error:
                if (result.Message != null)
                {
                    _ = Snackbar.Add(result.Message, Severity.Error);
                }
                break;
            case TagCardActionResultType.Success:
                if (result.Message != null)
                {
                    _ = Snackbar.Add(result.Message, Severity.Success);
                }
                break;
            case TagCardActionResultType.NoOp:
            default:
                break;
        }

        if (result.ShouldNotifyChanged)
        {
            StateHasChanged();
        }
    }
```

*After*:
```csharp
    private void ApplyResult(TagCardActionResult result)
    {
        if (result.Message is { Length: > 0 } message)
        {
            Severity? severity = result.Type switch
            {
                TagCardActionResultType.Warning => Severity.Warning,
                TagCardActionResultType.Error => Severity.Error,
                TagCardActionResultType.Success => Severity.Success,
                _ => null
            };

            if (severity.HasValue)
            {
                _ = Snackbar.Add(message, severity.Value);
            }
        }

        if (result.ShouldNotifyChanged)
        {
            StateHasChanged();
        }
    }
```
*Rationale*: Collapses three repetitive `if (result.Message != null) _ = Snackbar.Add(...)` blocks into a single declarative severity mapping with property pattern `result.Message is { Length: > 0 }`.

---

### Candidate 5: `SRNSMudApp/Components/Tag/TagTree.razor.cs` (Lines 213-221)
**Opportunity**: C# 12 List Pattern `is null or []`

*Before*:
```csharp
        switch (selectedIds)
        {
            case null:
            case { Count: 0 }:
                _ = Snackbar.Add("削除するタグが選択されていません。", Severity.Info);
                return;
            default:
                break;
        }
```

*After*:
```csharp
        if (selectedIds is null or [])
        {
            _ = Snackbar.Add("削除するタグが選択されていません。", Severity.Info);
            return;
        }
```
*Rationale*: Replaces a 9-line multi-case switch with a single-line C# 12 list pattern condition (`selectedIds is null or []`).

---

### Candidate 6: `SRNSMudApp/Components/Admin/TagManagement.razor.cs` (Lines 27-57)
*(Also applicable to `InvitationManager.razor.cs`, `ContractManagement.razor.cs`, `UserGroupMembers.razor.cs`)*
**Opportunity**: Discriminated Union Switch Expression to Message/Severity Tuple

*Before*:
```csharp
    private async Task SaveHierarchyLockSettingAsync()
    {
        var result = await ViewModel.SaveHierarchyLockSettingAsync();
        switch (result)
        {
            case Success<int> success:
                Snackbar.Add($"階層ロック設定を {success.Value} 階層目までに更新しました。", Severity.Success);
                break;
            case Failure failure:
                Snackbar.Add(failure.ErrorMessage, Severity.Error);
                break;
            default:
                break;
        }
    }

    private async Task ToggleTagLockAsync(int tagId)
    {
        var result = await ViewModel.ToggleTagLockAsync(tagId);
        switch (result)
        {
            case Success<bool> success:
                Snackbar.Add(success.Value ? "タグを個別ロックしました。" : "タグの個別ロックを解除しました。", Severity.Success);
                break;
            case Failure failure:
                Snackbar.Add(failure.ErrorMessage, Severity.Error);
                break;
            default:
                break;
        }
    }
```

*After*:
```csharp
    private async Task SaveHierarchyLockSettingAsync()
    {
        var result = await ViewModel.SaveHierarchyLockSettingAsync();
        var (message, severity) = result switch
        {
            Success<int> success => ($"階層ロック設定を {success.Value} 階層目までに更新しました。", Severity.Success),
            Failure failure => (failure.ErrorMessage, Severity.Error)
        };
        Snackbar.Add(message, severity);
    }

    private async Task ToggleTagLockAsync(int tagId)
    {
        var result = await ViewModel.ToggleTagLockAsync(tagId);
        var (message, severity) = result switch
        {
            Success<bool> success => (success.Value ? "タグを個別ロックしました。" : "タグの個別ロックを解除しました。", Severity.Success),
            Failure failure => (failure.ErrorMessage, Severity.Error)
        };
        Snackbar.Add(message, severity);
    }
```
*Rationale*: Removes dead `default: break;`, eliminates repetitive `Snackbar.Add` invocations, and maps union outcomes directly to UI presentation parameters.

---

### Candidate 7: `SRNSMudApp/Components/Account/Shared/PasskeySubmit.razor` (Lines 31-37)
**Opportunity**: Eliminate Awkward Single-Case Switch for Null-Check

*Before*:
```csharp
    protected override void OnInitialized()
    {
        var currentToken = AntiforgeryStateProvider.GetAntiforgeryToken();
        switch (currentToken)
        {
            case not null:
                requestTokenValue = currentToken.Value;
                break;
        }
    }
```

*After*:
```csharp
    protected override void OnInitialized()
    {
        requestTokenValue = AntiforgeryStateProvider.GetAntiforgeryToken()?.Value;
    }
```
*Rationale*: Replaces a 7-line single-case switch statement with standard C# null-conditional member access (`?.Value`).

---

### Candidate 8: `SRNSMudApp/Components/Item/AddItem.razor.cs` (Lines 225-232)
**Opportunity**: HashSet `UnionWith` Bulk Operation

*Before*:
```csharp
    private void HandleConfirmedSuggestedTagIdsChanged(IReadOnlyCollection<int> tagIds)
    {
        ViewModel.ConfirmedSuggestedTagIds.Clear();
        foreach (var id in tagIds)
        {
            ViewModel.ConfirmedSuggestedTagIds.Add(id);
        }
    }
```

*After*:
```csharp
    private void HandleConfirmedSuggestedTagIdsChanged(IReadOnlyCollection<int> tagIds)
    {
        ViewModel.ConfirmedSuggestedTagIds.Clear();
        ViewModel.ConfirmedSuggestedTagIds.UnionWith(tagIds);
    }
```
*Rationale*: `ConfirmedSuggestedTagIds` is a `HashSet<int>`. Calling `UnionWith(tagIds)` is direct, idiomatic, and eliminates imperative iteration.

---

### Candidate 9: `SRNSMudApp.Client/Components/TagTreeView.razor.cs` (Lines 288-305)
**Opportunity**: Declarative LINQ Node Filtering & Collection Expression

*Before*:
```csharp
    private static List<TagTreeNodeDto> FilterNodes(IReadOnlyList<TagTreeNodeDto> nodes, string term)
    {
        var result = new List<TagTreeNodeDto>();

        foreach (var node in nodes)
        {
            var matchingChildren = FilterNodes(node.Children, term);
            var isSelfMatch = node.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                              (node.Content != null && node.Content.Contains(term, StringComparison.OrdinalIgnoreCase));

            if (isSelfMatch || matchingChildren.Count > 0)
            {
                result.Add(node with { Children = matchingChildren.Count > 0 ? matchingChildren : node.Children });
            }
        }

        return result;
    }
```

*After*:
```csharp
    private static List<TagTreeNodeDto> FilterNodes(IReadOnlyList<TagTreeNodeDto> nodes, string term)
    {
        return [.. nodes
            .Select(node =>
            {
                var matchingChildren = FilterNodes(node.Children, term);
                var isSelfMatch = node.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                  node.Content?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
                return (node, matchingChildren, isSelfMatch);
            })
            .Where(x => x.isSelfMatch || x.matchingChildren.Count > 0)
            .Select(x => x.node with { Children = x.matchingChildren.Count > 0 ? x.matchingChildren : x.node.Children })];
    }
```
*Rationale*: Eliminates mutable list accumulation, uses null-propagation `node.Content?.Contains(...) == true`, and returns via collection expression `[.. ...]`.

---

### Candidate 10: `SRNSMudApp/Components/UI/ResourceList.razor.cs` (Lines 130-152)
**Opportunity**: Blazor Lifecycle & Async Best Practice (`async void` -> `async Task`)

*Before*:
```csharp
    private async void UpdateFocusUrl()
    {
        if (!EnableUrlUpdate)
        {
            return;
        }

        try
        {
            ItemListQueryState updated = ItemListQueryState.ParseFromUri(new Uri(NavigationManager.Uri)) with
            {
                FocusTagId = _focusTagId,
                FocusItemId = _focusItemId
            };
            var newUri = NavigationManager.GetUriWithQueryParameters(updated.BuildParameters());
            await JS.InvokeVoidAsync("contentOverflowHelper.updateUrl", newUri);
        }
        catch (Exception)
        {
            // ignored
        }
    }
```

*After*:
```csharp
    private async Task UpdateFocusUrlAsync()
    {
        if (!EnableUrlUpdate)
        {
            return;
        }

        try
        {
            ItemListQueryState updated = ItemListQueryState.ParseFromUri(new Uri(NavigationManager.Uri)) with
            {
                FocusTagId = _focusTagId,
                FocusItemId = _focusItemId
            };
            var newUri = NavigationManager.GetUriWithQueryParameters(updated.BuildParameters());
            await JS.InvokeVoidAsync("contentOverflowHelper.updateUrl", newUri);
        }
        catch (Exception)
        {
            // ignored
        }
    }
```
*(Caller sites updated from `UpdateFocusUrl();` to `_ = UpdateFocusUrlAsync();` or `await UpdateFocusUrlAsync();`)*  
*Rationale*: Conforms to .NET and Blazor guidelines that avoid `async void` outside of event handlers to ensure exception safety and circuit stability.

---

## 4. Caveats
- No code was modified during this survey. All observations and proposals are strictly read-only.
- In Blazor WebAssembly (`SRNSMudApp.Client/Pages/`), multiple pages make direct calls to `ApiClient` (e.g. `ApiClient.CreateTagAsync(request)`). This is normal and expected for Blazor WebAssembly client pages that communicate with the server-side API over HTTP, distinct from Blazor Server components which must delegate to ViewModels/Providers.
- In Razor templates (`.razor`), `@foreach` loops for rendering HTML markup should NOT be converted into LINQ `.Select(x => ...)` expressions, as Blazor's virtual DOM diffing engine relies on structural template loops. Only logic blocks and LINQ transformations within code-behind `.razor.cs` and `@code` expressions are targeted.

---

## 5. Conclusion & Recommendations for Milestone Decomposition

### Conclusion
The Razor Components and Pages layer in `SRNSMudApp` and `SRNSMudApp.Client` demonstrates strong foundational architecture:
- 100% adherence to no-direct-DbContext injection rules.
- 100% adherence to `IDialogLauncher` abstraction.
- Clean asynchronous patterns across all I/O.
- Proper event unsubscription implemented in all 13 event-subscribing components.

However, significant modernization opportunities exist in four primary areas:
1. **42+ Discriminated Union Switch Blocks**: Can be converted to clean C# switch expressions with tuple deconstruction, eliminating dead `default: break;` code and repetitive `Snackbar.Add` calls.
2. **5+ Single-Case Switch Statements**: Can be replaced with direct pattern matching (`is { Canceled: false }`, `is IncompleteSearch`, `is not null`, `?.Value`).
3. **14 Imperative Foreach Loops**: Can be converted to declarative LINQ pipelines (`GetEffectiveItemIds`, `ItemTagChipList`, `FilterNodes`) and `HashSet.UnionWith` operations.
4. **149 `!= null` & 77 `== null` Idioms**: Can be standardized to modern pattern matching (`is not null` / `is null` / `is null or []`).

### Recommended Milestone 2 Decomposition (Razor Components & Pages)
To ensure controlled execution, isolated testing, and zero regressions, we recommend decomposing Milestone 2 into four manageable sub-milestones:

```
Milestone 2: Razor Components & Pages Modernization
├── Sub-M2.1: Server Pages & Layout Modernization
│   ├── Target: SRNSMudApp/Components/Pages/ & Layout/
│   ├── Scope: TagDiagramPage, NotificationsPage, Home, RightAssetOverview, NavMenu
│   └── Focus: GetEffectiveItemIds LINQ, tuple deconstruction, ApplyResult switch expressions
│
├── Sub-M2.2: Shared UI & Item Components Modernization
│   ├── Target: SRNSMudApp/Components/UI/ & Item/
│   ├── Scope: ItemCard, ResourceList, NotificationBadge, TagCard, AddItem, ItemDetail, ItemList
│   └── Focus: ResourceList async void fix, ItemCard single-case switch removal, AddItem UnionWith
│
├── Sub-M2.3: Tag System Components & Popovers Modernization
│   ├── Target: SRNSMudApp/Components/Tag/
│   ├── Scope: TagTree, TagDetail, TagTable, ItemTagChip, ItemTagChipList, TagTreePopoverContent
│   └── Focus: ItemTagChipList nested switch to LINQ, TagTree is null or [], TagDetail Result switch expressions
│
└── Sub-M2.4: Admin, UserGroup, Bounty, Contract & Client WASM Modernization
    ├── Target: Admin/, UserGroup/, Bounty/, Contract/, PublicOffer/, Account/ & SRNSMudApp.Client/
    ├── Scope: TagManagement, InvitationManager, UserGroupMembers, BountyBoard, ContractManagement, PasskeySubmit, ItemSearchView, TagDiagramView, TagTreeView
    └── Focus: Standardize remaining 30+ Result switch expressions, PasskeySubmit null-coalescing, Client WASM null idioms
```

---

## 6. Verification Method

To independently verify the observations and validate proposed candidate blocks:
1. **Compilation Check**:
   ```bash
   dotnet build SRNSMudApp.sln -m:1 /nr:false
   ```
   *Expected*: 0 Errors, 0 Warnings.
2. **Formatting Rule Compliance**:
   ```bash
   dotnet format --diagnostics IDE0055 --verify-no-changes
   ```
   *Expected*: Exit code 0, 0 violations.
3. **Unit Tests Execution**:
   ```bash
   dotnet test SRNSMudApp.Tests/SRNSMudApp.Tests.csproj -m:1 /nr:false
   ```
   *Expected*: All unit tests pass cleanly.
4. **E2E Tests Execution**:
   ```bash
   dotnet test SRNSMudApp.E2ETests/SRNSMudApp.E2ETests.csproj -m:1 /nr:false
   ```
   *Expected*: All E2E tests pass cleanly.
5. **Code Inspection**:
   Inspect candidate files listed in Section 3 to confirm line numbers and exact verbatim patterns.
