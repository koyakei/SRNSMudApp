# Domain 3: Visibility and Authorization Controls Comprehensive Audit Report

## 1. Executive Summary

This report documents the findings of a deep architectural and source-code investigation into **Domain 3: Visibility and Authorization Controls** across the SRNSWebApp solution (`SRNSMudApp` and `SRNSMudApp.Client`).

The investigation identified **14 distinct vulnerabilities and specification discrepancies**, including:
- **9 High-Severity vulnerabilities**: Unauthenticated arbitrary private item exfiltration, full tag hijacking via IDOR, follower-scope escalation on group deletion, private content broadcast via public quote creation and public bounty boards, unauthorized replies creating public threads from private posts, system tag privilege escalation, and OAuth account takeover.
- **4 Medium-Severity vulnerabilities**: Profile-level private tag disclosure, global private tag leaks via diagram endpoints, split request information disclosure, and inconsistent access control logic between Blazor Server and the REST API.
- **1 Low/Medium-Severity vulnerability**: Private comment and item ID leakage via public tag weight ledgers.

---

## 2. In-Depth Findings and Vulnerability Analysis

---

### Issue 1: Arbitrary Private Item Data Exfiltration via Anonymous Tag Diagram Context Query
- **Severity**: **High**
- **Target Location**:
  - `SRNSMudApp/Controllers/TagDiagramController.cs:46-70`
  - `SRNSMudApp/Services/TagDiagramDataProvider.cs:99-142`
- **Inconsistency Details**:
  - *Expected*: Any endpoint that fetches item contents must enforce user visibility rules (`WhereVisibleToUser` or `IsItemVisibleToUserAsync`). Unauthenticated or unauthorized callers must not be able to read private posts (`IsPrivate = true` or `TargetUserGroupId != null`).
  - *Actual*: The endpoint `GET /api/tag-diagram/data?itemIds={ids}` is marked `[AllowAnonymous]`. It delegates to `_diagramDataProvider.GetContextItemsAsync(parsedIds)`. In `TagDiagramDataProvider.cs`, lines 105–109:
    ```csharp
    var items = await context.Items
        .AsNoTracking()
        .Include(i => i.TagRelations)
        .Where(i => idList.Contains(i.Id))
        .ToListAsync();
    ```
    Furthermore, lines 114–133 recursively parse internal links and `QuotedItemId`, loading all referenced items without any visibility filtering. All loaded items are projected to `TagDiagramResponse.ItemDtos` and returned directly to the caller.
- **Reproduction / Trigger Scenario**:
  1. User A creates a confidential post (`IsPrivate = true, TargetUserGroupId = 5`) with ID `42`.
  2. An unauthenticated attacker sends:
     `GET /api/tag-diagram/data?itemIds=42`
  3. The server returns HTTP 200 with the full content, author username, tags, and quoted contents of Item 42.
- **Recommended Remediation**:
  1. Pass `currentUserId` and `isAdmin` to `GetContextItemsAsync`.
  2. Apply `.WhereVisibleToUser(context, currentUserId, isAdmin)` to all queries loading items and related tags in `TagDiagramDataProvider.cs`.

---

### Issue 2: Arbitrary Private Item Content & Tag Leaks via Link Preview Endpoint
- **Severity**: **High**
- **Target Location**:
  - `SRNSMudApp/Controllers/LinkPreviewController.cs:34-69`
  - `SRNSMudApp/Services/Providers/ItemLinkPreviewProvider.cs:34-84`
- **Inconsistency Details**:
  - *Expected*: Internal link preview generation for `/ItemDetail/{id}` must verify whether the requesting user is authorized to view the target item, just like `TagLinkPreviewProvider.cs` does via `IsTagVisibleToUserAsync`.
  - *Actual*: `POST /api/link/preview` has `[AllowAnonymous]`. `ItemLinkPreviewProvider.cs` implements only `GetPreviewAsync(Uri, string, CancellationToken)` without `currentUserId` or `isAdmin`. In lines 47–53:
    ```csharp
    var item = await db.Items
        .AsNoTracking()
        .Include(i => i.TagRelations)
            .ThenInclude(tr => tr.Tag)
                .ThenInclude(t => t.Owner)
        .FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
    ```
    No check on `item.IsPrivate`, `item.IsAdminHidden`, or `item.TargetUserGroupId` exists. The method constructs a preview containing the first 200 characters of the item's content, site name, and all attached tags and their weights.
- **Reproduction / Trigger Scenario**:
  1. Alice writes a private note: `"Confidential roadmap password: xyz"`.
  2. Anonymous attacker sends:
     ```http
     POST /api/link/preview HTTP/1.1
     Content-Type: application/json

     { "url": "/ItemDetail/123" }
     ```
  3. The server returns HTTP 200 with:
     ```json
     {
       "title": "Item #123",
       "description": "Confidential roadmap password: xyz",
       "tags": [...],
       "isSuccess": true
     }
     ```
- **Recommended Remediation**:
  1. Update `ItemLinkPreviewProvider` to implement the `GetPreviewAsync(Uri, string, string? currentUserId, bool isAdmin, CancellationToken)` overload.
  2. Verify `await item.IsItemVisibleToUserAsync(db, currentUserId, isAdmin)`. If not visible, return `new LinkPreviewData { IsSuccess = false }` or a masked fallback.

---

### Issue 3: Critical IDOR / Unauthorized Tag Hijacking & Modification
- **Severity**: **High**
- **Target Location**:
  - `SRNSMudApp/Controllers/TagController.cs:94-126`
  - `SRNSMudApp/Services/TagCommandService.cs:122-180`
- **Inconsistency Details**:
  - *Expected*: Only the creator of a tag (`tag.OwnerId == currentUserId`) or an administrator (`isAdmin == true`) should be permitted to update a tag's name, description, privacy mode, target group, or auto-approval settings.
  - *Actual*: In `TagController.UpdateTag`:
    ```csharp
    string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrEmpty(currentUserId)) return Unauthorized(...);
    ...
    bool success = await _tagCommandService.UpdateTagAsync(
        tagId: id,
        name: request.Name.Trim(),
        content: request.Content?.Trim(),
        isAdmin: isAdmin,
        isPrivate: request.IsPrivate,
        targetUserGroupId: request.TargetUserGroupId);
    ```
    `currentUserId` is never passed to `UpdateTagAsync`. In `TagCommandService.UpdateTagAsync`, there is no owner check. Any logged-in user can overwrite any other user's tag.
- **Reproduction / Trigger Scenario**:
  1. User Alice creates a tag `Alpha` (ID `10`, `OwnerId = Alice`).
  2. User Bob (authenticated as `Bob`) sends:
     ```http
     PUT /api/tag/10 HTTP/1.1
     Authorization: Bearer <Bob_Token>
     Content-Type: application/json

     { "name": "DefacedTag", "content": "Compromised by Bob", "isPrivate": true }
     ```
  3. Alice's tag is renamed, its description changed, and its visibility flipped to private.
- **Recommended Remediation**:
  1. Pass `currentUserId` to `ITagCommandService.UpdateTagAsync`.
  2. Check `if (!isAdmin && tagToUpdate.OwnerId != currentUserId) return false;`.

---

### Issue 4: Private Post Content Leak via Public Quote Creation & Unrestricted Quote Endpoints
- **Severity**: **High**
- **Target Location**:
  - `SRNSMudApp/Controllers/ItemController.cs:349-379, 443-451, 652-660`
  - `SRNSMudApp/Services/ItemQuoteService.cs:26-94, 97-118`
- **Inconsistency Details**:
  - *Expected*:
    1. A private item must only be quotable by users who have permission to view it.
    2. A quote of a private item must never be publishable with `IsPrivate = false`.
    3. `GET /api/item/{id}/quotes` and `GET /api/item/{id}/quoted-by` must enforce visibility controls and not expose private quotes to unauthorized parties.
  - *Actual*:
    1. In `ItemController.CreateItem` (`POST /api/item/create`), the caller can provide any `request.QuotedItemId` and set `request.IsPrivate = false`. This creates a public item quoting a private item.
    2. In `ItemController.MapToItemDto`, `QuotedItemContent: item.QuotedItem?.Content` is projected without checking whether the viewer is permitted to see `item.QuotedItem`.
    3. In `ItemController.GetQuotes` (`GET /api/item/{id}/quotes`) and `ItemController.GetQuotedByItems` (`GET /api/item/{id}/quoted-by`), there is no `[Authorize]`, no check on whether `{id}` is visible, and `_itemQuoteService.GetQuotedByItemsAsync(id)` returns all quoting items including private and group items.
    4. In `ItemQuoteService.CreateQuoteItemAsync`: When Bob quotes Alice's follower-only item (`IsPrivate = true, TargetUserGroupId = null`), the quote is created with `OwnerId = Bob, IsPrivate = true, TargetUserGroupId = null`. Bob's followers can now view the quote containing Alice's private post, even though Bob's followers do not follow Alice.
- **Reproduction / Trigger Scenario**:
  1. Alice posts a follower-only secret item (ID `50`).
  2. Bob (follower of Alice) or an attacker who knows ID `50` calls `POST /api/item/create` with:
     `{ "content": "Quoting secret", "quotedItemId": 50, "isPrivate": false }`.
  3. Now the new item is public, and any anonymous visitor querying `POST /api/item/query` sees `QuotedItemContent: <Alice's secret content>`.
- **Recommended Remediation**:
  1. In `ItemQuoteService` and `ItemController.CreateItem`, verify that the caller can view `quotedItemId` before creating the quote.
  2. If the source item is private or group-restricted, enforce that the quote cannot be made more permissive than the source item.
  3. In `MapToItemDto`, check if `QuotedItem` is visible to `currentUserId`. If not, mask `QuotedItemContent` as `[非公開アイテム]`.
  4. In `GetQuotedByItemsAsync`, accept `currentUserId` and `isAdmin`, and apply `.WhereVisibleToUser(context, currentUserId, isAdmin)`.

---

### Issue 5: Unauthorized Reply to Private Items & Unfiltered Private Reply Exfiltration
- **Severity**: **High**
- **Target Location**:
  - `SRNSMudApp/Controllers/ItemController.cs:385-438`
  - `SRNSMudApp/Services/ItemReplyService.cs:40-153, 205-238`
- **Inconsistency Details**:
  - *Expected*: Only users who have visibility access to a private item can reply to it. A reply to a private item must inherit its private status so that replies do not leak the conversation into public feeds. Querying replies must filter by the viewer's visibility permissions.
  - *Actual*:
    1. In `ItemController.AddReply`:
       ```csharp
       bool isPrivate = request.IsPrivate;
       int? targetGroupId = isPrivate ? parentItem.TargetUserGroupId : null;
       ```
       If `parentItem` is private, but `request.IsPrivate` is false, `AddItemReplyAsync` creates a public item (`IsPrivate = false, TargetUserGroupId = null`). The reply appears in the public timeline.
       Furthermore, `AddReply` does not verify if `currentUserId` can view `parentItem`!
    2. In `ItemController.GetReplies` (`GET /api/item/{id}/replies`):
       It is unauthenticated (`[AllowAnonymous]` by default). It calls `_itemReplyService.GetItemRepliesAsync(id)`, which performs:
       ```csharp
       List<Item> regularReplies = await context.Items!
           ...
           .Where(i => i.ParentItemId == parentItemId)
           .OrderBy(i => i.CreatedDate)
           .ToListAsync();
       ```
       No visibility filter is applied. All replies (including private ones) are returned to any caller.
- **Reproduction / Trigger Scenario**:
  1. Alice creates a private group discussion item (ID `80`).
  2. An unauthorized user Bob calls `GET /api/item/80/replies` without logging in.
  3. Bob receives all replies and discussions from Alice's private group.
- **Recommended Remediation**:
  1. In `AddReply`, verify `await parentItem.IsItemVisibleToUserAsync(context, currentUserId, isAdmin)`. If false, return `Forbid()`.
  2. Enforce `isPrivate = parentItem.IsPrivate || request.IsPrivate` and maintain group constraints.
  3. In `GetReplies` and `GetItemRepliesAsync`, accept `currentUserId` and `isAdmin`, applying `.WhereVisibleToUser(context, currentUserId, isAdmin)`.

---

### Issue 6: UserGroup Deletion Escalates Confidential Group Items to General Followers
- **Severity**: **High**
- **Target Location**:
  - `SRNSMudApp/Data/ApplicationDbContext.cs:81-85`
  - `SRNSMudApp/Services/UserGroupDataProvider.cs:134-146`
- **Inconsistency Details**:
  - *Expected*: When a `UserGroup` is deleted, items restricted to that group must either be cascaded, deleted, or retain a restriction so they do not accidentally become visible to the author's general public or followers.
  - *Actual*: In `ApplicationDbContext.cs`:
    ```csharp
    builder.Entity<Item>()
        .HasOne(i => i.TargetUserGroup)
        .WithMany()
        .HasForeignKey(i => i.TargetUserGroupId)
        .OnDelete(DeleteBehavior.SetNull);
    ```
    When `UserGroup` is deleted, SQL Server sets `Item.TargetUserGroupId = null`. But `Item.IsPrivate` remains `true`!
    Under `ItemVisibilityExtensions.cs:47`:
    ```csharp
    (i.TargetUserGroupId == null && context.UserFollows.Any(f => f.OwnerId == currentUserId && f.FollowedUserId == i.OwnerId))
    ```
    Because `TargetUserGroupId` became `null`, the item is now evaluated as **Followers-only**! All users following the post owner can now read items that were previously confidential to that closed group.
- **Reproduction / Trigger Scenario**:
  1. User A creates a secret UserGroup "Board of Directors" (ID `9`).
  2. User A posts sensitive financial discussions with `IsPrivate = true, TargetUserGroupId = 9`.
  3. User A deletes UserGroup 9.
  4. All items posted to group 9 now have `TargetUserGroupId = null, IsPrivate = true`.
  5. Any regular follower of User A now sees all the sensitive financial items in their feed and profile.
- **Recommended Remediation**:
  1. In `UserGroupDataProvider.DeleteUserGroupAsync`, explicitly handle items belonging to the group before deleting the group (e.g., mark them as archived/admin-hidden, or delete them, or prevent group deletion while active items exist).

---

### Issue 7: Anonymous Public Bounty Board Leaks Private Item Contents
- **Severity**: **High**
- **Target Location**:
  - `SRNSMudApp/Controllers/BountyController.cs:45-88, 93-137`
  - `SRNSMudApp/Services/ContractDataProvider.cs:132-158`
- **Inconsistency Details**:
  - *Expected*: Private items must not be eligible for public bounties, or if a bounty is created, private item text must not be broadcast to the world.
  - *Actual*:
    1. In `BountyController.CreateBounty`:
       ```csharp
       Item? item = await _itemCardDataProvider.GetItemByIdAsync(request.TargetItemId);
       ```
       It does not check if `item.IsPrivate` or if `currentUserId` owns/can view `item`.
    2. In `BountyController.GetActiveBounties` (`[HttpGet] [AllowAnonymous]`):
       ```csharp
       TargetItemId: bounty.TargetItemId,
       TargetItemContent: bounty.TargetItem?.Content ?? string.Empty,
       ```
       `_bountyDataProvider.GetActiveBountiesAsync()` does not filter `b.Target.Item.IsPrivate`.
- **Reproduction / Trigger Scenario**:
  1. Alice writes a private item (ID `60`).
  2. Bob calls `POST /api/bounty` with `TargetItemId = 60`.
  3. Anyone visiting `GET /api/bounty` or the Bounty Board page reads Alice's private post in `TargetItemContent`.
- **Recommended Remediation**:
  1. Reject bounty creation if `targetItem.IsPrivate` or if `targetItem.OwnerId != currentUserId`.
  2. In `GetActiveBountiesAsync`, apply visibility filters to `bounty.Target.Item`.

---

### Issue 8: System Tag Creation & Privilege Escalation via Tagging Import
- **Severity**: **High**
- **Target Location**:
  - `SRNSMudApp/Controllers/TagImportController.cs:79-133`
  - `SRNSMudApp/Services/TaggingImportDataProvider.cs:109-140`
- **Inconsistency Details**:
  - *Expected*: Only administrators (`User.IsInRole("Admin")`) can create System tags (`TagKind = SystemClassificationTag`, `OwnerId = "system"`).
  - *Actual*: In `TagImportController.ImportTagging` (`POST /api/tag/import/tagging`):
    Line 125:
    ```csharp
    bool isSystem = string.Equals(relDto.Tag.TagKind, "SystemClassificationTag", StringComparison.OrdinalIgnoreCase);
    var tag = await _taggingImportDataProvider.FindExistingTagAsync(...)
        ?? await _taggingImportDataProvider.CreateTagAsync(relDto.Tag.Name, currentUserId, isSystem, ...);
    ```
    There is no check whether `User.IsInRole("Admin")` is true! Any authenticated user can upload a JSON payload with `TagKind: "SystemClassificationTag"` and have `TaggingImportDataProvider.CreateTagAsync` create a system-owned tag (`OwnerId = "system"`).
- **Reproduction / Trigger Scenario**:
  1. Standard authenticated user uploads a JSON payload to `/api/tag/import/tagging` containing a tag with `"TagKind": "SystemClassificationTag"`.
  2. A new tag is persisted with `OwnerId = "system"`, altering system classification tags.
- **Recommended Remediation**:
  Enforce: `bool isSystem = (User?.IsInRole("Admin") ?? false) && string.Equals(relDto.Tag.TagKind, "SystemClassificationTag", StringComparison.OrdinalIgnoreCase);`.

---

### Issue 9: Account Takeover via Unverified GitHub OAuth Email Matching
- **Severity**: **High**
- **Target Location**:
  - `SRNSMudApp/Controllers/AuthController.cs:57-77`
  - `SRNSMudApp/Services/Auth/ExternalTokenVerificationService.cs:123-136`
- **Inconsistency Details**:
  - *Expected*: When signing in via external social login (OAuth/OIDC), an existing account must only be linked if the identity provider guarantees that the email address is verified (`email_verified == true`), or the user explicitly proves possession of the existing account credentials.
  - *Actual*: In `ExternalTokenVerificationService.VerifyGithubTokenAsync`, it queries GitHub's `/user` endpoint and reads `doc.RootElement.TryGetProperty("email", ...)`. GitHub's public profile email is user-editable and can be unverified.
    In `AuthController.ProcessUserLoginAsync`:
    ```csharp
    if (existingUser is null && !string.IsNullOrEmpty(payload.Email))
    {
        existingUser = await userManager.FindByEmailAsync(payload.Email);
    }
    ...
    return await LinkAndSignInAsync(existingUser, userLoginInfo, request.Provider, cancellationToken);
    ```
    If an existing user registered with `victim@example.com`, an attacker can set their GitHub profile email to `victim@example.com` and automatically hijack the victim's account.
- **Reproduction / Trigger Scenario**:
  1. Victim creates an account with email `admin@victim.com`.
  2. Attacker creates a free GitHub account and sets the public email to `admin@victim.com`.
  3. Attacker triggers `POST /api/auth/external-login` with Provider `GITHUB`.
  4. The system logs the attacker in as `admin@victim.com`.
- **Recommended Remediation**:
  1. Use GitHub's `/user/emails` endpoint and require `verified == true && primary == true`.
  2. Do not automatically link accounts across different providers without an explicit password confirmation or email confirmation token.

---

### Issue 10: Leakage of User's Private Tags on Public User Profile Page & API
- **Severity**: **Medium**
- **Target Location**:
  - `SRNSMudApp/Controllers/UserController.cs:31-92`
  - `SRNSMudApp/Services/UserDataProvider.cs:123-132`
- **Inconsistency Details**:
  - *Expected*: When viewing a user's profile (`GET /api/user/{userId}` or `/UserDetail/{userId}`), private tags created by that user must be filtered out if the viewer is not authorized to see them.
  - *Actual*: In `UserDataProvider.GetUserDetailAsync`:
    While `userItems` correctly applies `.WhereVisibleToUser(dbContext, currentUserId)`, `userTags` does NOT:
    ```csharp
    List<Tag> userTags = await dbContext.Tags
        ...
        .Where(t => t.OwnerId == userId)
        .OrderByDescending(t => t.UpdatedDate)
        .ToListAsync();
    ```
    All tags created by `userId`, including `IsPrivate = true` tags and private group tags, are returned in `UserDetailDto.Tags` to any anonymous visitor.
- **Reproduction / Trigger Scenario**:
  1. User creates a secret tag `IsPrivate = true, TargetUserGroupId = 99`.
  2. Anonymous visitor calls `GET /api/user/{userId}`.
  3. The response contains the private tag's name, ID, and description.
- **Recommended Remediation**:
  Apply `.WhereVisibleToUser(dbContext, currentUserId, isAdmin)` to the `userTags` query in `UserDataProvider.GetUserDetailAsync`.

---

### Issue 11: Public Tag Diagram Exposes All Private & Group-Restricted Tags
- **Severity**: **Medium**
- **Target Location**:
  - `SRNSMudApp/Controllers/TagDiagramController.cs:46-52`
  - `SRNSMudApp/Services/TagDiagramDataProvider.cs:145-154`
- **Inconsistency Details**:
  - *Expected*: The global tag diagram should only display tags that the viewer is authorized to see.
  - *Actual*: In `TagDiagramDataProvider.LoadAllTagsAsync`:
    ```csharp
    return await context.Tags
        .Where(t => !Tag.VoteTagNames.Contains(t.Name) && !Tag.ReactionTagNames.Contains(t.Name))
        .WhereVisibleToUser()
        .OrderBy(t => t.Name)
        ...
    ```
    The parameterless overload `WhereVisibleToUser()` only filters `!t.Owner.IsBanned`. It does NOT filter `!t.IsPrivate`. Since `GET /api/tag-diagram/data` is `[AllowAnonymous]`, every private tag in the database is returned as a node in the diagram.
- **Reproduction / Trigger Scenario**:
  1. User creates private tag "SecretProject".
  2. Anonymous user visits `/TagDiagram` or calls `GET /api/tag-diagram/data`.
  3. "SecretProject" node is visible in the diagram graph.
- **Recommended Remediation**:
  Pass `currentUserId` and `isAdmin` to `LoadAllTagsAsync` and call `WhereVisibleToUser(context, currentUserId, isAdmin)`.

---

### Issue 12: Unrestricted Split Request Listing & Private Content Probing
- **Severity**: **Medium**
- **Target Location**:
  - `SRNSMudApp/Controllers/ItemSplitController.cs:107-117`
  - `SRNSMudApp/Services/ItemSplitService.cs:60-140`
- **Inconsistency Details**:
  - *Expected*: Only the item owner or the split requester should be able to view pending split requests for an item. An unauthorized user must not be able to probe or request splits on a private item.
  - *Actual*:
    1. `GET /api/item-split/item/{itemId}/pending` has no `[Authorize]` and does not check if the caller is the item owner. It returns all pending `ItemSplitRequestDto` records, including `SelectedText` (verbatim text from the original item, which may be private).
    2. `ItemSplitService.RequestSplitAsync` does not check if `requesterUserId` has permission to view `originalItem`. If an attacker submits split requests with various strings, successful proposal responses reveal whether substrings exist in a private item.
- **Reproduction / Trigger Scenario**:
  1. Alice writes a private item.
  2. Anonymous visitor calls `GET /api/item-split/item/{aliceItemId}/pending`.
  3. Any pending split request exposes portions of Alice's private post in `SelectedText`.
- **Recommended Remediation**:
  1. Require `[Authorize]` on `GetPendingSplitRequests` and check that `currentUserId == originalItem.OwnerId`.
  2. In `RequestSplitAsync`, verify `await originalItem.IsItemVisibleToUserAsync(context, requesterUserId)`.

---

### Issue 13: Architectural Discrepancy: Web API Item Detail Rejects Valid Followers/Group Members & Permits Admin-Hidden Items
- **Severity**: **Medium**
- **Target Location**:
  - `SRNSMudApp/Controllers/ItemController.cs:174-195`
  - `SRNSMudApp/Services/ItemDetailDataProvider.cs:93-107`
- **Inconsistency Details**:
  - *Expected*: The REST API (`ItemController.GetItemById`) and Blazor Server (`ItemDetailDataProvider.GetItemDetailAsync`) must have identical authorization and visibility logic.
  - *Actual*:
    - In `ItemDetailDataProvider.cs`:
      ```csharp
      if (!await item.IsItemVisibleToUserAsync(context, currentUserId, isAdmin)) return null;
      ```
      This correctly allows followers (for follower-only items) and group members (for group-only items).
    - In `ItemController.GetItemById`:
      ```csharp
      if (item.IsPrivate && item.OwnerId != currentUserId && !isAdmin)
      {
          return Forbid();
      }
      ```
      This naively compares `item.OwnerId != currentUserId`. Consequently, legitimate followers and group members are **forbidden (HTTP 403)** from viewing the item via the API/WASM!
      Conversely, if an item has `IsAdminHidden = true` and `IsPrivate = false`, `item.IsPrivate` is false, so `ItemController.GetItemById` returns HTTP 200 with the item, allowing ordinary users to read admin-hidden items via the API.
- **Reproduction / Trigger Scenario**:
  1. Alice posts a follower-only item. Bob follows Alice.
  2. Bob accesses the item via Blazor Server: Page loads successfully.
  3. Bob accesses the item via WASM / API (`GET /api/item/{id}`): Returns HTTP 403 Forbidden.
  4. Admin hides a public abusive item (`IsAdminHidden = true`). Any anonymous user calls `GET /api/item/{id}`: Returns HTTP 200 OK.
- **Recommended Remediation**:
  Replace lines 186–189 in `ItemController.cs` with:
  ```csharp
  if (!await item.IsItemVisibleToUserAsync(context, currentUserId, isAdmin))
  {
      return Forbid();
  }
  ```

---

### Issue 14: Public Tag Detail & Ledger Exposes Private Item IDs and Confidential Comments
- **Severity**: **Low / Medium**
- **Target Location**:
  - `SRNSMudApp/Controllers/TagController.cs:295-369, 449-486`
  - `SRNSMudApp/Services/TagDetailDataProvider.cs:124-153`
- **Inconsistency Details**:
  - *Expected*: When viewing a public tag's details or weight ledger, ledger entries associated with private items that the viewer cannot access must not reveal the private item ID or comment contents. Furthermore, `PendingRequests` on a tag must only be returned to the tag owner.
  - *Actual*:
    1. In `TagDetailDataProvider.GetTagDetailAsync`:
       - `pendingRequests` on the tag are returned to anyone calling `GET /api/tag/{id}`, even if they are not the tag owner.
       - `weightLedgers` are loaded without filtering `l.ItemId` for visibility. If a public tag was attached to a private item with a comment, `TagWeightLedgerDto` returns `ItemId`, `CommentItemId`, and `Comment` to anyone.
- **Reproduction / Trigger Scenario**:
  1. Alice attaches tag "Programming" to her private item and writes comment: "Private internal bug report #99".
  2. Any user visits `/TagDetail/Programming` and reads Alice's comment and item ID in the ledger tab.
- **Recommended Remediation**:
  1. Only include `PendingRequests` if `currentUserId == tag.OwnerId || isAdmin`.
  2. In `TagDetailPageData.WeightLedgers`, mask `Comment` and `ItemId` for items where `!await item.IsItemVisibleToUserAsync(context, currentUserId, isAdmin)`.

---

## 3. Summary Matrix

| # | Vulnerability / Issue | Target File & Lines | Severity | Impact |
|---|---|---|---|---|
| 1 | Private Item Exfiltration via Tag Diagram | `TagDiagramController.cs:46-70`<br>`TagDiagramDataProvider.cs:99-142` | **High** | Unauthenticated full read of any private post |
| 2 | Private Item Content Leak via Link Preview | `LinkPreviewController.cs:34-69`<br>`ItemLinkPreviewProvider.cs:34-84` | **High** | Unauthenticated snippet/tag read of any private post |
| 3 | IDOR / Unauthorized Tag Hijacking & Modification | `TagController.cs:94-126`<br>`TagCommandService.cs:122-180` | **High** | Any user can overwrite/hijack any other user's tag |
| 4 | Private Content Leak via Quotes & Quote APIs | `ItemController.cs:349-379, 443, 652`<br>`ItemQuoteService.cs:26-118` | **High** | Publicization of private posts via quote |
| 5 | Unauthorized Private Replies & Unfiltered Reply Leak | `ItemController.cs:385-438`<br>`ItemReplyService.cs:40-153, 205-238` | **High** | Unauthenticated read of private threads & public replies |
| 6 | Group Deletion Escalates Scope to Followers | `ApplicationDbContext.cs:81-85`<br>`UserGroupDataProvider.cs:134-146` | **High** | Closed group posts exposed to general followers |
| 7 | Bounty Board Broadcasts Private Items | `BountyController.cs:45-88, 93-137`<br>`ContractDataProvider.cs:132-158` | **High** | Unauthenticated read of private posts on bounty board |
| 8 | System Tag Privilege Escalation | `TagImportController.cs:79-133`<br>`TaggingImportDataProvider.cs:109-140` | **High** | Non-admin users create system tags |
| 9 | OAuth Account Takeover via Unverified Email | `AuthController.cs:57-77`<br>`ExternalTokenVerificationService.cs:123-136` | **High** | Hijack account with matching public GitHub email |
| 10 | Profile Page Exposes User's Private Tags | `UserController.cs:31-92`<br>`UserDataProvider.cs:123-132` | **Medium** | Unauthenticated disclosure of all private tags |
| 11 | Global Tag Diagram Exposes Private Tags | `TagDiagramController.cs:46-52`<br>`TagDiagramDataProvider.cs:145-154` | **Medium** | Unauthenticated disclosure of private tags |
| 12 | Split Requests Leak Private Snippets | `ItemSplitController.cs:107-117`<br>`ItemSplitService.cs:60-140` | **Medium** | Unauthenticated read of split text snippets |
| 13 | API Item Detail Inconsistent with Server | `ItemController.cs:174-195`<br>`ItemDetailDataProvider.cs:93-107` | **Medium** | Followers/group members blocked, admin-hidden leaked |
| 14 | Public Tag Ledger Leaks Private Item Comments | `TagController.cs:295-369, 449-486`<br>`TagDetailDataProvider.cs:124-153` | **Low/Med** | Private comments and item IDs disclosed via ledger |

---

## 4. Architectural Recommendations

1. **Centralized Visibility Enforcement Filter**:
   Ensure all query paths that retrieve `Item` or `Tag` entities consistently apply the canonical extensions:
   - `query.WhereVisibleToUser(context, currentUserId, isAdmin)`
   - `item.IsItemVisibleToUserAsync(context, currentUserId, isAdmin)`
   - `tag.IsTagVisibleToUserAsync(context, currentUserId, isAdmin)`
   Avoid ad-hoc `item.OwnerId != currentUserId` comparisons that fail to honor followers and group members.

2. **Immutable Privacy Boundaries for Derived Entities (Quotes / Replies / Splits)**:
   - When quoting or replying to an item with `IsPrivate = true`, the child entity must strictly enforce `IsPrivate = true`.
   - If the parent entity has a `TargetUserGroupId`, the child entity must inherit that group ID.
   - Quotation and reply services must reject creation if the requesting user cannot view the parent entity.

3. **Mandatory Ownership Checks in Mutation APIs**:
   - Explicitly verify ownership (`entity.OwnerId == currentUserId || isAdmin`) inside service methods (`UpdateTagAsync`, `DeleteUserGroupAsync`, etc.), and never rely on the controller alone.
   - Do not allow client DTOs to specify privileged attributes (`IsSystem`, `OwnerId = "system"`, `TargetUserGroupId`) without verifying membership or role.
