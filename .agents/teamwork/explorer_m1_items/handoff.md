# Domain 1 Audit Handoff Report: Items, Quotes, and Splits

## 1. Observation
Across Domain 1 (Items, Quotes, Splits), the following specific behaviors and code segments were directly observed in the codebase:

1. **Foreign Key Restrict on QuotedItemId**:
   In `SRNSMudApp/Data/ApplicationDbContext.cs:75-78`:
   ```csharp
   _ = builder.Entity<Item>()
       .HasOne(i => i.QuotedItem)
       .WithMany(i => i.QuotedByItems)
       .HasForeignKey(i => i.QuotedItemId)
       .OnDelete(DeleteBehavior.Restrict);
   ```
   In `SRNSMudApp/Services/ItemCardDataProvider.cs:68-100` (`DeleteItemAsync`):
   ```csharp
   public async Task DeleteItemAsync(int itemId)
   {
       ...
       List<Item> replyItems = await context.Items
           .Where(i => i.ParentItemId == itemId || i.RootItemId == itemId)
           .ToListAsync();
       if (replyItems.Count > 0)
       {
           context.Items.RemoveRange(replyItems);
       }
       _ = context.Items.Remove(item);
       _ = await context.SaveChangesAsync();
   }
   ```
   There is no query or nullification for `i.QuotedItemId == itemId`.

2. **Foreign Key Restrict on ItemSplitRequest.OriginalItemId and CreatedItemId**:
   In `SRNSMudApp/Data/ApplicationDbContext.cs:661-687`:
   ```csharp
   _ = builder.Entity<ItemSplitRequest>()
       .HasOne(r => r.OriginalItem)
       .WithMany()
       .HasForeignKey(r => r.OriginalItemId)
       .OnDelete(DeleteBehavior.Restrict);

   _ = builder.Entity<ItemSplitRequest>()
       .HasOne(r => r.CreatedItem)
       .WithMany()
       .HasForeignKey(r => r.CreatedItemId)
       .OnDelete(DeleteBehavior.Restrict);
   ```
   In `ItemCardDataProvider.DeleteItemAsync`, `ItemSplitRequest` is never cleaned up, nullified, or checked.

3. **Admin Forced Delete Implementation**:
   In `SRNSMudApp/Services/ItemCardDataProvider.cs:103-115`:
   ```csharp
   public async Task<bool> DeleteItemByAdminAsync(int itemId, string adminUserId)
   {
       await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
       Item? item = await context.Items.FindAsync(itemId);
       if (item is null)
       {
           return false;
       }

       _ = context.Items.Remove(item);
       _ = await context.SaveChangesAsync();
       return true;
   }
   ```
   Neither `replyItems` nor `relatedTaggingRequests` are deleted.

4. **Missing Visibility Filters in Public API Endpoints**:
   In `SRNSMudApp/Controllers/ItemController.cs:443-451`:
   ```csharp
   [HttpGet("{id:int}/quotes")]
   public async Task<ActionResult<IReadOnlyList<ItemDto>>> GetQuotes(
       int id,
       CancellationToken cancellationToken)
   {
       IReadOnlyList<Item> quotes = await _itemQuoteService.GetQuotedByItemsAsync(id, cancellationToken);
       var quoteDtos = quotes.Select(q => MapToItemDto(q, 0, 0)).ToList();
       return Ok(quoteDtos);
   }
   ```
   In `SRNSMudApp/Services/ItemQuoteService.cs:108-117`:
   ```csharp
   return await context.Items
       .AsNoTracking()
       ...
       .Where(i => i.QuotedItemId == quotedItemId)
       .OrderByDescending(i => i.CreatedDate)
       .ToListAsync(cancellationToken);
   ```
   No `WhereVisibleToUser` filter is applied. Similarly observed in `ItemController.GetReplies` (`ItemController.cs:385-393`) and `ItemSplitController.GetPendingSplitRequests` (`ItemSplitController.cs:107-117`).

5. **Conversation Opt-Out Ignored During Notification Dispatch**:
   In `SRNSMudApp/Services/ItemReplyService.cs:258-284`:
   `recipientIds` are computed from parent owner and thread repliers. `ConversationOptOuts` is never queried, and notifications are sent to all recipients via `_notificationService.NotifyUserAsync` unconditionally.

6. **Missing Transaction in Split Reject and Cancel**:
   In `SRNSMudApp/Services/ItemSplitService.cs:882-933` (`RejectSplitAsync`) and `936-985` (`CancelSplitAsync`):
   Status is modified and `DeleteRequesterQuoteItemAsync` is invoked without calling `await context.Database.BeginTransactionAsync()`.

7. **Duplicated Configuration Block in ApplicationDbContext**:
   In `SRNSMudApp/Data/ApplicationDbContext.cs:574-765`:
   The 15-line block configuring `ConversationOptOut` is duplicated 8 times consecutively.

---

## 2. Logic Chain
1. **From Observation 1**: Since `DeleteBehavior.Restrict` is enforced on `QuotedItemId` in SQL Server and `DeleteItemAsync` does not clear referencing quotes before removing the target item, executing a delete on any post that has been quoted will trigger an SQL foreign key reference conflict (`SqlException`), resulting in an unhandled 500 error. An attacker can permanently prevent deletion of any post simply by quoting it.
2. **From Observation 2**: Since `OriginalItemId` and `CreatedItemId` on `ItemSplitRequest` have `DeleteBehavior.Restrict` and are never cleared during item deletion, any post that was split or created from a split is permanently locked in the database and cannot be deleted.
3. **From Observation 3**: Since `DeleteItemByAdminAsync` only issues `context.Items.Remove(item)`, any item with replies or tagging requests fails with foreign key violation (`FK_Items_Items_ParentItemId` or `FK_TaggingRequestContracts_Items_RequestItemId`), rendering administrative moderation ineffective for active discussions.
4. **From Observation 4**: In the Blazor Server component (`ItemDetailDataProvider.cs`), quotes and replies are filtered through `WhereVisibleToUser(context, currentUserId, isAdmin)`. However, the Web API endpoints (`/api/item/{id}/quotes`, `/api/item/{id}/replies`, `/api/item-split/item/{id}/pending`) delegate to services (`ItemQuoteService`, `ItemReplyService`, `ItemSplitService`) that perform raw queries without visibility filtering. Consequently, anonymous or unauthorized clients can retrieve private posts, user-group posts, admin-quarantined content, and split text excerpts.
5. **From Observation 5**: Users who intentionally opt out of a conversation via `ToggleConversationOptOutAsync` continue to receive notification alerts and new records in `ItemReplyNotificationRecipients`, directly violating the feature contract.
6. **From Observation 6**: While split direct and approval methods use `IDbContextTransaction`, rejection and cancellation perform multi-step deletions without transaction boundaries, risking partial persistence if failures occur midway.

---

## 3. Caveats
- The investigation was performed in read-only mode without executing state mutations on the database.
- We assumed that SQL Server remains the production database provider, where `DeleteBehavior.Restrict` and `DeleteBehavior.NoAction` strictly block parent record deletions.
- Existing unit tests for `DeleteItemAsync` (`ItemCardDataProviderTests.cs:288-320`) pass only because they test an item with replies and tagging requests, but omit tests for items that are quoted or referenced by split requests.

---

## 4. Conclusion
Domain 1 exhibits critical vulnerabilities and architectural flaws that must be prioritized before release:
1. **Cascade Delete Blockers (High Priority)**: `ItemCardDataProvider.DeleteItemAsync` and `DeleteItemByAdminAsync` must be updated to clean up quotes, split requests, tag comments, and child dependencies inside an atomic transaction.
2. **Authorization & Privacy Leaks (High Priority)**: `ItemController` endpoints (`/quotes`, `/replies`) and `ItemSplitController` (`/pending`) must authenticate callers and enforce `WhereVisibleToUser`.
3. **Notification & Domain Consistency (Medium Priority)**: `ConversationOptOut` must be enforced during reply notification generation, backend content length validation must be introduced, and `ItemKindJson` must stay synchronized during item lifecycle transitions.

All 23 issues are cataloged in detail with trigger scenarios and remediation code in `analysis.md`.

---

## 5. Verification Method
To independently verify the observations and logic chain:
1. **Inspect Code Locations**:
   - `SRNSMudApp/Services/ItemCardDataProvider.cs` lines 68-115
   - `SRNSMudApp/Controllers/ItemController.cs` lines 385-393 and 443-451
   - `SRNSMudApp/Services/ItemQuoteService.cs` lines 97-118
   - `SRNSMudApp/Services/ItemSplitService.cs` lines 67-96, 235-327, 882-985
   - `SRNSMudApp/Data/ApplicationDbContext.cs` lines 73-78, 574-765
2. **Reproduction Test**:
   Create a test in `SRNSMudApp.Tests/Services/ItemCardDataProviderTests.cs`:
   ```csharp
   [Fact]
   public async Task DeleteItemAsync_WhenQuotedByAnotherItem_ThrowsOrFails()
   {
       var (db, sut, userId, _, itemId, tid) = await CreateScopeAsync();
       await using (db)
       {
           var quotingItem = new Item { Content = "quoting", OwnerId = userId, QuotedItemId = itemId };
           db.Items.Add(quotingItem);
           await db.SaveChangesAsync();

           await sut.DeleteItemAsync(itemId); // Fails with DbUpdateException
       }
   }
   ```
3. **Test Command**:
   Run `dotnet test SRNSMudApp.Tests/SRNSMudApp.Tests.csproj --filter "FullyQualifiedName~ItemCardDataProviderTests"` to observe current test suite execution.
