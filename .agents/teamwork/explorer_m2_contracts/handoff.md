# Handoff Report: Domain 2 Audit (Tags, Bounties, and Contracts)

## 1. Observation

Direct code observations from `SRNSMudApp` inspection:

1. **Missing TargetId Assignment in Bounty and Trigger Controllers**:
   - `SRNSMudApp/Controllers/BountyController.cs:118-127`:
     ```csharp
     TaggingRequestEntity bounty = new()
     {
         ContractType = "Bounty",
         OwnerId = currentUserId,
         RequesterUserId = currentUserId,
         TagOwnerUserId = tagData.Tag.OwnerId,
         TargetItemId = request.TargetItemId,
         RequestedTagId = request.RequestedTagId,
         Payload = new BountyPayload(request.OfferedRewardAssetId ?? 0)
     };
     ```
   - `SRNSMudApp/Data/TaggingRequestEntity.cs:24-28`:
     ```csharp
     [NotMapped]
     public int TargetItemId
     {
         get => Target?.Item?.Id ?? _targetItem?.Id ?? _targetItemId;
         set => _targetItemId = value;
     }
     ```
   - `SRNSMudApp/Data/ApplicationDbContext.cs:430-434`:
     ```csharp
     _ = builder.Entity<TaggingRequestEntity>()
         .HasOne(e => e.Target)
         .WithMany(t => t.TaggingRequests)
         .HasForeignKey(e => e.TargetId)
         .OnDelete(DeleteBehavior.Restrict);
     ```
   - `SRNSMudApp/Services/TaggingContractService.cs:235`:
     ```csharp
     .Where(r => r.TargetId == item.TagTargetId)
     ```
   - Setting `TargetItemId` does NOT populate `TargetId` (it remains 0). Queries by `item.TagTargetId` return no matching records.

2. **Move Contract Authorization Bypass & HierarchyId Severing**:
   - `SRNSMudApp/Services/TaggingContractService.cs:279-286`:
     ```csharp
     Result<bool> preCheck = contract.ContractType switch
     {
         ContractTypes.Gratis or ContractTypes.Mutual =>
             contract.TagOwnerUserId == currentUserId
                 ? new Success<bool>(true)
                 : new Failure("このコントラクトを承認する権限がありません。"),
         _ => new Success<bool>(true)
     };
     ```
     `Move` contracts fall into the wildcard branch and are accepted by anyone.
   - `SRNSMudApp/Services/Contracts/MoveContractExecutor.cs:68-70`:
     ```csharp
     tagToUpdate.Node = parentNode.GetDescendant(lastChild, null);
     tagToUpdate.ParentTagId = newParentTagId;
     tagToUpdate.UpdatedDate = DateTime.UtcNow;
     ```
     Only `tagToUpdate.Node` is updated; all descendant tags' `HierarchyId` nodes are left pointing to the old prefix, permanently corrupting the tree structure.

3. **Universal Bounty and Trigger Rejection Vulnerability**:
   - `SRNSMudApp/Services/TaggingService.cs:111-115`:
     ```csharp
     RejectAuthorization authState = (request.TagOwnerUserId == rejectUserId ||
                                      request.RequesterUserId == rejectUserId ||
                                      request.ContractType == "Trigger" ||
                                      request.ContractType == "Bounty") switch
     {
         true => new AuthorizedToReject(),
         false => new UnauthorizedToReject()
     };
     ```
     Because `request.ContractType == "Bounty"` and `request.ContractType == "Trigger"` have no equality check with `rejectUserId`, any authenticated user is authorized to reject any Bounty or Trigger contract in the database.

4. **Bounty Reward Double Spending & Lack of Escrow**:
   - `SRNSMudApp/Controllers/BountyController.cs:126`: Bounty is created with `BountyPayload(request.OfferedRewardAssetId)`. No deduction or escrow occurs.
   - `SRNSMudApp/Services/Contracts/BountyContractExecutor.cs:195-202`:
     ```csharp
     RightAsset? rewardAsset = await dbContext.RightAssets
         .FirstOrDefaultAsync(a => a.Id == rewardAssetId && a.OwnerId == contract.RequesterUserId);
     Result<bool> authResult = (rewardAsset?.IsBurned == false) switch
     {
         true => CompleteRewardTransfer(dbContext, rewardAsset, fulfillerUserId),
         false => new Failure("約束された報酬アセットが無効になっています。")
     };
     ```
     If the requester transfers, burns, or pledges the asset elsewhere, fulfillment fails, leaving the bounty in `TradeStatus.Proposed` forever with no cancellation endpoint.

5. **Mutual Weight vs Asset Consumption Asymmetry & Ghost Minting**:
   - `SRNSMudApp/Services/Contracts/MutualContractExecutor.cs:57`:
     `consumedAsset.Amount -= 1;` (always subtracts 1 regardless of `contract.ProposedWeight`).
   - `SRNSMudApp/Services/Contracts/MutualContractExecutor.cs:197-204`:
     ```csharp
     var offeredTagAsset = new RightAsset
     {
         OwnerId = contract.RequesterUserId,
         TargetTagId = mutualPayload.OfferedTagId,
         IsBurned = true,
         Status = new Burned(_timeProvider.GetUtcNow().UtcDateTime)
     };
     _ = dbContext.RightAssets.Add(offeredTagAsset);
     ```
     When a Mutual contract requests tag removal (`TaggingRequestType.Remove`), line 91 executes `ProcessMutualOfferedAsync`, unconditionally adding the offered tag and minting a burned `RightAsset` without balance validation.

6. **Silent Desynchronization of TagRelation, TagWeightLedger, and CachedWeight**:
   - `SRNSMudApp/Services/ItemTagService.cs:308`:
     `entity.TagId = newTagId;` updates tag ID without decrementing old tag weight, incrementing new tag weight, or writing to `TagWeightLedger`.
   - `SRNSMudApp/Services/TagWeightLedgerService.cs:272-294`: `AddTagAsync` updates `TagRelation` and `tag.CachedWeight += 1` but writes zero records to `TagWeightLedger`.
   - `SRNSMudApp/Services/TaggingImportDataProvider.cs:239`: Bulk-creates `TagRelation` without updating `tag.CachedWeight` or `TagWeightLedger`.
   - `SRNSMudApp/Services/ItemCardDataProvider.cs:112`: `context.Items.Remove(item)` cascades deletion to `TagRelation`s, leaving phantom weight on `Tag.CachedWeight`.

7. **Foreign Key Violation on Tag Deletion**:
   - `SRNSMudApp/Data/ApplicationDbContext.cs:461-465`: `RightAsset` foreign key to `TargetTag` has `OnDelete(DeleteBehavior.Restrict)`.
   - `SRNSMudApp/Data/ApplicationDbContextTagExtensions.cs:190-265`: `RemoveTagRestrictedDependenciesAsync` deletes dependent collections 1-9 but leaves `RightAsset` untouched, triggering a SQL foreign key exception upon `context.Tags.Remove(tag)`.

8. **Information Disclosure of Private Tags via RightAsset Overview**:
   - `SRNSMudApp/Controllers/RightAssetController.cs:57-58`: `[HttpGet("overview/{tagId:int}")] [AllowAnonymous]`
   - `SRNSMudApp/Services/RightAssetDataProvider.cs:79-93`: Queries `Tags` without `WhereVisibleToUser()` and returns holder user IDs, user names, and asset balances.

---

## 2. Logic Chain

1. **Bounty & Trigger FK Disconnection**:
   - *Premise*: EF Core requires foreign key `TargetId` to join `TaggingRequestEntity` with `TaggableTarget`.
   - *Observation*: `BountyController:124` assigns only `TargetItemId = request.TargetItemId`. `TaggingRequestEntity:27` assigns only `_targetItemId` backing field without setting `TargetId`.
   - *Deduction*: `bounty.TargetId` defaults to `0`. `TaggingContractService.GetRequestsByItemIdAsync` queries `Where(r => r.TargetId == item.TagTargetId)`.
   - *Conclusion*: Since `0 != item.TagTargetId`, queries for item bounties/triggers return empty, and `bounty.Target` remains null on the Bounty Board, breaking UI content rendering.

2. **Move Contract Authorization & Hierarchy**:
   - *Premise*: Hierarchy modifications must be authorized by the tag owner and must maintain tree node invariants.
   - *Observation*: `TaggingContractService:279-286` validates owner identity only for `Gratis` and `Mutual`. `MoveContractExecutor:68-70` sets `tagToUpdate.Node` without calling `GetReparentedValue` on descendants.
   - *Conclusion*: Any user can approve a Move contract for any tag, and execution severs all descendant nodes from the tree.

3. **Universal Bounty/Trigger Cancellation**:
   - *Premise*: Only stakeholders should be able to reject tag contracts.
   - *Observation*: `TaggingService:111-115` has `(request.ContractType == "Trigger" || request.ContractType == "Bounty") switch { true => new AuthorizedToReject() }`.
   - *Conclusion*: The boolean condition does not test user ID. Any caller can reject any bounty or trigger.

4. **Bounty Double Spending & Zombies**:
   - *Premise*: Promised rewards must be guaranteed available at settlement.
   - *Observation*: `BountyController:126` does not escrow the reward asset. `BountyContractExecutor:196-201` fails if the asset is missing or burned.
   - *Conclusion*: Requesters can spend the asset elsewhere; subsequent fulfillment attempts fail, and with no bounty cancellation endpoint, the bounty becomes an unclaimable zombie on the board.

5. **Ledger & Weight Desynchronization**:
   - *Premise*: `Tag.CachedWeight` must equal the sum of active `TagRelation.Weight` values, and all changes must be audited in `TagWeightLedger`.
   - *Observation*: `ItemTagService.ExecuteChangeItemTagAsync` mutates `TagRelation.TagId` without updating weights or inserting ledger rows. `ItemCardDataProvider.DeleteItemByAdminAsync` deletes relations via cascade without decrementing `CachedWeight`.
   - *Conclusion*: Tag weights and ledgers diverge over time, producing phantom weights and corrupt audit trails.

---

## 3. Caveats

- **Test Execution Environment**: The investigation was conducted strictly read-only by code inspection and pattern matching against the codebase. No unit test was run in this turn to avoid mutating the workspace.
- **SQL Server vs In-Memory/SQLite Differences**: In SQLite or InMemory fakes, foreign key restrict constraints or `HierarchyId` operations may behave differently than on production SQL Server (`Microsoft.EntityFrameworkCore.SqlServer.HierarchyId`). On real SQL Server, setting `TargetId = 0` will fail if no record with ID 0 exists in `TaggableTargets`.
- **UI Frontend Consumption**: MudBlazor components were inspected for data flow; client-side validation may partially conceal these issues from UI users, but backend API endpoints remain fully exposed.

---

## 4. Conclusion

Domain 2 suffers from critical architectural issues spanning authorization bypasses, foreign key disconnections, lack of asset escrowing, and ledger desynchronization. In particular:
- **P0 Priority**: Issue 1 (Bounty TargetId disconnection), Issue 2 (Move contract auth bypass & tree severing), Issue 3 (Universal bounty rejection), and Issue 4 (Bounty reward double-spending).
- **P1 Priority**: Issue 6 (Weight vs asset arbitrage), Issue 8 (Silent ledger/weight desync), Issue 9 (Tag deletion FK restrict crash), and Issue 10 (Private tag data leakage).
- **P2 Priority**: Issue 5 (Bounty self-fulfillment), Issue 7 (Unsupported decrease weight), and Issue 11 (Shadow contract architecture).

Detailed remediation proposals and code snippets are documented in `analysis.md`.

---

## 5. Verification Method

1. **Verify TargetId Bug**:
   - Inspect `SRNSMudApp/Controllers/BountyController.cs:118-127` and `SRNSMudApp/Data/TaggingRequestEntity.cs:24-48`.
   - In test suite, create a bounty and verify whether `bounty.TargetId == 0`.
2. **Verify Move Authorization & Hierarchy Severing**:
   - Inspect `SRNSMudApp/Services/TaggingContractService.cs:279-286` and `SRNSMudApp/Services/Contracts/MoveContractExecutor.cs:68-70`.
   - Propose moving a parent tag with child tags; assert whether child `Node` values match `IsDescendantOf` after move.
3. **Verify Rejection Authorization**:
   - Inspect `SRNSMudApp/Services/TaggingService.cs:111-115`.
   - Create a Bounty by User A. Call `POST /api/item/tag-requests/{bountyId}/reject` as User B. Observe that `AuthorizedToReject` is granted.
4. **Verify Tag Deletion Foreign Key Constraint**:
   - Inspect `SRNSMudApp/Data/ApplicationDbContextTagExtensions.cs:190-260` vs `SRNSMudApp/Data/ApplicationDbContext.cs:461-465`.
   - Attempt to delete a Tag that has an associated `RightAsset` via `TagCommandService.DeleteTagAsync`.
5. **Project Test Execution**:
   - Run `dotnet test SRNSMudApp.Tests/SRNSMudApp.Tests.csproj --filter "FullyQualifiedName~TaggingContract"`
