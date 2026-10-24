# Handoff Report: Domain 5 Investigation (Blazor Server vs WASM and API Validation Consistency)

## 1. Observation

Direct code observations across the repository:

1. **Missing `@rendermode` on 18 Client Feature Pages**:
   - In `SRNSMudApp/Components/App.razor:54`, the router is mounted as `<Routes />` without a top-level `@rendermode`.
   - In `SRNSMudApp.Client/Pages/`, 18 feature pages do not contain `@rendermode`:
     - `NotificationPage.razor`
     - `BountyBoardPage.razor`
     - `ContractManagementPage.razor`
     - `PublicOfferBoardPage.razor`
     - `RightAssetOverviewPage.razor`
     - `TagDetailPage.razor`
     - `AddTagPage.razor`
     - `TagListPage.razor`
     - `TagManagementPage.razor`
     - `ReportManagementPage.razor`
     - `UserManagementPage.razor`
     - `UserDetailPage.razor`
     - `UserSearchPage.razor`
     - `TagSearchPage.razor`
     - `TagImportPage.razor`
     - `TaggingImportPage.razor`
     - `RequireConfirmedAccountPage.razor`
     - `InvitationManagementPage.razor`
   - In `SRNSMudApp.Tests/Architecture/PageRenderModeTests.cs:45`, the test queries only `var appAssembly = typeof(App).Assembly;` and does not scan `SRNSMudApp.Client`.

2. **Catastrophic ID Confusion Bug (`TagId` vs `RelationId`)**:
   - `SRNSMudApp.Client/Models/Api/ItemListResponse.cs:23-28`:
     ```csharp
     public sealed record TagRelationDto(
         int TagId,
         string TagName,
         int? Weight,
         string? OwnerUserName
     );
     ```
     `TagRelationDto` omits `ItemTagRelation.Id`.
   - `SRNSMudApp/Controllers/ItemController.cs:199-206`: `MapToItemDto` instantiates `TagRelationDto` using `tr.TagId`, not `tr.Id`.
   - `SRNSMudApp.Client/Pages/ItemDetailPage.razor:118-122`:
     ```razor
     <MudIconButton Icon="@Icons.Material.Filled.Add" Size="Size.Small"
                    OnClick="@(() => ChangeWeightAsync(tr.TagId, 1))" aria-label="ウェイト加算" />
     <MudIconButton Icon="@Icons.Material.Filled.Close" Size="Size.Small" Color="Color.Error"
                    OnClick="@(() => RemoveTagAsync(tr.TagId))" aria-label="タグ削除" />
     ```
   - `SRNSMudApp.Client/Services/ItemListApiClient.cs:641-645`:
     ```csharp
     public async Task<bool> RemoveTagFromItemAsync(int itemId, int relationId, CancellationToken cancellationToken = default)
     {
         using HttpResponseMessage response = await _httpClient.DeleteAsync(new Uri($"api/item/{itemId}/tags/{relationId}", UriKind.Relative), cancellationToken);
         return response.IsSuccessStatusCode;
     }
     ```
   - `SRNSMudApp/Controllers/ItemController.cs:559-578` & `626-645`: Endpoints expect `relationId` (`ItemTagRelation` table primary key) and call `_itemTagService.RemoveTagRelationAsync(relationId, currentUserId)`.

3. **Tag Direct Overwrite Authorization Bypass**:
   - `SRNSMudApp/Controllers/TagController.cs:100-118`:
     ```csharp
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
     `currentUserId` is extracted but never forwarded to `_tagCommandService.UpdateTagAsync`.
   - `SRNSMudApp/Services/TagCommandService.cs:122-165`: `UpdateTagAsync` checks lock status, but never checks `tagToUpdate.OwnerId == currentUserId`.
   - `SRNSMudApp.Client/Pages/TagDetailPage.razor:193-220`: The UI conceals direct editing behind `@if (IsOwner)` and forces non-owners to use "名前を提案" (`TagProposalController.cs:38`).

4. **Public Offer Zero-Cost & Unowned Tag Exploit**:
   - `SRNSMudApp/Controllers/PublicOfferController.cs:72-111`: Does not verify if `currentUserId` owns `request.OfferedTagId` or has active `RightAsset`s. Does not check `RequiredAssetAmount > 0`.
   - `SRNSMudApp/Services/ContractExecutors/TriggerContractExecutor.cs:40-75`: On execution, creates a new `RightAsset` with `IsBurned = true` for the offer owner out of thin air.

5. **Bounty Self-Fulfillment**:
   - `SRNSMudApp.Client/ViewModels/BountyBoardViewModel.cs:69`: Sets `CanFulfill = !IsMyBounty && ...`.
   - `SRNSMudApp.Client/Pages/BountyBoardPage.razor:135`: Disables fulfillment button for the requester.
   - `SRNSMudApp/Controllers/BountyController.cs:142-166` & `SRNSMudApp/Services/TaggingContractService.cs:279-286`: In `preCheckResult`, `ContractTypes.Bounty` falls through with no check against `RequesterUserId == currentUserId`.

6. **Contract Rejection State Machine Inconsistency**:
   - `SRNSMudApp/Controllers/ContractController.cs:83-101`: `POST api/contract/{id}/reject` invokes `_taggingContractService.CancelContractAsync(id, currentUserId)`.
   - `SRNSMudApp/Services/TaggingContractService.cs:374-386`: `ProcessCancelAsync` sets `TradeStatus.Canceled` and invokes `entity.Cancel()`.
   - `SRNSMudApp/Data/TaggingRequestEntity.cs:107`: `public void Reject() => TransitionTo(TradeStatus.Rejected);` is never invoked anywhere in the codebase.

7. **Missing UI Guards on `ItemDetailPage.razor`**:
   - `SRNSMudApp.Client/Pages/ItemDetailPage.razor:185-194`: Renders "承認" and "却下" on `PendingRequests` without checking `@if (IsOwner)`.
   - Lines 117–123 render `+`, `-`, and `X` to unauthenticated visitors.

8. **Error Swallowing in `ItemListApiClient.cs`**:
   - `SRNSMudApp.Client/Services/ItemListApiClient.cs:517-535`: Returns `null` on HTTP errors without capturing response error bodies.
   - `SRNSMudApp.Client/Components/ItemSearchView.razor.cs:525`: Hardcodes error message: `"アイテムの作成に失敗しました。ログイン状態を確認してください。"`.

9. **DTO vs Entity Validation Mismatches**:
   - `SRNSMudApp/Models/Item.cs:17`: `[StringLength(1000)] public string Content`. DB column is `nvarchar(1000)`.
   - `SRNSMudApp.Client/Models/Api/ItemListRequest.cs:11`: `CreateItemRequest` has no `StringLength` or `MaxLength`.
   - `SRNSMudApp/Controllers/ItemController.cs:290-310`: Only checks `string.IsNullOrWhiteSpace(request.Content)`. Exceeding 1000 chars triggers SQL truncation 500 error.

---

## 2. Logic Chain

1. **Blazor Rendermodes**: In ASP.NET Core Blazor Web App, components without `@rendermode` under a non-interactive router default to Static SSR (Obs 1). Since 18 pages lack `@rendermode`, they render without WebAssembly or Server circuit event bindings. Therefore, any interactive elements on those 18 pages are non-functional when accessed. `PageRenderModeTests` failed to flag this because it only iterated `typeof(App).Assembly`.
2. **Tag Removal/Weight Modification Failure**: `ItemController` deletion and weight endpoints look up `ItemTagRelation.Id` (Obs 2). `TagRelationDto` only provides `TagId`. The client passes `TagId` as `relationId`. Because `ItemTagRelation.Id` rarely equals `TagId`, the lookup fails with 404/400 (or inadvertently alters an unrelated relation if an ID happens to match).
3. **Authorization Bypass on Tag Modification**: `TagController.UpdateTag` receives `currentUserId` but discards it without passing it to `_tagCommandService.UpdateTagAsync` (Obs 3). `TagCommandService.UpdateTagAsync` executes updates without verifying ownership. Therefore, any authenticated client can bypass the UI proposal workflow and directly overwrite any tag's name or content in the system.
4. **Economic / Logic Exploit in Public Offers**: `PublicOfferController.CreateOffer` performs no checks on tag ownership or non-zero requirements (Obs 4). `TriggerContractExecutor` mints a burned asset on completion. An attacker can offer unowned or high-value tags for 0 cost and gain artificial asset records.
5. **Contract State Machine Bug**: `ContractController.RejectContract` routes directly into `CancelContractAsync` (Obs 6), setting `TradeStatus.Canceled`. As a result, the domain event/transition `TradeStatus.Rejected` and `entity.Reject()` are unreachable, corrupting audit trails and preventing differentiation between proposal retraction and rejection.

---

## 3. Caveats

- **No Caveats.** All findings are verified by direct inspection of source code and tests.
- Note: This investigation was strictly read-only; no application source files or test files were modified.

---

## 4. Conclusion

The SRNSWebApp Blazor client and server API suffer from significant structural gaps in rendermode declarations (18 WASM pages dead in the browser), parameter mapping (`TagId` vs `relationId`), and authorization parity (tag direct editing, public offers, bounty self-fulfillment).
Addressing these issues requires a 3-phase remediation plan (Phase 1: RenderModes & DTO PK fixes; Phase 2: Authorization & Guard parity; Phase 3: Validation and Error handling standardization), as detailed in `analysis.md`.

---

## 5. Verification Method

To independently verify these findings:

1. **Verify Missing RenderModes on Client Pages**:
   Run grep across `SRNSMudApp.Client/Pages/`:
   ```bash
   grep -L "@rendermode" SRNSMudApp.Client/Pages/*.razor
   ```
   *Expected result*: Lists 18 pages missing the directive.

2. **Verify Architecture Test Blind Spot**:
   Inspect `SRNSMudApp.Tests/Architecture/PageRenderModeTests.cs:45`.
   Note that only `typeof(App).Assembly` is queried, while `typeof(SRNSMudApp.Client._Imports).Assembly` is omitted.

3. **Verify ID Confusion Bug**:
   Inspect `SRNSMudApp.Client/Models/Api/ItemListResponse.cs:23-28` vs `SRNSMudApp.Client/Pages/ItemDetailPage.razor:118-122` and `SRNSMudApp/Controllers/ItemController.cs:559-578`.
   Confirm that `tr.TagId` is passed into `RemoveTagFromItemAsync(ItemId, tagId)` which maps to route parameter `{relationId:int}`.

4. **Verify Tag Direct Overwrite Authorization Bypass**:
   Inspect `SRNSMudApp/Controllers/TagController.cs:100-118` and `SRNSMudApp/Services/TagCommandService.cs:122-165`.
   Confirm that `currentUserId` is never passed to `UpdateTagAsync`, and `tagToUpdate.OwnerId == currentUserId` is never asserted.

5. **Verify Contract Reject Dead Code**:
   Inspect `SRNSMudApp/Controllers/ContractController.cs:83-101` and search for all references to `TaggingRequestEntity.Reject`:
   ```bash
   rg "entity\.Reject\("
   ```
   *Expected result*: Only defined in `TaggingRequestEntity.cs:107` and never called by any service or controller.
