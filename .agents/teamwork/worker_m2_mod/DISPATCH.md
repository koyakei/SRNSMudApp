# Dispatch — worker_m2_mod

- **Milestone**: Milestone 2 (Blazor UI Components, ViewModels & Client Modernization)
- **Assigned Scope & Exclusive Write Ownership**:
  - `SRNSMudApp/Components/Item/ItemEditViewModel.cs`
  - `SRNSMudApp/Components/UI/TagCardViewModel.cs`
  - `SRNSMudApp/Components/Pages/TagDiagramPage.razor.cs`
  - `SRNSMudApp/Components/Pages/TagDiagramPageViewModel.cs`
  - `SRNSMudApp/Components/Item/ItemListViewModel.cs`
  - `SRNSMudApp/Components/UI/ItemCardViewModel.cs`
  - `SRNSMudApp/Components/Item/ItemListQueryState.cs`
  - `SRNSMudApp/Components/Tag/TagTreeViewModel.cs`
  - `SRNSMudApp/Components/Tag/TagResolutionViewModel.cs`
  - `SRNSMudApp/Components/UI/UrlPreviewCardViewModel.cs`
  - `SRNSMudApp.Client/Services/DoubleSubmissionGuard.cs`
  - Selected ViewModels for Primary Constructor adoption:
    - `SRNSMudApp/Components/Tag/TagAutocompleteViewModel.cs`
    - `SRNSMudApp/Components/Tag/TagResolutionViewModel.cs`
    - `SRNSMudApp/Components/Item/QuotedItemListViewModel.cs`
    - `SRNSMudApp/Components/User/UserSearchViewModel.cs`
    - `SRNSMudApp/Components/Admin/ReportManagerViewModel.cs`

- **Tasks**:
  1. `ItemEditViewModel.cs:139-167`: Convert `ParsePillsToHtml` imperative loop and indexed array writes to declarative LINQ string projection.
  2. `TagCardViewModel.cs:284-297`: Convert virtual delete event relation insertion to LINQ `AddRange`.
  3. `TagDiagramPage.razor.cs:61-68`: Convert `GetEffectiveItemIds` into declarative LINQ with `Distinct()`.
  4. `TagDiagramPageViewModel.cs:109-142, 184`: Replace discarded `_ = Set.Add(id)` loops with canonical `UnionWith`.
  5. `ItemListViewModel.cs:66-103`: Modernize `InitializeAsync` filter mapping and sort entry mapping with LINQ.
  6. `ItemCardViewModel.cs:176-191`: Replace clumsy nested boolean switch in `GetTagDisplayWeight` with clean pattern-matching switch expression on `highlightEvent?.ParsedEventType`.
  7. `ItemListQueryState.cs:92-108`: Adopt C# list pattern `parts is [var idStr, var orderStr]` and switch expressions.
  8. `TagTreeViewModel.cs:406-420`: Modernize `BuildTreeDataInternal` pending move projection with LINQ `.Select(...)`.
  9. `TagResolutionViewModel.cs:121-128`: Replace manual insertion loop with LINQ `.ExceptBy(...)` and `.Take(...)`.
  10. `UrlPreviewCardViewModel.cs:36-72`: Clean up `ProcessTextFragment` using LINQ and modern pattern matching.
  11. `DoubleSubmissionGuard.cs:117-136`: Modernize `CleanupExpired` to LINQ collection filtering.
  12. Adopt primary constructors on the assigned single/double-dependency ViewModels (`TagAutocompleteViewModel`, `QuotedItemListViewModel`, `UserSearchViewModel`, `ReportManagerViewModel`).

- **INVARIANT GUARDRAILS (STRICT)**:
  - Invariant 1: ALL 119 `@foreach` and `@for` loops in `.razor` markup MUST REMAIN INTACT. DO NOT TOUCH `.razor` markup files!
  - Invariant 2: DbContext asynchronous loops (`UserManagementViewModel`, etc.) MUST remain sequential `await` loops. Never convert to `Task.WhenAll`.
  - Invariant 3: Performance span loops remain imperative.
  - Invariant 4: Japanese explanatory comments explaining design rationale MUST be included in every modified method.

- **Verification**:
  - Verify compilation using local preview Roslyn compiler (`csc.dll` via .dotnet/dotnet exec).
  - Run adversarial suite (`scratch/adversarial_suite.cs`) and test runner (`scratch/testrunner/runner.dll`).
  - Verify git diff for correctness and layout compliance.

## 2026-10-28T10:30:47Z
You are worker_m2_mod, a teamwork_worker specializing in Blazor UI and C# modernization.
Your working directory is: /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/worker_m2_mod

MANDATORY FIRST STEPS:
1. Read /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/ORIGINAL_REQUEST.md before starting work.
2. Read /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/worker_m2_mod/DISPATCH.md.
3. Read /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/PROJECT.md and /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/AGENTS.md.

EXCLUSIVE WRITE OWNERSHIP:
You have exclusive write ownership of the following files:
- SRNSMudApp/Components/Item/ItemEditViewModel.cs
- SRNSMudApp/Components/UI/TagCardViewModel.cs
- SRNSMudApp/Components/Pages/TagDiagramPage.razor.cs
- SRNSMudApp/Components/Pages/TagDiagramPageViewModel.cs
- SRNSMudApp/Components/Item/ItemListViewModel.cs
- SRNSMudApp/Components/UI/ItemCardViewModel.cs
- SRNSMudApp/Components/Item/ItemListQueryState.cs
- SRNSMudApp/Components/Tag/TagTreeViewModel.cs
- SRNSMudApp/Components/Tag/TagResolutionViewModel.cs
- SRNSMudApp/Components/UI/UrlPreviewCardViewModel.cs
- SRNSMudApp.Client/Services/DoubleSubmissionGuard.cs
- SRNSMudApp/Components/Tag/TagAutocompleteViewModel.cs
- SRNSMudApp/Components/Item/QuotedItemListViewModel.cs
- SRNSMudApp/Components/User/UserSearchViewModel.cs
- SRNSMudApp/Components/Admin/ReportManagerViewModel.cs

Do NOT modify any other files in the solution!

STRICT INVARIANTS:
- Invariant 1: ALL 119 `@foreach` and `@for` loops inside `.razor` markup files MUST REMAIN INTACT. DO NOT TOUCH ANY `.razor` markup files!
- Invariant 2: DbContext loops performing async I/O MUST remain sequential `await` loops. Never convert to `Task.WhenAll`.
- Invariant 3: Comments in Japanese explaining design rationale MUST be included in every modified method.

TASKS:
Execute the 12 modernization items detailed in your DISPATCH.md:
1. `ItemEditViewModel.cs:139-167`: Modernize `ParsePillsToHtml` to declarative LINQ string projection.
2. `TagCardViewModel.cs:284-297`: Modernize virtual delete event relation insertion to LINQ `AddRange`.
3. `TagDiagramPage.razor.cs:61-68`: Modernize `GetEffectiveItemIds` into declarative LINQ with `Distinct()`.
4. `TagDiagramPageViewModel.cs:109-142, 184`: Replace discarded `_ = Set.Add(id)` loops with canonical `UnionWith`.
5. `ItemListViewModel.cs:66-103`: Modernize `InitializeAsync` filter and sort entry mapping with LINQ.
6. `ItemCardViewModel.cs:176-191`: Replace clumsy nested boolean switch in `GetTagDisplayWeight` with clean pattern-matching switch expression on `highlightEvent?.ParsedEventType`.
7. `ItemListQueryState.cs:92-108`: Adopt C# list pattern `parts is [var idStr, var orderStr]` and switch expressions.
8. `TagTreeViewModel.cs:406-420`: Modernize `BuildTreeDataInternal` pending move projection with LINQ `.Select(...)`.
9. `TagResolutionViewModel.cs:121-128`: Replace manual insertion loop with LINQ `.ExceptBy(...)` and `.Take(...)`.
10. `UrlPreviewCardViewModel.cs:36-72`: Clean up `ProcessTextFragment` using LINQ and modern pattern matching.
11. `DoubleSubmissionGuard.cs:117-136`: Modernize `CleanupExpired` to LINQ collection filtering.
12. Adopt primary constructors on the assigned ViewModels (`TagAutocompleteViewModel`, `QuotedItemListViewModel`, `UserSearchViewModel`, `ReportManagerViewModel`, `TagResolutionViewModel`).

VERIFICATION:
- Verify compilation with the local Roslyn preview compiler (`csc.dll` via .dotnet/dotnet exec).
- Execute the test runner harnesses (`scratch/adversarial_suite.cs` and `scratch/testrunner/runner.dll`).
- Inspect `git diff` to ensure 100% logical equivalence, zero regressions, and clean Japanese comments.

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

OUTPUT REQUIREMENTS:
Write your hard handoff report to:
`/Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/worker_m2_mod/handoff.md`
Then use `send_message` to notify the orchestrator with your results.

