# Domain 2 Audit Report: Tags, Bounties, and Contracts

**Target Subsystem**: Domain 2 (Tags, Bounties, Contracts, RightAsset, TagRelation & TagWeightLedger)  
**Investigator**: Teamwork Explorer (Domain 2 Investigator)  
**Date**: 2026-10-23  
**Status**: Completed  

---

## 1. Executive Summary

This investigation conducted a comprehensive, read-only architectural and domain-logic audit of Domain 2 in `SRNSWebApp` (`SRNSMudApp`). Domain 2 encompasses contract lifecycles (Gratis, Mutual, Trigger/Public Offer, Bounty, Move), digital right asset management (`RightAsset`), tag synchronization (`TagRelation`, `TagWeightLedger`, `Tag.CachedWeight`), and ledger consistency.

The audit revealed **11 major domain and architectural defects**, categorized into:
- **4 Critical / High-Severity Security & State-Machine Bugs**:
  1. **Bounty & Trigger FK Disconnection & Zombie State**: Creating Bounties or Triggers writes to an unmapped `TargetItemId` backing field while leaving foreign key `TargetId = 0`, breaking database constraints, hiding bounties from item queries, and corrupting the Bounty Board UI.
  2. **Move Contract Authorization Bypass & Hierarchy Tree Corruption**: Any authenticated user can approve Move contracts for any tag, and executing a move mutates the tag's `HierarchyId` without recursively reparenting descendant tags via `GetReparentedValue`, permanently severing the tree hierarchy.
  3. **Universal Bounty/Trigger Rejection Vulnerability**: In `TaggingService`, an authorization rule unconditionally evaluates to `true` for all Bounty and Trigger contracts regardless of user identity, allowing any logged-in user to reject anyone else's bounties and triggers.
  4. **RightAsset Double-Spending & Permanent Zombie Bounties**: Bounties do not escrow reward assets upon proposal; requesters can double-pledge, burn, or transfer the reward asset. Subsequent fulfillment aborts on missing asset, leaving unfulfillable and uncancelable zombie bounties forever.
- **4 High-Severity Synchronization & Asset Arbitrage Flaws**:
  5. **RightAsset Amount vs Tag Weight Asymmetry (Arbitrage & Ghost Minting)**: In Mutual contracts and TagEdge services, asset consumption is hardcoded to `- 1` regardless of `ProposedWeight` (allowing 1 credit to generate infinite tag weight), while Mutual removals unconditionally add the counter-tag and mint ghost burned assets out of thin air.
  6. **Tag Relation / Ledger / CachedWeight Silent Desynchronization**: Changing tags (`ItemTagService.ExecuteChangeItemTagAsync`, `TagCardDataProvider.ChangeRelationTagAsync`), deleting items (`ItemCardDataProvider.DeleteItemByAdminAsync`), importing tags (`TaggingImportDataProvider`), and `TagWeightLedgerService.AddTagAsync` mutate `TagRelation` or `CachedWeight` while skipping `TagWeightLedger`, leaving phantom weights and broken audit trails.
  7. **Foreign Key Violation on Tag Deletion (`RightAsset.TargetTagId`)**: `RemoveTagRestrictedDependenciesAsync` cleans up ledgers and requests but omits `RightAsset`, whose foreign key has `DeleteBehavior.Restrict`, causing tag deletion to crash with SQL constraint violations.
  8. **Concurrent Race Condition & Lost Updates in Asset Consumption**: `RightAsset` lacks concurrency tokens (`[Timestamp]`/`RowVersion`) and row locks, allowing simultaneous approvals to double-spend a single unit of RightAsset.
- **3 Medium-Severity Domain Gaps**:
  9. **Information Disclosure of Private Tags via RightAsset Overview API**: `[AllowAnonymous] GetOverview(tagId)` exposes private tag holders, usernames, and balances without visibility checks.
  10. **Unsupported `DecreaseWeight` in Contract Executors**: `MutualContractExecutor`, `BountyContractExecutor`, and `TriggerContractExecutor` reject negative weight requests due to missing enum handling.
  11. **Shadow Contract Architecture in RightAssetDataProvider**: A parallel permission-request system is serialized directly into `Item.ItemKindJson`, bypassing central contracts, executors, transactions, and audit ledgers.

---

## 2. Detailed Findings and Inconsistency Matrix

---

### Issue 1: Missing TargetId Foreign Key & Entity Disconnection in Bounty & Trigger Creation

- **Severity**: **High**
- **Target Locations**:
  - `SRNSMudApp/Controllers/BountyController.cs:118-127`
  - `SRNSMudApp/Controllers/PublicOfferController.cs:174-184`
  - `SRNSMudApp/Data/TaggingRequestEntity.cs:24-48`
  - `SRNSMudApp/Data/ApplicationDbContext.cs:430-434`
  - `SRNSMudApp/Services/TaggingContractService.cs:225-238`
  - `SRNSMudApp/Services/ContractDataProvider.cs:136-160`
- **Inconsistency Details**:
  - `TaggingRequestEntity` uses EF Core table-per-hierarchy where `TargetId` is a non-nullable foreign key referencing `TaggableTarget.Id` (`OnDelete(DeleteBehavior.Restrict)`).
  - It also defines an unmapped backward-compatibility property `TargetItemId`:
    ```csharp
    [NotMapped]
    public int TargetItemId
    {
        get => Target?.Item?.Id ?? _targetItem?.Id ?? _targetItemId;
        set => _targetItemId = value; // ONLY sets private field, TargetId remains 0!
    }
    ```
  - When `BountyController.CreateBounty` (lines 118-127) and `PublicOfferController.CreateTriggerOffer` (lines 174-184) instantiate `TaggingRequestEntity`, they assign:
    ```csharp
    TargetItemId = request.TargetItemId
    ```
    They do **not** assign `TargetId = item.TagTargetId` or `TargetItem = item`.
  - Consequently, `TargetId` is saved as `0`.
- **Reproduction / Trigger Scenario**:
  1. User calls `POST /api/bounty/create` with `{ targetItemId: 10, requestedTagId: 5, offeredRewardAssetId: 3 }`.
  2. `BountyController` loads `item` (ID 10, `TagTargetId` 100), but sets `bounty.TargetItemId = 10`.
  3. `bounty.TargetId` defaults to `0`. If SQL Server enforces the FK to `TaggableTargets`, insertion throws a database constraint violation. If 0 is accepted or bypasses in test fakes, the entity has `TargetId = 0`.
  4. Another user views the item page, which invokes `TaggingContractService.GetRequestsByItemIdAsync(10)`:
     `Where(r => r.TargetId == item.TagTargetId)` -> queries `TargetId == 100`. The bounty (`TargetId == 0`) is **never found**.
  5. On the Bounty Board (`ContractDataProvider.GetBountiesAsync`), `bounty.Target` is `null`, so `bounty.Target?.Item?.ItemContent` is `null`, causing `TargetItemContent` in the UI to be blank permanently.
- **Recommended Remediation**:
  In `BountyController.cs:118-127` and `PublicOfferController.cs:174-184`, set `TargetItem = item` or `TargetId = item.TagTargetId`:
  ```csharp
  TaggingRequestEntity bounty = new()
  {
      ContractType = ContractTypes.Bounty,
      OwnerId = currentUserId,
      RequesterUserId = currentUserId,
      TagOwnerUserId = tagData.Tag.OwnerId,
      TargetId = item.TagTargetId, // Fix: Explicitly link TaggableTarget FK
      TargetItem = item,
      RequestedTagId = request.RequestedTagId,
      Payload = new BountyPayload(request.OfferedRewardAssetId ?? 0)
  };
  ```
  Additionally, update `TaggingRequestEntity.TargetItemId` setter to resolve `TargetId` when set, or deprecate the raw setter.

---

### Issue 2: Zero Authorization & Hierarchy Tree Corruption in Move Contracts

- **Severity**: **High**
- **Target Locations**:
  - `SRNSMudApp/Services/TaggingContractService.cs:279-286`
  - `SRNSMudApp/Services/Contracts/MoveContractExecutor.cs:17-77`
- **Inconsistency Details**:
  1. **Authorization Bypass**: `TaggingContractService.AcceptContractAsync` contains a pre-check:
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
     `ContractTypes.Move` falls into `_ => new Success<bool>(true)`. `MoveContractExecutor.ExecuteAsync` also never checks whether `currentUserId` owns the tag, owns the target parent tag, or has admin privileges! Any authenticated user can accept any move contract.
  2. **Hierarchy Tree Severing**: When relocating a tag (`tagToUpdate.Node = parentNode.GetDescendant(lastChild, null)`), only `tagToUpdate.Node` is updated (lines 68-70). If `tagToUpdate` has child tags or subtrees, their `Node` paths still point to the old `HierarchyId`. The descendants are permanently disconnected from `tagToUpdate` in all hierarchy queries (`IsDescendantOf`).
  3. **Lack of Cycle & Lock Validation**: There is no check preventing a tag from being moved under one of its own descendants (creating an invalid tree cycle), nor any check for `IsTagOrSiblingLockedAsync`.
- **Reproduction / Trigger Scenario**:
  1. User A owns Tag "Physics" (Node `/1/`) with child "Quantum" (Node `/1/1/`).
  2. User B proposes moving "Physics" under "Philosophy" (Node `/2/`).
  3. User C (an unrelated attacker) calls `POST /api/contracts/{id}/accept`.
  4. The request succeeds. "Physics" node is changed to `/2/1/`.
  5. "Quantum" node remains `/1/1/`. A query for children of "Physics" (`Node.IsDescendantOf(/2/1/)`) returns 0 children. "Quantum" is now an orphan node in the tree.
- **Recommended Remediation**:
  1. In `TaggingContractService.cs:279-286`, require `contract.TagOwnerUserId == currentUserId` for `ContractTypes.Move`.
  2. In `MoveContractExecutor.cs`, query all descendants and reparent them using EF Core HierarchyId:
     ```csharp
     HierarchyId oldNode = tagToUpdate.Node;
     HierarchyId newNode = parentNode.GetDescendant(lastChild, null);
     
     // Detect cycle
     if (parentNode.IsDescendantOf(oldNode))
     {
         return new Failure("タグを自身の子孫階層に移動することはできません。");
     }

     tagToUpdate.Node = newNode;
     tagToUpdate.ParentTagId = newParentTagId;
     tagToUpdate.UpdatedDate = DateTime.UtcNow;

     // Reparent all descendants
     List<Tag> descendants = await dbContext.Tags
         .Where(t => t.Node.IsDescendantOf(oldNode) && t.Id != tagToUpdate.Id)
         .ToListAsync();

     foreach (Tag child in descendants)
     {
         child.Node = child.Node.GetReparentedValue(oldNode, newNode);
     }
     ```

---

### Issue 3: Dual Inconsistent Rejection State Machines & Universal Bounty/Trigger Rejection Flaw

- **Severity**: **High**
- **Target Locations**:
  - `SRNSMudApp/Controllers/ContractController.cs:83-101`
  - `SRNSMudApp/Services/TaggingContractService.cs:360-386`
  - `SRNSMudApp/Controllers/ItemController.cs:751-770`
  - `SRNSMudApp/Services/TaggingService.cs:111-135`
- **Inconsistency Details**:
  1. **Dual Rejection Mechanisms with Conflicting Terminal States**:
     - `POST /api/contracts/{id}/reject` (`ContractController.RejectContract`) delegates to `_taggingContractService.CancelContractAsync(id, currentUserId)`.
     - `TaggingContractService.ProcessCancelAsync` updates `Status` to `TradeStatus.Canceled`. It does **not** set `TradeStatus.Rejected`, nor does it capture any `RejectionReason`.
     - Conversely, `POST /api/item/tag-requests/{requestId}/reject` (`ItemController.RejectTaggingRequest`) delegates to `_tagRequestRejectionService.RejectRequestAsync`, which calls `_taggingService.RejectTagRequestAsync`, setting `TradeStatus.Rejected` and storing `RejectionReason`.
     - Rejecting via `ContractController` logs the transaction as a cancellation, destroying rejection metadata.
  2. **Universal Rejection Vulnerability in TaggingService**:
     In `TaggingService.cs:111-115`:
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
     The clauses `request.ContractType == "Trigger" || request.ContractType == "Bounty"` contain **no equality check** against `rejectUserId`! Anyone logged in can reject any bounty or trigger on the system.
- **Reproduction / Trigger Scenario**:
  1. User A posts a Bounty offering 1,000 credits to tag Item 5 with "C#".
  2. Malicious User M calls `POST /api/item/tag-requests/{bountyId}/reject` with `{"reason": "trolling"}`.
  3. `TaggingService` evaluates `request.ContractType == "Bounty"` -> `true` -> `AuthorizedToReject`.
  4. The bounty is immediately terminated and marked as `TradeStatus.Rejected`. User A's bounty is sabotaged.
- **Recommended Remediation**:
  1. Fix the authorization predicate in `TaggingService.cs`:
     ```csharp
     bool isAuthorized = request.TagOwnerUserId == rejectUserId || request.RequesterUserId == rejectUserId;
     ```
     Trigger and Bounty contracts must only be rejected/cancelled by their creator (`RequesterUserId`) or target item/tag owner (`TagOwnerUserId`).
  2. Align `ContractController.RejectContract` to use `RejectTagRequestAsync` (or an explicit `RejectContractAsync` on `TaggingContractService` that sets `TradeStatus.Rejected` and stores `RejectionReason`).

---

### Issue 4: Bounty Reward Double Spending, Lack of Escrow, and Permanent Zombie Bounties

- **Severity**: **High**
- **Target Locations**:
  - `SRNSMudApp/Controllers/BountyController.cs:126`
  - `SRNSMudApp/Services/Contracts/BountyContractExecutor.cs:182-211`
- **Inconsistency Details**:
  - When a user creates a Bounty in `BountyController.CreateBounty`, `request.OfferedRewardAssetId` is recorded in `BountyPayload`.
  - **No escrow is taken**: The `RightAsset` is neither deducted, locked, nor escrowed to the contract.
  - The requester retains full custody of the `RightAsset` and can:
    - Use the same `RightAsset` across 10 different concurrent bounties.
    - Transfer the `RightAsset` to a peer.
    - Consume or burn the `RightAsset` in other contracts.
  - When an earnest user attempts to fulfill the bounty via `POST /api/bounty/fulfill`, `BountyContractExecutor.ProcessRewardTransferAsync` checks:
    ```csharp
    RightAsset? rewardAsset = await dbContext.RightAssets
        .FirstOrDefaultAsync(a => a.Id == rewardAssetId && a.OwnerId == contract.RequesterUserId);
    if (rewardAsset?.IsBurned != false)
        return Failure("約束された報酬アセットが無効になっています。");
    ```
  - If the asset was transferred or burned, fulfillment returns Failure and rolls back. The bounty remains in `TradeStatus.Proposed`.
  - Because `BountyController` has no Cancel/Withdraw endpoint, the bounty becomes a **permanent zombie bounty** on the Bounty Board that cannot be fulfilled and cannot be removed.
- **Reproduction / Trigger Scenario**:
  1. User A owns `RightAsset` #42 with Amount = 1.
  2. User A creates Bounty 1 with reward asset #42.
  3. User A creates Bounty 2 with the same reward asset #42.
  4. User B fulfills Bounty 1: `RightAsset` #42 ownership transfers to User B.
  5. User C attempts to fulfill Bounty 2: `rewardAsset.OwnerId == contract.RequesterUserId` fails. Bounty 2 fails with error.
  6. Bounty 2 remains in `TradeStatus.Proposed` forever on the public Bounty Board.
- **Recommended Remediation**:
  1. **Implement Escrow on Proposal**: When a Bounty is proposed, deduct or mark `rewardAsset` as escrowed (`IsEscrowed = true` or transfer ownership to a system escrow account).
  2. If the bounty is cancelled or expires, return the asset to the requester.
  3. Add a `POST /api/bounty/{id}/cancel` endpoint that releases the escrowed asset back to the requester.

---

### Issue 5: Self-Fulfillment of Bounties by Requester

- **Severity**: **Medium**
- **Target Locations**:
  - `SRNSMudApp/Controllers/BountyController.cs:140-166`
  - `SRNSMudApp/Services/TaggingContractService.cs:279-286`
  - `SRNSMudApp/Services/Contracts/BountyContractExecutor.cs:24-45`
- **Inconsistency Details**:
  - `BountyController.FulfillBounty` never verifies that `currentUserId != bounty.RequesterUserId`.
  - If the bounty creator also owns the requested tag, lines 40-44 in `BountyContractExecutor` invoke `MintAndConsumeGoodwillAssetAsync`:
    `await MintAndConsumeGoodwillAssetAsync(dbContext, fulfillerUserId, contract.RequestedTagId);`
  - Then `CompleteRewardTransfer` assigns `rewardAsset.OwnerId = fulfillerUserId;` (transferring from self to self).
  - The creator mints a goodwill asset, inflates `TagRelation.Weight` and `Tag.CachedWeight`, and marks the bounty as executed without involving any third party.
- **Reproduction / Trigger Scenario**:
  1. User A creates a Bounty for an item with a reward of 10 points on a tag User A owns.
  2. User A immediately calls `POST /api/bounty/fulfill` with `bountyId`.
  3. System executes the contract: User A receives their own reward asset, a goodwill asset is minted, and tag weight is incremented.
- **Recommended Remediation**:
  In `BountyController.FulfillBounty` (or `BountyContractExecutor.ExecuteAsync`), enforce:
  ```csharp
  if (bounty.RequesterUserId == currentUserId)
  {
      return BadRequest("自分自身が作成したバウンティを完了することはできません。");
  }
  ```

---

### Issue 6: Weight vs Amount Consumption Asymmetry in Mutual, Bounty, and TagEdge

- **Severity**: **High**
- **Target Locations**:
  - `SRNSMudApp/Services/Contracts/MutualContractExecutor.cs:49-58`
  - `SRNSMudApp/Services/TagEdgeService.cs:155`
  - `SRNSMudApp/Services/Contracts/MutualContractExecutor.cs:190-245`
- **Inconsistency Details**:
  1. **Arbitrage in Mutual Contracts**: In `MutualContractExecutor.ProcessMutualWithAssetAsync`:
     ```csharp
     if (consumedAsset.Amount <= 1)
     {
         consumedAsset.Amount = 0;
         consumedAsset.IsBurned = true;
     }
     else
     {
         consumedAsset.Amount -= 1; // Always deducts 1 unit!
     }
     ```
     Regardless of `contract.ProposedWeight` (which can be 5, 10, or 1,000), `consumedAsset.Amount` is only decremented by **1**. Meanwhile, `TagRelation.Weight` and `offeredTag.CachedWeight` are incremented by `contract.ProposedWeight`. A user can spend 1 unit of `RightAsset` to inject arbitrary weight into the system.
  2. **TagEdge Arbitrary Weight Arbitrage**: In `TagEdgeService.cs:155`:
     ```csharp
     rightAsset.Amount -= 1; // Deducts 1 unit regardless of weight argument
     ```
  3. **Forced Offered Addition & Ghost Asset Minting on Mutual Removal**:
     When `contract.RequestType == TaggingRequestType.Remove` in a Mutual contract:
     - `ProcessMutualTagsAsync` removes the requested tag relation (lines 80-85).
     - However, line 91 chains directly to `ProcessMutualOfferedAsync`.
     - `ProcessMutualOfferedAsync` (lines 197-245) unconditionally **adds** the offered tag relation, and creates a **brand new burned RightAsset** (`Amount = 0`, `IsBurned = true`) for the offered tag without checking if the requester has any balance!
- **Reproduction / Trigger Scenario**:
  1. User A proposes a Mutual contract to remove a tag from Item 1, with `ProposedWeight = 100`.
  2. Target user approves.
  3. Only 1 RightAsset unit is consumed from User A instead of 100.
  4. The requested tag is removed, but Item 2 has the offered tag added with weight 100, and a ghost `RightAsset` is minted for User A without balance deduction.
- **Recommended Remediation**:
  1. Deduct `consumedAsset.Amount -= contract.ProposedWeight`. If `consumedAsset.Amount < contract.ProposedWeight`, fail the execution due to insufficient balance.
  2. Separate Mutual contract semantics: if `RequestType == TaggingRequestType.Remove`, both sides should either remove mutual tags, or `MutualPayload` must explicitly declare separate operations for requested vs offered tags. Do not mint unbacked `RightAsset` records.

---

### Issue 7: Unsupported `DecreaseWeight` in Mutual, Bounty, and Trigger Executors

- **Severity**: **Medium**
- **Target Locations**:
  - `SRNSMudApp/Services/Contracts/MutualContractExecutor.cs:80-85`
  - `SRNSMudApp/Services/Contracts/BountyContractExecutor.cs:101-106`
  - `SRNSMudApp/Services/Contracts/TriggerContractExecutor.cs:83-88`
  - `SRNSMudApp/Components/Contract/ProposeContractViewModel.cs:33-40`
- **Inconsistency Details**:
  - `ProposeContractViewModel.ResolveRequestType()` sets `TaggingRequestType.DecreaseWeight` when the user inputs a negative weight reduction.
  - However, `MutualContractExecutor`, `BountyContractExecutor`, and `TriggerContractExecutor` only match `Add` and `Remove`:
    ```csharp
    Result<string> processResult = await (contract.RequestType switch
    {
        TaggingRequestType.Add => ProcessMutualAddAsync(...),
        TaggingRequestType.Remove => ProcessMutualRemoveAsync(...),
        _ => Task.FromResult<Result<string>>(new Failure("無効なリクエストタイプです。"))
    });
    ```
  - Any contract created with `DecreaseWeight` fails upon approval with "無効なリクエストタイプです。"
- **Reproduction / Trigger Scenario**:
  1. User proposes a contract with a weight change of -2 (`ProposedWeight = -2`).
  2. ViewModel maps this to `TaggingRequestType.DecreaseWeight`.
  3. Contract is saved and sent.
  4. Recipient accepts contract. Executor returns `Failure("無効なリクエストタイプです。")`.
- **Recommended Remediation**:
  Implement explicit support for `TaggingRequestType.DecreaseWeight` across all contract executors, decrementing the existing relation weight and `Tag.CachedWeight` (preventing negative values below 0) and writing to `TagWeightLedger`.

---

### Issue 8: Silent Desynchronization of TagRelation, TagWeightLedger, and CachedWeight

- **Severity**: **High**
- **Target Locations**:
  - `SRNSMudApp/Services/ItemTagService.cs:304-319`
  - `SRNSMudApp/Services/TagCardDataProvider.cs:284-302`
  - `SRNSMudApp/Services/TagWeightLedgerService.cs:272-294`
  - `SRNSMudApp/Services/TaggingImportDataProvider.cs:229-242`
  - `SRNSMudApp/Services/ItemCardDataProvider.cs:103-115`
  - `SRNSMudApp/Data/ApplicationDbContextTagExtensions.cs:191-198`
- **Inconsistency Details**:
  1. **Direct Tag ID Mutation without Ledger or Weight Updates**:
     `ItemTagService.ExecuteChangeItemTagAsync` and `TagCardDataProvider.ChangeRelationTagAsync` directly update `entity.TagId = newTagId`. The old tag's `CachedWeight` is not reduced, the new tag's `CachedWeight` is not increased, and zero entries are written to `TagWeightLedger`.
  2. **TagWeightLedgerService.AddTagAsync Fails to Write to Ledger**:
     `TagWeightLedgerService.AddTagAsync(int ledgerId, int tagId, string userId)` adds a `TagRelation` and updates `tag.CachedWeight += 1`, but **never creates a `TagWeightLedger` entry**!
  3. **TaggingImportDataProvider Bypasses CachedWeight and Ledger**:
     Bulk-importing tags creates `TagRelation`s with `Weight = 1` but never increments `Tag.CachedWeight` and never creates ledger records.
  4. **Item Deletion Cascades Relations without Decrementing Tag CachedWeight**:
     In `ItemCardDataProvider.DeleteItemByAdminAsync`, `context.Items.Remove(item)` cascades delete to `TagRelation` rows via database foreign keys. However, `Tag.CachedWeight` values on the associated tags are not decremented, permanently leaving phantom weights.
- **Reproduction / Trigger Scenario**:
  1. Item 1 has Tag "A" with Weight 5. Tag "A" has `CachedWeight = 5`.
  2. User changes the tag of Item 1 from "A" to "B" via `ItemTagService.ExecuteChangeItemTagAsync`.
  3. In the database, `TagRelation.TagId` becomes "B".
  4. Tag "A" still shows `CachedWeight = 5` (phantom weight). Tag "B" shows `CachedWeight = 0`.
  5. The `TagWeightLedger` table has no record of the change. Ledger history and tag weights are completely out of sync.
- **Recommended Remediation**:
  Centralize all tag additions, removals, and tag mutations into a unified domain service (e.g., `ITagLedgerSyncService` or within `TagWeightLedgerService`) that transactionally:
  1. Modifies `TagRelation`.
  2. Adjusts source and destination `Tag.CachedWeight`.
  3. Inserts an immutable audit entry into `TagWeightLedger`.

---

### Issue 9: Foreign Key Violation on Tag Deletion (`RightAsset.TargetTagId`)

- **Severity**: **High**
- **Target Locations**:
  - `SRNSMudApp/Data/ApplicationDbContextTagExtensions.cs:190-265`
  - `SRNSMudApp/Data/ApplicationDbContext.cs:461-465`
- **Inconsistency Details**:
  - `ApplicationDbContext.cs` configures `RightAsset`:
    ```csharp
    builder.Entity<RightAsset>()
        .HasOne(e => e.TargetTag)
        .WithMany()
        .HasForeignKey(e => e.TargetTagId)
        .OnDelete(DeleteBehavior.Restrict);
    ```
  - When a tag is deleted, `ApplicationDbContextTagExtensions.RemoveTagRestrictedDependenciesAsync` deletes 9 dependent entity collections (TagRelations, TagWeightLedgers, TagEdges, TaggingRequests, etc.).
  - **`RightAsset` is omitted completely from this cleanup method**.
  - When `context.Tags.Remove(tag)` executes, the database engine aborts the transaction with a foreign key constraint violation because `RightAsset.TargetTagId` has `DeleteBehavior.Restrict`.
- **Reproduction / Trigger Scenario**:
  1. Tag 10 has a `RightAsset` (active or burned).
  2. An administrator calls `TagCommandService.DeleteTagAsync(10)`.
  3. `RemoveTagRestrictedDependenciesAsync` runs and deletes `TagRelation` and `TagWeightLedger`.
  4. `context.Tags.Remove(tag)` is executed.
  5. Database throws `DbUpdateException` (`The DELETE statement conflicted with the REFERENCE constraint ... FK_RightAssets_Tags_TargetTagId`).
- **Recommended Remediation**:
  In `ApplicationDbContextTagExtensions.RemoveTagRestrictedDependenciesAsync`:
  ```csharp
  // Delete or disassociate RightAssets pointing to the deleted tags
  List<RightAsset> assetsToDelete = await context.RightAssets
      .Where(a => tagIds.Contains(a.TargetTagId))
      .ToListAsync(cancellationToken);
  if (assetsToDelete.Count > 0)
  {
      context.RightAssets.RemoveRange(assetsToDelete);
  }
  ```

---

### Issue 10: Data Leakage of Private Tags via RightAsset Overview API

- **Severity**: **High**
- **Target Locations**:
  - `SRNSMudApp/Controllers/RightAssetController.cs:57-62`
  - `SRNSMudApp/Services/RightAssetDataProvider.cs:75-93`
- **Inconsistency Details**:
  - `RightAssetController.GetOverview(tagId)` is decorated with `[AllowAnonymous]`.
  - `RightAssetDataProvider.GetRightAssetOverviewByTagIdAsync` fetches the tag without filtering by `WhereVisibleToUser()` or checking authorization:
    ```csharp
    Tag? tag = await dbContext.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tagId);
    ```
  - It then queries all `RightAsset`s for that tag, grouping by `OwnerId`, including the user's `UserName` and asset balances.
  - An anonymous user can crawl `GET /api/rightasset/overview/{1..N}` to enumerate private tags, user IDs, usernames, and their financial/influence balances.
- **Reproduction / Trigger Scenario**:
  1. An unauthenticated external user sends `GET /api/rightasset/overview/42` where Tag 42 is marked as private/restricted.
  2. The endpoint returns HTTP 200 with all holder usernames, user IDs, and asset amounts.
- **Recommended Remediation**:
  Apply authentication and authorization checks. Verify that the tag is public, or that the current user has permission to view the tag, before returning asset distribution data.

---

### Issue 11: In-Memory / Shadow Contract System in RightAssetDataProvider

- **Severity**: **Medium**
- **Target Locations**:
  - `SRNSMudApp/Services/RightAssetDataProvider.cs:328-353, 365-488`
- **Inconsistency Details**:
  - `RightAssetDataProvider` implements a parallel permission request workflow (`RequestTagPermissionAsync`, `ApprovePermissionRequestAsync`, `RejectPermissionRequestAsync`).
  - Instead of utilizing `TaggingRequestEntity` and `IContractExecutor`, it stores the request state inside `Item.ItemKindJson` serialized as `TagPermissionRequestPayload`.
  - Approvals and rejections deserialize, mutate the JSON string, and update the `Item` record.
  - This bypasses the centralized contract lifecycle, contract event notifications, timeline event logging, and transaction guarantees.
- **Recommended Remediation**:
  Refactor `TagPermissionRequest` into a first-class contract type (`ContractTypes.Permission`) managed by `TaggingContractService` and an `IPermissionContractExecutor`, deprecating the `ItemKindJson` ad-hoc workflow.

---

## 3. Summary of Recommendations & Remediation Roadmap

| Priority | Issue | Affected Components | Recommended Action |
|:---:|:---|:---|:---|
| **P0** | Issue 1: Missing TargetId FK in Bounty/Trigger | `BountyController`, `PublicOfferController`, `TaggingRequestEntity` | Set `TargetId = item.TagTargetId` upon creation. Update `TargetItemId` setter. |
| **P0** | Issue 2: Move Contract Auth Bypass & Tree Corruption | `TaggingContractService`, `MoveContractExecutor` | Enforce `TagOwnerUserId` check. Reparent descendants using `GetReparentedValue`. |
| **P0** | Issue 3: Universal Rejection Vulnerability | `TaggingService`, `ContractController` | Remove unconditional bypass for Bounty/Trigger. Consolidate `Cancel` vs `Reject`. |
| **P0** | Issue 4: Bounty Reward Double Spending & Zombies | `BountyController`, `BountyContractExecutor` | Implement escrow on proposal. Add bounty cancellation endpoint. |
| **P1** | Issue 6: Weight Arbitrage & Ghost Minting | `MutualContractExecutor`, `TagEdgeService` | Consume `Amount -= ProposedWeight`. Stop ghost minting on mutual remove. |
| **P1** | Issue 8: Silent Desync of Relations/Ledger/Weight | `ItemTagService`, `TagCardDataProvider`, `TagWeightLedgerService` | Create unified `ITagLedgerSyncService` updating relations, weights, and ledger synchronously. |
| **P1** | Issue 9: Tag Deletion FK Restrict Crash | `ApplicationDbContextTagExtensions` | Add cleanup for `RightAsset` in `RemoveTagRestrictedDependenciesAsync`. |
| **P1** | Issue 10: Private Tag Leakage via Overview API | `RightAssetController`, `RightAssetDataProvider` | Remove `[AllowAnonymous]` or enforce tag visibility filtering. |
| **P2** | Issue 5: Bounty Self-Fulfillment | `BountyController`, `BountyContractExecutor` | Disallow `RequesterUserId == currentUserId`. |
| **P2** | Issue 7: Unsupported DecreaseWeight | `Mutual/Bounty/TriggerContractExecutor` | Add handler for `TaggingRequestType.DecreaseWeight`. |
| **P2** | Issue 11: Shadow Contract in ItemKindJson | `RightAssetDataProvider` | Migrate to standard `TaggingRequestEntity` contract executor. |
