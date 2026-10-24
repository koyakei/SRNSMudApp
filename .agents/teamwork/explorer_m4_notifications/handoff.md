# Handoff Report: Domain 4 (Notifications & Timeline Events)

## 1. Observation

Direct code inspections across the solution revealed the following concrete implementations and defects:

1. **`ItemSplitService.cs:306-326`**:
   ```csharp
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
   `transaction.CommitAsync()` is called before `NotifyUserAsync()`. If `NotifyUserAsync()` throws an exception, the `catch` block invokes `RollbackAsync()` on an already-committed transaction.

2. **`ItemReplyService.cs:247-285` & `NotificationsDataProvider.cs:90-101`**:
   In `AddItemReplyAsync`:
   ```csharp
   var parentOwnerId = await context.Items.Where(i => i.Id == parentItemId).Select(i => i.OwnerId).FirstOrDefaultAsync();
   var replierIds = await context.Items.Where(i => i.ParentItemId == parentItemId).Select(i => i.OwnerId).ToListAsync();
   recipientIds = [.. (new[] { parentOwnerId }.Concat(replierIds)).Where(id => !string.IsNullOrEmpty(id) && id != userId).Distinct()!];
   ```
   `ConversationOptOuts` is not checked. If `replyItem.IsPrivate == true`, `replyItem.Content` is sent verbatim via WebPush to all recipients without verifying membership in `targetUserGroupId`.
   In `NotificationsDataProvider.cs:95-101`, `itemReplies` queries `i.NotificationRecipients.Any(r => r.RecipientUserId == userId)` without checking `ConversationOptOuts` or `IsPrivate`.
   In `NotificationsDataProvider.cs:50-65`, `GetAssociatedItemsAsync` queries `context.Items.Where(i => itemIds.Contains(i.Id))` without checking `WhereVisibleToUser` or `IsAdminHidden`.

3. **`GratisContractExecutor.cs:272-279`**:
   In `ProcessRelationDecreaseAsync`:
   ```csharp
   _ = dbContext.TimelineEvents.Add(new TimelineEvent
   {
       OwnerId = executorUserId,
       Target = new ItemTarget(contract.TargetItemId),
       FollowedTagId = contract.RequestedTagId,
       ParsedEventType = TimelineEventType.Update,
       NewWeight = contract.ProposedWeight
   });
   ```
   `ParsedEventType` is hardcoded to `TimelineEventType.Update` even when `isRemove` is true and the tag relation was deleted from the database. `PreviousWeight` is unassigned (0), and `NewWeight` is set to `contract.ProposedWeight` (>0).

4. **`NotificationsDataProvider.cs:80-87, 142, 156-160, 172-177`**:
   - `tagRequests` (line 85): `Where(r => r.RequesterUserId != userId && (r.Target.OwnerId == userId || r.RequestedTag.OwnerId == userId))` does not filter `r.Status == TradeStatus.Proposed` or `r.Status != TradeStatus.Canceled`.
   - `splitRequests` (line 142): Excludes `r.Status != TradeStatus.Canceled`, but `NotificationType.cs` has no `ItemSplitCanceledNotification`, and `resolvedSplitRequests` only queries `Executed || Rejected`.
   - `tagContentProposals` (line 159) and `tagNameProposals` (line 176): Do not filter out `Status == TradeStatus.Canceled`.

5. **`NotificationService.cs:56-63` & `ServiceCollectionExtensions.cs:141`**:
   ```csharp
   services.AddScoped<INotificationService, NotificationService>();
   ```
   `public event EventHandler? NotificationsChanged;` is an in-memory event on a Scoped service. When an actor calls `NotifyNotificationsChanged()`, the event only raises in the actor's Blazor circuit, never reaching the recipient's circuit.

6. **`HomeDataProvider.cs:138-179, 210-239` & `ItemCardDataProvider.cs:80-115`**:
   `TimelineEvent.Target` is serialized to `TimelineTargetJson` with no foreign key to `Items`. `DeleteItemAsync` does not delete `TimelineEvents`. In `HomeDataProvider`, pagination (`Skip`/`Take`) is applied over `TimelineEvents` before checking `WhereVisibleToUser` or whether the target item still exists. Deleted items return null and are discarded at line 233, resulting in short pages.

7. **`ItemQuoteService.cs:26-94`**:
   `CreateQuoteItemAsync` creates quote items, but does not inject `INotificationService`, generates zero notifications for `quotedItem.OwnerId`, and creates zero `TimelineEvents`.

8. **`NotificationPage.razor:204-213` (WASM)**:
   `GetNotificationIcon` checks `"TagRequestApproved"` and `"TagRequestRejected"`, whereas `NotificationType.SourceType` produces `"RequestApproved"` and `"RequestRejected"`.

---

## 2. Logic Chain

1. **From Observation 1**: Because `transaction.CommitAsync()` executes before `_notificationService.NotifyUserAsync(...)` within a `try` block, any failure in WebPush invokes `RollbackAsync()` on a committed transaction. In EF Core, rolling back a completed transaction is an illegal operation (`InvalidOperationException`). Therefore, notification network issues trigger a catastrophic crash that conceals successful DB mutations.
2. **From Observation 2**: Because `AddItemReplyAsync` calculates recipients purely based on previous repliers without querying `ConversationOptOuts`, and `NotificationsDataProvider` does not filter by opt-outs, the opt-out mechanism is completely disconnected from notification generation and retrieval. Because private reply recipients are not validated against `targetUserGroupId` and `GetAssociatedItemsAsync` omits `WhereVisibleToUser`, private contents are leaked through push payloads and rendered via `ItemCard` on the `/notifications` page.
3. **From Observation 3**: Because `ProcessRelationDecreaseAsync` in `GratisContractExecutor` hardcodes `ParsedEventType = TimelineEventType.Update` and assigns `NewWeight = contract.ProposedWeight`, removing a tag emits a timeline event stating the tag was updated to a positive weight. In `ItemTagChipViewModel.cs`, this causes tag removal events to be evaluated as updates rather than deletions.
4. **From Observation 4**: Because `NotificationsDataProvider` fails to filter active request queries by `TradeStatus.Proposed`, canceled requests continue to match and produce notifications with the text `"リクエストが届いています"`. Because `ItemSplitCanceledNotification` does not exist and `NotificationsDataProvider` drops canceled split requests, users receiving push notifications for split cancellations encounter a dead link with no notification item on `/notifications`.
5. **From Observation 5**: Because `INotificationService` is registered with Scoped lifetime in DI, each Blazor Server circuit has its own independent `NotificationService` instance. Raising `NotificationsChanged` on one circuit has zero effect on any other user's circuit, making real-time badge updates impossible without page navigation.
6. **From Observation 6**: Because `TimelineEvent` records are not cascaded when items are deleted, and `HomeDataProvider` groups and pages over `TimelineEvents` prior to resolving and filtering items, deleted items leave orphaned timeline records that generate pagination gaps and incorrect total counts in the home feed.
7. **From Observation 7**: Because quoting an item has no corresponding notification model in `NotificationType` and `ItemQuoteService` has no notification trigger, resource authors are never notified when their work is quoted.
8. **From Observation 8**: Because string identifiers in WASM (`TagRequestApproved`/`TagRequestRejected`) do not match backend `SourceType` strings (`RequestApproved`/`RequestRejected`), icon resolution falls back to default.

---

## 3. Caveats

1. **External WebPush Infrastructure**: Actual WebPush delivery depends on valid VAPID keys and browser push service availability (Google FCM, Apple Push Notification service, Mozilla Push Service).
2. **Read-Only Scope**: In accordance with the Explorer archetype and project constraints, no production code was modified during this investigation. All remediation proposals are documented in `analysis.md`.
3. **Database Constraints**: `TimelineTargetJson` was introduced as a JSON column to support union targets (`ItemTarget` vs `TagTarget`). While this provides polymorphism, it prevents foreign key constraints, requiring manual cascade management in domain services.

---

## 4. Conclusion

Domain 4 (Notifications & Timeline Events) contains 13 identified issues:
- **3 High-severity issues**: Post-commit `RollbackAsync()` crash in `ItemSplitService.ApproveSplitAsync`, non-functional `ConversationOptOut` in reply notifications, and private item/group data leaks in WebPush payloads and `NotificationsDataProvider`.
- **5 Medium-severity issues**: Zombie canceled requests, inverted `TimelineEventType` in `GratisContractExecutor`, broken cross-circuit real-time notification events in Blazor Server, timeline pagination holes from orphaned events, and missing quote notifications.
- **5 Low-severity issues**: Stale comment timestamps, WASM client icon mapping mismatches, missing edge timeline events, admin internal note disclosure, and missing permission request push alerts.

Remediating these issues requires:
1. Moving notification dispatch outside database transaction boundaries.
2. Filtering recipients by `ConversationOptOuts` and `targetUserGroupId`.
3. Applying `.WhereVisibleToUser(db, userId)` in `NotificationsDataProvider`.
4. Implementing a cross-circuit notification broker for Blazor Server.
5. Correcting `TimelineEventType` assignment in `GratisContractExecutor`.

---

## 5. Verification Method

To independently verify the observations:
1. **Inspect Code Locations**:
   - `view_file` on `SRNSMudApp/Services/ItemSplitService.cs` lines 306-326 to observe `RollbackAsync()` after `CommitAsync()`.
   - `view_file` on `SRNSMudApp/Services/ItemReplyService.cs` lines 240-285 to confirm lack of `ConversationOptOuts` check.
   - `view_file` on `SRNSMudApp/Services/Contracts/GratisContractExecutor.cs` lines 272-279 to observe hardcoded `TimelineEventType.Update`.
   - `view_file` on `SRNSMudApp/Services/NotificationsDataProvider.cs` lines 80-87, 90-101 to verify missing status and visibility filters.
   - `view_file` on `SRNSMudApp/Extensions/ServiceCollectionExtensions.cs` line 141 to verify Scoped lifetime of `INotificationService`.
2. **Run Test Suite**:
   ```bash
   dotnet test SRNSMudApp.Tests/SRNSMudApp.Tests.csproj --filter "Notification|Timeline|ItemReply|ItemSplit|Gratis"
   ```
   Existing tests pass because they currently assert happy paths or do not mock WebPush failures; new regression tests can be written targeting the scenarios identified above.
