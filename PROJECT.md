# Project: SRNSWebApp C# Modernization & Codebase Quality

## Architecture
- **Framework**: .NET 11.0 (`net11.0`, LangVersion: preview, C# 15), ASP.NET Core Blazor Web App (InteractiveServer & WASM), MudBlazor 9.7
- **Layers**:
  - `SRNSMudApp`: Backend Services, Data, Controllers, Models, Blazor Server Components
  - `SRNSMudApp.Client`: Blazor WASM Components, Client Services, Models
  - `SRNSMudApp.Tests`: Unit and Integration Tests (xUnit 2.9, bUnit 2.11, Moq 4.20)
  - `SRNSMudApp.E2ETests`: End-to-End Tests (NUnit 4.3, Playwright 1.48, Testcontainers.MsSql 3.8)
- **Key Objectives**:
  - Modernize legacy imperative loops (`for`, `foreach`) to declarative LINQ pipelines (`Select`, `Where`, `OfType`, `Distinct`, `UnionWith`, `AddRange`) ensuring 100% logical equivalence and readability.
  - Apply modern C# features (pattern matching, switch expressions, target-typed `new()`, collection expressions `[...]`, primary constructors, list patterns).
  - Resolve analyzer warnings (Roslynator `RCS1146`, redundant casts `IDE0004`, style warnings).
  - Comply with format policy: `dotnet format --diagnostics IDE0055`.
  - Maintain zero compiler errors and pass 100% of the test suite.

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | BACKEND-LINQ-01 | Convert `ContentParser.ExtractUrls` and `GetContentSegments` imperative loops to LINQ (`Concat`, `Select`, `Distinct`) and remove redundant `.Cast<Match>()` | M1 | Survey 1 |
| 2 | BACKEND-LINQ-02 | Convert `TagSuggestionService.GetSuggestedTagsAsync` and `TagSimilarityService.FindSimilarTags` to declarative LINQ pipelines | M1 | Survey 1 |
| 3 | BACKEND-LINQ-03 | Modernize filter extraction in `ItemListDataProvider.GetItemsWithTagsAsync` using `.OfType<TagIdFilter>()` / `TagNameFilter` with collection expressions | M1 | Survey 1 |
| 4 | BACKEND-LINQ-04 | Modernize batch seeding in `UserDataProvider.EnsureDefaultReactionTagsAsync` and ledger additions in `ItemDetailDataProvider.GetItemDetailAsync` | M1 | Survey 1 |
| 5 | BACKEND-LINQ-05 | Convert `TagTreeDataProvider.GetPendingTagMovesAsync` and `AdminDataProvider.ImportItemsWithTagsAsync` to LINQ projections | M1 | Survey 1 |
| 6 | BACKEND-SWITCH-01 | Replace boolean switch anti-patterns (`switch (bool)`) with idiomatic `if` guard clauses in `ContractDataProvider`, `NotificationsDataProvider`, `TagTableDataProvider`, `UserDataProvider`, `TagCardDataProvider` | M1 | Survey 1 |
| 7 | BACKEND-MODERN-01 | Apply collection expressions `[...]`, target-typed `new()`, tuple patterns, and `is null` / `is not null` across backend services and models | M1 | Survey 1 |
| 8 | UI-LINQ-01 | Convert `ItemEditViewModel.ParsePillsToHtml` to declarative LINQ projection, preserving identical HTML output | M2 | Survey 2 |
| 9 | UI-LINQ-02 | Modernize `TagCardViewModel.BuildDisplayList` and `ItemTagChipList.razor` to declarative LINQ queries with `AddRange` | M2 | Survey 2 |
| 10 | UI-LINQ-03 | Modernize `TagDiagramPage.GetEffectiveItemIds` using `.Distinct()` and collection expressions | M2 | Survey 2 |
| 11 | UI-SET-01 | Replace iterative `HashSet.Add` loops in `TagDiagramPageViewModel` with idiomatic `HashSet.UnionWith` | M2 | Survey 2 |
| 12 | UI-LINQ-04 | Modernize `ItemListViewModel.InitializeAsync` filter mapping using pattern-matching `Select` and `DoubleSubmissionGuard.CleanupExpired` with LINQ | M2 | Survey 2 |
| 13 | UI-SWITCH-01 | Replace nested boolean switch in `ItemCardViewModel.GetTagDisplayWeight` with clean pattern-matching switch expression | M2 | Survey 2 |
| 14 | UI-MODERN-01 | Apply primary constructors in candidate ViewModels and adopt collection expressions and list patterns in `ItemListQueryState` | M2 | Survey 2 |
| 15 | TEST-WARN-01 | Resolve all 6 `IDE0004` (redundant cast) warnings in `SRNSMudApp.Tests` (`TagDetailViewModelTests`, `ItemCardPrivacyBadgeTests`, `ItemReplyThreadTests`) | M3 | Survey 3 |
| 16 | TEST-LINQ-01 | Convert nested imperative loops in architecture tests (`ComponentArchitectureRuleTests`, `ComponentInjectedDependenciesTests`, `PageRenderModeTests`) to LINQ queries | M3 | Survey 3 |
| 17 | TEST-LINQ-02 | Modernize test helper loops in `DbContextExtensions`, `SqlMemoryExhaustionInterceptor`, and data generator loops to LINQ | M3 | Survey 3 |
| 18 | TEST-MODERN-01 | Adopt collection expressions `[...]` and `Assert.All` for collection assertions across `SRNSMudApp.Tests` | M3 | Survey 3 |
| 19 | VERIF-01 | Execute full solution build (`dotnet build`) with 0 errors and 0 warnings | M4 | All Surveys |
| 20 | VERIF-02 | Execute full unit test suite (2,849 tests in `SRNSMudApp.Tests`) ensuring 100% pass rate | M4 | All Surveys |
| 21 | VERIF-03 | Execute E2E test suite (3 tests in `SRNSMudApp.E2ETests`) ensuring 100% pass rate | M4 | All Surveys |
| 22 | VERIF-04 | Verify formatting policy compliance with `dotnet format --diagnostics IDE0055 --verify-no-changes` | M4 | All Surveys |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | Backend Utilities, Services & Data Providers Modernization | BACKEND-LINQ-01, BACKEND-LINQ-02, BACKEND-LINQ-03, BACKEND-LINQ-04, BACKEND-LINQ-05, BACKEND-SWITCH-01, BACKEND-MODERN-01 | none | DONE |
| 2 | Blazor UI Components, ViewModels & Client Modernization | UI-LINQ-01, UI-LINQ-02, UI-LINQ-03, UI-SET-01, UI-LINQ-04, UI-SWITCH-01, UI-MODERN-01 | M1 | IN_PROGRESS |
| 3 | Test Suite Modernization & Analyzer Warning Resolution | TEST-WARN-01, TEST-LINQ-01, TEST-LINQ-02, TEST-MODERN-01 | M1, M2 | PLANNED |
| 4 | Solution-Wide Verification & Acceptance | VERIF-01, VERIF-02, VERIF-03, VERIF-04 | M1, M2, M3 | PLANNED |

## Interface Contracts & Invariant Guardrails
### Invariant 1: Razor Markup Loops
- ALL 119 `@foreach` and `@for` loops inside `.razor` markup files MUST remain intact as Razor markup. Do NOT convert markup loops into LINQ or RenderFragments.

### Invariant 2: DbContext Concurrency Protection
- Loops performing asynchronous operations against `DbContext` (`UserManagementViewModel`, `TagController`, `UserController`, `TagImportController`) MUST remain sequential `await` loops. Never convert to `Task.WhenAll` on a shared DbContext.

### Invariant 3: Ref Structs & Span Performance
- Loops operating on `ReadOnlySpan<T>`, `Span<T>`, or `ValueMatchEnumerator` (`StringSimilarity`, `InternalLinkConversionService` text windowing, `ItemSplitTextPolicy`) MUST remain imperative for memory safety and zero allocations.

### Invariant 4: Format & Style Constraints
- `dotnet format` must always be run with `--diagnostics IDE0055`.
- Using-aliases must remain inside the namespace block.
- Comments explaining design rationale must be in Japanese.

## Code Layout
- `SRNSMudApp/Models/` — Domain models, text policies, parsers (`ContentParser.cs`, `ItemSplitTextPolicy.cs`)
- `SRNSMudApp/Services/` — Domain services, similarity algorithms, data providers, push services
- `SRNSMudApp/Data/` — Entity models, DbContext, change interceptors
- `SRNSMudApp/Controllers/` — REST API controllers
- `SRNSMudApp/Components/` — Blazor Server pages, dialogs, viewmodels, UI components
- `SRNSMudApp.Client/` — Blazor WebAssembly components, client services, query state
- `SRNSMudApp.Tests/` — Unit tests, architecture rule tests, bUnit component tests
- `SRNSMudApp.E2ETests/` — Playwright end-to-end tests
