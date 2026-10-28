# Milestone 1: Backend Data Providers & Entity Management Analysis & Implementation Plan

- **Author**: `explorer_m1_mod_2` (teamwork_explorer)
- **Target Scope**:
  - `SRNSMudApp/Services/UserDataProvider.cs`
  - `SRNSMudApp/Services/TagTreeDataProvider.cs`
  - `SRNSMudApp/Services/ItemDetailDataProvider.cs`
  - `SRNSMudApp/Services/TagLockService.cs`
  - `SRNSMudApp/Services/AdminDataProvider.cs`
- **Date**: 2026-10-28
- **Parent**: `orchestrator_1` (conversation ID: `8e52f4ce-17b2-4afb-b7c7-36f230e9a5a1`)

---

## 1. Observation

### 1.1 Direct File Observations of Target Candidates

#### Candidate 1: `SRNSMudApp/Services/UserDataProvider.cs:202-231` (`EnsureDefaultReactionTagsAsync`)
- **Current Implementation**:
```csharp
        var now = DateTime.UtcNow;
        var created = false;
        foreach (var name in reactionNames)
        {
            if (!existingNames.Contains(name))
            {
                dbContext.Tags.Add(new Tag
                {
                    Name = name,
                    IsSystem = true,
                    OwnerId = userId,
                    CreatedDate = now,
                    UpdatedDate = now
                });
                created = true;
            }
        }

        if (created)
        {
            try
            {
                await dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // 並行作成時のユニーク制約競合は許容
            }
        }
```
- **Context**: `reactionNames` is `string[]` (from `ReactionTagNames.All`), `existingNames` is `List<string>`. The loop iterates over each reaction tag name, checks `!existingNames.Contains(name)`, calls `dbContext.Tags.Add(...)` individually, and sets `created = true`.
- **Additional Observation in File**: Lines 259–278 (`ToggleFollowUserAsync`) uses a pseudo-switch on nullable entity: `switch (existing) { case not null: ... default: ... }`.

#### Candidate 2: `SRNSMudApp/Services/TagTreeDataProvider.cs:394-409` (`LoadPendingTagMovesAsync`)
*(Note: Named `LoadPendingTagMovesAsync` in code, referenced as `GetPendingTagMovesAsync` in dispatch)*
- **Current Implementation**:
```csharp
        List<PendingTagMoveDto> dtos = [];
        foreach (TaggingRequestEntity req in requests)
        {
            if (req.Payload is TagMovePayload payload)
            {
                dtos.Add(new PendingTagMoveDto(
                    req.Id,
                    req.RequestedTagId,
                    req.RequestedTag?.Name ?? "不明なタグ",
                    payload.NewParentTagId,
                    req.RequesterUserId,
                    req.TagOwnerUserId));
            }
        }

        return dtos;
```
- **Context**: `requests` is `List<TaggingRequestEntity>` fetched with `.AsNoTracking()`. The loop manually initializes `dtos`, performs pattern matching `req.Payload is TagMovePayload payload`, and projects to `PendingTagMoveDto`.

#### Candidate 3: `SRNSMudApp/Services/ItemDetailDataProvider.cs:193-229` (`GetItemDetailAsync`)
- **Current Implementation**:
```csharp
            var existingLedgerRelationIds = ledgers
                .Where(l => l.SourceId.HasValue)
                .Select(l => l.SourceId!.Value)
                .ToHashSet();

            var existingLedgerTagIds = ledgers
                .Where(l => l.ItemId == itemId)
                .Select(l => l.TagId)
                .ToHashSet();

            foreach (TagRelation tr in item.TagRelations)
            {
                if (!existingLedgerRelationIds.Contains(tr.Id) && !existingLedgerTagIds.Contains(tr.TagId))
                {
                    ledgers.Add(new TagWeightLedger
                    {
                        TagId = tr.TagId,
                        Tag = tr.Tag!,
                        TagNameSnapshot = tr.Tag?.Name ?? string.Empty,
                        ItemId = itemId,
                        SourceType = "TagRelationInsert",
                        SourceId = tr.Id,
                        TagRelation = tr,
                        CommentItemId = tr.CommentItemId,
                        CommentItem = tr.CommentItem,
                        Delta = tr.Weight,
                        PreviousWeight = 0,
                        NewWeight = tr.Tag?.CachedWeight ?? tr.Weight,
                        IsOwnerAction = tr.OwnerId == item.OwnerId,
                        Reason = "タグの新規追加",
                        OwnerId = tr.OwnerId,
                        Owner = tr.Owner ?? tr.Tag?.Owner ?? item.Owner,
                        CreatedDate = tr.CreatedDate,
                        UpdatedDate = tr.UpdatedDate
                    });
                }
            }

            ledgers = [.. ledgers.OrderByDescending(l => l.CreatedDate)];
```
- **Context**: `ledgers` is an in-memory `List<TagWeightLedger>` loaded with `.AsNoTracking()`. Missing tag relations are appended to `ledgers` as synthesized ledger entries for read display.

#### Candidate 4: `SRNSMudApp/Services/TagLockService.cs:311-325` (`GetAllTagsWithLockStatusAsync`)
*(Note: Method is `GetAllTagsWithLockStatusAsync`, referenced as `GetTagLockSettingsAsync` in dispatch)*
- **Current Implementation**:
```csharp
        // 各親配下で個別ロックされているタグが存在するかマップを作成
        HashSet<int?> parentsWithLockedChildren = [];
        HashSet<HierarchyId> parentNodesWithLockedChildren = [];

        foreach (Tag t in tags.Where(t => t.IsLocked))
        {
            if (t.ParentTagId.HasValue)
            {
                _ = parentsWithLockedChildren.Add(t.ParentTagId.Value);
            }
            if (t.Node is not null && t.Node.GetLevel() > 0)
            {
                _ = parentNodesWithLockedChildren.Add(t.Node.GetAncestor(1));
            }
        }
```
- **Context**: `tags` is loaded with `.AsNoTracking()`. The loop populates two separate sets using discard assignment `_ = ...Add(...)`.

#### Candidate 5: `SRNSMudApp/Services/AdminDataProvider.cs:47-93` (`ImportItemsWithTagsAsync`)
- **Current Implementation**:
```csharp
            IEnumerable<string> newTagNames = allTagNames.Where(name => !existingTags.ContainsKey(name));
            List<Tag> newTags = [];
            foreach (var tagName in newTagNames)
            {
                var newTag = new Tag { Name = tagName, OwnerId = userId };
                newTags.Add(newTag);
                existingTags[tagName] = newTag;
            }

            if (newTags.Count != 0)
            {
                context.Tags.AddRange(newTags);
            }

            var importedItemCount = 0;
            foreach (var values in linesToProcess)
            {
                var content = values[0];
                var newItem = new Item
                {
                    Content = content,
                    OwnerId = userId,
                    TagRelations = []
                };
                _ = context.Items.Add(newItem);

                IEnumerable<string> tagNamesInLine =
                    values.Skip(1).Where(tn => !string.IsNullOrWhiteSpace(tn)).Distinct();
                foreach (var tagName in tagNamesInLine)
                {
                    if (!existingTags.TryGetValue(tagName, out Tag? tag))
                    {
                        continue;
                    }

                    var newRelation = new TagRelation
                    {
                        Item = newItem,
                        Tag = tag!,
                        Weight = 1,
                        OwnerId = userId
                    };
                    newItem.TagRelations.Add(newRelation);
                }

                importedItemCount++;
            }
```
- **Context**: Lines 75–90 use an imperative `foreach` with `existingTags.TryGetValue(...)` to populate `newItem.TagRelations.Add(...)`. Lines 48–54 also imperatively populate `newTags` and `existingTags`.

---

### 1.2 Direct File Observations of Untouchable Loops

#### Loop 1: Sequential `await` on `DbContext` (`TagTreeDataProvider.cs:92-110`)
```csharp
                    List<int> unlockedAuthorizedIds = [];
                    foreach (var id in authorizedIds)
                    {
                        var isLocked = await _tagLockService.IsTagOrSiblingLockedAsync(id);
                        if (!isLocked)
                        {
                            unlockedAuthorizedIds.Add(id);
                        }
                        ...
                    }
```
Also observed in:
- `Controllers/TagController.cs:82`: `foreach (var parentId in request.ParentTagIds) { await _tagCardDataProvider.AddTagToTagAsync(...); }`
- `Controllers/UserController.cs:142`: `foreach (var user in users) { await _userDataProvider.IsUserInRoleAsync(user, "Admin"); }`
- `Controllers/TagImportController.cs:124, 139`: `foreach (var relDto in payload.TagRelations) { await _taggingImportDataProvider.FindExistingTagAsync(...); }`
- `Program.cs:256`: `for (var attempt = 1; attempt <= maxRetries; attempt++) { await db.Database.MigrateAsync(); await Task.Delay(5000); }`

#### Loop 2: Sibling HierarchyId Node Generation (`TagTreeDataProvider.cs:145-153` & `TagCommandService.cs:353-361`)
```csharp
                            foreach (Tag child in orphanedChildren)
                            {
                                child.ParentTagId = rootTag?.Id;
                                if (rootTag != null)
                                {
                                    child.Node = rootTag.Node.GetDescendant(lastChildNode, null);
                                    lastChildNode = child.Node;
                                }
                            }
```

#### Loop 3: Balance Deduction Loop (`RightAssetDataProvider.cs:614-630`)
```csharp
        int remainingToDeduct = requestedAmount;
        foreach (var asset in approverAssets)
        {
            if (remainingToDeduct <= 0) break;
            if (asset.Amount <= remainingToDeduct)
            {
                remainingToDeduct -= asset.Amount;
                asset.Amount = 0;
                asset.IsBurned = true;
                asset.Status = new Burned(DateTime.UtcNow);
            }
            else
            {
                asset.Amount -= remainingToDeduct;
                remainingToDeduct = 0;
            }
        }
```

#### Loop 4: Entity Mutation Loops in `ItemSplitService.cs:389-772`
13+ loops updating tracked EF Core entities retrieved without `AsNoTracking()` (e.g. `quotedByItems`, `itemLedgers`, `replyItems`, `rootReferencingItems`, `taggingRequests`, `targetBounties`, `mutualRequests`, `contentReports`, `timelineEvents`):
```csharp
        foreach (Item quotingItem in quotedByItems)
        {
            quotingItem.QuotedItemId = targetItemId;
            quotingItem.ItemKindJson = JsonSerializer.Serialize(new QuoteItem(targetItemId));
            if (!string.IsNullOrEmpty(quotingItem.Content))
            {
                quotingItem.Content = ReplaceItemLinks(quotingItem.Content);
            }
            quotingItem.UpdatedDate = DateTime.UtcNow;
        }
```

#### Loop 5: Tracked Entity Property Reset Loops Before `SaveChangesAsync`
- `TagTreeDataProvider.cs:157`: `foreach (Tag tag in tagsToDelete) { tag.ParentTagId = null; }`
- `TagTreeDataProvider.cs:296`: `foreach (Tag descendant in descendants) { descendant.Node = reparented; descendant.UpdatedDate = DateTime.UtcNow; }`
- `TagLockService.cs:224-228 & 250-254`: `foreach (Tag ancestor in ancestors) { ancestor.IsLocked = true/false; ancestor.UpdatedDate = DateTime.UtcNow; }`

---

## 2. Logic Chain

### 2.1 Transformation of Imperative Loops to Declarative LINQ

1. **`UserDataProvider.EnsureDefaultReactionTagsAsync`**:
   - *Observation*: 1.1 Candidate 1.
   - *Reasoning*: The existing code filters `reactionNames` with `!existingNames.Contains(name)`, instantiates a `Tag`, calls `dbContext.Tags.Add(...)`, and sets `created = true`.
   - *Transformation*:
     - Replace the filter and instantiation with `.Where(name => !existingNames.Contains(name)).Select(...)`.
     - Construct `List<Tag> newTags` via collection expression `[.. ...]`.
     - Check `if (newTags.Count > 0)` in place of `created`.
     - Call `dbContext.Tags.AddRange(newTags)`.
   - *Roslynator*: Directly satisfies `RCS1146` ("Use LINQ method instead of loop").

2. **`TagTreeDataProvider.LoadPendingTagMovesAsync`**:
   - *Observation*: 1.1 Candidate 2.
   - *Reasoning*: The existing code filters `requests` with `req.Payload is TagMovePayload payload` and accumulates new `PendingTagMoveDto` records.
   - *Transformation*:
     - Stream `requests` through `.Where(req => req.Payload is TagMovePayload)`.
     - Project via `.Select(...)` casting `(TagMovePayload)req.Payload!`.
     - Collect into `List<PendingTagMoveDto>` with collection expression `[.. ...]`.
   - *Result*: Eliminates the temporary mutable list and nesting.

3. **`ItemDetailDataProvider.GetItemDetailAsync`**:
   - *Observation*: 1.1 Candidate 3.
   - *Reasoning*: An imperative `foreach` checks two `HashSet` conditions (`!existingLedgerRelationIds.Contains` and `!existingLedgerTagIds.Contains`) and adds to `ledgers`.
   - *Transformation*:
     - Convert `existingLedgerRelationIds` and `existingLedgerTagIds` instantiation to collection expressions `[.. ...]`.
     - Call `ledgers.AddRange(item.TagRelations.Where(...).Select(...))`.
   - *Result*: Declarative, zero mutable state in loop, keeps the subsequent `OrderByDescending` intact.

4. **`TagLockService.GetAllTagsWithLockStatusAsync`**:
   - *Observation*: 1.1 Candidate 4.
   - *Reasoning*: A single loop populates two separate sets (`parentsWithLockedChildren` and `parentNodesWithLockedChildren`) using discard assignments `_ = ...Add(...)`.
   - *Transformation*:
     - Initialize `HashSet<int?> parentsWithLockedChildren = [.. tags.Where(t => t.IsLocked && t.ParentTagId.HasValue).Select(t => t.ParentTagId)];`.
     - Initialize `HashSet<HierarchyId> parentNodesWithLockedChildren = [.. tags.Where(t => t.IsLocked && t.Node is not null && t.Node.GetLevel() > 0).Select(t => t.Node!.GetAncestor(1))];`.
   - *Result*: Clean, decoupled, immutable declarations.

5. **`AdminDataProvider.ImportItemsWithTagsAsync`**:
   - *Observation*: 1.1 Candidate 5.
   - *Reasoning*:
     - In lines 75–90, `tagNamesInLine` is matched against `existingTags.TryGetValue(...)`. Using `existingTags.GetValueOrDefault(tn)` allows filtering non-null values with `.Where(tag => tag is not null)`.
     - Note on compiler constraint: Inside `new Item { TagRelations = ... }`, `newItem` cannot be captured before assignment (CS0165: "Use of unassigned local variable 'newItem'"). Therefore, `newItem` must be declared first, followed by `newItem.TagRelations = [.. ...]`.
     - In lines 48–54, `newTags` can be declared with LINQ projection `[.. allTagNames.Where(...).Select(...)]`, followed by `existingTags[tag.Name] = tag`.

---

### 2.2 EF Core Entity Change Tracking Verification

1. **`DbSet<T>.AddRange` Semantics in EF Core**:
   - In EF Core, `DbSet<T>.AddRange(IEnumerable<T> entities)` traverses all passed entities and marks their `EntityEntry.State` as `EntityState.Added`.
   - Performance benefit: `AddRange` suspends change detection until all entities in the enumerable are added, executing change detection in a single batch pass rather than $O(N)$ individual passes in a loop.
2. **`UserDataProvider.EnsureDefaultReactionTagsAsync` Verification**:
   - Each `Tag` created in `newTags` is newly instantiated with `Id = 0`.
   - `dbContext.Tags.AddRange(newTags)` attaches them as `EntityState.Added`.
   - Calling `await dbContext.SaveChangesAsync()` generates `INSERT INTO Tags` statements within the existing transaction.
   - Concurrency conflict handling (`catch (DbUpdateException)`) behaves identically.
3. **`AdminDataProvider.ImportItemsWithTagsAsync` Verification**:
   - Setting `newItem.TagRelations = [.. ...]` where each `TagRelation` has `Item = newItem` and `Tag = tag!`:
     - `context.Items.Add(newItem)` places `newItem` into `EntityState.Added`.
     - Because EF Core's change tracker recursively traverses navigation properties of an `Added` entity, all `TagRelation` entities in `newItem.TagRelations` are automatically tracked in `EntityState.Added`.
     - When `tag` is an existing entity from `context.Tags`, EF Core establishes foreign key fixup `TagRelation.TagId = tag.Id` (or tracks the pending new tag).
     - `await context.SaveChangesAsync()` executes inside `context.Database.ExecuteWithStrategyAsync`, ensuring transactional integrity.
4. **Read-Only / In-Memory Safety (`TagTreeDataProvider`, `ItemDetailDataProvider`, `TagLockService`)**:
   - In `TagTreeDataProvider`, `ItemDetailDataProvider`, and `TagLockService`, the entities are loaded with `.AsNoTracking()`.
   - In `ItemDetailDataProvider`, `ledgers` is a local display list; `ledgers.AddRange(...)` modifies only the in-memory `List<TagWeightLedger>`. No `DbContext.SaveChanges` is called on these objects.
   - Therefore, there is zero risk of unintended EF Core change tracking state corruption.

---

### 2.3 Untouchable Loops: Logical Proofs for Non-Conversion

| Loop Location | Loop Type | Why It MUST NOT Be Converted to LINQ |
|---|---|---|
| `TagTreeDataProvider.cs:93` | Sequential `await` on `DbContext` | Awaits `_tagLockService.IsTagOrSiblingLockedAsync(id)`. Attempting to convert to LINQ (`Task.WhenAll(authorizedIds.Select(async ...))`) triggers concurrent queries on the same `DbContext` instance, throwing `InvalidOperationException` ("A second operation was started on this context instance before a previous operation completed"). |
| `Controllers/TagController.cs:82`, `UserController.cs:142`, `TagImportController.cs:124`, `Program.cs:256` | Sequential `await` / Retry loop | Sequential database access or retry backoff with `Task.Delay`. Must remain sequential imperative loops. |
| `TagTreeDataProvider.cs:145-153` & `TagCommandService.cs:353-361` | Sibling `HierarchyId` generation | `child.Node = rootTag.Node.GetDescendant(lastChildNode, null); lastChildNode = child.Node;`. Each iteration $i$ strictly requires the computed result of iteration $i-1$. This recurrence relation cannot be converted to declarative LINQ without mutable accumulation hacks (`Aggregate` with side effects) that destroy readability and introduce bugs. |
| `RightAssetDataProvider.cs:614-630` | Balance deduction state machine | Stateful algorithm: decrements `remainingToDeduct`, breaks early when `<= 0`, and mutates `asset.Amount`, `asset.IsBurned`, and `asset.Status`. Converting this to LINQ violates functional query principles and introduces side-effecting lambdas. |
| `ItemSplitService.cs:389-772` (13+ loops) | EF Core tracked entity mutation | Tracked entities retrieved without `AsNoTracking()` must have properties mutated so EF Core's change tracker detects changes and generates SQL `UPDATE` statements on `SaveChangesAsync()`. Side-effecting LINQ queries are an anti-pattern. |
| `TagTreeDataProvider.cs:157`, `TagLockService.cs:224, 250` | Tracked entity property updates | In-place entity property mutations (`tag.ParentTagId = null`, `ancestor.IsLocked = ...`) preceding `SaveChangesAsync()`. Must remain imperative. |

---

## 3. Caveats

1. **Method Naming Discrepancies**:
   - The dispatch message referred to `TagTreeDataProvider.GetPendingTagMovesAsync` and `TagLockService.GetTagLockSettingsAsync`.
   - In the actual codebase, the methods are `TagTreeDataProvider.LoadPendingTagMovesAsync` (line 383) and `TagLockService.GetAllTagsWithLockStatusAsync` (line 298). The diffs in this report target the exact physical method definitions.
2. **Read-Only Scope**:
   - In accordance with the Teamwork explorer archetype and system constraints, no source code modifications have been committed to the repository. The proposed code diffs are ready for direct application by the implementation agent.
3. **Sandbox Build Environment**:
   - In this development sandbox, NuGet package restore requires offline cache (`--no-restore`). Because .NET 11 preview packages use floating versions (`11.0.*-*`), verification commands must be executed with `--no-restore` against the local package cache.

---

## 4. Conclusion & Concrete Code Diff Proposals

### 4.1 Target 1: `SRNSMudApp/Services/UserDataProvider.cs`

#### Candidate: `EnsureDefaultReactionTagsAsync` (Lines 202–231)
- **Before**:
```csharp
        var now = DateTime.UtcNow;
        var created = false;
        foreach (var name in reactionNames)
        {
            if (!existingNames.Contains(name))
            {
                dbContext.Tags.Add(new Tag
                {
                    Name = name,
                    IsSystem = true,
                    OwnerId = userId,
                    CreatedDate = now,
                    UpdatedDate = now
                });
                created = true;
            }
        }

        if (created)
        {
            try
            {
                await dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // 並行作成時のユニーク制約競合は許容
            }
        }
```
- **After**:
```csharp
        var now = DateTime.UtcNow;
        List<Tag> newTags = [.. reactionNames
            .Where(name => !existingNames.Contains(name))
            .Select(name => new Tag
            {
                Name = name,
                IsSystem = true,
                OwnerId = userId,
                CreatedDate = now,
                UpdatedDate = now
            })];

        if (newTags.Count > 0)
        {
            dbContext.Tags.AddRange(newTags);
            try
            {
                await dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // 並行作成時のユニーク制約競合は許容
            }
        }
```
- **Rationale**: Replaces the imperative loop and mutable boolean flag `created` with declarative LINQ filtering, projection, and batch `AddRange`. Change tracking marks all entities as `Added`.

#### Bonus Modernization: `ToggleFollowUserAsync` (Lines 259–278)
- **Before**:
```csharp
        switch (existing)
        {
            case not null:
                _ = dbContext.UserFollows.Remove(existing);
                _ = await dbContext.SaveChangesAsync();
                return false;
            default:
                {
                    var newFollow = new UserFollow
                    {
                        OwnerId = currentUserId,
                        FollowedUserId = targetUserId,
                        CreatedDate = DateTime.UtcNow,
                        UpdatedDate = DateTime.UtcNow
                    };
                    _ = dbContext.UserFollows.Add(newFollow);
                    _ = await dbContext.SaveChangesAsync();
                    return true;
                }
        }
```
- **After**:
```csharp
        if (existing is not null)
        {
            _ = dbContext.UserFollows.Remove(existing);
            _ = await dbContext.SaveChangesAsync();
            return false;
        }

        var newFollow = new UserFollow
        {
            OwnerId = currentUserId,
            FollowedUserId = targetUserId,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };
        _ = dbContext.UserFollows.Add(newFollow);
        _ = await dbContext.SaveChangesAsync();
        return true;
```
- **Rationale**: Replaces awkward pseudo-switch on nullable entity with idiomatic guard clause and pattern matching.

---

### 4.2 Target 2: `SRNSMudApp/Services/TagTreeDataProvider.cs`

#### Candidate: `LoadPendingTagMovesAsync` (Lines 394–409)
- **Before**:
```csharp
        List<PendingTagMoveDto> dtos = [];
        foreach (TaggingRequestEntity req in requests)
        {
            if (req.Payload is TagMovePayload payload)
            {
                dtos.Add(new PendingTagMoveDto(
                    req.Id,
                    req.RequestedTagId,
                    req.RequestedTag?.Name ?? "不明なタグ",
                    payload.NewParentTagId,
                    req.RequesterUserId,
                    req.TagOwnerUserId));
            }
        }

        return dtos;
```
- **After**:
```csharp
        return [.. requests
            .Where(req => req.Payload is TagMovePayload)
            .Select(req =>
            {
                var payload = (TagMovePayload)req.Payload!;
                return new PendingTagMoveDto(
                    req.Id,
                    req.RequestedTagId,
                    req.RequestedTag?.Name ?? "不明なタグ",
                    payload.NewParentTagId,
                    req.RequesterUserId,
                    req.TagOwnerUserId);
            })];
```
- **Rationale**: Converts loop with accumulation list to declarative pipeline using pattern matching filter `.Where(req => req.Payload is TagMovePayload)` and collection expression `[.. ...]`.

---

### 4.3 Target 3: `SRNSMudApp/Services/ItemDetailDataProvider.cs`

#### Candidate: `GetItemDetailAsync` (Lines 193–229)
- **Before**:
```csharp
            var existingLedgerRelationIds = ledgers
                .Where(l => l.SourceId.HasValue)
                .Select(l => l.SourceId!.Value)
                .ToHashSet();

            var existingLedgerTagIds = ledgers
                .Where(l => l.ItemId == itemId)
                .Select(l => l.TagId)
                .ToHashSet();

            foreach (TagRelation tr in item.TagRelations)
            {
                if (!existingLedgerRelationIds.Contains(tr.Id) && !existingLedgerTagIds.Contains(tr.TagId))
                {
                    ledgers.Add(new TagWeightLedger
                    {
                        TagId = tr.TagId,
                        Tag = tr.Tag!,
                        TagNameSnapshot = tr.Tag?.Name ?? string.Empty,
                        ItemId = itemId,
                        SourceType = "TagRelationInsert",
                        SourceId = tr.Id,
                        TagRelation = tr,
                        CommentItemId = tr.CommentItemId,
                        CommentItem = tr.CommentItem,
                        Delta = tr.Weight,
                        PreviousWeight = 0,
                        NewWeight = tr.Tag?.CachedWeight ?? tr.Weight,
                        IsOwnerAction = tr.OwnerId == item.OwnerId,
                        Reason = "タグの新規追加",
                        OwnerId = tr.OwnerId,
                        Owner = tr.Owner ?? tr.Tag?.Owner ?? item.Owner,
                        CreatedDate = tr.CreatedDate,
                        UpdatedDate = tr.UpdatedDate
                    });
                }
            }
```
- **After**:
```csharp
            HashSet<int> existingLedgerRelationIds = [.. ledgers
                .Where(l => l.SourceId.HasValue)
                .Select(l => l.SourceId!.Value)];

            HashSet<int> existingLedgerTagIds = [.. ledgers
                .Where(l => l.ItemId == itemId)
                .Select(l => l.TagId)];

            ledgers.AddRange(item.TagRelations
                .Where(tr => !existingLedgerRelationIds.Contains(tr.Id) && !existingLedgerTagIds.Contains(tr.TagId))
                .Select(tr => new TagWeightLedger
                {
                    TagId = tr.TagId,
                    Tag = tr.Tag!,
                    TagNameSnapshot = tr.Tag?.Name ?? string.Empty,
                    ItemId = itemId,
                    SourceType = "TagRelationInsert",
                    SourceId = tr.Id,
                    TagRelation = tr,
                    CommentItemId = tr.CommentItemId,
                    CommentItem = tr.CommentItem,
                    Delta = tr.Weight,
                    PreviousWeight = 0,
                    NewWeight = tr.Tag?.CachedWeight ?? tr.Weight,
                    IsOwnerAction = tr.OwnerId == item.OwnerId,
                    Reason = "タグの新規追加",
                    OwnerId = tr.OwnerId,
                    Owner = tr.Owner ?? tr.Tag?.Owner ?? item.Owner,
                    CreatedDate = tr.CreatedDate,
                    UpdatedDate = tr.UpdatedDate
                }));
```
- **Rationale**: Replaces `foreach` and nested `if` with declarative `.Where(...).Select(...)` and `ledgers.AddRange(...)`. Adopts collection expressions for lookup set definitions.

---

### 4.4 Target 4: `SRNSMudApp/Services/TagLockService.cs`

#### Candidate: `GetAllTagsWithLockStatusAsync` (Lines 311–325)
- **Before**:
```csharp
        // 各親配下で個別ロックされているタグが存在するかマップを作成
        HashSet<int?> parentsWithLockedChildren = [];
        HashSet<HierarchyId> parentNodesWithLockedChildren = [];

        foreach (Tag t in tags.Where(t => t.IsLocked))
        {
            if (t.ParentTagId.HasValue)
            {
                _ = parentsWithLockedChildren.Add(t.ParentTagId.Value);
            }
            if (t.Node is not null && t.Node.GetLevel() > 0)
            {
                _ = parentNodesWithLockedChildren.Add(t.Node.GetAncestor(1));
            }
        }
```
- **After**:
```csharp
        // 各親配下で個別ロックされているタグが存在するかマップを作成
        HashSet<int?> parentsWithLockedChildren = [.. tags
            .Where(t => t.IsLocked && t.ParentTagId.HasValue)
            .Select(t => t.ParentTagId)];

        HashSet<HierarchyId> parentNodesWithLockedChildren = [.. tags
            .Where(t => t.IsLocked && t.Node is not null && t.Node.GetLevel() > 0)
            .Select(t => t.Node!.GetAncestor(1))];
```
- **Rationale**: Eliminates the imperative loop with discard additions `_ = ...Add(...)`, constructing the hash sets independently and declaratively via collection expressions.

---

### 4.5 Target 5: `SRNSMudApp/Services/AdminDataProvider.cs`

#### Candidate: `ImportItemsWithTagsAsync` (Lines 47–93)
- **Before**:
```csharp
            IEnumerable<string> newTagNames = allTagNames.Where(name => !existingTags.ContainsKey(name));
            List<Tag> newTags = [];
            foreach (var tagName in newTagNames)
            {
                var newTag = new Tag { Name = tagName, OwnerId = userId };
                newTags.Add(newTag);
                existingTags[tagName] = newTag;
            }

            if (newTags.Count != 0)
            {
                context.Tags.AddRange(newTags);
            }

            var importedItemCount = 0;
            foreach (var values in linesToProcess)
            {
                var content = values[0];
                var newItem = new Item
                {
                    Content = content,
                    OwnerId = userId,
                    TagRelations = []
                };
                _ = context.Items.Add(newItem);

                IEnumerable<string> tagNamesInLine =
                    values.Skip(1).Where(tn => !string.IsNullOrWhiteSpace(tn)).Distinct();
                foreach (var tagName in tagNamesInLine)
                {
                    if (!existingTags.TryGetValue(tagName, out Tag? tag))
                    {
                        continue;
                    }

                    var newRelation = new TagRelation
                    {
                        Item = newItem,
                        Tag = tag!,
                        Weight = 1,
                        OwnerId = userId
                    };
                    newItem.TagRelations.Add(newRelation);
                }

                importedItemCount++;
            }
```
- **After**:
```csharp
            List<Tag> newTags = [.. allTagNames
                .Where(name => !existingTags.ContainsKey(name))
                .Select(tagName => new Tag { Name = tagName, OwnerId = userId })];

            foreach (var tag in newTags)
            {
                existingTags[tag.Name] = tag;
            }

            if (newTags.Count != 0)
            {
                context.Tags.AddRange(newTags);
            }

            var importedItemCount = 0;
            foreach (var values in linesToProcess)
            {
                var content = values[0];
                var newItem = new Item
                {
                    Content = content,
                    OwnerId = userId
                };
                _ = context.Items.Add(newItem);

                newItem.TagRelations = [.. values.Skip(1)
                    .Where(tn => !string.IsNullOrWhiteSpace(tn))
                    .Distinct()
                    .Select(tn => existingTags.GetValueOrDefault(tn))
                    .Where(tag => tag is not null)
                    .Select(tag => new TagRelation
                    {
                        Item = newItem,
                        Tag = tag!,
                        Weight = 1,
                        OwnerId = userId
                    })];

                importedItemCount++;
            }
```
- **Rationale**:
  - Replaces the inner imperative relation-building loop with a single LINQ pipeline using `GetValueOrDefault`, null filtering, projection, and collection expression `[.. ...]`.
  - Replaces `newTags` construction with declarative projection.
  - Safely initializes `newItem` before referencing it in `new TagRelation { Item = newItem, ... }`, avoiding CS0165 while allowing EF Core relationship navigation to track all new `TagRelation` entities as `Added`.

---

## 5. Verification Method

### 5.1 Static Verification
1. Inspect the modified files against `.editorconfig` rules:
   - Ensure `RCS1146` is satisfied (no imperative collection population loops remaining where LINQ applies).
   - Ensure formatting adheres to IDE0055 without unformatted blocks:
     ```bash
     dotnet format --diagnostics IDE0055 --verify-no-changes --no-restore
     ```
2. Verify that no C# compiler errors CS0165 (unassigned local variable in self-referencing initializer) exist in `AdminDataProvider.cs`.

### 5.2 Build Verification
Execute compilation of the affected project:
```bash
dotnet build SRNSMudApp/SRNSMudApp.csproj --no-restore
```

### 5.3 Automated Test Suite Verification
Run the existing unit test suite covering the affected services:
1. `TagTreeDataProviderTests.cs`:
   - Specifically `LoadPendingTagMovesAsync_ReturnsProposedMoveRequestsOnly` (verifies pending move DTO projection).
2. `ItemDetailDataProviderTests.cs`:
   - Specifically `GetItemDetailAsync_WhenTagRelationIsDeleted_StillReturnsTagWeightLedgers` and `GetItemDetailAsync_ReturnsAncestorsRepliesAndSiblings` (verifies `TagWeightLedger` synthesis and collection counts).
3. `TagLockServiceTests.cs`:
   - Verifies ancestor and sibling lock calculations.
4. `Controllers/ItemControllerTests.cs` and `Components/Item/ItemImportViewModelTests.cs`:
   - Verifies item and tag batch import behavior.
5. Command to execute full test suite:
   ```bash
   dotnet test SRNSMudApp.Tests/SRNSMudApp.Tests.csproj --no-restore
   ```

### 5.4 Invalidation Conditions
1. Any change that replaces sequential `await` loops (`TagTreeDataProvider.cs:93`, `TagController.cs:82`, etc.) with `Task.WhenAll` is invalid due to `DbContext` thread-safety violations.
2. Any change that alters the recurrence calculation of sibling `HierarchyId` generation (`TagTreeDataProvider.cs:145-153`) is invalid.
3. Any change that mutates EF Core tracked entities inside LINQ lambda closures (`ItemSplitService.cs`, `RightAssetDataProvider.cs`) is invalid.
4. Any change to `AdminDataProvider.cs` that attempts to reference `newItem` inside its own object initializer before assignment is invalid (causes compiler error CS0165).
