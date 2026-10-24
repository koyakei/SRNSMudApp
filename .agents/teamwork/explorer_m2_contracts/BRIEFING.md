# BRIEFING — 2026-10-23T12:31:00Z

## Mission
Investigate Domain 2 (Tags, Bounties, and Contracts - Gratis, Mutual, Trade) for specification inconsistencies, domain model contradictions, contract state machine bugs, asset leakages, and ledger synchronization flaws.

## 🔒 My Identity
- Archetype: teamwork_preview_explorer
- Roles: Domain 2 Investigator (Tags, Bounties, Contracts)
- Working directory: /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/explorer_m2_contracts
- Original parent: 629c38f0-535d-4d7f-a234-fb927a1c7015
- Milestone: Domain 2 Specification Audit

## 🔒 Key Constraints
- Read-only investigation — do NOT implement
- Exact file paths and line numbers required
- Classify severity (High/Med/Low)
- Provide expected vs actual behavior, reproduction scenario, recommended remediation
- Write report to analysis.md and handoff.md, message parent upon completion

## Current Parent
- Conversation ID: 629c38f0-535d-4d7f-a234-fb927a1c7015
- Updated: not yet

## Investigation State
- **Explored paths**:
  - `SRNSMudApp/Controllers/` (BountyController, ContractController, PublicOfferController, RightAssetController, ItemController)
  - `SRNSMudApp/Services/Contracts/` (GratisContractExecutor, MutualContractExecutor, BountyContractExecutor, TriggerContractExecutor, MoveContractExecutor)
  - `SRNSMudApp/Services/` (TaggingContractService, TaggingService, ItemTagService, TagWeightLedgerService, TagCardDataProvider, TaggingImportDataProvider, RightAssetDataProvider, ContractDataProvider, ItemCardDataProvider)
  - `SRNSMudApp/Data/` (TaggingRequestEntity, RightAsset, TagWeightLedger, TagRelation, Tag, ApplicationDbContext, ApplicationDbContextTagExtensions)
- **Key findings**:
  - Issue 1 (High): Missing TargetId FK assignment in Bounty/Trigger creation -> defaults to 0, breaks queries and board UI.
  - Issue 2 (High): Move contract authorization bypass (any user can accept) & tree hierarchy corruption (descendants not reparented).
  - Issue 3 (High): Universal Bounty/Trigger rejection vulnerability in TaggingService & dual contradictory rejection state machines.
  - Issue 4 (High): Bounty reward double-spending, lack of escrow, and unfulfillable zombie bounties.
  - Issue 5 (Med): Self-fulfillment of bounties by requester.
  - Issue 6 (High): RightAsset amount vs tag weight asymmetry (arbitrage) & ghost asset minting on mutual remove.
  - Issue 7 (Med): Unsupported DecreaseWeight in contract executors.
  - Issue 8 (High): Silent desynchronization of TagRelation, TagWeightLedger, and Tag.CachedWeight.
  - Issue 9 (High): Tag deletion foreign key restrict crash due to omitted RightAsset cleanup.
  - Issue 10 (High): Private tag holder and balance data leakage via anonymous RightAsset overview API.
  - Issue 11 (Med): Shadow contract architecture stored inside Item.ItemKindJson.
- **Unexplored areas**: None remaining within Domain 2 scope.

## Key Decisions Made
- Fully documented all 11 issues in `analysis.md` with file locations, severity, reproduction scenarios, and remediation sketches.
- Prepared 5-component `handoff.md` report.

## Artifact Index
- DISPATCH.md — Task instructions
- BRIEFING.md — Persistent working memory
- progress.md — Liveness heartbeat
- analysis.md — Full comprehensive audit report (11 issues)
- handoff.md — 5-component handoff report
