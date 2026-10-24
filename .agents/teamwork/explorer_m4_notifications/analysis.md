# Domain 4 Investigation Report: Notifications and Timeline Events

## Executive Summary

An exhaustive audit of Domain 4 (Notifications & Timeline Events) across the SRNSWebApp codebase revealed critical architectural, transactional, security, and state-consistency issues. 
In SRNSWebApp, notifications are not stored as standalone records in a dedicated `Notification` table; instead, they are dynamically aggregated from 8 different database tables (`TaggingRequestEntities`, `Items`, `ContentReports`, `ItemSplitRequests`, `TagContentProposals`, `TagNameProposals`, `TagRelations`, `RightAssets`) and coupled with a separate `NotificationReadState` table and WebPush background delivery.

This hybrid architecture suffers from several high-impact flaws:
1. **Critical Transaction / Error Handling Defect**: In `ItemSplitService.ApproveSplitAsync`, external WebPush dispatch (`NotifyUserAsync`) is invoked inside the same `try` block after `transaction.CommitAsync()`. If WebPush fails, the `catch` block invokes `RollbackAsync()` on an already committed transaction, throwing an `InvalidOperationException` that crashes the request and reports failure to the user despite data modifications having already succeeded.
2. **Opt-Out Disconnect**: `ConversationOptOut` (thread notification mute) exists in the database and UI, but is completely bypassed by `ItemReplyService.AddItemReplyAsync` and `NotificationsDataProvider.cs`. Muted users still receive push notifications and inbox entries.
3. **Private Item & Authorization Leaks**: Private replies and admin-hidden items (`IsAdminHidden`) are queried by `NotificationsDataProvider` without `WhereVisibleToUser` authorization filters, rendering full item content via `ItemCard` on unauthorized recipients' `/notifications` page. In addition, WebPush payloads leak private group content to non-group participants.
4. **Zombie Notifications & Missing Event Handlers**: Canceled/withdrawn contracts, proposals, and splits remain stuck in owners' notification inboxes as active incoming requests (`"リクエストが届いています"`) without action buttons, or result in broken dead-end push notifications.
5. **Timeline Event Type Corruption**: `GratisContractExecutor` records tag removals as `TimelineEventType.Update` instead of `Delete` with `NewWeight = 1` and `PreviousWeight = 0`, corrupting client timeline interpretations and chip UI states.
6. **Cross-Circuit Real-Time Failure**: `NotificationService` is registered as a Scoped service, meaning its in-memory `NotificationsChanged` event only fires in the acting user's Blazor circuit and never reaches the recipient's circuit.

---

## Detailed Findings & Inconsistency Matrix

| ID | Issue | Target Location | Severity | Category |
|---|---|---|---|---|
| **D4-01** | Post-Commit `RollbackAsync()` Crash on WebPush Failure | `ItemSplitService.cs:306-326` | **High** | Transaction / Reliability |
| **D4-02** | Conversation Opt-Out (`ConversationOptOut`) Completely Ignored | `ItemReplyService.cs:240-285`<br>`NotificationsDataProvider.cs:90-101` | **High** | Recipient Mismatch |
| **D4-03** | Private Item & Group Content Leaked via WebPush & `NotificationsDataProvider` | `ItemReplyService.cs:240-285`<br>`NotificationsDataProvider.cs:50-65, 90-101`<br>`NotificationsViewModel.cs:112-123` | **High** | Security / Privacy Leak |
| **D4-04** | Canceled/Withdrawn Requests & Proposals Cause Zombie Notifications and Dead Push Links | `TaggingContractService.cs:351-386`<br>`ItemSplitService.cs:936-985`<br>`NotificationsDataProvider.cs:80-87, 142, 156-160`<br>`NotificationService.cs:110-135, 367-402` | **Medium** | Missing Triggers / State Sync |
| **D4-05** | `GratisContractExecutor` Records Tag Deletion as `Update` with Inverted Weights | `GratisContractExecutor.cs:272-279` | **Medium** | TimelineEventType Misalignment |
| **D4-06** | Scoped `INotificationService` Cannot Broadcast Real-Time Events Across Blazor Circuits | `NotificationService.cs:56-63`<br>`ServiceCollectionExtensions.cs:141`<br>`NotificationBadgeViewModel.cs:23` | **Medium** | Architecture / Real-Time Failure |
| **D4-07** | Orphaned `TimelineEvents` on Item Deletion Break Home Timeline Pagination | `TimelineEvent.cs:14-26`<br>`ItemCardDataProvider.cs:80-115`<br>`HomeDataProvider.cs:136-240` | **Medium** | Data Integrity & Feed Pagination |
| **D4-08** | Complete Omission of Notifications and Timeline Events for Item Quotes | `ItemQuoteService.cs:26-94`<br>`NotificationType.cs` | **Medium** | Missing Domain Event Trigger |
| **D4-09** | Tag Relation Comment Notification Uses Stale `TagRelation.CreatedDate` Timestamp | `NotificationService.cs:542`<br>`NotificationsDataProvider.cs:189-196` | **Low** | Sorting / UX Bug |
| **D4-10** | WASM Client `SourceType` String Mismatch Breaks Notification Icons | `NotificationPage.razor:204-213`<br>`NotificationType.cs:158-159` | **Low** | WASM Client Inconsistency |
| **D4-11** | TagEdge Attachments and Move Contracts Omit `TimelineEvents` | `GratisContractExecutor.cs:102-184`<br>`MoveContractExecutor.cs:17-77` | **Low** | Feature Asymmetry |
| **D4-12** | ContentReport Resolution Leaks Administrator Internal Notes to Reporters | `NotificationService.cs:255-260`<br>`ContentReportCommands.cs:80-120` | **Low** | Information Disclosure |
| **D4-13** | Tag Permission Request Submission Omits WebPush Notification | `RightAssetDataProvider.cs:359` | **Low** | Push Notification Gap |

---

## In-Depth Analysis of Findings

### D4-01: Post-Commit `RollbackAsync()` Crash on WebPush Failure
- **Severity**: **High**
- **Location**: `SRNSMudApp/Services/ItemSplitService.cs:306-326`
- **Expected Specification**: External side-effects (push notifications, telemetry) must be completely decoupled from the transactional boundary. If notification delivery fails, the underlying database transaction must remain valid and committed, and the error should be logged as a non-fatal warning without re-throwing or crashing the main business flow.
- **Actual Implementation**:
  ```csharp
  // ItemSplitService.cs:306-326
  _ = await context.SaveChangesAsync(cancellationToken);
  await transaction.CommitAsync(cancellationToken);

  _notificationService.NotifyNotificationsChanged();
  LogSplitApproved(_logger, splitRequestId, request.OriginalItemId, newItem.Id, ownerUserId, quoteItemId);

  // 分割リクエストの申請者に承認を通知
  await _notificationService.NotifyUserAsync(
      request.RequesterUserId,
      "分割リクエストが承認されました",
      "あなたの分割リクエストが承認され、新しいアイテムが作成されました。",
      "/notifications", cancellationToken);

  return Result.Ok(newItem);
  }
  catch
  {
      await transaction.RollbackAsync(cancellationToken);
      throw;
  }
  ```
  `transaction.CommitAsync()` is executed at line 307. Then `NotifyUserAsync()` is awaited inside the same `try` block. If `NotifyUserAsync()` throws an exception (e.g. push gateway timeout, network interruption, or client cancellation), the `catch` block attempts `await transaction.RollbackAsync(cancellationToken)`. Calling rollback on a completed transaction throws `System.InvalidOperationException: This DbTransaction has completed; it is no longer usable.`
- **Reproduction Scenario**:
  1. User B requests a split of User A's item.
  2. User A clicks "承認" (Approve).
  3. The database updates the request status to `TradeStatus.Executed`, creates the new item, deletes the quote item, and successfully commits the transaction.
  4. During WebPush delivery to User B, an ephemeral socket error or cancellation occurs.
  5. The `catch` block attempts `RollbackAsync()`, crashing with `InvalidOperationException`.
  6. User A receives an unhandled exception / error dialog indicating that the split operation failed, even though the split permanently took effect in the database.
- **Recommended Remediation**:
  Commit the transaction and close the transaction scope first. Move `NotifyNotificationsChanged()` and `NotifyUserAsync()` outside the transaction `try-catch`, wrapped in a dedicated safe-execution block:
  ```csharp
  await context.SaveChangesAsync(cancellationToken);
  await transaction.CommitAsync(cancellationToken);

  try
  {
      _notificationService.NotifyNotificationsChanged();
      await _notificationService.NotifyUserAsync(
          request.RequesterUserId,
          "分割リクエストが承認されました",
          "あなたの分割リクエストが承認され、新しいアイテムが作成されました。",
          "/notifications", CancellationToken.None);
  }
  catch (Exception ex)
  {
      _logger.LogWarning(ex, "Failed to deliver split approval notification to {UserId}", request.RequesterUserId);
  }

  return Result.Ok(newItem);
  ```

---

### D4-02: Conversation Opt-Out (`ConversationOptOut`) Completely Ignored During Item Reply Notifications
- **Severity**: **High**
- **Location**:
  - `SRNSMudApp/Services/ItemReplyService.cs:240-285`
  - `SRNSMudApp/Services/NotificationsDataProvider.cs:90-101`
- **Expected Specification**: When a user opts out of a conversation (`ToggleConversationOptOutAsync`), they must no longer receive notification alerts (neither WebPush nor `/notifications` inbox entries) for any replies posted to that conversation tree (`RootItemId`).
- **Actual Implementation**:
  In `ItemReplyService.AddItemReplyAsync` (lines 247-261):
  ```csharp
  // 省略時はスレッド参加者（親アイテムオーナー + 既存リプライ投稿者、自分を除く）を自動対象とする
  var parentOwnerId = await context.Items
      .Where(i => i.Id == parentItemId)
      .Select(i => i.OwnerId)
      .FirstOrDefaultAsync();

  var replierIds = await context.Items
      .Where(i => i.ParentItemId == parentItemId)
      .Select(i => i.OwnerId)
      .ToListAsync();

  recipientIds = [.. (new[] { parentOwnerId }.Concat(replierIds))
      .Where(id => !string.IsNullOrEmpty(id) && id != userId)
      .Distinct()!];
  ```
  `ConversationOptOuts` is never queried! Every thread participant is saved as an `ItemReplyNotificationRecipient` and receives WebPush. Furthermore, in `NotificationsDataProvider.GetNotificationRawDataAsync` (line 96):
  ```csharp
  i.NotificationRecipients.Any(r => r.RecipientUserId == userId)
  ```
  This unconditionally returns all replies where the user was designated as a recipient, completely ignoring `ConversationOptOuts`.
- **Reproduction Scenario**:
  1. User A replies to Item 100 (`RootItemId = 100`).
  2. Because the thread is active, User A calls `ToggleConversationOptOutAsync(100, "userA")`. An entry is saved in `ConversationOptOuts`.
  3. User B posts a reply to Item 100.
  4. User A receives a push notification and sees the reply in `/notifications`. Opting out had zero effect.
- **Recommended Remediation**:
  1. In `ItemReplyService.AddItemReplyAsync`, query `context.ConversationOptOuts.Where(o => o.RootItemId == (parentRootId ?? parentItemId)).Select(o => o.UserId).ToListAsync()`, and exclude opted-out user IDs from `recipientIds`.
  2. In `NotificationsDataProvider.GetNotificationRawDataAsync`, add `!context.ConversationOptOuts.Any(o => o.RootItemId == (i.RootItemId ?? i.ParentItemId) && o.UserId == userId)` to the query.

---

### D4-03: Private Item & Group Content Leaked via WebPush & `NotificationsDataProvider`
- **Severity**: **High**
- **Location**:
  - `SRNSMudApp/Services/ItemReplyService.cs:240-285`
  - `SRNSMudApp/Services/NotificationsDataProvider.cs:50-65, 90-101`
  - `SRNSMudApp/Components/Pages/NotificationsViewModel.cs:112-123`
- **Expected Specification**: When an item or reply is marked private (`IsPrivate = true`) or restricted to a specific group (`TargetUserGroupId != null`), non-members must never receive its raw text content or view it in notifications. In addition, items that have been taken down by administrators (`IsAdminHidden = true`) must not be accessible via notification cards.
- **Actual Implementation**:
  1. `ItemReplyService.AddItemReplyAsync` (lines 278-283) sends raw `replyItem.Content` via WebPush to all recipients without verifying if each recipient is a member of `targetUserGroupId`. If `targetUserIds` was omitted, all earlier thread participants (including non-group members) receive the private group content.
  2. `NotificationsDataProvider.GetAssociatedItemsAsync` (lines 50-65) executes:
     ```csharp
     context.Items
         .AsNoTracking()
         ...
         .Where(i => itemIds.Contains(i.Id))
         .ToListAsync(cancellationToken)
     ```
     It completely omits `.WhereVisibleToUser(db, userId)` and fails to check `i.IsAdminHidden`.
  3. In `NotificationsPage.razor` (line 368), `<ItemCard Item="@notification.AssociatedItem" ... />` displays the full associated item to the recipient regardless of privacy or admin-hidden flags.
- **Reproduction Scenario**:
  1. User A creates a private reply in Group G (`IsPrivate = true, TargetUserGroupId = G`).
  2. User C, who commented on the public parent item previously but is NOT a member of Group G, is automatically added to `recipientIds`.
  3. User C receives a WebPush notification on their device displaying User A's private group text.
  4. User C navigates to `/notifications`. `GetAssociatedItemsAsync` fetches the private item without checking group membership, and `ItemCard` displays it in full.
- **Recommended Remediation**:
  1. In `ItemReplyService`, if `isPrivate` is true, validate that all `recipientIds` belong to `targetUserGroupId`. For push notifications, mask private message text: `isPrivate ? "新しい非公開メッセージが届きました" : replyItem.Content`.
  2. In `NotificationsDataProvider.GetAssociatedItemsAsync`, apply `.WhereVisibleToUser(context, userId)` and filter `!i.IsAdminHidden`.

---

### D4-04: Canceled/Withdrawn Requests & Proposals Cause Zombie Notifications and Dead Push Links
- **Severity**: **Medium**
- **Location**:
  - `SRNSMudApp/Services/TaggingContractService.cs:351-386`
  - `SRNSMudApp/Services/TagContentProposalService.cs:224-257`
  - `SRNSMudApp/Services/TagNameProposalService.cs:266-302`
  - `SRNSMudApp/Services/ItemSplitService.cs:936-985`
  - `SRNSMudApp/Services/NotificationsDataProvider.cs:80-87, 142, 156-160`
  - `SRNSMudApp/Models/NotificationType.cs:31-60`
- **Expected Specification**: When a request/proposal is canceled/withdrawn by its creator, the target owner's pending notification should be updated to a canceled status or dismissed. If a push notification is dispatched directing the user to `/notifications`, the notification must exist on `/notifications`.
- **Actual Implementation**:
  1. **Tag Requests (`tagRequests`)**: `NotificationsDataProvider` (lines 80-87) queries `TaggingRequestEntities` without filtering by `r.Status == TradeStatus.Proposed`. When a contract is canceled via `CancelContractAsync`, `r.Status` becomes `TradeStatus.Canceled`. The tag owner continues to see `"{tagName}の追加リクエストが届いています。"` in `/notifications` indefinitely.
  2. **Tag Proposals (`tagContentProposals`, `tagNameProposals`)**: In `NotificationsDataProvider` (lines 159, 176), canceled proposals are not filtered out. In `NotificationsPage.razor` (lines 92-102, 157-167), only `Executed` and `Rejected` render status chips. Canceled proposals render as `"提案が届いています"` with no buttons and no chip.
  3. **Split Request Cancellation**: `ItemSplitService.CancelSplitAsync` sends Web Push to `request.OwnerUserId` linking to `/notifications`. However, `NotificationsDataProvider` (line 142) explicitly excludes canceled requests (`&& r.Status != TradeStatus.Canceled`), and `NotificationType` defines no `ItemSplitCanceledNotification`. The owner taps the push notification, navigates to `/notifications`, and finds zero matching notifications.
- **Reproduction Scenario**:
  1. User A proposes a tag name change to User B's tag.
  2. User A cancels the proposal.
  3. User B visits `/notifications`. The item is listed as `"名前変更提案（「新タグ名」）が届いています"` with no action buttons and no status chip.
- **Recommended Remediation**:
  1. Filter incoming proposal/request queries in `NotificationsDataProvider` with `r.Status == TradeStatus.Proposed`.
  2. For split cancellations, add `ItemSplitCanceledNotification` to `NotificationType` and query it for the item owner, or remove the push notification if cancellation is considered a silent dismissal.

---

### D4-05: `GratisContractExecutor` Records Tag Deletion as `Update` with Inverted Weights
- **Severity**: **Medium**
- **Location**: `SRNSMudApp/Services/Contracts/GratisContractExecutor.cs:272-279`
- **Expected Specification**: When a tag relation is removed (`TaggingRequestType.Remove` or weight decremented to 0), the recorded `TimelineEvent` must specify `ParsedEventType = TimelineEventType.Delete`, `PreviousWeight = prevWeight`, and `NewWeight = 0`.
- **Actual Implementation**:
  In `ProcessRelationDecreaseAsync`:
  ```csharp
  // GratisContractExecutor.cs:272-279
  _ = dbContext.TimelineEvents.Add(new TimelineEvent
  {
      OwnerId = executorUserId,
      Target = new ItemTarget(contract.TargetItemId),
      FollowedTagId = contract.RequestedTagId,
      ParsedEventType = TimelineEventType.Update,
      NewWeight = contract.ProposedWeight
  });
  ```
  - `ParsedEventType` is hardcoded to `TimelineEventType.Update` even when `isRemove` is true and `dbContext.TagRelations.Remove(relation)` occurred.
  - `PreviousWeight` is never assigned (defaults to `0`).
  - `NewWeight` is set to `contract.ProposedWeight` (e.g. `1`), implying the tag gained weight rather than being deleted.
  - In `ItemTagChipViewModel.cs` (lines 45-46), `isDeleted` evaluates to `false` and `isUpdated` evaluates to `true`, causing deleted tags to be highlighted as updated tags.
- **Reproduction Scenario**:
  1. User A approves a Gratis deletion request for Tag T on Item 5.
  2. The `TagRelation` is removed from the database.
  3. A `TimelineEvent` is inserted with `EventType = "Update"`, `PreviousWeight = 0`, `NewWeight = 1`.
  4. The timeline feed displays this deletion event as an "Update", and tag chip highlight logic fails to render it as deleted.
- **Recommended Remediation**:
  Branch the timeline event creation based on `isRemove || prevWeight + delta <= 0`:
  ```csharp
  var isDeleted = isRemove || prevWeight + delta <= 0;
  _ = dbContext.TimelineEvents.Add(new TimelineEvent
  {
      OwnerId = executorUserId,
      Target = new ItemTarget(contract.TargetItemId),
      FollowedTagId = contract.RequestedTagId,
      ParsedEventType = isDeleted ? TimelineEventType.Delete : TimelineEventType.Update,
      PreviousWeight = prevWeight,
      NewWeight = isDeleted ? 0 : prevWeight + delta
  });
  ```

---

### D4-06: Scoped `INotificationService` Cannot Broadcast Real-Time Events Across Blazor Circuits
- **Severity**: **Medium**
- **Location**:
  - `SRNSMudApp/Services/NotificationService.cs:56-63`
  - `SRNSMudApp/Extensions/ServiceCollectionExtensions.cs:141`
  - `SRNSMudApp/Components/UI/NotificationBadgeViewModel.cs:23`
- **Expected Specification**: When User A performs an action that sends a notification to User B, User B's active Blazor Server session should update its unread badge count in real time.
- **Actual Implementation**:
  `INotificationService` is registered as Scoped (`services.AddScoped<INotificationService, NotificationService>()`). In Blazor Server, each user connection operates in an isolated DI scope (Circuit). When User A's action calls `_notificationService.NotifyNotificationsChanged()`, it invokes `NotificationsChanged?.Invoke(...)` on User A's circuit instance. User B's `NotificationBadgeViewModel` is subscribed to User B's distinct `NotificationService` instance, so User B receives zero event notifications.
- **Reproduction Scenario**:
  1. User A and User B both view the app in separate browsers.
  2. User A sends a split request to User B's item.
  3. User A's `NotificationBadge` receives the event and refreshes.
  4. User B's `NotificationBadge` remains at 0 until User B clicks a link or manually reloads.
- **Recommended Remediation**:
  Implement a Singleton notification broker or mediator (e.g. `INotificationEventBroker`) injected into `NotificationService` that maintains a mapping of `userId -> Action` across circuits and dispatches events targeting specific user IDs.

---

### D4-07: Orphaned `TimelineEvents` on Item Deletion Break Home Timeline Pagination
- **Severity**: **Medium**
- **Location**:
  - `SRNSMudApp/Data/TimelineEvent.cs:14-26`
  - `SRNSMudApp/Services/ItemCardDataProvider.cs:80-115`
  - `SRNSMudApp/Services/HomeDataProvider.cs:136-240`
- **Expected Specification**: When an item is deleted, timeline events referencing it should be purged, or the home timeline query should filter out non-existent items prior to applying pagination (`Skip`/`Take`).
- **Actual Implementation**:
  `TimelineEvent.Target` is serialized into `TimelineTargetJson` without a foreign key. When `ItemCardDataProvider.DeleteItemAsync` or `DeleteItemByAdminAsync` deletes an item, `TimelineEvents` are left orphaned. In `HomeDataProvider.GetHomeTimelineAsync` (lines 165-180), `TimelineEvents` are grouped and paged before fetching items. For deleted items, `feedGroup.Item` returns null and the group is discarded. This results in pages returning fewer items than requested (e.g. 3 items instead of 10) and an inflated `totalGroups` count.
- **Recommended Remediation**:
  1. Clean up `TimelineEvents` when deleting an item: `context.TimelineEvents.Where(e => e.TimelineTargetJson.Contains($"\"TargetItemId\":{itemId}")).ExecuteDeleteAsync()`.
  2. In `HomeDataProvider`, inner-join on valid, viewable items prior to pagination.

---

### D4-08: Complete Omission of Notifications and Timeline Events for Item Quotes
- **Severity**: **Medium**
- **Location**:
  - `SRNSMudApp/Services/ItemQuoteService.cs:26-94`
  - `SRNSMudApp/Models/NotificationType.cs`
- **Expected Specification**: Quoting an item is a significant social interaction. The author of the quoted item should be notified that their post was quoted, and a timeline event should optionally be recorded.
- **Actual Implementation**:
  `ItemQuoteService` does not inject `INotificationService`. `CreateQuoteItemAsync` saves the quote item but creates zero notifications, defines no `ItemQuoteNotification` in `NotificationType.cs`, and creates zero `TimelineEvents`.
- **Recommended Remediation**:
  Define `ItemQuoteNotification(int QuotingItemId, int QuotedItemId, string ActorName)` in `NotificationType.cs`, and call `NotifyUserAsync(targetItem.OwnerId, "アイテムが引用されました", ...)` in `ItemQuoteService.CreateQuoteItemAsync`.

---

### D4-09: Tag Relation Comment Notification Uses Stale `TagRelation.CreatedDate` Timestamp
- **Severity**: **Low**
- **Location**:
  - `SRNSMudApp/Services/NotificationService.cs:542`
  - `SRNSMudApp/Services/NotificationsDataProvider.cs:189-196`
- **Expected Specification**: Tag comment notifications should be sorted based on when the comment was added (`tr.CommentItem.CreatedDate`), not when the tag relation was initially attached.
- **Actual Implementation**:
  Line 542 of `NotificationService.cs`:
  `CreatedAt = new DateTimeOffset(tr.CreatedDate, TimeSpan.Zero)`
  If a tag was attached 6 months ago and a user comments on it today, the notification is dated 6 months ago and placed at the bottom of the feed.
- **Recommended Remediation**:
  Use `CreatedAt = new DateTimeOffset(tr.CommentItem?.CreatedDate ?? tr.CreatedDate, TimeSpan.Zero)`.

---

### D4-10: WASM Client `SourceType` String Mismatch Breaks Notification Icons
- **Severity**: **Low**
- **Location**:
  - `SRNSMudApp.Client/Pages/NotificationPage.razor:204-213`
  - `SRNSMudApp/Models/NotificationType.cs:158-159`
- **Expected Specification**: The WebAssembly client should display proper icons for approved and rejected requests matching the server-side `SourceType` strings.
- **Actual Implementation**:
  `NotificationPage.razor` checks `"TagRequestApproved"` and `"TagRequestRejected"`, but `NotificationType.SourceType` yields `"RequestApproved"` and `"RequestRejected"`. The icons always fall back to the generic `Notifications` bell icon.
- **Recommended Remediation**:
  Update `NotificationPage.razor` to check `"RequestApproved"` and `"RequestRejected"`, and add cases for the other 14 notification types.

---

### D4-11: TagEdge Attachments and Move Contracts Omit `TimelineEvents`
- **Severity**: **Low**
- **Location**:
  - `SRNSMudApp/Services/Contracts/GratisContractExecutor.cs:102-184`
  - `SRNSMudApp/Services/Contracts/MoveContractExecutor.cs:17-77`
- **Expected Specification**: Changes to tag attachments on TagEdges and tag moves in the hierarchy should record `TimelineEvent` with `TagTarget`.
- **Actual Implementation**:
  `ProcessGratisAddEdgeAsync`, `ProcessGratisRemoveOrDecreaseEdgeAsync`, and `MoveContractExecutor.ExecuteAsync` record `TagWeightLedger` records but create no `TimelineEvents`.
- **Recommended Remediation**:
  Record `TimelineEvent` with `Target = new TagTarget(...)` in edge tag actions and move operations.

---

### D4-12: ContentReport Resolution Leaks Administrator Internal Notes to Reporters
- **Severity**: **Low**
- **Location**: `SRNSMudApp/Services/NotificationService.cs:255-260`
- **Expected Specification**: Administrative notes (`ResolutionNote`) may contain internal moderation rationale and must not be exposed verbatim to reporting users.
- **Actual Implementation**:
  `message += $" (メモ: {report.ResolutionNote})";` appends the administrator's raw resolution note to the reporter's notification message.
- **Recommended Remediation**:
  Separate public resolution feedback from internal administrative notes.

---

### D4-13: Tag Permission Request Submission Omits WebPush Notification
- **Severity**: **Low**
- **Location**: `SRNSMudApp/Services/RightAssetDataProvider.cs:359`
- **Expected Specification**: Submitting a tag permission request should deliver a WebPush notification to `TargetUserId`.
- **Actual Implementation**:
  Only `NotifyNotificationsChanged()` is called; `NotifyUserAsync()` is omitted on submission (unlike approval and rejection).
- **Recommended Remediation**:
  Add `await _notificationService.NotifyUserAsync(request.TargetUserId, "タグ操作権限リクエスト", ...)` in `SubmitPermissionRequestAsync`.

---

## Conclusion & Architectural Recommendations

The notification and timeline subsystem requires three foundational architectural improvements:
1. **Transaction & Notification Decoupling**: Establish an "Outbox" or post-commit event dispatcher pattern so that failure of third-party push services never invokes `RollbackAsync()` on committed database transactions.
2. **Cross-Circuit Notification Broker**: Replace the Scoped `EventHandler? NotificationsChanged` with a Singleton or SignalR-backed notification mediator that routes events across Blazor Server circuits to the correct recipient user.
3. **Authorization & Privacy Enforcement in Providers**: Mandate that `NotificationsDataProvider` and all notification builders enforce `.WhereVisibleToUser(db, userId)` and filter `!IsAdminHidden`, preventing private group content and administrative takedowns from leaking through notification channels.
