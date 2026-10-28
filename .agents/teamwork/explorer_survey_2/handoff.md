# Handoff Report: Blazor UI & Client Modernization Survey

- **Agent**: explorer_survey_2 (teamwork_explorer)
- **Scope**: `SRNSMudApp/Components/` and `SRNSMudApp.Client/`
- **Date**: 2026-10-28

---

## 1. Observation

### 1.1 Total Files and Loop Distribution in Scope
A complete scan of `SRNSMudApp/Components/` and `SRNSMudApp.Client/` identified **172 total loops**:
- **119 loops in Razor markup templates** (`@foreach`, `@for` inside `.razor` rendering blocks).
- **53 loops in C# code** (ViewModels, code-behind `.razor.cs`, service classes, query codecs, and `@code` blocks).

All C# files in scope already use **file-scoped namespaces** (`namespace ...;`), but numerous files still use verbose constructor boilerplate, imperative list manipulation, nested conditionals, and obsolete switch patterns.

### 1.2 Concrete Loop Candidates & Current Implementations

#### Candidate 1: `SRNSMudApp/Components/Item/ItemEditViewModel.cs:139-167`
- **Location**: `ParsePillsToHtml(string? text)` lines 138-167
- **Current Code**:
```csharp
var lines = text.Split('\n');
for (int i = 0; i < lines.Length; i++)
{
    var line = lines[i];
    var segments = ItemCardViewModel.GetContentSegments(line);
    var sb = new StringBuilder();
    foreach (var segment in segments)
    {
        if (segment.IsUrl && segment.Text.StartsWith('/', StringComparison.Ordinal))
        {
            var displayText = segment.Text;
            if (segment.Text.StartsWith("/TagDetail/", StringComparison.Ordinal))
            {
                displayText = "#タグ";
            }
            else if (segment.Text.StartsWith("/User/UserDetail/", StringComparison.Ordinal))
            {
                displayText = "@ユーザー";
            }

            sb.Append(CultureInfo.InvariantCulture, $"<span class=\"internal-link-preview-pill\" data-testid=\"internal-link-preview-pill\" data-url=\"{segment.Text}\" contenteditable=\"false\">{displayText}</span>&#8203;&nbsp;");
        }
        else
        {
            sb.Append(WebUtility.HtmlEncode(segment.Text).Replace(" ", "&nbsp;", StringComparison.Ordinal));
        }
    }

    lines[i] = sb.ToString();
}

return string.Join("<br>", lines);
```
- **Analysis**: Uses indexed array modification (`lines[i]`), per-line `StringBuilder` instantiation, and imperative nested loops for pure string projection.

#### Candidate 2: `SRNSMudApp/Components/UI/TagCardViewModel.cs:284-297`
- **Location**: `BuildDisplayList` lines 283-297
- **Current Code**:
```csharp
if (highlightEvents is not null)
{
    foreach (TimelineEvent ev in highlightEvents.Where(e => e.ParsedEventType == Models.TimelineEventType.Delete))
    {
        if (ev.FollowedTag is not null && allTags.All(t => t.TagId != ev.FollowedTagId))
        {
            allTags.Add(new TagRelationToTag
            {
                TagId = ev.FollowedTagId,
                Tag = ev.FollowedTag,
                Weight = ev.PreviousWeight,
                OwnerId = ev.OwnerId // fake owner
            });
        }
    }
}
```
- **Analysis**: Imperative loop filtering and appending virtual tag relations into `allTags`.

#### Candidate 3: `SRNSMudApp/Components/Tag/ItemTagChipList.razor:16-30`
- **Location**: Razor `@code` / code section lines 13-32
- **Current Code**:
```csharp
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
- **Analysis**: Nested `switch` statements simulating null checks inside a `foreach` loop.

#### Candidate 4: `SRNSMudApp/Components/Pages/TagDiagramPage.razor.cs:61-68`
- **Location**: `GetEffectiveItemIds()` lines 54-69
- **Current Code**:
```csharp
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
```
- **Analysis**: Imperative accumulation into `List<int>` with an $O(N)$ `!result.Contains(id)` deduplication check inside a loop.

#### Candidate 5: `SRNSMudApp/Components/Pages/TagDiagramPageViewModel.cs:109-142, 184`
- **Location**: `InitializeAsync` (lines 107-142) and `UpdateContextItemAsync` (lines 184-188)
- **Current Code**:
```csharp
if (preservedExtraIds != null)
{
    foreach (int id in preservedExtraIds)
    {
        _ = PinnedTagIds.Add(id);
    }
}
if (preservedHiddenTagIds != null)
{
    foreach (int id in preservedHiddenTagIds)
    {
        _ = HiddenTagIds.Add(id);
    }
}
if (preservedHiddenItemIds != null)
{
    foreach (int id in preservedHiddenItemIds)
    {
        _ = HiddenItemIds.Add(id);
    }
}
// ...
foreach (int id in contextTagIds)
{
    _ = PinnedTagIds.Add(id);
}
```
- **Analysis**: `PinnedTagIds`, `HiddenTagIds`, and `HiddenItemIds` are `HashSet<int>`. Iterating through collections and adding elements one by one with discarded returns (`_ = PinnedTagIds.Add(id)`) ignores canonical BCL set operations (`UnionWith`).

#### Candidate 6: `SRNSMudApp/Components/Item/ItemListViewModel.cs:66-103`
- **Location**: `InitializeAsync(Uri uri)` lines 66-103
- **Current Code**:
```csharp
var initialFilters = new List<TagFilter>();
foreach (FilterEntry filter in state.Filters)
{
    if (filter.TagId.HasValue)
    {
        if (tagsById.TryGetValue(filter.TagId.Value, out Data.Tag? tag))
        {
            initialFilters.Add(new TagFilter
            {
                TagId = tag.Id,
                Tag = tag,
                TagName = tag.Name,
                UserName = filter.UserName
            });
        }
    }
    else if (!string.IsNullOrWhiteSpace(filter.TagName))
    {
        _ = tagsByName.TryGetValue(filter.TagName, out Data.Tag? tag);
        initialFilters.Add(new TagFilter
        {
            TagName = filter.TagName,
            Tag = tag,
            UserName = filter.UserName
        });
    }
}
TagSearch.InitializeFilters(initialFilters);

_sortConditions.Clear();
foreach (SortEntry entry in state.SortEntries)
{
    if (tagsById.TryGetValue(entry.TagId, out Data.Tag? tag))
    {
        _sortConditions.Add(new SortCondition { Tag = tag, Order = entry.Order });
    }
}
```
- **Analysis**: Nested `if/else` checks for DTO to ViewModel mapping, plus an imperative loop for sort condition mapping.

#### Candidate 7: `SRNSMudApp/Components/Tag/TagTreeViewModel.cs:406-420`
- **Location**: `BuildTreeDataInternal` lines 405-420
- **Current Code**:
```csharp
IEnumerable<PendingTagMoveDto> matchingMoves = pendingMoves.Where(m => m.NewParentTagId == parentId);
foreach (PendingTagMoveDto move in matchingMoves)
{
    var canCancel = !string.IsNullOrEmpty(currentUserId) &&
                    (currentUserId == move.RequesterUserId || currentUserId == move.TagOwnerUserId);

    result.Add(new
    {
        id = $"move-req-{move.RequestId}",
        name = move.TagName,
        isPendingMove = true,
        requestId = move.RequestId,
        targetTagId = move.TagId,
        canCancel
    });
}
```
- **Analysis**: Imperative loop creating and adding anonymous objects to a list.

#### Candidate 8: `SRNSMudApp/Components/Tag/TagResolutionViewModel.cs:121-128`
- **Location**: `SearchParentCandidatesAsync` lines 121-128
- **Current Code**:
```csharp
foreach (var pTag in defaultParents)
{
    if (parentCandidates.All(s => s.Id != pTag.Id))
    {
        parentCandidates.Add(pTag);
        if (parentCandidates.Count >= 8) break;
    }
}
```
- **Analysis**: Manual conditional insertion with inner loop break checking count limit.

#### Candidate 9: `SRNSMudApp/Components/UI/UrlPreviewCardViewModel.cs:36-72`
- **Location**: `ProcessTextFragment` lines 36-72
- **Current Code**:
```csharp
List<string> results = [];
foreach (var part in parts)
{
    var text = TryDecode(part);
    var suffixSep = text.LastIndexOf(",-", StringComparison.Ordinal);
    text = suffixSep switch { >= 0 => text[..suffixSep], _ => text };
    var prefixSep = text.IndexOf("-,", StringComparison.Ordinal);
    text = prefixSep switch { >= 0 => text[(prefixSep + 2)..], _ => text };
    var segments = text.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
    switch (segments.Length)
    {
        case > 0:
            results.Add(string.Join("〜", segments));
            break;
    }
}
return results.Count switch { > 0 => string.Join(" | ", results), _ => null };
```
- **Analysis**: Uses mutable list accumulation, nested single-case switch statement on array length (`switch (segments.Length) { case > 0: ... }`), and repetitive slicing.

#### Candidate 10: `SRNSMudApp/Components/Tag/TagTreePopoverContent.razor.cs:44-47`
- **Location**: `OnParametersSet` lines 41-47
- **Current Code**:
```csharp
if (_expandedTreeTagIds.Count == 0)
{
    foreach (var id in TagTreePopoverViewModel.GetAutoExpandIds(TargetTag, AllTags.ToList()))
    {
        _expandedTreeTagIds.Add(id);
    }
}
```
- **Analysis**: Iterates over a new `List<int>` created via `.ToList()` just to call `_expandedTreeTagIds.Add(id)`.

#### Candidate 11: `SRNSMudApp.Client/Services/DoubleSubmissionGuard.cs:117-136`
- **Location**: `CleanupExpired(DateTimeOffset now)` lines 117-136
- **Current Code**:
```csharp
List<string>? keysToRemove = null;
foreach (var (k, timestamp) in _recentOperations)
{
    if (now - timestamp >= _recentWindow)
    {
        keysToRemove ??= [];
        keysToRemove.Add(k);
    }
}

if (keysToRemove != null)
{
    foreach (var k in keysToRemove)
    {
        _recentOperations.Remove(k);
    }
}
```
- **Analysis**: Two-step manual list allocation with null-coalescing and sequential deletion.

### 1.3 Modern C# Opportunities (Switch Expressions, Pattern Matching, Primary Constructors)

#### 1. Nested Boolean Switch Expressions
- **Location**: `SRNSMudApp/Components/UI/ItemCardViewModel.cs:176-191` (`GetTagDisplayWeight`)
- **Current Code**:
```csharp
public static string GetTagDisplayWeight(TagRelation relation, TimelineEvent? highlightEvent)
{
    var isUpdated = highlightEvent?.ParsedEventType == TimelineEventType.Update;
    var isInserted = highlightEvent?.ParsedEventType == TimelineEventType.Insert;
    var isDeleted = highlightEvent?.ParsedEventType == TimelineEventType.Delete;

    return (isUpdated || isInserted) switch
    {
        true => $"{highlightEvent?.PreviousWeight ?? 0} → {highlightEvent?.NewWeight}",
        false => isDeleted switch
        {
            true => highlightEvent?.PreviousWeight.ToString(CultureInfo.InvariantCulture) ?? "",
            false => relation.Weight.ToString(CultureInfo.InvariantCulture)
        }
    };
}
```
- **Analysis**: Clumsy nested boolean switches on precomputed boolean variables instead of direct pattern matching on `highlightEvent?.ParsedEventType`.

#### 2. Switch Statements Convertible to Switch Expressions
- **Location**: `SRNSMudApp/Components/Tag/TagTreeViewModel.cs:347-363`
```csharp
switch (parentId)
{
    case null:
        // ...
        break;
    default:
        // ...
        break;
}
```
- **Location**: `SRNSMudApp/Components/Pages/TagDiagramPage.razor.cs:416-427` (Setting `n.FocusRole` via `if/else if/else`).
- **Location**: `SRNSMudApp/Components/Item/ItemListQueryState.cs:95-107` (Switch statement inside parser).

#### 3. List Pattern Matching
- **Location**: `SRNSMudApp/Components/Item/ItemListQueryState.cs:92-108`
```csharp
var parts = value.Split(':');
if (parts.Length == 2 && int.TryParse(parts[0], CultureInfo.InvariantCulture, out var tagId))
{
    switch (parts[1]) ...
}
```
Can be modernized to C# 11+ list pattern:
`if (parts is [var idStr, var orderStr] && int.TryParse(idStr, CultureInfo.InvariantCulture, out var tagId))`

#### 4. Primary Constructors in ViewModels
The following 14 ViewModels/Services contain heavy constructor injection boilerplate assigning fields:
1. `SRNSMudApp/Components/Pages/NotificationsViewModel.cs:40-49` (10 dependencies)
2. `SRNSMudApp/Components/Item/ItemDetailViewModel.cs:36-42` (7 dependencies)
3. `SRNSMudApp/Components/UI/ItemCardActionViewModel.cs:40-46` (7 dependencies)
4. `SRNSMudApp/Components/Tag/TagDetailViewModel.cs:30-34` (5 dependencies)
5. `SRNSMudApp/Components/UI/TaggingRequestThreadViewModel.cs:21-23` (3 dependencies)
6. `SRNSMudApp/Components/User/UserManagementViewModel.cs:31-32` (2 dependencies)
7. `SRNSMudApp/Components/PublicOffer/CreatePublicOfferViewModel.cs:23-24` (2 dependencies)
8. `SRNSMudApp/Components/PublicOffer/TriggerPublicOfferViewModel.cs:23-24` (2 dependencies)
9. `SRNSMudApp/Components/Admin/ReportDetailActionViewModel.cs:23-24` (2 dependencies)
10. `SRNSMudApp/Components/Tag/TagResolutionViewModel.cs:20` (1 dependency)
11. `SRNSMudApp/Components/Tag/TagAutocompleteViewModel.cs:21` (1 dependency)
12. `SRNSMudApp/Components/Item/QuotedItemListViewModel.cs:19` (1 dependency)
13. `SRNSMudApp/Components/User/UserSearchViewModel.cs:15` (1 dependency)
14. `SRNSMudApp/Components/Admin/ReportManagerViewModel.cs:20` (1 dependency)

### 1.4 Analyzer Warnings & Suppressions in Scope
The following suppressions exist and were analyzed:
- **`IDE0072` / `IDE0010` (Missing cases in switch)**:
  - `RequestInfoAlertViewModel.cs:7`, `TagCardViewModel.cs:3`, `TagCard.razor.cs:3`, `ItemCard.razor.cs:21`, `UrlPreviewCardViewModel.cs:10`, `ItemListApiClient.cs:1615`.
  - Cause: Nullable enums (`TradeStatus?`, `TimelineEventType?`, `HttpStatusCode`) where the compiler warns if all enum values are not listed explicitly, even when `_ =>` default is present.
- **`CA1508` (Avoid dead code)**:
  - Suppressed in `NotificationsViewModel.cs:388`, `ItemTagTableViewModel.cs:2`, `ItemDetailViewModel.cs:9`, `ItemDetailRequestFilter.cs:1`, `TagDiagramCanvasViewModel.cs:5`, `ItemDetail.razor.cs:25`, `ItemList.razor.cs:2`.
  - Cause: Static analysis false positives on LINQ/Entity relationships where nullability checkers misinfer dead branches.
- **`CA1031` (Do not catch general exception types)**:
  - Present on 30+ methods across ViewModels and dialogs (`ReportManagerViewModel`, `TagManagementViewModel`, `NavMenuViewModel`, `UserManagementViewModel`, etc.).
  - Justification: In Blazor Server/WASM UI, unhandled exceptions terminate the SignalR circuit or crash the UI; catching `Exception` to display user-friendly `ISnackbar` messages or return `Result.Failure` is an architectural requirement.
- **`RCS1075` (Avoid empty catch clause)**:
  - `ResourceList.razor.cs:90, 128` (suppressed with justification).
- **`IDE1006` (Naming Styles)**:
  - `NotificationsPage.razor.cs`, `TagTable.razor.cs`, `TagTree.razor.cs`, `ItemList.razor.cs`, `TagRelationLinkWidget.cs` (for Blazor bindings / MudBlazor parameters).
- **`BL0012` (Component parameter should be a public property)**:
  - `ItemTagChip.razor.cs:51`, `ItemDetail.razor.cs:154`.
- **`CA1054` / `CA1055` / `CA1056` (URI parameters/properties should not be strings)**:
  - `ReactionBarViewModel.cs`, `ItemEditViewModel.cs`, `NavMenuViewModel.cs`, `ItemListRequest.cs`, `ItemListResponse.cs`, `ItemListApiClient.cs`.
  - Justification: Blazor `NavigationManager` operates on relative/absolute URL strings for navigation and routing.

---

## 2. Logic Chain

1. **Safety & Equivalence Evaluation for LINQ Conversions**:
   - For Candidates 1-11, each represents a functional mapping, projection, or collection aggregation without side effects on external state or threading.
   - For Candidate 1 (`ItemEditViewModel.ParsePillsToHtml`), converting the nested `for` loop to `string.Join("<br>", text.Split('\n').Select(...))` preserves the exact string output while removing mutable array mutations.
   - For Candidate 2 & 3 (`TagCardViewModel` and `ItemTagChipList`), replacing iterative `.Add()` with `.AddRange(highlightEvents.Where(...).Select(...))` preserves the insertion order and identical duplicate exclusion check (`allTags.All(...)`).
   - For Candidate 4 (`TagDiagramPage.GetEffectiveItemIds`), using `.Distinct()` replaces the manual `!result.Contains(id)` check and improves time complexity from $O(N^2)$ to $O(N)$ while preserving identical semantics.
   - For Candidate 5 (`TagDiagramPageViewModel`), `HashSet<int>.UnionWith` performs the exact same mathematical set union as iterating and calling `HashSet.Add(id)`, but executes faster and is the idiomatic BCL approach.

2. **C# Modern Feature Applicability**:
   - `ItemCardViewModel.GetTagDisplayWeight` contains an awkward nested boolean switch. Direct pattern matching on `highlightEvent?.ParsedEventType` (`TimelineEventType.Update or TimelineEventType.Insert => ...`) improves readability and directly eliminates the temporary boolean flags.
   - Converting the 14 identified ViewModels to C# 12 primary constructors (`class Foo(IDataProvider provider)`) satisfies `.editorconfig` rule `csharp_style_prefer_primary_constructors = true`, eliminates ~150 lines of field assignment boilerplate, and ensures constructor arguments remain private to the type unless explicitly exposed.
   - List pattern matching in `ItemListQueryState.cs` simplifies string splitting and length verification in a single expressive pattern.

---

## 3. Caveats & Invariant Guardrails

### 3.1 Blazor Razor Markup Template Loops MUST NOT Be Converted to LINQ
- **Observation**: 119 loops are `@foreach (...) { <Mud...> }` inside `.razor` files.
- **Rule**: Do NOT convert `@foreach` or `@for` in Razor markup into LINQ (`.Select(x => (RenderFragment)...)`).
- **Rationale**: The Razor compiler translates `@foreach` directly into sequence calls on `RenderTreeBuilder`. Wrapping items in `.Select` with render fragments degrades runtime rendering performance, breaks Blazor's tree diffing engine, and damages markup readability.

### 3.2 Asynchronous I/O inside Loops (DbContext Concurrency Violation)
- **Observation**: In `SRNSMudApp/Components/User/UserManagementViewModel.cs:48-57`:
  ```csharp
  foreach (var user in users)
  {
      bool isAdmin = await _userDataProvider.IsUserInRoleAsync(user, "Admin");
      items.Add(new UserManagementItemViewModel { ... });
  }
  ```
- **Rule**: Do NOT convert this loop to `Task.WhenAll(users.Select(async u => ...))` or LINQ.
- **Rationale**: `_userDataProvider.IsUserInRoleAsync` executes EF Core queries against a scoped `DbContext`. EF Core contexts are not thread-safe. Running concurrent queries on the same context instance throws `InvalidOperationException: A second operation was started on this context instance before a previous operation completed`. This loop must strictly remain sequential!

### 3.3 Interactive Modal Dialog Sequences
- **Observation**: In `SRNSMudApp/Components/Tag/ImportTaggingViewModel.cs:143, 186, 221`:
  ```csharp
  foreach (var relDto in payload.TagRelations)
  {
      // ...
      var dialog = await _dialogLauncher.ShowAsync<TagResolutionDialog>(...);
      var dialogResult = await dialog.Result;
  }
  ```
- **Rule**: Do NOT convert loops invoking interactive user dialogs to LINQ or concurrent tasks.
- **Rationale**: User resolution requires serial modal interactions. Parallelizing would spam the UI with simultaneous modal dialogs or crash MudBlazor's dialog provider.

### 3.4 Stateful Tree Traversal & Cycle Detection
- **Observation**:
  - `TagDiagramPage.razor.cs:176-200, 233-246`: Layout calculation loops (`col = i % columns`, `row = i / columns`) with offset accumulation.
  - `TagTreeViewModel.cs:461-495`: `DetectAndBreakCycles` with depth-first traversal and recursion stack.
  - `ItemNodeViewModel.cs:55-84`: Text truncation with character count accumulator, URL boundary protection, and ellipsis insertion.
- **Rule**: Keep these algorithm loops imperative.
- **Rationale**: Converting complex stateful traversals with early `break`/`continue` into LINQ reduces readability and increases allocation overhead.

### 3.5 Using-Alias Policy (`dotnet-format-policy.md`)
- **Rule**: All using aliases (such as `using Tag = SRNSMudApp.Data.Tag;`) must stay **inside** the namespace declaration:
  ```csharp
  namespace SRNSMudApp.Components.UI;
  using Tag = SRNSMudApp.Data.Tag;
  ```
- **Rule**: Any code formatting execution must include `--diagnostics IDE0055`. Never run `dotnet format` without arguments.

---

## 4. Conclusion & Proposed Code Transformations

### 4.1 Concrete Proposed Code Snippets

#### 1. `SRNSMudApp/Components/Item/ItemEditViewModel.cs:138-170`
```csharp
    /// <summary>
    ///     テキスト内の内部リンク（/TagDetail/ や /User/UserDetail/）を contenteditable 用の pill HTML に変換する。
    /// </summary>
    public static string ParsePillsToHtml(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        static string RenderSegment(ContentSegment segment)
        {
            if (segment.IsUrl && segment.Text.StartsWith('/', StringComparison.Ordinal))
            {
                var displayText = segment.Text switch
                {
                    var t when t.StartsWith("/TagDetail/", StringComparison.Ordinal) => "#タグ",
                    var t when t.StartsWith("/User/UserDetail/", StringComparison.Ordinal) => "@ユーザー",
                    _ => segment.Text
                };

                return string.Create(CultureInfo.InvariantCulture, $"<span class=\"internal-link-preview-pill\" data-testid=\"internal-link-preview-pill\" data-url=\"{segment.Text}\" contenteditable=\"false\">{displayText}</span>&#8203;&nbsp;");
            }

            return WebUtility.HtmlEncode(segment.Text).Replace(" ", "&nbsp;", StringComparison.Ordinal);
        }

        return string.Join("<br>", text.Split('\n').Select(line =>
            string.Concat(ItemCardViewModel.GetContentSegments(line).Select(RenderSegment))));
    }
```

#### 2. `SRNSMudApp/Components/UI/TagCardViewModel.cs:281-297`
```csharp
        // 削除されたタグが TimelineEvent に含まれている場合は、仮想的な TagRelationToTag としてリストに追加して表示する
        if (highlightEvents is not null)
        {
            allTags.AddRange(highlightEvents
                .Where(ev => ev is { ParsedEventType: Models.TimelineEventType.Delete, FollowedTag: not null }
                             && allTags.All(t => t.TagId != ev.FollowedTagId))
                .Select(ev => new TagRelationToTag
                {
                    TagId = ev.FollowedTagId,
                    Tag = ev.FollowedTag!,
                    Weight = ev.PreviousWeight,
                    OwnerId = ev.OwnerId // fake owner
                }));
        }
```

#### 3. `SRNSMudApp/Components/UI/ItemCardViewModel.cs:176-191`
```csharp
    /// <summary>
    ///     タグチップに表示するウェイト文字列を生成する（イベントの差分表示を含む）。
    /// </summary>
    public static string GetTagDisplayWeight(TagRelation relation, TimelineEvent? highlightEvent) =>
        highlightEvent?.ParsedEventType switch
        {
            TimelineEventType.Update or TimelineEventType.Insert =>
                $"{highlightEvent.PreviousWeight} → {highlightEvent.NewWeight}",
            TimelineEventType.Delete =>
                highlightEvent.PreviousWeight.ToString(CultureInfo.InvariantCulture),
            _ => relation.Weight.ToString(CultureInfo.InvariantCulture)
        };
```

#### 4. `SRNSMudApp/Components/Pages/TagDiagramPage.razor.cs:52-70`
```csharp
    private List<int> GetEffectiveItemIds()
    {
        IEnumerable<int> initial = QueryItemId.HasValue ? [QueryItemId.Value] : [];
        if (string.IsNullOrWhiteSpace(QueryItemIds))
        {
            return [.. initial];
        }

        var parsed = QueryItemIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var id) ? (int?)id : null)
            .OfType<int>();

        return [.. initial.Concat(parsed).Distinct()];
    }
```

#### 5. `SRNSMudApp/Components/Pages/TagDiagramPageViewModel.cs:107-142, 184`
```csharp
        if (preservedExtraIds is not null)
        {
            PinnedTagIds.UnionWith(preservedExtraIds);
        }

        if (preservedHiddenTagIds is not null)
        {
            HiddenTagIds.UnionWith(preservedHiddenTagIds);
        }

        if (preservedHiddenItemIds is not null)
        {
            HiddenItemIds.UnionWith(preservedHiddenItemIds);
        }

        // ...

        if (validItemIds.Count > 0)
        {
            LoadedContextItemIds = validItemIds;
            List<int> contextTagIds = validItemIds.Count == 1
                ? await _dataProvider.GetContextTagIdsForItemAsync(validItemIds[0]) ?? []
                : await _dataProvider.GetContextTagIdsForItemsAsync(validItemIds) ?? [];

            PinnedTagIds.UnionWith(contextTagIds);
            // ...
        }
```

#### 6. `SRNSMudApp/Components/Item/ItemListViewModel.cs:66-103`
```csharp
        List<TagFilter> initialFilters = [.. state.Filters
            .Select(filter => filter switch
            {
                { TagId: { } id } when tagsById.TryGetValue(id, out Data.Tag? tag) => new TagFilter
                {
                    TagId = tag.Id,
                    Tag = tag,
                    TagName = tag.Name,
                    UserName = filter.UserName
                },
                { TagName: { Length: > 0 } name } => new TagFilter
                {
                    TagName = name,
                    Tag = tagsByName.GetValueOrDefault(name),
                    UserName = filter.UserName
                },
                _ => null
            })
            .OfType<TagFilter>()];

        TagSearch.InitializeFilters(initialFilters);

        _sortConditions = [.. state.SortEntries
            .Select(entry => tagsById.TryGetValue(entry.TagId, out Data.Tag? tag)
                ? new SortCondition { Tag = tag, Order = entry.Order }
                : null)
            .OfType<SortCondition>()];
```

#### 7. `SRNSMudApp/Components/Tag/TagTreeViewModel.cs:404-422`
```csharp
        // 移動申請中のノードをこの親ノード直下に生やす
        result.AddRange(pendingMoves
            .Where(m => m.NewParentTagId == parentId)
            .Select(move => new
            {
                id = $"move-req-{move.RequestId}",
                name = move.TagName,
                isPendingMove = true,
                requestId = move.RequestId,
                targetTagId = move.TagId,
                canCancel = !string.IsNullOrEmpty(currentUserId) &&
                            (currentUserId == move.RequesterUserId || currentUserId == move.TagOwnerUserId)
            }));
```

#### 8. `SRNSMudApp/Components/Tag/TagResolutionViewModel.cs:116-130`
```csharp
        if (parentCandidates.Count < 5)
        {
            try
            {
                var defaultParents = await _dataProvider.SearchParentCandidateTagsAsync(null, cancellationToken);
                if (defaultParents is not null)
                {
                    parentCandidates.AddRange(defaultParents
                        .Where(p => parentCandidates.All(s => s.Id != p.Id))
                        .Take(8 - parentCandidates.Count));
                }
            }
            catch
            {
                // エラー時は現在の候補で継続
            }
        }
```

#### 9. `SRNSMudApp.Client/Services/DoubleSubmissionGuard.cs:117-136`
```csharp
    private void CleanupExpired(DateTimeOffset now)
    {
        var keysToRemove = _recentOperations
            .Where(pair => now - pair.Value >= _recentWindow)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var k in keysToRemove)
        {
            _recentOperations.Remove(k);
        }
    }
```

---

### 4.2 Recommended Decomposition & Milestone Grouping

To minimize risk and ensure clean verification, we recommend executing the work in 4 cohesive milestones:

| Milestone | Target Scope | Focus | Risk Level |
|---|---|---|---|
| **M1: Pure ViewModel Functional Modernization** | `ItemEditViewModel.cs`, `TagCardViewModel.cs`, `ItemCardViewModel.cs`, `TagResolutionViewModel.cs`, `ItemListQueryState.cs`, `TagFilterQueryCodec.cs`, `UrlPreviewCardViewModel.cs`, `DoubleSubmissionGuard.cs` | Convert imperative loops to pure LINQ pipelines, apply switch expressions, property patterns, and list patterns. 100% unit-testable without UI rendering. | **Low** |
| **M2: Primary Constructors & DI Cleanup** | 14 identified ViewModels in `Components/` (`NotificationsViewModel`, `ItemDetailViewModel`, `TagDetailViewModel`, `ItemCardActionViewModel`, etc.) | Convert DI constructor field assignments to C# 12 primary constructors. Remove redundant field declarations. | **Low** |
| **M3: Set Operations & Component Code-Behind** | `TagDiagramPageViewModel.cs`, `TagDiagramPage.razor.cs`, `TagTreePopoverContent.razor.cs`, `ItemTagChipList.razor`, `ItemListViewModel.cs` | Replace manual loops with `HashSet.UnionWith`, modernize node focus switch expressions, and apply collection expressions. | **Low-Medium** |
| **M4: Client Services & DTO Modernization** | `SRNSMudApp.Client/Services/ItemListApiClient.cs`, `ItemListRequest.cs`, `ItemListResponse.cs` | Modernize query parameter builders with collection expressions and pattern matching. Verify API contracts. | **Low** |

---

## 5. Verification Method

To independently verify the survey observations and subsequent refactoring implementations:

1. **Verify No Forbidden Loop Conversions**:
   - Inspect `.razor` files: ensure all 119 markup `@foreach` / `@for` blocks remain intact as Razor markup.
   - Inspect `UserManagementViewModel.cs`: ensure `IsUserInRoleAsync` loop remains sequential (not `Task.WhenAll`).
   - Inspect `ImportTaggingViewModel.cs`: ensure modal dialog sequence remains sequential.

2. **Verify Code Formatting & Namespace Policy**:
   - Verify all using-aliases in `Components/UI/` remain inside the namespace block.
   - Run format verification:
     ```bash
     dotnet format SRNSMudApp/SRNSMudApp.csproj --diagnostics IDE0055 --verify-no-changes
     ```

3. **Verify Build & Unit Tests**:
   - Build:
     ```bash
     dotnet build SRNSMudApp/SRNSMudApp.csproj --no-restore
     ```
   - Execute Unit Tests:
     ```bash
     dotnet test SRNSMudApp.Tests/SRNSMudApp.Tests.csproj --no-restore
     ```
   - Invalidation condition: If any unit test for `ItemCardViewModel`, `ItemEditViewModel`, `TagCardViewModel`, or `TagFilterQueryCodec` fails, check for subtle off-by-one errors or string encoding discrepancies.
