# Domain 5 Investigation Report: Blazor Server vs WASM and API Validation Consistency

**Author**: Teamwork Explorer (Domain 5 Investigator)  
**Date**: 2026-10-23  
**Target Repository**: `SRNSWebApp`  
**Scope**: Client-side (WASM / Client Services / Components / ViewModels) vs Server-side (Controllers / Server Services / EF Core / DTOs), validation discrepancies, UI vs API guards, lifecycle transitions, and error handling.

---

## Executive Summary

A comprehensive, read-only architectural investigation was conducted across `SRNSMudApp` (Server), `SRNSMudApp.Client` (WASM), and `SRNSMudApp.Tests`. The audit uncovered **10 significant discrepancies and architectural defects** across 4 primary risk categories:
1. **Critical Blazor Hosting Model & RenderMode Failures**: 18 of 25 WASM feature pages completely lack the `@rendermode` declaration. Under ASP.NET Core Blazor Web App conventions, this causes them to default to Static Server-Side Rendering (Static SSR), completely breaking interactivity (all MudBlazor button clicks, search queries, pagination, tabs, and form submissions are dead in the browser). The existing architectural test `PageRenderModeTests` failed to catch this due to inspecting only the Server assembly.
2. **Catastrophic ID Confusion Bug**: The DTO `TagRelationDto` omits the primary key `Id` of `ItemTagRelation`. As a result, the client UI passes `TagId` as `relationId` into `DELETE api/item/{id}/tags/{relationId}` and `PUT api/item/{id}/tags/{relationId}/weight`, resulting in persistent 404 errors or accidental modification/deletion of unrelated tag relations.
3. **Severe Authorization & Security Guard Asymmetries**: 
   - `TagController.UpdateTag` (`PUT api/tag/{id}`) completely ignores the caller's identity and checks no ownership or proposal workflow, allowing any authenticated user to directly overwrite any tag's name and description, completely bypassing the proposal system (`TagProposalController`).
   - `BountyController.FulfillBounty` and `TaggingContractService.AcceptContractAsync` lack self-fulfillment checks despite the UI explicitly disabling it.
   - `PublicOfferController.CreateOffer` allows offering any tag with 0 asset requirements without ownership checks, leading to arbitrary RightAsset minting.
   - UI on `ItemDetailPage.razor` displays approve/reject buttons to unauthorized visitors.
4. **Error Handling & State Machine Inconsistencies**:
   - `ContractController.RejectContract` sets contract status to `Canceled` instead of `Rejected`, leaving `TaggingRequestEntity.Reject()` as dead code and erasing audit differentiation.
   - `ItemListApiClient` swallows backend error payloads (`ProblemDetails`), causing WASM views to present misleading generic error dialogs.
   - String length and regex constraints present on EF Core entities and Server Blazor components are completely missing on API DTOs, producing raw 500 `DbUpdateException` errors when input exceeds database column boundaries.

---

## Findings Matrix

| ID | Title | Severity | Impact Area | Primary Files |
|---|---|---|---|---|
| **D5-01** | 18 WASM Feature Pages Lack `@rendermode` (Fallback to Static SSR) | **High** | Blazor Architecture | `SRNSMudApp.Client/Pages/*.razor`, `PageRenderModeTests.cs` |
| **D5-02** | Catastrophic ID Confusion Bug (`TagId` vs `RelationId`) in WASM | **High** | Data Integrity / Bug | `ItemListResponse.cs`, `ItemDetailPage.razor`, `ItemSearchView.razor.cs`, `ItemController.cs` |
| **D5-03** | Tag Direct Overwrite Authorization Bypass (Bypasses Proposal Workflow) | **High** | Authorization / Security | `TagController.cs`, `TagCommandService.cs`, `TagDetailPage.razor` |
| **D5-04** | Public Offer Zero-Cost & Unowned Tag Minting Exploit | **High** | Business Logic / Fraud | `PublicOfferController.cs`, `TriggerContractExecutor.cs`, `PublicOfferBoardPage.razor` |
| **D5-05** | Bounty Self-Fulfillment & Reward Asset Invalidation | **Medium** | Business Logic | `BountyController.cs`, `TaggingContractService.cs`, `BountyBoardViewModel.cs` |
| **D5-06** | Contract Rejection State Machine Inconsistency (`Canceled` vs `Rejected`) | **Medium** | State Machine / Audit | `ContractController.cs`, `TaggingContractService.cs`, `TaggingRequestEntity.cs` |
| **D5-07** | Missing UI Ownership & Auth Guards in `ItemDetailPage.razor` | **Medium** | UI / UX / Security | `ItemDetailPage.razor` |
| **D5-08** | Error Swallowing & Misleading Toast Messages in `ItemListApiClient` | **Medium** | Client Error Handling | `ItemListApiClient.cs`, `ItemSearchView.razor.cs` |
| **D5-09** | DTO vs Entity / DB Validation Asymmetries (Unbounded Lengths & Regex) | **Medium** | Validation & Stability | `ItemListRequest.cs`, `Item.cs`, `Tag.cs`, `ItemController.cs`, `TagController.cs` |
| **D5-10** | Notification URL Routing Fragmentation Between Server & WASM | **Low** | Navigation / UX | `NotificationController.cs`, `NotificationPage.razor`, `TargetUrl.cs` |

---

## Detailed Investigation Findings

---

### Finding D5-01: 18 WASM Feature Pages Lack `@rendermode` (Fallback to Static SSR) and Architecture Test Blind Spot

- **Severity**: **High**
- **Target Locations**:
  - `SRNSMudApp.Client/Pages/NotificationPage.razor:1`
  - `SRNSMudApp.Client/Pages/BountyBoardPage.razor:1`
  - `SRNSMudApp.Client/Pages/ContractManagementPage.razor:1`
  - `SRNSMudApp.Client/Pages/PublicOfferBoardPage.razor:1`
  - `SRNSMudApp.Client/Pages/RightAssetOverviewPage.razor:1`
  - `SRNSMudApp.Client/Pages/TagDetailPage.razor:1`
  - `SRNSMudApp.Client/Pages/AddTagPage.razor:1`
  - `SRNSMudApp.Client/Pages/TagListPage.razor:1`
  - `SRNSMudApp.Client/Pages/TagManagementPage.razor:1`
  - `SRNSMudApp.Client/Pages/ReportManagementPage.razor:1`
  - `SRNSMudApp.Client/Pages/UserManagementPage.razor:1`
  - `SRNSMudApp.Client/Pages/UserDetailPage.razor:1`
  - `SRNSMudApp.Client/Pages/UserSearchPage.razor:1`
  - `SRNSMudApp.Client/Pages/TagSearchPage.razor:1`
  - `SRNSMudApp.Client/Pages/TagImportPage.razor:1`
  - `SRNSMudApp.Client/Pages/TaggingImportPage.razor:1`
  - `SRNSMudApp.Client/Pages/RequireConfirmedAccountPage.razor:1`
  - `SRNSMudApp.Client/Pages/InvitationManagementPage.razor:1`
  - Architecture test: `SRNSMudApp.Tests/Architecture/PageRenderModeTests.cs:45`

#### Inconsistency Details
In ASP.NET Core 8/9/11 Blazor Web Apps, root component `App.razor` defines `<Routes />` without a top-level `@rendermode`. Consequently, component interactivity is determined per page.
In `SRNSMudApp.Client/Pages/`, only 7 pages declare `@rendermode InteractiveWebAssembly`:
- `ItemDetailPage.razor`
- `ItemSearchPage.razor`
- `TagTreePage.razor`
- `TagDiagramPage.razor`
- `UserGroupPage.razor`
- `UserGroupMembersPage.razor`
- `Auth.razor`

The other 18 pages contain **NO `@rendermode` declaration**.
When navigated to via client routes (e.g. `/Bounty/WasmBoard`, `/Tag/WasmDetail/{id}`, `/Notification/Wasm`), Blazor defaults their render mode to **Static SSR**. Event handlers (e.g., MudBlazor button `OnClick`, MudTable sorting/paging, tabs, filter expansions, form inputs) are completely inert in the user's browser.
Furthermore, `PageRenderModeTests.cs` (lines 42–87) was written specifically to ensure that all feature pages declare a render mode, but line 45 specifically queries:
```csharp
var appAssembly = typeof(App).Assembly;
```
It completely ignores `typeof(SRNSMudApp.Client._Imports).Assembly`, creating an architecture test blind spot.

#### Reproduction Scenario
1. Run `SRNSMudApp`.
2. Navigate directly to `/Bounty/WasmBoard` or `/Tag/WasmDetail/1`.
3. Click any button (e.g. "フィルター", "受諾", "提案する", pagination buttons).
4. **Observed**: Nothing happens. No network requests are fired, no dialog opens, because the page was rendered as Static SSR without a WebAssembly or Server circuit attached.

#### Recommended Remediation
1. Add `@rendermode InteractiveWebAssembly` (or `@rendermode new InteractiveWebAssemblyRenderMode(prerender: false)`) to all 18 pages in `SRNSMudApp.Client/Pages/`.
2. Update `PageRenderModeTests.cs` to test both assemblies:
   - `typeof(App).Assembly` (checking for `InteractiveServerRenderMode`)
   - `typeof(SRNSMudApp.Client._Imports).Assembly` (checking for `InteractiveWebAssemblyRenderMode`).

---

### Finding D5-02: Catastrophic ID Confusion Bug (`TagId` vs `RelationId`) in WASM Tag Operations

- **Severity**: **High**
- **Target Locations**:
  - `SRNSMudApp.Client/Models/Api/ItemListResponse.cs:23-28`
  - `SRNSMudApp/Controllers/ItemController.cs:199-206`, `559-578`, `626-645`
  - `SRNSMudApp.Client/Pages/ItemDetailPage.razor:118-122`, `512-516`
  - `SRNSMudApp.Client/Components/ItemSearchView.razor:269`
  - `SRNSMudApp.Client/Components/ItemSearchView.razor.cs:567-573`
  - `SRNSMudApp.Client/Services/ItemListApiClient.cs:641-645`

#### Inconsistency Details
In `SRNSMudApp.Client/Models/Api/ItemListResponse.cs`, the DTO `TagRelationDto` is defined as:
```csharp
public sealed record TagRelationDto(
    int TagId,
    string TagName,
    int? Weight,
    string? OwnerUserName
);
```
Notice that it contains `TagId`, but **does NOT contain the relation primary key `Id`** (`ItemTagRelation.Id`).
In `ItemController.cs` (lines 199–206), `MapToItemDto` maps:
```csharp
var relations = item.TagRelations
    .Select(tr => new TagRelationDto(
        TagId: tr.TagId,
        TagName: tr.Tag?.Name ?? string.Empty,
        Weight: tr.Weight,
        OwnerUserName: tr.Owner?.UserName ?? tr.Tag?.Owner?.UserName
    ))
    .ToList();
```
However, the backend endpoints for deleting tag relations and updating weights are:
- `DELETE api/item/{id}/tags/{relationId}` (`ItemController.cs:559`)
- `PUT api/item/{id}/tags/{relationId}/weight` (`ItemController.cs:626`)
- `PUT api/item/{id}/tags/{relationId}/change` (`ItemController.cs:583`)

In `ItemDetailPage.razor` (lines 118–122):
```razor
<MudIconButton Icon="@Icons.Material.Filled.Add" Size="Size.Small"
               OnClick="@(() => ChangeWeightAsync(tr.TagId, 1))" aria-label="ウェイト加算" />
<MudIconButton Icon="@Icons.Material.Filled.Close" Size="Size.Small" Color="Color.Error"
               OnClick="@(() => RemoveTagAsync(tr.TagId))" aria-label="タグ削除" />
```
And in `ItemSearchView.razor.cs` (lines 567–573):
```csharp
// ここでは tagId をキーとして RemoveTagFromItemAsync を呼び出す
bool success = await ApiClient.RemoveTagFromItemAsync(itemId, tagId);
```
Both WASM components pass `tr.TagId` (the Tag's ID) into the parameter expecting `relationId` (the `ItemTagRelation` table PK).
- If `ItemTagRelation` with `Id == tagId` does not exist, the API returns `404 NotFound` or `400 BadRequest`.
- If another item's tag relation happens to have primary key equal to this `tagId`, the API attempts to delete or mutate **an entirely different item's relation** (subject to user permission checks)!

#### Reproduction Scenario
1. Create an Item (e.g. ID = 10) and associate Tag with `TagId = 42`. The created `ItemTagRelation` has `Id = 150`.
2. In WASM `ItemDetailPage` (`/Item/WasmDetail/10`), click `+` on the tag chip or click the `X` delete button.
3. The client calls `DELETE api/item/10/tags/42`.
4. `ItemController.RemoveTag(10, 42)` calls `_itemTagService.RemoveTagRelationAsync(42, userId)`.
5. DB queries `ItemTagRelations.FindAsync(42)`. No relation with ID 42 exists (it is 150).
6. **Observed**: Server returns 400 "タグ関連付けが見つかりません。" and WASM displays "タグの削除に失敗しました。"

#### Recommended Remediation
1. Update `TagRelationDto` in `SRNSMudApp.Client/Models/Api/ItemListResponse.cs`:
   ```csharp
   public sealed record TagRelationDto(
       int Id,        // ItemTagRelation PK
       int TagId,
       string TagName,
       int? Weight,
       string? OwnerUserName
   );
   ```
2. Update `ItemController.cs:200` to pass `Id: tr.Id`.
3. Update `ItemDetailPage.razor` and `ItemSearchView.razor` to invoke `RemoveTagAsync(tr.Id)` and `ChangeWeightAsync(tr.Id, delta)`.

---

### Finding D5-03: Tag Direct Overwrite Authorization Bypass (Bypasses Proposal Workflow)

- **Severity**: **High**
- **Target Locations**:
  - `SRNSMudApp/Controllers/TagController.cs:94-126`
  - `SRNSMudApp/Services/TagCommandService.cs:122-165`
  - `SRNSMudApp.Client/Pages/TagDetailPage.razor:193-220`
  - Compare with: `SRNSMudApp/Controllers/TagProposalController.cs:124-177`

#### Inconsistency Details
The system architecture specifies that tags owned by other users cannot be directly modified; changes must be submitted as proposals (`TagNameProposal` / `TagContentProposal`) via `TagProposalController`, which requires approval by the tag owner.
In the UI (`TagDetailPage.razor` lines 193–220):
- If `IsOwner` is true, the user sees the "編集" button.
- If `IsOwner` is false, the user is prevented from editing and only offered "名前を提案" and "説明を提案".

However, in `TagController.UpdateTag` (`PUT api/tag/{id}`):
```csharp
[HttpPut("{id:int}")]
public async Task<ActionResult> UpdateTag(
    int id,
    [FromBody] UpdateTagRequest request,
    CancellationToken cancellationToken)
{
    string? currentUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrEmpty(currentUserId))
    {
        return Unauthorized("タグを更新するにはログインが必要です。");
    }
    // ...
    var isAdmin = User?.IsInRole("Admin") ?? false;
    bool success = await _tagCommandService.UpdateTagAsync(
        tagId: id,
        name: request.Name.Trim(),
        content: request.Content?.Trim(),
        isAdmin: isAdmin,
        isPrivate: request.IsPrivate,
        targetUserGroupId: request.TargetUserGroupId);
```
Notice:
1. `currentUserId` is extracted at line 100, checked for null, but **never passed to `_tagCommandService.UpdateTagAsync`**!
2. In `TagCommandService.UpdateTagAsync` (lines 122–165), it only checks `_tagLockService.IsTagOrSiblingLockedAsync(tagId)`. It **NEVER verifies `tagToUpdate.OwnerId == currentUserId` or `isAdmin`**!
Any authenticated user can issue a `PUT api/tag/{id}` request with arbitrary name/content and overwrite any tag in the database, including tags owned by other users or administrators, completely circumventing the proposal workflow.

#### Reproduction Scenario
1. User A creates Tag "Physics" (ID: 5).
2. User B logs in. In the UI, User B cannot edit Tag 5 and can only submit a proposal.
3. User B sends `PUT /api/tag/5` with body `{"name":"Defaced","content":"Hacked"}`.
4. `TagController.UpdateTag` checks `User.Identity.IsAuthenticated` (true), calls `_tagCommandService.UpdateTagAsync(5, "Defaced", "Hacked", ...)`.
5. **Observed**: Tag 5's name is overwritten to "Defaced", without owner consent or proposal creation.

#### Recommended Remediation
1. Update `ITagCommandService.UpdateTagAsync` and its implementation to accept `currentUserId`.
2. Enforce:
   ```csharp
   if (!isAdmin && tagToUpdate.OwnerId != currentUserId)
   {
       return false; // Or throw/return unauthorized
   }
   ```
3. In `TagController.UpdateTag`, pass `currentUserId` into `UpdateTagAsync`, and return `Forbid("タグの編集権限がありません。変更を希望する場合は提案機能を利用してください。")` on failure.

---

### Finding D5-04: Public Offer Zero-Cost & Unowned Tag Minting Exploit

- **Severity**: **High**
- **Target Locations**:
  - `SRNSMudApp/Controllers/PublicOfferController.cs:72-111`
  - `SRNSMudApp/Services/ContractExecutors/TriggerContractExecutor.cs:40-75`
  - `SRNSMudApp.Client/Pages/PublicOfferBoardPage.razor:110-140`

#### Inconsistency Details
In `PublicOfferController.CreateOffer` (`POST api/public-offer`):
```csharp
TaggingRequestEntity offer = new()
{
    ContractType = ContractTypes.Trigger,
    OwnerId = currentUserId,
    RequesterUserId = currentUserId,
    TagOwnerUserId = tagData.Tag.OwnerId,
    RequestedTagId = request.OfferedTagId,
    TargetItemId = 0,
    Payload = new TriggerPayload(request.RequiredAssetAmount)
};
```
1. **No Tag Ownership Verification**: Any user can create an offer where `OfferedTagId` is a tag they do not own and have no RightAssets for!
2. **Zero-Cost Asset Creation**: `request.RequiredAssetAmount` is not validated for `> 0`. A user can set `RequiredAssetAmount = 0`.
3. When another user accepts/triggers this offer, `TriggerContractExecutor.ExecuteAsync` executes:
   ```csharp
   RightAsset newBurnedAsset = new()
   {
       OwnerId = offer.OwnerId,
       TargetTagId = offer.RequestedTagId,
       Amount = payload.RequiredAmount,
       IsBurned = true
   };
   dbContext.RightAssets.Add(newBurnedAsset);
   ```
   This generates a burned `RightAsset` for the offer creator out of thin air, without deducting any assets from the triggerer, and attaches the tag to the target item!

#### Reproduction Scenario
1. User A creates a public offer via API: `POST /api/public-offer` with `OfferedTagId: 1` (a rare/system tag owned by Admin) and `RequiredAssetAmount: 0`.
2. Offer is published to the public board.
3. User B views `/PublicOffer/WasmBoard` and clicks "このオファーを適用してタグ付け".
4. The contract executes: User B gets the tag attached to their item for free, and User A receives minted RightAsset records without having held the tag or rights.

#### Recommended Remediation
1. In `PublicOfferController.CreateOffer`, validate that `request.RequiredAssetAmount > 0`.
2. Verify that the offer creator owns the tag (`tagData.Tag.OwnerId == currentUserId`) or possesses sufficient active `RightAsset` balance for `OfferedTagId`.

---

### Finding D5-05: Bounty Self-Fulfillment & Reward Asset Invalidation

- **Severity**: **Medium**
- **Target Locations**:
  - `SRNSMudApp/Controllers/BountyController.cs:100-137`, `142-166`
  - `SRNSMudApp/Services/TaggingContractService.cs:279-286`
  - `SRNSMudApp.Client/ViewModels/BountyBoardViewModel.cs:65-72`
  - `SRNSMudApp.Client/Pages/BountyBoardPage.razor:130-145`

#### Inconsistency Details
1. **Self-Fulfillment UI vs API Mismatch**:
   - In `BountyBoardViewModel.cs` (line 69), `CanFulfill` is computed:
     `CanFulfill = !IsMyBounty && ...`
   - In `BountyBoardPage.razor`, the fulfillment button is disabled and labeled "自分の依頼" when `bounty.RequesterUserId == currentUserId`.
   - In `BountyController.FulfillBounty` and `TaggingContractService.AcceptContractAsync` (lines 279–286), the pre-check validates:
     ```csharp
     { ContractType: ContractTypes.Trigger, RequesterUserId: var reqId } when reqId != currentUserId => new Failure("実行できない契約です。"),
     { ContractType: ContractTypes.Gratis or ContractTypes.Mutual, TagOwnerUserId: var ownerId } when ownerId != currentUserId => new Failure("承認できない契約です。"),
     _ => new Success<TaggingRequestEntity>(entity)
     ```
     There is **NO guard for `ContractTypes.Bounty` checking `RequesterUserId == currentUserId`**! A user can bypass the disabled UI button and fulfill their own bounty via `POST api/bounty/{id}/fulfill`.
2. **Missing Reward Asset Ownership Guard on Creation**:
   - In `BountyController.CreateBounty` (lines 100–127), `request.OfferedRewardAssetId` is accepted blindly without checking if the asset exists, belongs to `currentUserId`, or is already burned.
   - If an invalid or unowned asset ID is supplied, the bounty is created and displayed on the public board. When another user fulfills it, `BountyContractExecutor.cs` fails with an exception during transfer, causing unexpected transaction aborts.

#### Reproduction Scenario
1. User A creates a bounty for Tag 3 on Item 10.
2. In the WASM Bounty Board, User A sees "自分の依頼" disabled.
3. User A issues `POST /api/bounty/{bountyId}/fulfill` via HTTP client.
4. `TaggingContractService.AcceptContractAsync` processes the request because the bounty switch clause falls through to `Success`.
5. **Observed**: User A fulfills their own bounty, tags the item, and transfers the reward asset back to themselves.

#### Recommended Remediation
1. In `TaggingContractService.AcceptContractAsync`, add a guard to `preCheckResult`:
   ```csharp
   { ContractType: ContractTypes.Bounty, RequesterUserId: var reqId } when reqId == currentUserId => new Failure("自分のバウンティは完了できません。"),
   ```
2. In `BountyController.CreateBounty`, validate `request.OfferedRewardAssetId`: verify that the asset exists, `asset.OwnerId == currentUserId`, and `!asset.IsBurned`.

---

### Finding D5-06: Contract Rejection State Machine Inconsistency (`Canceled` vs `Rejected`)

- **Severity**: **Medium**
- **Target Locations**:
  - `SRNSMudApp/Controllers/ContractController.cs:83-101`, `106-124`
  - `SRNSMudApp/Services/TaggingContractService.cs:351-386`
  - `SRNSMudApp/Data/TaggingRequestEntity.cs:105-115`
  - `SRNSMudApp/Data/TradeStatus.cs`

#### Inconsistency Details
In `ContractController.cs`:
- `POST api/contract/{id}/reject` (Rejecting an incoming contract offer)
- `POST api/contract/{id}/cancel` (Canceling an outgoing contract request)
Both endpoints invoke the exact same method:
```csharp
Result<string> result = await _taggingContractService.CancelContractAsync(id, currentUserId);
```
In `TaggingContractService.ProcessCancelAsync`:
```csharp
int updatedRows = await dbContext.TaggingRequestEntities
    .Where(e => e.Id == entity.Id && e.Status == TradeStatus.Proposed)
    .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, TradeStatus.Canceled));
entity.Cancel();
```
Both actions unconditionally transition the contract to `TradeStatus.Canceled`.
However:
- `TradeStatus.Rejected` exists in `TradeStatus.cs`.
- `TaggingRequestEntity.Reject()` exists in `TaggingRequestEntity.cs`:
  ```csharp
  public void Reject() => TransitionTo(TradeStatus.Rejected);
  ```
Because `CancelContractAsync` is called for both operations, `TaggingRequestEntity.Reject()` is dead code.
In the database, contract logs, and UI, it is impossible to distinguish between a requester who retracted their proposal and a receiver who rejected it.

#### Recommended Remediation
1. Add `RejectContractAsync(int contractId, string currentUserId)` to `ITaggingContractService` and `TaggingContractService`.
2. Transition the entity status to `TradeStatus.Rejected` and invoke `entity.Reject()`.
3. In `ContractController.RejectContract`, call `_taggingContractService.RejectContractAsync(id, currentUserId)`.

---

### Finding D5-07: Missing UI Ownership & Auth Guards in `ItemDetailPage.razor`

- **Severity**: **Medium**
- **Target Locations**:
  - `SRNSMudApp.Client/Pages/ItemDetailPage.razor:117-123`, `185-194`
  - Compare with: `SRNSMudApp.Client/Pages/ItemDetailPage.razor:216-234`

#### Inconsistency Details
In `ItemDetailPage.razor`:
1. **Pending Tagging Requests (Lines 185–194)**:
   ```razor
   <div class="d-flex align-center gap-2">
       <MudButton Variant="Variant.Filled" Color="Color.Success" Size="Size.Small"
                  OnClick="@(() => ApproveRequestAsync(req.Id))">
           承認
       </MudButton>
       <MudButton Variant="Variant.Outlined" Color="Color.Error" Size="Size.Small"
                  OnClick="@(() => RejectRequestAsync(req.Id))">
           却下
       </MudButton>
   </div>
   ```
   Unlike `PendingSplitRequests` (lines 216–226), which properly checks `@if (IsOwner)`, `PendingRequests` has **no ownership or auth check**.
   Any unauthenticated visitor or non-owner viewing the page sees active "承認" and "却下" buttons. Clicking them issues an API call that returns 401 Unauthorized or 400 BadRequest.
2. **Tag Chip Action Buttons (Lines 117–123)**:
   `+`, `-`, and `X` (delete) buttons are displayed unconditionally on each tag chip, regardless of whether the user is logged in or owns the item/tag relation. Clicking them fails with error toasts for unauthenticated visitors.

#### Recommended Remediation
1. Wrap lines 185–194 in `@if (IsOwner)`.
2. Wrap lines 117–123 in `@if (IsOwner || !string.IsNullOrEmpty(_currentUserId))`.

---

### Finding D5-08: Error Swallowing & Misleading Toast Messages in `ItemListApiClient`

- **Severity**: **Medium**
- **Target Locations**:
  - `SRNSMudApp.Client/Services/ItemListApiClient.cs:517-535`, `635-645`, `800-830`
  - `SRNSMudApp.Client/Components/ItemSearchView.razor.cs:520-540`
  - `SRNSMudApp/Controllers/ItemController.cs:290-330`

#### Inconsistency Details
In `ItemListApiClient.cs`:
Methods such as `CreateItemAsync`, `AddReplyAsync`, and `CreateTagAsync` execute:
```csharp
using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/item", request, cancellationToken);
if (!response.IsSuccessStatusCode)
{
    return null; // or false
}
```
The server's response content (which contains detailed `BadRequest("理由")` or `ProblemDetails`) is discarded.
Then, in `ItemSearchView.razor.cs`:
```csharp
ItemDto? created = await ApiClient.CreateItemAsync(request);
if (created == null)
{
    Snackbar.Add("アイテムの作成に失敗しました。ログイン状態を確認してください。", Severity.Error);
}
```
If an item creation fails because the user entered invalid data (or exceeded DB limits), the UI tells the user: "ログイン状態を確認してください。" (Please check your login status). The user is mislead into thinking their session expired.
Furthermore, in `GetItemByIdAsync`:
```csharp
public async Task<ItemDto?> GetItemByIdAsync(int id, CancellationToken cancellationToken = default)
{
    return await _httpClient.GetFromJsonAsync<ItemDto>($"api/item/{id}", cancellationToken);
}
```
`GetFromJsonAsync` throws `HttpRequestException` on non-success status codes (e.g. 404 or 403). `ItemDetailPage.razor` does not catch `HttpRequestException`, causing unhandled exceptions in the WASM runtime rather than displaying a graceful "Not Found" state.

#### Recommended Remediation
1. In `ItemListApiClient.cs`, read `await response.Content.ReadAsStringAsync()` or parse `ProblemDetails` when `!response.IsSuccessStatusCode`, and return a result union or informative exception.
2. In `GetItemByIdAsync`, catch `HttpRequestException` when status code is 404 and return `null`.

---

### Finding D5-09: DTO vs Entity / DB Validation Asymmetries (Unbounded Lengths & Regex)

- **Severity**: **Medium**
- **Target Locations**:
  - `SRNSMudApp.Client/Models/Api/ItemListRequest.cs:1-50`
  - `SRNSMudApp/Models/Item.cs:17`
  - `SRNSMudApp/Models/Tag.cs:14-22`
  - `SRNSMudApp/Controllers/ItemController.cs:280-320`
  - `SRNSMudApp/Controllers/TagController.cs:40-75`
  - `SRNSMudApp.Client/Components/ItemSearchView.razor` vs `SRNSMudApp/Components/Pages/AddItem.razor`

#### Inconsistency Details
1. **Item Content Length Constraint**:
   - `Item.cs:17`: `[StringLength(1000)] public string Content { get; set; } = string.Empty;`
   - Database schema: `nvarchar(1000)`
   - Server Blazor page `AddItem.razor`: Enforces max 1000 chars with live counter.
   - Client DTO `CreateItemRequest`:
     ```csharp
     public sealed record CreateItemRequest(
         string Content,
         bool IsPrivate = false,
         string? TargetUserGroupName = null,
         IReadOnlyList<TagRelationDto>? TargetTagRelations = null
     );
     ```
     Has **NO `[StringLength(1000)]` or `[MaxLength(1000)]` attribute**.
   - `ItemController.CreateItem`: Only checks `if (string.IsNullOrWhiteSpace(request.Content)) return BadRequest(...)`.
   - If a client posts 1001 characters via WASM or API, EF Core executes `INSERT`, SQL Server throws `SqlException: String or binary data would be truncated`, and the server returns a raw **500 Internal Server Error**.
2. **Tag Name Constraints**:
   - `Tag.cs`:
     ```csharp
     [Required]
     [MaxLength(100)]
     [RegularExpression(@"^[^/\\:\*\?""<>\|\s\u3000]+$", ErrorMessage = "...")]
     public string Name { get; set; } = string.Empty;
     ```
   - `CreateTagRequest`: Contains no validation attributes.
   - `TagController.CreateTag`: Only checks `string.IsNullOrWhiteSpace(request.Name)`. Regex and length checks are completely bypassed until EF Core entity validation or DB insert.
3. **ContentReport Reason**:
   - `ContentReport.cs`: `[Required] [MaxLength(100)] public string Reason { get; set; }`
   - `CreateContentReportRequest`: No validation attributes. `ReportController.CreateReport` fails to validate empty reasons.

#### Recommended Remediation
1. Decorate all API request DTOs in `SRNSMudApp.Client/Models/Api/` with the corresponding DataAnnotations (`[Required]`, `[MaxLength]`, `[RegularExpression]`).
2. Add `[ApiController]` automatic model validation or explicit `ModelState.IsValid` validation before executing service logic.

---

### Finding D5-10: Notification URL Routing Fragmentation Between Server & WASM

- **Severity**: **Low**
- **Target Locations**:
  - `SRNSMudApp/Controllers/NotificationController.cs:38-48`
  - `SRNSMudApp.Client/Pages/NotificationPage.razor:95-115`
  - `SRNSMudApp/Models/TargetUrl.cs`

#### Inconsistency Details
- `NotificationController.GetNotifications` returns `TargetPath = n.TargetUrl.ToHref()`.
  The Server-generated URLs are `/ItemDetail/{id}`, `/TagDetail/{id}`, `/tag-tree`.
- In `NotificationPage.razor` (lines 95–115), the WASM page completely ignores `TargetPath`! Instead, it attempts manual route construction:
  ```razor
  @if (item.AssociatedItemId > 0)
  {
      <MudLink Href="@($"/Item/WasmDetail/{item.AssociatedItemId}")" ...>
  }
  else if (item.HighlightTagId.HasValue && item.HighlightTagId.Value > 0)
  {
      <MudLink Href="@($"/Tag/WasmDetail/{item.HighlightTagId.Value}")" ...>
  }
  ```
  Any notification pointing to user groups, contracts, proposals, or trees that do not have `AssociatedItemId > 0` or `HighlightTagId > 0` are rendered without clickable links in the WASM notification center.

#### Recommended Remediation
1. Unify routing conventions across Server and WASM (e.g. use standard `/items/{id}` or route redirects).
2. In `NotificationPage.razor`, use `item.TargetPath` if present, with fallback to item/tag links.

---

## Architectural Remediation Plan

```
Phase 1: Critical Stability & Data Integrity (Immediate)
├── 1. Add @rendermode InteractiveWebAssembly to the 18 Client pages.
├── 2. Update PageRenderModeTests to assert both Server & Client assemblies.
└── 3. Add 'Id' (relation PK) to TagRelationDto and fix ItemDetailPage/ItemSearchView calls.

Phase 2: Authorization & Security Hardening
├── 1. Add tag ownership verification to TagController.UpdateTag & TagCommandService.
├── 2. Prevent bounty self-fulfillment in TaggingContractService.AcceptContractAsync.
├── 3. Enforce tag ownership & asset balance in PublicOfferController.CreateOffer.
└── 4. Add @if (IsOwner) checks to ItemDetailPage pending requests and tag actions.

Phase 3: State Machine & Validation Standardization
├── 1. Introduce RejectContractAsync in TaggingContractService and call TaggingRequestEntity.Reject().
├── 2. Mirror EF Core entity validation attributes (StringLength, Regex) on all API DTOs.
└── 3. Propagate ProblemDetails from ItemListApiClient to avoid generic/misleading toasts.
```
