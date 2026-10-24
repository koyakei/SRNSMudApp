# Domain 1 Audit Report: Items, Quotes, and Splits

## Executive Summary
This report presents the findings of a comprehensive, read-only architectural and domain logic audit of **Domain 1 (Items, Quotes, and Splits)** within the `SRNSWebApp` codebase.
Our investigation covered entity definitions, EF Core relationship mappings, lifecycle services (`ItemCardDataProvider`, `ItemQuoteService`, `ItemSplitService`, `ItemReplyService`, `ItemTagService`, `ItemReactionService`), controllers (`ItemController`, `ItemSplitController`), UI view models (`ItemCardActionViewModel`, `ItemCardSplitCoordinator`, `ItemEditViewModel`, `QuoteItemViewModel`, `QuotedItemPreviewViewModel`), and client API integration.

We identified **23 distinct issues** across 4 major architectural categories:
- **Cascade Delete & Foreign Key Constraint Violations**: 6 issues (5 High, 1 Med)
- **Authorization, Privacy & Information Disclosure Flaws**: 7 issues (5 High, 2 Med)
- **Domain Specification Inconsistencies & Business Rule Violations**: 6 issues (4 Med, 2 Low)
- **Transaction Rollback, Concurrency & Entity Synchronization Gaps**: 4 issues (1 High, 2 Med, 1 Low)

---

## Issue Summary Matrix

| ID | Category | Severity | Target File & Lines | Summary |
|---|---|---|---|---|
| SEC-01 | Privacy / Auth | **High** | `SRNSMudApp/Controllers/ItemController.cs:443-451`<br>`SRNSMudApp/Services/ItemQuoteService.cs:97-118` | Anonymous information disclosure of private & admin-hidden items via Quote API |
| SEC-02 | Privacy / Auth | **High** | `SRNSMudApp/Controllers/ItemController.cs:385-393`<br>`SRNSMudApp/Services/ItemReplyService.cs:40-153` | Anonymous information disclosure of private & group-restricted replies via Reply API |
| SEC-03 | Privacy / Auth | **High** | `SRNSMudApp/Controllers/ItemSplitController.cs:107-117`<br>`SRNSMudApp/Services/ItemSplitService.cs:988-1001` | Unauthenticated public endpoint leaks `SelectedText` of private items |
| SEC-04 | Privacy / Auth | **High** | `SRNSMudApp/Components/UI/QuotedItemPreviewViewModel.cs:57-69`<br>`SRNSMudApp/Services/ItemQuoteService.cs:135-152` | `QuotedItemPreview` bypasses visibility filters and displays quarantined/hidden items |
| SEC-05 | Privacy / Auth | **High** | `SRNSMudApp/Services/ItemReplyService.cs:239-285` | Private reply notifications leak full text to users outside `TargetUserGroupId` |
| CAS-01 | Cascade / Integrity | **High** | `SRNSMudApp/Services/ItemCardDataProvider.cs:68-100`<br>`SRNSMudApp/Data/ApplicationDbContext.cs:73-78` | Standard item deletion crashes with SQL FK Restrict violation if item is quoted |
| CAS-02 | Cascade / Integrity | **High** | `SRNSMudApp/Services/ItemCardDataProvider.cs:68-100`<br>`SRNSMudApp/Data/ApplicationDbContext.cs:661-687` | Item deletion permanently blocked by historical or pending `ItemSplitRequests` |
| CAS-03 | Cascade / Integrity | **High** | `SRNSMudApp/Services/ItemCardDataProvider.cs:103-115` | Admin forced deletion (`DeleteItemByAdminAsync`) crashes on any item with replies or tagging requests |
| CAS-04 | Cascade / Integrity | **High** | `SRNSMudApp/Services/ItemCardDataProvider.cs:68-100`<br>`SRNSMudApp/Services/ItemReactionService.cs:126-173` | Deleting `TagCommentItem` or its parent item triggers FK Restrict/NoAction exceptions |
| CAS-05 | Cascade / Integrity | **High** | `SRNSMudApp/Services/ItemCardDataProvider.cs:68-100`<br>`SRNSMudApp/Services/HomeDataProvider.cs:165-237` | Deleting items leaves orphaned `TimelineEvents`, corrupting timeline pagination and count |
| CAS-06 | Cascade / Integrity | **High** | `SRNSMudApp/Services/ItemCardDataProvider.cs:68-100`<br>`SRNSMudApp/Data/ApplicationDbContext.cs:236-247` | Item deletion cascade deletes `TagRelation`s without decrementing `CachedWeight` or writing ledger |
| CON-01 | Concurrency | **High** | `SRNSMudApp/Services/ItemSplitService.cs:235-327, 882-985` | Concurrency race between split approval and cancellation causes split/delete collision |
| SPEC-01 | Specification | **Medium** | `SRNSMudApp/Services/ItemReplyService.cs:239-285` | `ConversationOptOut` is completely ignored at notification dispatch time |
| SPEC-02 | Specification | **Medium** | `SRNSMudApp/Data/ApplicationDbContextTagExtensions.cs:82-145`<br>`SRNSMudApp/Services/ItemQuoteService.cs:70-84` | Contradiction: `RightAsset` required for direct tags, but bypassed in Quotes, Replies, and Creation |
| SPEC-03 | Specification | **Medium** | `SRNSMudApp/Services/ItemQuoteService.cs:56-66`<br>`SRNSMudApp/Components/UI/QuotedItemPreviewViewModel.cs:72-78` | Lack of quote content snapshotting allows author to retroactively alter meaning of quotes |
| SPEC-04 | Specification | **Medium** | `SRNSMudApp/Services/ItemSplitService.cs:808-829` | First-match `IndexOf` in split selection makes it impossible to split repetitive words |
| SPEC-05 | Specification | **Medium** | `SRNSMudApp/Controllers/ItemController.cs:349-380, 456-492`<br>`SRNSMudApp/Services/ItemQuoteService.cs:26-94` | Missing content length validation in backend controllers & services causes unhandled 500 error |
| SPEC-06 | Privacy / Oracle | **Medium** | `SRNSMudApp/Services/ItemSplitService.cs:67-96` | Split request creation acts as an oracle leaking whether substring exists in private items |
| AUTH-01 | Authorization | **Medium** | `SRNSMudApp/Components/UI/ItemCardActionViewModel.cs:101-125`<br>`SRNSMudApp/Components/Item/ItemEditViewModel.cs:70-75` | `UpdateItemContentAsync` in ViewModel layer lacks ownership check |
| TX-01 | Transaction | **Medium** | `SRNSMudApp/Services/ItemSplitService.cs:882-933, 936-985` | Multi-step quote cleanup in split rejection and cancellation lacks `IDbContextTransaction` |
| TX-02 | Transaction | **Medium** | `SRNSMudApp/Services/ItemReplyService.cs:205-311` | `AddItemReplyAsync` calls `SaveChangesAsync` three times sequentially without transaction |
| SYN-01 | Data Sync | **Medium** | `SRNSMudApp/Services/ItemSplitService.cs:514-540, 795-806` | `ItemKindJson` union out-of-sync with relational columns after split migration |
| SPEC-07 | Specification | **Low** | `SRNSMudApp/Services/ItemSplitService.cs:92-96` | Overly conservative replacement length check (22) falsely blocks valid splits |
| CLN-01 | Hygiene | **Low** | `SRNSMudApp/Data/ApplicationDbContext.cs:574-765` | Duplicate configuration block for `ConversationOptOut` repeated 8 times |

---

## Detailed Findings

### 1. Cascade Delete & Data Integrity Failures

#### CAS-01: Standard Item Deletion Crashes on Quoted Items due to Restrict Foreign Key
- **Severity**: **High** (Unhandled crash, denial of deletion)
- **Target Location**:
  - `SRNSMudApp/Services/ItemCardDataProvider.cs:68-100` (`DeleteItemAsync`)
  - `SRNSMudApp/Data/ApplicationDbContext.cs:73-78` (`QuotedItem` relationship)
- **Inconsistency Details**:
  The domain specification allows items to be quoted and later deleted by their owners. However, EF Core configuration defines `DeleteBehavior.Restrict` on `Item.QuotedItemId`:
  ```csharp
  builder.Entity<Item>()
      .HasOne(i => i.QuotedItem)
      .WithMany(i => i.QuotedByItems)
      .HasForeignKey(i => i.QuotedItemId)
      .OnDelete(DeleteBehavior.Restrict);
  ```
  In `ItemCardDataProvider.DeleteItemAsync`, it removes `relatedTaggingRequests` and `replyItems`, but performs **no cleanup or nullification** of `QuotedItemId` on referencing items (`QuotedByItems`).
- **Reproduction/Trigger Scenario**:
  1. User A posts Item 1.
  2. User B posts Item 2 quoting Item 1 (`QuotedItemId = 1`).
  3. User A attempts to delete Item 1 via the UI or `DELETE /api/item/1`.
  4. EF Core executes `DELETE FROM Items WHERE Id = 1`.
  5. SQL Server throws `SqlException`: `The DELETE statement conflicted with the REFERENCE constraint "FK_Items_Items_QuotedItemId"`.
  6. The request terminates with an unhandled 500 error. User A can never delete their own post as long as any user has quoted it.
- **Recommended Remediation**:
  In `ItemCardDataProvider.DeleteItemAsync`, query all quoting items (`context.Items.Where(i => i.QuotedItemId == itemId)`) and set `QuotedItemId = null` (and update `ItemKindJson`), exactly as is done in `ItemSplitService.cs:795-803`. Alternatively, configure `DeleteBehavior.SetNull` on `QuotedItemId`.

---

#### CAS-02: Item Deletion Permanently Blocked by Historical or Pending ItemSplitRequests
- **Severity**: **High** (Permanent foreign key lock, denial of deletion)
- **Target Location**:
  - `SRNSMudApp/Services/ItemCardDataProvider.cs:68-100` (`DeleteItemAsync`)
  - `SRNSMudApp/Data/ApplicationDbContext.cs:661-687` (`ItemSplitRequest.OriginalItem` and `CreatedItem`)
- **Inconsistency Details**:
  In `ApplicationDbContext.cs`, both `ItemSplitRequest.OriginalItemId` and `CreatedItemId` are configured with `DeleteBehavior.Restrict`:
  ```csharp
  builder.Entity<ItemSplitRequest>()
      .HasOne(r => r.OriginalItem)
      .WithMany()
      .HasForeignKey(r => r.OriginalItemId)
      .OnDelete(DeleteBehavior.Restrict);

  builder.Entity<ItemSplitRequest>()
      .HasOne(r => r.CreatedItem)
      .WithMany()
      .HasForeignKey(r => r.CreatedItemId)
      .OnDelete(DeleteBehavior.Restrict);
  ```
  `ItemCardDataProvider.DeleteItemAsync` contains zero logic to handle associated `ItemSplitRequest` records.
- **Reproduction/Trigger Scenario**:
  1. User A posts Item 10.
  2. User B submits a split request for Item 10 (which creates an `ItemSplitRequest`).
  3. Whether the request is Proposed, Rejected, Canceled, or Executed, User A (or an admin) later attempts to delete Item 10.
  4. SQL Server rejects the deletion due to `FK_ItemSplitRequests_Items_OriginalItemId`.
  5. Similarly, if Item 11 was generated via split execution (`CreatedItemId = 11`), Item 11 can never be deleted due to `FK_ItemSplitRequests_Items_CreatedItemId`.
- **Recommended Remediation**:
  In `ItemCardDataProvider.DeleteItemAsync`, handle associated split requests:
  - If pending (`TradeStatus.Proposed`), mark as `TradeStatus.Canceled` or delete them.
  - If historical (`Executed`, `Rejected`), configure `OnDelete(DeleteBehavior.SetNull)` or remove the request history when the principal item is purged.

---

#### CAS-03: Admin Forced Deletion (`DeleteItemByAdminAsync`) Crashes on Any Non-Trivial Item
- **Severity**: **High** (Administrative moderation failure, unhandled crash)
- **Target Location**:
  - `SRNSMudApp/Services/ItemCardDataProvider.cs:103-115` (`DeleteItemByAdminAsync`)
- **Inconsistency Details**:
  `DeleteItemByAdminAsync` is intended as a forced administrative purge for moderated content. However, its implementation is:
  ```csharp
  public async Task<bool> DeleteItemByAdminAsync(int itemId, string adminUserId)
  {
      await using ApplicationDbContext context = await _dbFactory.CreateDbContextAsync();
      Item? item = await context.Items.FindAsync(itemId);
      if (item is null) return false;

      _ = context.Items.Remove(item);
      _ = await context.SaveChangesAsync();
      return true;
  }
  ```
  It does not delete child replies (`ParentItemId`), does not delete `TaggingRequestEntities`, does not clear quotes, and does not clean up `ItemSplitRequests`.
- **Reproduction/Trigger Scenario**:
  1. A malicious user posts Item 50.
  2. Another user replies to Item 50 (creating Item 51 with `ParentItemId = 50`).
  3. An Admin clicks "【管理者】強制削除" on Item 50.
  4. `DeleteItemByAdminAsync` directly calls `context.Items.Remove(item)`.
  5. `FK_Items_Items_ParentItemId` (`DeleteBehavior.Restrict`) throws a database constraint exception.
  6. The admin operation fails, leaving the offending content intact.
- **Recommended Remediation**:
  Delegate `DeleteItemByAdminAsync` to a shared, robust `PurgeItemGraphAsync` method that cascades replies, tagging requests, quotes, split requests, and tag comments within a database transaction.

---

#### CAS-04: Deleting TagCommentItem Fails or Leaves Orphan Items
- **Severity**: **High** (Database constraint failure, orphan data accumulation)
- **Target Location**:
  - `SRNSMudApp/Services/ItemReactionService.cs:126-173` (`ApplyReactionChangeAsync`)
  - `SRNSMudApp/Services/ItemTagService.cs:134-165` (`ExecuteRemoveTagRelationAsync`)
  - `SRNSMudApp/Data/ApplicationDbContext.cs:251-256, 321-326`
- **Inconsistency Details**:
  When comments are attached to tag votes or relations, a `TagCommentItem` entity is created in `Items`.
  - In `ItemReactionService.ApplyReactionChangeAsync`: when a comment is cleared (lines 130-135), it removes `existingRelation.CommentItem`, but does NOT clear `TagWeightLedger.CommentItemId`. Since `TagWeightLedger.CommentItem` is `DeleteBehavior.NoAction`, SQL Server throws an FK violation if the ledger references that comment.
  - In `ItemTagService.ExecuteRemoveTagRelationAsync`: when a `TagRelation` is removed, it deletes `TagRelation` but completely ignores `currentRelation.CommentItem`. The comment item remains permanently in the `Items` table as an unreferenced orphan.
- **Reproduction/Trigger Scenario**:
  1. User votes on a tag and enters a comment, creating `TagCommentItem` and a `TagWeightLedger` pointing to it.
  2. User edits the reaction to remove the comment.
  3. `ItemReactionService` attempts to remove `CommentItem`, failing with an FK conflict on `TagWeightLedgers.CommentItemId`.
- **Recommended Remediation**:
  Always nullify `TagWeightLedger.CommentItemId` and `TagRelation.CommentItemId` before removing comment items, and clean up orphan comment items when `TagRelation` is deleted in `ItemTagService`.

---

#### CAS-05: Cascade Deletion of Item Leaves Orphaned TimelineEvents & Distorts Timeline Pagination
- **Severity**: **High** (Feed degradation, empty pages, inaccurate counters)
- **Target Location**:
  - `SRNSMudApp/Services/ItemCardDataProvider.cs:68-100` (`DeleteItemAsync`)
  - `SRNSMudApp/Services/HomeDataProvider.cs:165-237` (`LoadTimelineAsync`)
  - `SRNSMudApp/Data/TimelineEvent.cs:13-27`
- **Inconsistency Details**:
  `TimelineEvent` stores its target as serialized JSON: `{"$type":"ItemTarget","TargetItemId":X}`. Because it is not a relational foreign key, deleting an `Item` leaves all associated `TimelineEvents` orphaned in the database.
  `HomeDataProvider` paginates the timeline by grouping `TimelineEvents` by `TimelineTargetJson`:
  ```csharp
  var totalGroups = await groupedQuery.CountAsync(cancellationToken);
  var pagedGroups = await groupedQuery.Skip(startIndex).Take(count).ToListAsync(cancellationToken);
  ...
  if (feedGroup.Item != null || feedGroup.Tag != null)
  {
      feedGroups.Add(feedGroup);
  }
  return new HomeTimelinePage(feedGroups, totalGroups);
  ```
- **Reproduction/Trigger Scenario**:
  1. 50 items are created and tagged, generating 50 `TimelineEvents`.
  2. 20 of those items are deleted.
  3. A user views the Home timeline with page size 20.
  4. The first page queries the top 20 timeline groups. If 15 of them correspond to deleted items, `feedGroup.Item` evaluates to `null`.
  5. The user is served only 5 items on page 1, while `totalGroups` still reports 50. Page 2 may display 0 items.
- **Recommended Remediation**:
  In `ItemCardDataProvider.DeleteItemAsync` (and admin delete), delete all `TimelineEvents` matching `$"\"TargetItemId\":{itemId}"`.

---

#### CAS-06: Deleting Item Cascade-Deletes TagRelations Without Decrementing CachedWeight or Writing Ledger
- **Severity**: **High** (Data divergence, permanent weight inflation)
- **Target Location**:
  - `SRNSMudApp/Services/ItemCardDataProvider.cs:68-100` (`DeleteItemAsync`)
  - `SRNSMudApp/Data/ApplicationDbContext.cs:236-247`
- **Inconsistency Details**:
  When an `Item` is removed, EF Core cascades deletion to all `TagRelation`s attached to that item.
  However, `Tag.CachedWeight` is not decremented, and no record is added to `TagWeightLedger`.
- **Reproduction/Trigger Scenario**:
  1. Tag "Rust" has `CachedWeight = 10`.
  2. Item A has a "Rust" tag relation with weight 5.
  3. Item A is deleted.
  4. The `TagRelation` is removed from the database.
  5. Tag "Rust" still retains `CachedWeight = 10`, but the sum of active relations is now only 5.
  6. The tag's cached weight is permanently inflated.
- **Recommended Remediation**:
  Before removing the item, iterate over its `TagRelations`, decrement each `Tag.CachedWeight`, and record `TagRelationDelete` in `TagWeightLedger` (or invoke `ItemTagService.RemoveTagRelationAsync`).

---

### 2. Authorization, Privacy & Information Disclosure Flaws

#### SEC-01: Anonymous Information Disclosure of Private & Admin-Hidden Items via Quote API
- **Severity**: **High** (Authorization bypass, privacy leak)
- **Target Location**:
  - `SRNSMudApp/Controllers/ItemController.cs:443-451` (`GetQuotes`)
  - `SRNSMudApp/Services/ItemQuoteService.cs:97-118` (`GetQuotedByItemsAsync`)
- **Inconsistency Details**:
  `ItemDetailDataProvider` correctly uses `.WhereVisibleToUser(context, currentUserId, isAdmin)` when fetching quotes for the Blazor UI.
  However, the public Web API endpoint `GET /api/item/{id}/quotes` delegates to `ItemQuoteService.GetQuotedByItemsAsync`, which executes:
  ```csharp
  return await context.Items
      .AsNoTracking()
      .Include(i => i.Owner)
      .Include(i => i.TagRelations)...
      .Where(i => i.QuotedItemId == quotedItemId)
      .ToListAsync(cancellationToken);
  ```
  It performs **no visibility filtering** whatsoever.
- **Reproduction/Trigger Scenario**:
  1. User A posts a public Item 100.
  2. User B creates a private/secret group Item 200 quoting Item 100 (`IsPrivate = true`, `TargetUserGroupId = 99`).
  3. An anonymous, unauthenticated user calls `GET /api/item/100/quotes`.
  4. The response includes Item 200 with full text, author details, and metadata.
- **Recommended Remediation**:
  Inject `IHttpContextAccessor` or pass `ClaimsPrincipal` to `ItemController.GetQuotes`, extract `currentUserId` and `isAdmin`, and apply `WhereVisibleToUser` in `ItemQuoteService.GetQuotedByItemsAsync`.

---

#### SEC-02: Anonymous Information Disclosure of Private & Group-Restricted Replies via Reply API
- **Severity**: **High** (Authorization bypass, privacy leak)
- **Target Location**:
  - `SRNSMudApp/Controllers/ItemController.cs:385-393` (`GetReplies`)
  - `SRNSMudApp/Services/ItemReplyService.cs:40-153` (`GetItemRepliesAsync`)
- **Inconsistency Details**:
  Similar to SEC-01, `GET /api/item/{id}/replies` calls `ItemReplyService.GetItemRepliesAsync(id)`, which loads all regular replies and ledger comments without applying `WhereVisibleToUser`.
- **Reproduction/Trigger Scenario**:
  1. A private reply is made within a confidential UserGroup on Item 300.
  2. An unauthorized user calls `GET /api/item/300/replies`.
  3. The endpoint returns the private reply payload to the unauthorized caller.
- **Recommended Remediation**:
  Accept `currentUserId` and `isAdmin` in `GetItemRepliesAsync` and filter replies through `WhereVisibleToUser(context, currentUserId, isAdmin)`.

---

#### SEC-03: Unauthenticated Public Endpoint Leaks SelectedText of Private Items
- **Severity**: **High** (Data leak, missing [Authorize])
- **Target Location**:
  - `SRNSMudApp/Controllers/ItemSplitController.cs:107-117` (`GetPendingSplitRequests`)
  - `SRNSMudApp/Services/ItemSplitService.cs:988-1001` (`GetPendingSplitRequestsForOriginalItemAsync`)
- **Inconsistency Details**:
  The endpoint `GET /api/item-split/item/{itemId}/pending` lacks `[Authorize]` and does not verify whether `itemId` is visible to the caller:
  ```csharp
  [HttpGet("item/{itemId:int}/pending")]
  public async Task<ActionResult<IReadOnlyList<ItemSplitRequestDto>>> GetPendingSplitRequests(...)
  ```
  It returns all pending `ItemSplitRequest` records, each containing `SelectedText` (verbatim excerpt from the original item).
- **Reproduction/Trigger Scenario**:
  1. User A has a private Item 400 accessible only to User A.
  2. User A (or a collaborator) requested a split, creating a pending request with `SelectedText = "Secret financial figures"`.
  3. An anonymous user calls `GET /api/item-split/item/400/pending`.
  4. The confidential `SelectedText` is returned.
- **Recommended Remediation**:
  Add `[Authorize]` and verify that the caller has permission to view the item (or is the owner) using `item.IsItemVisibleToUserAsync`.

---

#### SEC-04: QuotedItemPreview Bypasses Visibility Checks & Renders Admin-Hidden / Quarantined Content
- **Severity**: **High** (Quarantine bypass, content policy violation)
- **Target Location**:
  - `SRNSMudApp/Components/UI/QuotedItemPreviewViewModel.cs:57-69`
  - `SRNSMudApp/Services/ItemQuoteService.cs:135-152` (`GetQuotedItemAsync`)
- **Inconsistency Details**:
  When rendering a quote preview, `QuotedItemPreviewViewModel` invokes `_itemQuoteService.GetQuotedItemAsync(quotedItemId)`.
  This service method loads the item by ID with `AsNoTracking()` without checking `IsAdminHidden` or `IsPrivate`.
- **Reproduction/Trigger Scenario**:
  1. User A posts defamatory content in Item 500.
  2. User B quotes Item 500 in Item 501.
  3. An Admin marks Item 500 as hidden (`IsAdminHidden = true`).
  4. Any user viewing Item 501 triggers `QuotedItemPreviewViewModel`, which fetches Item 500 and renders its defamatory text inside the quote box.
- **Recommended Remediation**:
  In `GetQuotedItemAsync`, verify `!item.IsAdminHidden` and check visibility against `currentUserId`. If not visible, return `null` so the UI displays: "引用元のアイテムは削除されたか、表示できません。".

---

#### SEC-05: Private Reply Notifications Leak Full Text to Users Outside TargetUserGroupId
- **Severity**: **High** (Notification data leak)
- **Target Location**:
  - `SRNSMudApp/Services/ItemReplyService.cs:239-285` (`AddItemReplyAsync`)
- **Inconsistency Details**:
  When a reply is posted, `recipientIds` are automatically resolved or accepted from `targetUserIds`.
  `AddItemReplyAsync` immediately calls:
  ```csharp
  foreach (string rid in recipientIds)
  {
      _ = _notificationService.NotifyUserAsync(
          rid,
          "新しい返信",
          replyItem.Content,
          $"/items/{parentItemId}");
  }
  ```
  If `replyItem.IsPrivate == true` and `replyItem.TargetUserGroupId != null`, it does NOT verify whether `rid` is a member of that group.
- **Reproduction/Trigger Scenario**:
  1. A thread started as public, involving User X.
  2. Later, User Y posts a private reply restricted to Group A (`TargetUserGroupId = GroupA`).
  3. User X is not a member of Group A.
  4. `AddItemReplyAsync` sends a notification to User X containing the full secret content of `replyItem.Content`.
- **Recommended Remediation**:
  Filter `recipientIds` against the group membership if `isPrivate && targetUserGroupId.HasValue`.

---

#### AUTH-01: UpdateItemContentAsync in ViewModel Layer Lacks Ownership Check
- **Severity**: **Medium** (UI layer authorization gap)
- **Target Location**:
  - `SRNSMudApp/Components/UI/ItemCardActionViewModel.cs:101-125` (`UpdateItemContentAsync`)
  - `SRNSMudApp/Components/Item/ItemEditViewModel.cs:70-75` (`SaveAsync`)
- **Inconsistency Details**:
  `ItemController.UpdateItem` validates `item.OwnerId == currentUserId || isAdmin`.
  However, in Blazor Server, `ItemEditViewModel.SaveAsync` calls `_actionViewModel.UpdateItemContentAsync(Item.Id, EditContent)`.
  Neither `ItemEditViewModel` nor `ItemCardActionViewModel` checks the logged-in user's identity or verifies ownership against `Item.OwnerId`.
- **Reproduction/Trigger Scenario**:
  If `ItemEditDialog` is displayed via direct deep-link, browser console injection, or state manipulation with another user's item, saving the dialog updates that other user's post directly in the database.
- **Recommended Remediation**:
  Pass `currentUserId` and verify `item.OwnerId == currentUserId || isAdmin` in `ItemCardActionViewModel.UpdateItemContentAsync`.

---

### 3. Domain Specification Inconsistencies & Business Rule Violations

#### SPEC-01: Conversation Opt-Out is Completely Ignored at Notification Dispatch Time
- **Severity**: **Medium** (Feature non-functional, notification spam)
- **Target Location**:
  - `SRNSMudApp/Services/ItemReplyService.cs:239-285` (`AddItemReplyAsync`)
  - `SRNSMudApp/Services/ItemReplyService.cs:313-353` (`ToggleConversationOptOutAsync`)
- **Inconsistency Details**:
  The application maintains a dedicated `ConversationOptOuts` table and methods (`ToggleConversationOptOutAsync`, `IsUserOptedOutAsync`, `GetOptedOutUsersAsync`).
  However, inside `AddItemReplyAsync` (where reply notifications are generated), `ConversationOptOuts` is never queried!
- **Reproduction/Trigger Scenario**:
  1. User A participates in a thread and later clicks "会話から離脱 / オプトアウト".
  2. `ConversationOptOuts` records `UserId = UserA, RootItemId = RootId`.
  3. Other users reply to the thread.
  4. User A continues to receive notifications for every single reply.
- **Recommended Remediation**:
  In `AddItemReplyAsync`, query `ConversationOptOuts` for `parentRootId` and filter out opted-out users from `recipientIds`:
  ```csharp
  var optedOutUsers = await context.ConversationOptOuts
      .Where(o => o.RootItemId == (parentRootId ?? parentItemId))
      .Select(o => o.UserId)
      .ToListAsync();
  recipientIds = recipientIds.Except(optedOutUsers).ToList();
  ```

---

#### SPEC-02: RightAsset Requirement Contradiction Across Tag Operations
- **Severity**: **Medium** (Domain model inconsistency)
- **Target Location**:
  - `SRNSMudApp/Data/ApplicationDbContextTagExtensions.cs:82-145` (`CreateFreeTagRelationAsync`)
  - `SRNSMudApp/Services/ItemCardDataProvider.cs:164-227` (`CreateItemAsync`)
  - `SRNSMudApp/Services/ItemQuoteService.cs:70-84` (`CreateQuoteItemAsync`)
  - `SRNSMudApp/Services/ItemReplyService.cs:287-303` (`AddItemReplyAsync`)
- **Inconsistency Details**:
  The core architecture of SRNS (Sub-Right Network System) dictates that attaching tags consumes/burns `RightAsset`s.
  - `CreateFreeTagRelationAsync` strictly issues, burns, and links a `RightAsset` (`ConsumedRightAssetId`).
  - However, `CreateItemAsync` (initial tags), `CreateQuoteItemAsync` (initial tags), and `AddItemReplyAsync` (inherited tags) create `TagRelation`s directly without `RightAsset` consumption.
  - Furthermore, `CreateQuoteItemAsync` and `AddItemReplyAsync` do not even increment `Tag.CachedWeight` or record `TagWeightLedger`.
- **Recommended Remediation**:
  Harmonize tag attachment logic: unify all tag-attaching pathways to use a single domain service that issues/burns `RightAsset` and updates `CachedWeight` and `TagWeightLedger` consistently.

---

#### SPEC-03: Lack of Quote Content Snapshotting Allows Deceptive Retrospective Modification
- **Severity**: **Medium** (Integrity flaw, quote attribution fraud)
- **Target Location**:
  - `SRNSMudApp/Services/ItemQuoteService.cs:56-66`
  - `SRNSMudApp/Components/UI/QuotedItemPreviewViewModel.cs:72-78`
- **Inconsistency Details**:
  When a quote item is created, only `QuotedItemId` is persisted.
  When rendering the quote preview, `QuotedItemPreview` fetches the original item's current `Content`.
  If the original author subsequently edits their item, the quote reflects the new text without any historical record, snapshot, or indication of edit.
- **Reproduction/Trigger Scenario**:
  1. Author A writes: "Option 1 is best".
  2. User B quotes Author A saying: "Totally agree with this!".
  3. Author A edits the post to: "Option 1 is illegal and harmful".
  4. User B's quote now appears to say "Totally agree with this!" to the harmful claim.
- **Recommended Remediation**:
  Store a `QuotedContentSnapshot` string in `Item` (or in `QuoteItem` union payload) at the time of quotation, and display a diff/warning if `QuotedItem.UpdatedDate > QuoteItem.CreatedDate`.

---

#### SPEC-04: First-Match IndexOf Bug in Split Selection on Repetitive Words
- **Severity**: **Medium** (Logic bug)
- **Target Location**:
  - `SRNSMudApp/Services/ItemSplitService.cs:808-829` (`FindSelectedText`)
- **Inconsistency Details**:
  `ItemSplitRequest` stores only `SelectedText`. To locate the text for replacement, `FindSelectedText` uses:
  ```csharp
  int index = content.IndexOf(candidate, StringComparison.Ordinal);
  ```
  `IndexOf` always matches the **first occurrence** in the text.
- **Reproduction/Trigger Scenario**:
  1. Item content is: "First section: details. Second section: details."
  2. User highlights the second "details" and requests a split.
  3. When approved, the service replaces the **first** "details" instead of the second.
- **Recommended Remediation**:
  Record `SelectionStartIndex` and `SelectionLength` (or surrounding context snippet) in `ItemSplitRequest` to ensure exact positioning.

---

#### SPEC-05: Missing Content Length Validation in Backend Controllers & Services
- **Severity**: **Medium** (Unhandled 500 error)
- **Target Location**:
  - `SRNSMudApp/Controllers/ItemController.cs:349-380, 456-492`
  - `SRNSMudApp/Services/ItemQuoteService.cs:26-94`
  - `SRNSMudApp/Services/ItemReplyService.cs:205-238`
- **Inconsistency Details**:
  `Item.Content` is constrained to 1000 characters in the database schema (`[StringLength(1000)]`).
  `ItemEditViewModel` validates `Content.Length <= 1000`.
  However, `CreateItemRequest`, `UpdateItemRequest`, `CreateItem`, `UpdateItem`, `AddReply`, `CreateQuoteItemAsync`, and `AddItemReplyAsync` perform no length check.
- **Reproduction/Trigger Scenario**:
  Submitting a post with 1001 characters via API returns a 500 Internal Server Error due to `DbUpdateException`.
- **Recommended Remediation**:
  Add `[StringLength(1000)]` on `CreateItemRequest`, `UpdateItemRequest`, `CreateReplyRequest`, and validate lengths in service entry points.

---

#### SPEC-06: Split Request Creation Oracle Attack on Private Items
- **Severity**: **Medium** (Side-channel information leak)
- **Target Location**:
  - `SRNSMudApp/Services/ItemSplitService.cs:67-96` (`RequestSplitAsync`)
- **Inconsistency Details**:
  `RequestSplitAsync` loads `originalItem` with `AsNoTracking()` without checking if the requester has permission to view the item.
  It then tests whether `selectedText` exists in `originalItem.Content`.
  If it does not exist, it returns `SplitSelectedTextNotInContent`. If it exists, it returns `Ok`.
- **Reproduction/Trigger Scenario**:
  An unauthorized user guesses words in a confidential item. By observing whether the API returns `SplitSelectedTextNotInContent` vs `Success`, the attacker reconstructs the private content.
- **Recommended Remediation**:
  Check `await originalItem.IsItemVisibleToUserAsync(context, requesterUserId)` before validating `selectedText`. If not visible, return `SplitItemNotFound`.

---

### 4. Transaction Rollback, Concurrency & Entity Synchronization Gaps

#### CON-01: Concurrency Race Between Split Approval and Rejection/Cancellation
- **Severity**: **High** (Race condition, state divergence)
- **Target Location**:
  - `SRNSMudApp/Services/ItemSplitService.cs:235-327` (`ApproveSplitAsync`)
  - `SRNSMudApp/Services/ItemSplitService.cs:882-985` (`RejectSplitAsync`, `CancelSplitAsync`)
- **Inconsistency Details**:
  `ItemSplitRequest` has no concurrency token (`RowVersion` or `[Timestamp]`).
  In `ApproveSplitAsync`, `RejectSplitAsync`, and `CancelSplitAsync`, the status check:
  `if (request.Status != TradeStatus.Proposed)`
  occurs under Read Committed isolation without locking.
- **Reproduction/Trigger Scenario**:
  1. Owner clicks "Approve", while Requester concurrently clicks "Cancel".
  2. Both requests read `Status = Proposed`.
  3. `CancelSplitAsync` sets `Status = Canceled` and deletes the quote item via `DeleteRequesterQuoteItemAsync`.
  4. `ApproveSplitAsync` continues, creates `newItem`, modifies `originalItem.Content`, and tries to migrate the deleted quote item.
  5. The system ends up with an `Executed` request pointing to a deleted quote and partial edits, or fails with a concurrency exception.
- **Recommended Remediation**:
  Add a `byte[] RowVersion` concurrency token to `ItemSplitRequest` (or `BaseEntity`), and wrap the status check and execution inside an atomic transaction.

---

#### TX-01: Multi-Step Quote Cleanup in Split Rejection and Cancellation Lacks IDbContextTransaction
- **Severity**: **Medium** (Partial persistence on failure)
- **Target Location**:
  - `SRNSMudApp/Services/ItemSplitService.cs:882-933` (`RejectSplitAsync`)
  - `SRNSMudApp/Services/ItemSplitService.cs:936-985` (`CancelSplitAsync`)
- **Inconsistency Details**:
  While `SplitDirectlyAsync` and `ApproveSplitAsync` use `await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync()`, `RejectSplitAsync` and `CancelSplitAsync` do not use an explicit transaction when deleting the associated quote item, its tagging requests, and replies.
- **Recommended Remediation**:
  Wrap `RejectSplitAsync` and `CancelSplitAsync` in `BeginTransactionAsync`.

---

#### TX-02: AddItemReplyAsync Calls SaveChangesAsync Three Times Sequentially Without Transaction
- **Severity**: **Medium** (Partial state persistence)
- **Target Location**:
  - `SRNSMudApp/Services/ItemReplyService.cs:205-311` (`AddItemReplyAsync`)
- **Inconsistency Details**:
  `AddItemReplyAsync` executes three separate `SaveChangesAsync` calls:
  1. Line 237: saves `replyItem`.
  2. Line 272: saves `ItemReplyNotificationRecipient`s.
  3. Line 302: saves inherited `TagRelation`s.
  If an exception occurs at step 2 or 3, the reply is persisted without recipients or without tags.
- **Recommended Remediation**:
  Perform all entity additions within a single unit-of-work and call `SaveChangesAsync` once, or wrap in a transaction.

---

#### SYN-01: ItemKindJson Out of Sync After Split Migration & Quote Dereferencing
- **Severity**: **Medium** (Data model inconsistency)
- **Target Location**:
  - `SRNSMudApp/Services/ItemSplitService.cs:514-540` (`MigrateRepliesAndDescendantsAsync`)
  - `SRNSMudApp/Services/ItemSplitService.cs:795-806` (`DeleteRequesterQuoteItemAsync`)
- **Inconsistency Details**:
  - In `MigrateRepliesAndDescendantsAsync`: `replyItem.ParentItemId = targetItem.Id` is updated, but `replyItem.ItemKindJson` is left untouched, still containing `{"ParentItemId": <oldQuoteItemId>}`.
  - In `DeleteRequesterQuoteItemAsync`: quoting items have `q.QuotedItemId = null`, but `q.ItemKindJson` still contains `{"QuotedItemId": <deletedQuoteItemId>}`.
- **Recommended Remediation**:
  Update `ItemKindJson` whenever `ParentItemId` or `QuotedItemId` is modified.

---

#### CLN-01: Duplicate Configuration Block for ConversationOptOut in ApplicationDbContext
- **Severity**: **Low** (Code hygiene)
- **Target Location**:
  - `SRNSMudApp/Data/ApplicationDbContext.cs:574-765`
- **Inconsistency Details**:
  The exact same 15-line fluent configuration block for `ConversationOptOut` is copy-pasted **8 times** consecutively throughout lines 574–765 of `ApplicationDbContext.cs`.
- **Recommended Remediation**:
  Remove the duplicate blocks and consolidate into a single configuration block.
