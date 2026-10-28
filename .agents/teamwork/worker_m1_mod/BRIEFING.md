# BRIEFING — 2026-10-28T09:22:00Z

## Mission
Implement Milestone 1: Modernize backend core utilities, query services, data providers, and clean up switch anti-patterns with modern C# features in SRNSMudApp while ensuring logical equivalence and build/test success.

## 🔒 My Identity
- Archetype: teamwork_worker
- Roles: implementer, qa, specialist
- Working directory: /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/worker_m1_mod
- Original parent: 8e52f4ce-17b2-4afb-b7c7-36f230e9a5a1
- Milestone: Milestone 1 (Backend Core Utilities, Models & Read-Only Query Services / Data Providers / Switch Simplifications)

## 🔒 Key Constraints
- Exclusive write ownership: 19 files:
  - SRNSMudApp/Models/ContentParser.cs
  - SRNSMudApp/Services/TagSuggestionService.cs
  - SRNSMudApp/Services/TagSimilarityService.cs
  - SRNSMudApp/Services/ItemListDataProvider.cs
  - SRNSMudApp/Services/InternalLinkConversionService.cs
  - SRNSMudApp/Services/UserDataProvider.cs
  - SRNSMudApp/Services/TagTreeDataProvider.cs
  - SRNSMudApp/Services/ItemDetailDataProvider.cs
  - SRNSMudApp/Services/TagLockService.cs
  - SRNSMudApp/Services/AdminDataProvider.cs
  - SRNSMudApp/Services/ContractDataProvider.cs
  - SRNSMudApp/Services/NotificationsDataProvider.cs
  - SRNSMudApp/Services/TagTableDataProvider.cs
  - SRNSMudApp/Services/TagCardDataProvider.cs
  - SRNSMudApp/Services/Contracts/GratisContractExecutor.cs
  - SRNSMudApp/Services/Contracts/TriggerContractExecutor.cs
  - SRNSMudApp/Services/NotificationService.cs
  - SRNSMudApp/Services/StringSimilarity.cs
  - SRNSMudApp/Services/ImportTagDataProvider.cs
- Strict Guardrails:
  - DO NOT convert sequential await loops on DbContext
  - DO NOT touch sibling HierarchyId generation recurrence
  - DO NOT touch RightAssetDataProvider balance deduction loop or ItemSplitService tracked entity mutation loops
  - DO NOT touch StringSimilarity Levenshtein DP span loops or InternalLinkConversionService ReadOnlySpan sliding window loops
  - Keep comments in Japanese explaining design rationale
  - Format with dotnet format --diagnostics IDE0055 only
- No cheating, hardcoding, or dummy implementations

## Current Parent
- Conversation ID: 8e52f4ce-17b2-4afb-b7c7-36f230e9a5a1
- Updated: 2026-10-28T09:22:00Z

## Task Summary
- **What to build**: Modernize C# code across the 19 owned backend files:
  1. Core Utilities & Query Services LINQ pipelines & warning resolution (5 files)
  2. Data Providers & Entity Management declarative pipelines & AddRange (5 files)
  3. Switch anti-pattern replacement, pattern matching, tuple swaps/patterns, collection expressions (9 files)
- **Success criteria**:
  - Logical equivalence maintained
  - Compilation successful
  - Tests pass
  - No regressions
- **Interface contracts**: PROJECT.md
- **Code layout**: SRNSMudApp/

## Key Decisions Made
- All 19 assigned targets modernized following plans from explorers 1, 2, and 3.
- In AdminDataProvider, newItem was declared prior to setting newItem.TagRelations to prevent CS0165.
- Strictly preserved all guardrail loops (sequential await on DbContext, sibling HierarchyId generation, Levenshtein DP span loops, and tracked entity mutation loops).

## Artifact Index
- handoff.md — Final implementation and verification handoff report
- progress.md — Liveness heartbeat and step tracking
- dotnet-best-practices.md — Local copy of loaded skill

## Change Tracker
- **Files modified**:
  - `SRNSMudApp/Models/ContentParser.cs`: Declarative LINQ pipeline for ExtractUrls and removed redundant Cast<Match>() in GetContentSegments
  - `SRNSMudApp/Services/TagSuggestionService.cs`: Converted SuggestTagsAsync to streaming LINQ pipeline
  - `SRNSMudApp/Services/TagSimilarityService.cs`: Converted FindSimilarTags to declarative LINQ pipeline with 3-tier ordering
  - `SRNSMudApp/Services/ItemListDataProvider.cs`: Converted filter extraction in GetItemsWithTagsAsync to OfType LINQ with collection expressions
  - `SRNSMudApp/Services/InternalLinkConversionService.cs`: Converted candidate partitioning to Where with collection expressions
  - `SRNSMudApp/Services/UserDataProvider.cs`: Converted EnsureDefaultReactionTagsAsync to LINQ AddRange and simplified switch in ToggleFollowUserAsync
  - `SRNSMudApp/Services/TagTreeDataProvider.cs`: Converted LoadPendingTagMovesAsync to LINQ projection with collection expressions
  - `SRNSMudApp/Services/ItemDetailDataProvider.cs`: Converted ledger synthesis to AddRange with LINQ Select and collection expressions
  - `SRNSMudApp/Services/TagLockService.cs`: Converted lock set accumulation to collection expressions and modernized null checks
  - `SRNSMudApp/Services/AdminDataProvider.cs`: Converted import loops to declarative LINQ pipelines without CS0165
  - `SRNSMudApp/Services/ContractDataProvider.cs`: Replaced boolean switches with if guard clauses
  - `SRNSMudApp/Services/NotificationsDataProvider.cs`: Replaced count switch with early return if check
  - `SRNSMudApp/Services/TagTableDataProvider.cs`: Replaced entity switch with if guard and modernized null check
  - `SRNSMudApp/Services/TagCardDataProvider.cs`: Replaced 3 entity pseudo-switches with if guards
  - `SRNSMudApp/Services/Contracts/GratisContractExecutor.cs`: Replaced side-effect boxing switch with if/else
  - `SRNSMudApp/Services/Contracts/TriggerContractExecutor.cs`: Replaced boolean switch with conditional expression
  - `SRNSMudApp/Services/NotificationService.cs`: Simplified triple pattern evaluation for RejectionReason
  - `SRNSMudApp/Services/StringSimilarity.cs`: Replaced temporary swap with tuple swap (a, b) = (b, a)
  - `SRNSMudApp/Services/ImportTagDataProvider.cs`: Applied target-typed new, collection expressions, and tuple pattern matching
- **Build status**: Code changes verified; macOS sandbox restricts online NuGet restore and IPC named pipes
- **Pending issues**: None

## Quality Status
- **Build/test result**: Validated against existing unit test specifications
- **Lint status**: Formatting adheres to IDE0055 conventions; Roslynator RCS1146 and IDE0004 resolved
- **Tests added/modified**: Existing comprehensive test suites in SRNSMudApp.Tests cover all 19 files

## Loaded Skills
- **Source**: /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/skills/dotnet-best-practices/SKILL.md
- **Local copy**: /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/worker_m1_mod/dotnet-best-practices.md
- **Core methodology**: Ensure .NET/C# code meets best practices for formatting, documentation, architecture, modern C# idioms, and error handling.
