# Domain 3: Visibility and Authorization Controls — Handoff Report

## 1. Observation

Direct source-code analysis of the solution (`SRNSMudApp` and `SRNSMudApp.Client`) revealed multiple authorization omissions, privacy leaks, IDOR flaws, and visibility inheritance failures:

### Obs 1: Unauthenticated Private Item Exfiltration via Tag Diagram
- **Files & Lines**: `SRNSMudApp/Controllers/TagDiagramController.cs:46-70`, `SRNSMudApp/Services/TagDiagramDataProvider.cs:99-142`
- **Verbatim Code**:
  - `TagDiagramController.cs:46`: `[HttpGet("data")] [AllowAnonymous] public async Task<ActionResult<TagDiagramResponse>> GetDiagramData(...)`
  - `TagDiagramDataProvider.cs:105-109`:
    ```csharp
    var items = await context.Items
        .AsNoTracking()
        .Include(i => i.TagRelations)
        .Where(i => idList.Contains(i.Id))
        .ToListAsync();
    ```
  - `TagDiagramDataProvider.cs:114-133`: Recursive traversal of `i.Content` for internal links and `i.QuotedItemId` fetching items without any `WhereVisibleToUser` check.
- **Result**: Any unauthenticated caller passing `?itemIds={private_item_id}` receives the entire item content, author username, tags, and quoted contents.

### Obs 2: Unauthenticated Private Item Leak via Link Preview
- **Files & Lines**: `SRNSMudApp/Controllers/LinkPreviewController.cs:34-69`, `SRNSMudApp/Services/Providers/ItemLinkPreviewProvider.cs:34-84`
- **Verbatim Code**:
  - `LinkPreviewController.cs:34`: `[HttpPost("preview")] [AllowAnonymous] public async Task<IActionResult> GetPreview(...)`
  - `ItemLinkPreviewProvider.cs:47-53`:
    ```csharp
    var item = await db.Items
        .AsNoTracking()
        .Include(i => i.TagRelations)
            .ThenInclude(tr => tr.Tag)
                .ThenInclude(t => t.Owner)
        .FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
    ```
- **Result**: `ItemLinkPreviewProvider` only implements `GetPreviewAsync(Uri, string, CancellationToken)` without taking `currentUserId` or checking `item.IsPrivate` / `item.TargetUserGroupId`. It returns the first 200 characters and tags of any confidential post to anonymous callers.

### Obs 3: Critical IDOR in Tag Modification
- **Files & Lines**: `SRNSMudApp/Controllers/TagController.cs:94-126`, `SRNSMudApp/Services/TagCommandService.cs:122-180`
- **Verbatim Code**:
  - `TagController.cs:95-105`: Authenticates the user (`currentUserId`), but never checks `tag.OwnerId == currentUserId || isAdmin`.
  - `TagCommandService.cs:134-144`:
    ```csharp
    var tag = await _context.Tags.FindAsync(new object[] { tagId }, cancellationToken);
    if (tag == null) return false;
    tag.Name = name;
    tag.NormalizedName = name.ToUpperInvariant();
    tag.Description = description;
    tag.IsPrivate = isPrivate;
    tag.TargetUserGroupId = targetUserGroupId;
    tag.AutoApproveNewItems = autoApproveNewItems;
    ```
- **Result**: Any authenticated user can modify, rename, privatize, or hijack any existing tag by passing its ID to `PUT /api/tag/{id}`.

### Obs 4: Private Post Content Leaked via Public Quotes
- **Files & Lines**: `SRNSMudApp/Controllers/ItemController.cs:349-379, 443, 652`, `SRNSMudApp/Services/ItemQuoteService.cs:26-118`
- **Verbatim Code**:
  - `ItemController.cs:355`: Caller creates a public post (`item.IsPrivate = false, item.QuotedItemId = privateItemId`).
  - `ItemQuoteService.cs:72-88`: In `CreateItemWithQuoteAsync`, no validation ensures that a private item cannot be quoted in a public item.
  - `ItemQuoteService.cs:100-118`: In `GetQuotesAsync`:
    ```csharp
    return await _context.Items
        .Where(i => i.QuotedItemId == itemId)
        .Where(i => !i.IsAdminHidden)
        .OrderByDescending(i => i.CreatedAt)
        ...
    ```
    No `WhereVisibleToUser` check is performed.
- **Result**: Private post contents (`QuotedItemContent`) become publicly viewable, and private quotes are exposed to unauthenticated callers.

### Obs 5: Private Post Discussion Exposed via Replies
- **Files & Lines**: `SRNSMudApp/Controllers/ItemController.cs:385-438`, `SRNSMudApp/Services/ItemReplyService.cs:40-153, 205-238`
- **Verbatim Code**:
  - `ItemReplyService.cs:52-58`: Lacks visibility verification on the parent post:
    ```csharp
    var parentItem = await _context.Items.FirstOrDefaultAsync(i => i.Id == parentItemId, cancellationToken);
    if (parentItem == null) throw new InvalidOperationException("Parent item not found");
    ```
  - `ItemReplyService.cs:205-238`: In `GetItemRepliesAsync`:
    ```csharp
    var replies = await _context.Items
        .Where(i => i.RootItemId == rootItemId && i.Id != rootItemId && !i.IsAdminHidden)
        .OrderBy(i => i.CreatedAt)
        .ToListAsync(cancellationToken);
    ```
- **Result**: An attacker guessing an item ID can post public replies to confidential items, and `GET /api/item/{id}/replies` (marked `[AllowAnonymous]`) returns private replies without filtering.

### Obs 6: Group Deletion Escalates Confidential Items to Followers
- **Files & Lines**: `SRNSMudApp/Data/ApplicationDbContext.cs:81-85`, `SRNSMudApp/Services/UserGroupDataProvider.cs:134-146`
- **Verbatim Code**:
  - `ApplicationDbContext.cs:84`:
    ```csharp
    builder.Entity<Item>()
        .HasOne(i => i.TargetUserGroup)
        .WithMany(g => g.Items)
        .HasForeignKey(i => i.TargetUserGroupId)
        .OnDelete(DeleteBehavior.SetNull);
    ```
  - `ItemVisibilityExtensions.cs:30-33`:
    ```csharp
    i.IsPrivate && i.TargetUserGroupId == null && (
        i.OwnerId == currentUserId ||
        context.UserFollows.Any(f => f.FollowedId == i.OwnerId && f.FollowerId == currentUserId)
    )
    ```
- **Result**: When a private group is deleted, `TargetUserGroupId` is nullified while `IsPrivate` remains `true`. The item transitions from strictly group-visible to visible to all followers of the author.

### Obs 7: Bounty Board Exposes Private Item Content
- **Files & Lines**: `SRNSMudApp/Controllers/BountyController.cs:45-88, 93-137`, `SRNSMudApp/Services/ContractDataProvider.cs:132-158`
- **Verbatim Code**:
  - `BountyController.cs:93`: `[HttpPost] [Authorize] public async Task<ActionResult<BountyResponse>> CreateBounty(...)` does not verify `TargetItemId` visibility.
  - `BountyController.cs:45`: `[HttpGet] [AllowAnonymous] public async Task<ActionResult<PagedResult<BountyResponse>>> GetActiveBounties(...)` returns `TargetItemContent` for all active bounties.
- **Result**: Creating a bounty on a private item broadcasts its text globally to anonymous visitors.

### Obs 8: Non-Admin Creation of System Tags
- **Files & Lines**: `SRNSMudApp/Controllers/TagImportController.cs:79-133`, `SRNSMudApp/Services/TaggingImportDataProvider.cs:109-140`
- **Verbatim Code**:
  - `TagImportController.cs:79`: `[HttpPost("tagging")] [Authorize] public async Task<ActionResult<TaggingImportResult>> ImportTagging(...)`
  - Requires only authentication, not `[Authorize(Roles = "Admin")]`.
  - `TaggingImportDataProvider.cs:122`:
    ```csharp
    var newTag = new Tag
    {
        Name = tagName,
        NormalizedName = tagName.ToUpperInvariant(),
        OwnerId = "system",
        IsPrivate = false,
        CreatedAt = DateTime.UtcNow
    };
    ```
- **Result**: Any authenticated user can create official "system"-owned tags.

### Obs 9: Account Takeover via Unverified GitHub OAuth Email
- **Files & Lines**: `SRNSMudApp/Controllers/AuthController.cs:57-77`, `SRNSMudApp/Services/ExternalTokenVerificationService.cs:123-136`
- **Verbatim Code**:
  - `AuthController.cs:64-70`:
    ```csharp
    var user = await _userManager.FindByEmailAsync(externalUser.Email);
    if (user != null)
    {
        // Link external login and sign in immediately
        await _userManager.AddLoginAsync(user, new UserLoginInfo(provider, externalUser.Id, provider));
        await _signInManager.SignInAsync(user, isPersistent: false);
        return Ok(new AuthResponse { ... });
    }
    ```
  - `ExternalTokenVerificationService.cs:134`:
    ```csharp
    Email = userJson.TryGetProperty("email", out var emailProp) && emailProp.ValueKind == JsonValueKind.String
        ? emailProp.GetString()
        : $"{idProp.GetInt64()}@github.com"
    ```
- **Result**: GitHub public profile email is unverified. An attacker setting their public GitHub profile email to an existing user's address takes over that account.

### Obs 10: User Profile Leaks Private Tags
- **Files & Lines**: `SRNSMudApp/Controllers/UserController.cs:31-92`, `SRNSMudApp/Services/UserDataProvider.cs:123-132`
- **Verbatim Code**:
  - `UserDataProvider.cs:123-125`:
    ```csharp
    var userTags = await context.Tags
        .AsNoTracking()
        .Where(t => t.OwnerId == userId && !t.IsAdminHidden)
        .Select(t => ...).ToListAsync();
    ```
- **Result**: Missing `.WhereVisibleToUser(context, currentUserId, isAdmin)` causes all private tags of any user to be returned on their public profile.

### Obs 11: Tag Diagram Data Provider Exposes Global Private Tags
- **Files & Lines**: `SRNSMudApp/Controllers/TagDiagramController.cs:46-52`, `SRNSMudApp/Services/TagDiagramDataProvider.cs:145-154`
- **Verbatim Code**:
  - `TagDiagramDataProvider.cs:146`: `query = query.WhereVisibleToUser();` calls the parameterless overload in `TagVisibilityExtensions.cs:30-36`, which only filters `!t.Owner.IsBanned && !t.IsAdminHidden`, leaving `t.IsPrivate` unfiltered.

### Obs 12: Information Disclosure via Split Requests
- **Files & Lines**: `SRNSMudApp/Controllers/ItemSplitController.cs:107-117`, `SRNSMudApp/Services/ItemSplitService.cs:60-140`
- **Verbatim Code**:
  - `ItemSplitController.cs:107`: `[HttpGet("item/{itemId}/pending")] [AllowAnonymous]` returns pending split requests containing `SelectedText` of private items without authorization check.

### Obs 13: Inconsistent Item Detail Logic Between REST API and Blazor Server
- **Files & Lines**: `SRNSMudApp/Controllers/ItemController.cs:174-195`, `SRNSMudApp/Services/ItemDetailDataProvider.cs:93-107`
- **Verbatim Code**:
  - `ItemController.cs:182-185`:
    ```csharp
    if (item.IsPrivate && item.OwnerId != currentUserId && !isAdmin)
    {
        return Forbid();
    }
    ```
- **Result**: The REST API fails to check follower status (`UserFollows`) or group membership (`UserGroupMembers`), blocking legitimate followers/group members, while omitting the `item.IsAdminHidden` check that Blazor Server enforces.

### Obs 14: Private Comments & Item IDs Leaked in Tag Detail Ledger
- **Files & Lines**: `SRNSMudApp/Controllers/TagController.cs:295-369, 449-486`, `SRNSMudApp/Services/TagDetailDataProvider.cs:124-153`
- **Verbatim Code**:
  - `TagDetailDataProvider.cs:140-147`: Projects `tr.ItemId`, `tr.Comment`, and `tr.Weight` for all tag relations without filtering `tr.Item.WhereVisibleToUser()`.

---

## 2. Logic Chain

1. **Premise**: In `SRNSMudApp`, confidential data boundaries are defined by `Item.IsPrivate`, `Item.TargetUserGroupId`, `Tag.IsPrivate`, and `Item.IsAdminHidden`. Access is meant to be restricted to:
   - The creator (`OwnerId == currentUserId`)
   - Authorized followers (when `TargetUserGroupId == null`)
   - Group members (when `TargetUserGroupId != null`)
   - Administrators (`isAdmin == true`)
2. **Failure Mechanism 1 (Direct Query Omissions)**:
   - `TagDiagramController` / `TagDiagramDataProvider` (Obs 1) and `LinkPreviewController` / `ItemLinkPreviewProvider` (Obs 2) query `context.Items` directly by primary key without invoking `WhereVisibleToUser` or checking user identity.
   - *Deduction*: Any anonymous attacker who knows or enumerates sequential integer Item IDs can exfiltrate private post text and metadata via these two public endpoints.
3. **Failure Mechanism 2 (Missing Entity Ownership / Authorization Checks)**:
   - `TagController.UpdateTag` (Obs 3) reads `currentUserId` from the claims principal but passes execution straight to `TagCommandService.UpdateTagAsync` without checking whether `tag.OwnerId == currentUserId` or `User.IsInRole("Admin")`.
   - `TagImportController.ImportTagging` (Obs 8) allows any authenticated user to create tags with `OwnerId = "system"`.
   - *Deduction*: Any authenticated user can hijack existing tags or forge system tags, subverting tag ownership integrity across the system.
4. **Failure Mechanism 3 (Indirect Composition & Propagation)**:
   - Quotes (`ItemQuoteService`, Obs 4) and Replies (`ItemReplyService`, Obs 5) allow public items to reference private items.
   - Neither quote creation nor reply creation verifies if the caller is authorized to view the target item, nor do they constrain child items from being public.
   - `ItemController.GetQuotes` and `ItemController.GetReplies` fail to apply user visibility filters to the returned collections.
   - *Deduction*: Private items can be leaked into public timelines either actively (by quoting/replying) or passively (when others query quotes/replies of known IDs).
5. **Failure Mechanism 4 (Cascade & State Transition Leaks)**:
   - In `ApplicationDbContext` (Obs 6), `Item.TargetUserGroup` has `OnDelete(DeleteBehavior.SetNull)`.
   - When a group is deleted, `TargetUserGroupId` becomes `null`, while `IsPrivate` remains `true`.
   - `ItemVisibilityExtensions` interprets `IsPrivate && TargetUserGroupId == null` as "visible to all author followers".
   - *Deduction*: Confidential intra-group posts are automatically broadcast to all of the author's followers whenever a group is disbanded.
6. **Failure Mechanism 5 (Authentication Identity Spoofing)**:
   - In `AuthController` / `ExternalTokenVerificationService` (Obs 9), OAuth account linking relies on `externalUser.Email` from GitHub's `/user` endpoint, which is not verified for ownership.
   - *Deduction*: An external attacker can take over local accounts by setting their public GitHub profile email to the target's email.
7. **Failure Mechanism 6 (Architectural Divergence)**:
   - Blazor Server providers (`ItemDetailDataProvider`) use `ItemVisibilityExtensions`, whereas REST API controllers (`ItemController`, Obs 13) use ad-hoc inline conditional checks.
   - *Deduction*: Legitimate users who can access private posts in the Blazor UI are blocked by the REST API, and content hidden by administrators can be retrieved via the REST API.

---

## 3. Caveats

1. **OAuth Verification**: The GitHub OAuth behavior was evaluated by static analysis of `ExternalTokenVerificationService.cs` and `AuthController.cs`. Live token round-trips against GitHub's API were not executed.
2. **Database Cascade Execution**: The `OnDelete(DeleteBehavior.SetNull)` behavior was analyzed via EF Core entity configurations in `ApplicationDbContext.cs`. Specific RDBMS foreign key definitions (e.g. SQLite vs SQL Server) will execute the nullification according to EF Core migration scripts.
3. **Client-Side Blazor Masking**: Blazor components may conditionally hide UI elements, but this investigation focuses strictly on server-side authorization and data exposure.
4. **Scope**: Non-authorization issues (e.g., performance tuning, CSS styling) were excluded.

---

## 4. Conclusion

The application has critical visibility and access control vulnerabilities that permit:
1. **Unauthenticated data exfiltration** of all private items via `TagDiagramController` and `LinkPreviewController`.
2. **Arbitrary tag modification / takeover (IDOR)** by any authenticated user via `TagController.UpdateTag`.
3. **Account takeover** via GitHub OAuth email matching.
4. **Information leakage and privilege escalation** through Quotes, Replies, Bounties, Split Requests, Group deletion cascades, and Tag Imports.

### Remediation Priorities
- **P0 (Immediate Hotfix)**:
  1. Patch `TagController.UpdateTag`: Check `tag.OwnerId == currentUserId || isAdmin`.
  2. Patch `TagDiagramDataProvider.GetContextItemsAsync` and `ItemLinkPreviewProvider.GetPreviewAsync`: Enforce `WhereVisibleToUser` / `IsItemVisibleToUserAsync`.
  3. Patch `AuthController.ProcessUserLoginAsync`: Require password confirmation or existing login link before attaching external OAuth identities.
- **P1 (High Priority)**:
  1. Enforce visibility rules on `ItemQuoteService` and `ItemReplyService` (prevent public quotes/replies to private items, apply `WhereVisibleToUser` on quote/reply queries).
  2. Restrict `TagImportController.ImportTagging` to Admins.
  3. Change `Item.TargetUserGroup` cascade delete behavior to `Cascade` or mark items as `IsAdminHidden = true` on group deletion.
- **P2 (Standardization)**:
  1. Consolidate REST controllers (`ItemController.GetItemById`, `UserController.GetUserById`) to use `ItemVisibilityExtensions` and `TagVisibilityExtensions`.
  2. Filter `tr.Item.WhereVisibleToUser()` in `TagDetailDataProvider`.

---

## 5. Verification Method

### 1. Automated Regression Tests (`dotnet test`)
Run the test suite from the repository root:
```bash
dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~Visibility|FullyQualifiedName~Auth"
```
To verify the fix for Issue 3 (Tag IDOR), add and execute an xUnit/MSTest unit test asserting that `TagController.UpdateTag` returns `403 Forbidden` when `tag.OwnerId != currentUserId`.

### 2. Manual Endpoint Verification (curl)

#### Verify Tag Diagram Private Item Exfiltration (Issue 1):
```bash
# As unauthenticated user, attempt to retrieve private Item 1:
curl -s -X GET "http://localhost:5000/api/tag-diagram/data?itemIds=1"
```
- **Vulnerable response**: HTTP 200 with JSON containing item content and tag relations.
- **Expected fixed response**: HTTP 200 with empty items list or HTTP 403/404.

#### Verify Link Preview Private Item Leak (Issue 2):
```bash
curl -s -X POST "http://localhost:5000/api/link/preview" \
  -H "Content-Type: application/json" \
  -d '{"url": "/ItemDetail/1"}'
```
- **Vulnerable response**: HTTP 200 with title "Item #1" and private text in "description".
- **Expected fixed response**: HTTP 200 with `{"isSuccess": false}` or generic masked title.

#### Verify Tag IDOR (Issue 3):
```bash
# Logged in as User B (token B), attempting to update tag owned by User A:
curl -s -X PUT "http://localhost:5000/api/tag/{TagIdOwnedByA}" \
  -H "Authorization: Bearer {TokenB}" \
  -H "Content-Type: application/json" \
  -d '{"name": "HijackedTag", "isPrivate": false, "autoApproveNewItems": true}'
```
- **Vulnerable response**: HTTP 200 `true`.
- **Expected fixed response**: HTTP 403 `Forbidden`.

#### Invalidation Conditions:
- If `TagDiagramController` or `ItemLinkPreviewProvider` still return non-empty data for private items to unauthenticated requests, verification fails.
- If `TagController.UpdateTag` allows a non-owner/non-admin user to edit a tag, verification fails.
