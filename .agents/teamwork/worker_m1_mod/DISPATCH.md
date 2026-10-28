## 2026-10-28T09:12:05Z
You are worker_m1_mod, an implementation agent (teamwork_worker).
Your working directory is: /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/worker_m1_mod
Your parent is orchestrator_1 (conversation ID: 8e52f4ce-17b2-4afb-b7c7-36f230e9a5a1).

Read the authoritative user request at:
/Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/ORIGINAL_REQUEST.md
Also read project architecture and guidelines at:
- /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/PROJECT.md
- /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/AGENTS.md
- /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/rules/mainRules.md
- /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/skills/dotnet-best-practices/SKILL.md

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Detailed Implementation Instructions & Reports to follow:
1. /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/explorer_m1_mod_1/handoff.md
   (Patch artifact: /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/explorer_m1_mod_1/milestone1_linq_modernization.patch)
2. /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/explorer_m1_mod_2/handoff.md
3. /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/explorer_m1_mod_3/handoff.md
   (Patch artifacts: /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/explorer_m1_mod_3/m1_mod_3_proposed.patch and m1_mod_3_idioms_proposed.patch)

Your exclusive write ownership:
- `SRNSMudApp/Models/ContentParser.cs`
- `SRNSMudApp/Services/TagSuggestionService.cs`
- `SRNSMudApp/Services/TagSimilarityService.cs`
- `SRNSMudApp/Services/ItemListDataProvider.cs`
- `SRNSMudApp/Services/InternalLinkConversionService.cs`
- `SRNSMudApp/Services/UserDataProvider.cs`
- `SRNSMudApp/Services/TagTreeDataProvider.cs`
- `SRNSMudApp/Services/ItemDetailDataProvider.cs`
- `SRNSMudApp/Services/TagLockService.cs`
- `SRNSMudApp/Services/AdminDataProvider.cs`
- `SRNSMudApp/Services/ContractDataProvider.cs`
- `SRNSMudApp/Services/NotificationsDataProvider.cs`
- `SRNSMudApp/Services/TagTableDataProvider.cs`
- `SRNSMudApp/Services/TagCardDataProvider.cs`
- `SRNSMudApp/Services/Contracts/GratisContractExecutor.cs`
- `SRNSMudApp/Services/Contracts/TriggerContractExecutor.cs`
- `SRNSMudApp/Services/NotificationService.cs`
- `SRNSMudApp/Services/StringSimilarity.cs`
- `SRNSMudApp/Services/ImportTagDataProvider.cs`

Core Tasks to Implement:
1. Core Utilities & Query Services:
   - Convert `ContentParser.ExtractUrls` and `GetContentSegments` imperative loops to declarative LINQ pipelines (`Concat`, `Select`, `Distinct`). Resolve `IDE0004` (redundant `.Cast<Match>()`).
   - Convert `TagSuggestionService.SuggestTagsAsync` to streaming LINQ pipeline (`Where`, `Select`, `Where`, `OrderByDescending`, `Take(20)`).
   - Convert `TagSimilarityService.FindSimilarTags` to declarative LINQ pipeline with proper 3-tier ordering.
   - Convert `ItemListDataProvider.GetItemsWithTagsAsync` filter extraction to `.OfType<TagIdFilter>()` / `.OfType<TagNameFilter>()` with collection expressions `[.. ...]`.
   - Convert `InternalLinkConversionService.DetectLinkCandidatesAsync` partitioning to `.Where()`.
2. Data Providers & Entities:
   - Convert `UserDataProvider.EnsureDefaultReactionTagsAsync` to `.Where().Select()` with `dbContext.Tags.AddRange(newTags)`.
   - Convert `TagTreeDataProvider.LoadPendingTagMovesAsync` to pattern matching filter and LINQ projection.
   - Convert `ItemDetailDataProvider.GetItemDetailAsync` to `ledgers.AddRange(...)` with collection expression set lookups.
   - Convert `TagLockService.GetAllTagsWithLockStatusAsync` hashset definitions to declarative collection expressions `[.. ...]`.
   - Convert `AdminDataProvider.ImportItemsWithTagsAsync` to declarative tag and relation pipelines. CAUTION: Declare `newItem` before assigning `newItem.TagRelations` to prevent compiler error CS0165!
3. Switch Anti-patterns & Cleanups:
   - Replace boolean switches (`switch (bool)`) and pseudo-switches (`switch (entity) { case not null: ... }`) with idiomatic `if` guard clauses in `ContractDataProvider`, `NotificationsDataProvider`, `TagTableDataProvider`, `TagCardDataProvider`, and `UserDataProvider`.
   - Replace side-effect object boxing switch in `GratisContractExecutor.cs` with `if/else`.
   - Replace switch in `TriggerContractExecutor.cs` with conditional expression.
   - Simplify triple pattern evaluation in `NotificationService.cs`.
   - Apply tuple swap `(a, b) = (b, a)` in `StringSimilarity.cs` and tuple patterns in `ImportTagDataProvider.cs`.
   - Apply collection expressions `[...]` and `is null` / `is not null` across the owned files.

Strict Guardrails (DO NOT TOUCH):
- DO NOT convert sequential `await` loops on `DbContext` (`TagTreeDataProvider:93`, `TagController:82`, `UserController:142`, `TagImportController:124`, `Program.cs:256`).
- DO NOT touch sibling `HierarchyId` generation recurrence (`TagTreeDataProvider:145`, `TagCommandService:353`).
- DO NOT touch `RightAssetDataProvider` balance deduction loop or `ItemSplitService` tracked entity mutation loops.
- DO NOT touch `StringSimilarity` Levenshtein DP span loops or `InternalLinkConversionService` `ReadOnlySpan` sliding window loops.
- Keep comments in Japanese explaining design rationale.
- If formatting code, strictly use `dotnet format --diagnostics IDE0055`.

Verification:
- Run compilation verification on `SRNSMudApp/SRNSMudApp.csproj` (with `--no-restore` if offline).
- Document all modified files, diffs, and verification commands in `/Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/worker_m1_mod/handoff.md`.
- Notify orchestrator_1 when finished.
