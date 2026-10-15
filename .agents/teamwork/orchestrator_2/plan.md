# Plan — Modernizing Active C# Code Across SRNSMudApp

## Objective
Modernize active C# code (Controllers, Middleware, and Pages/Components) across SRNSMudApp and SRNSMudApp.Client by adopting modern C# features (pattern matching, switch expressions, target-typed new(), declarative LINQ) and eliminating analyzer/Roslynator warnings with zero behavioral regression.

## Execution Phases

### Phase 0: Architecture & Codebase Survey
- Spawn 3 parallel Explorers:
  1. `explorer_survey_ctrl_mid`: Survey Controllers and Middleware in SRNSMudApp.
  2. `explorer_survey_comp_pages`: Survey Components and Pages across SRNSMudApp and SRNSMudApp.Client.
  3. `explorer_survey_baseline`: Verify current build, tests, and analyzer warnings baseline.
- Synthesize findings into `PROJECT.md` Feature Inventory and Milestone Plan.

### Phase 1: Milestone 1 — Modernizing Controllers & Middleware
- Scope: All Controllers (`SRNSMudApp/Controllers/`) and Middleware (`SRNSMudApp/Middleware/`, `Program.cs` pipeline).
- Cycle:
  1. Explorers (x3) analyze specific transformation points.
  2. Worker implements modernizations and ensures clean build & tests.
  3. Reviewers (x2) perform independent code and architecture review.
  4. Challengers (x2) test edge cases, concurrency, and behavioral equivalence.
  5. Forensic Auditor verifies integrity (zero cheating / genuine implementation).
  6. Gate verification (all pass required).

### Phase 2: Milestone 2 — Modernizing Razor Components & Pages
- Scope: Components and Pages in `SRNSMudApp/Components/` and `SRNSMudApp.Client/`.
- Cycle:
  1. Explorers (x3) analyze component modernization points.
  2. Worker implements modernizations adhering to Blazor UI separation and asynchronous I/O.
  3. Reviewers (x2) verify MudBlazor usage, UI logic, and analyzer cleanliness.
  4. Challengers (x2) test component lifecycle, event handling, and rendering equivalence.
  5. Forensic Auditor verifies integrity.
  6. Gate verification.

### Phase 3: Final Verification & Cleanliness Milestone
- Scope: Whole solution verification (`SRNSMudApp.sln`).
- Steps:
  1. `dotnet build` with 0 warnings/errors.
  2. `dotnet format --diagnostics IDE0055` passes with 0 violations.
  3. `dotnet test SRNSMudApp.Tests/SRNSMudApp.Tests.csproj` passes 100%.
  4. `dotnet test SRNSMudApp.E2ETests/SRNSMudApp.E2ETests.csproj` passes 100%.
  5. Final Auditor check.
  6. Deliver consolidated final report and notify Sentinel.
