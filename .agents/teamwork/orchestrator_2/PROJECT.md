# Project: SRNSMudApp Modern C# Codebase Modernization

## Architecture
- **Framework**: .NET 11.0 (`net11.0`, LangVersion: `preview`), ASP.NET Core Blazor Web App (InteractiveServer & InteractiveWebAssembly), MudBlazor 9.7
- **Layers**:
  - `SRNSMudApp/Controllers/`: 19 ASP.NET Core Web API controllers.
  - `SRNSMudApp/Middlewares/`: Antiforgery and pipeline middleware.
  - `SRNSMudApp/Endpoints/`: Minimal API and sitemap endpoints.
  - `SRNSMudApp/Components/`: Blazor Server Razor Components, Pages, and Dialogs.
  - `SRNSMudApp.Client/`: Blazor WebAssembly client components and pages.
  - `SRNSMudApp.Tests/`: Unit and integration test suite (2,849 tests).
  - `SRNSMudApp.E2ETests/`: End-to-end test suite (3 tests).

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | MOD-CTRL-01 Push Auth & Null Patterns | Modernize PushNotificationController with property patterns and simplified user claim extraction | M1 | Survey 1 |
| 2 | MOD-CTRL-02 Bounty LINQ & Switch | Convert imperative BountyController DTO loop to declarative LINQ .Select() and Result switch expression | M1 | Survey 1 |
| 3 | MOD-CTRL-03 Contract Switch Expressions | Modernize ContractController Result<T> switch statements to declarative switch expressions | M1 | Survey 1 |
| 4 | MOD-CTRL-04 Invitation Patterns & new() | Modernize InvitationController MapToDto status logic with property patterns and target-typed new() | M1 | Survey 1 |
| 5 | MOD-CTRL-05 Item Declarative Filters | Replace ItemController imperative filter foreach with declarative LINQ .Select().OfType<ItemListFilter>() | M1 | Survey 1 |
| 6 | MOD-CTRL-06 ItemSplit Switch Expressions | Convert 5 ItemSplitController Result<T> switch statements to switch expressions | M1 | Survey 1 |
| 7 | MOD-CTRL-07 LinkPreview & PublicOffer | Standardize LinkConversionCandidate mapping with target-typed new() and PublicOffer switch expressions | M1 | Survey 1 |
| 8 | MOD-CTRL-08 Report & RightAsset Async | Ternary async resolution expressions in ReportController and Result<RightAsset> switch expression | M1 | Survey 1 |
| 9 | MOD-CTRL-09 Tag & Diagram Modernization | SystemTags property pattern, LINQ tuple projection in TagDiagramController, async coalescing in TagImport | M1 | Survey 1 |
| 10 | MOD-MID-01 Antiforgery Middleware | Primary constructor and declarative LINQ invalid cookie filtering in AntiforgeryCookieCleanupMiddleware | M1 | Survey 1 |
| 11 | MOD-MID-02 Program & Endpoints | Property extraction pattern matching in Program.cs and target-typed new() in SitemapEndpoints.cs | M1 | Survey 1 |
| 12 | MOD-COMP-01 Server Pages Modernization | TagDiagramPage GetEffectiveItemIds LINQ, tuple property pattern matching, NotificationsPage ApplyResult | M2 | Survey 2 |
| 13 | MOD-COMP-02 Shared UI & Item Components | Fix ResourceList async void to async Task, eliminate ItemCard single-case switch, AddItem HashSet.UnionWith | M2 | Survey 2 |
| 14 | MOD-COMP-03 Tag Components Modernization | Convert ItemTagChipList nested switch to declarative LINQ AddRange, TagTree is null or [], Result switch | M2 | Survey 2 |
| 15 | MOD-COMP-04 Admin & Client WASM | TagManagement/InvitationManager union switch expressions to (msg, sev), PasskeySubmit null-coalescing, Client LINQ | M2 | Survey 2 |
| 16 | MOD-VERIF-01 Solution Verification | Verify dotnet build (0 warn/0 err), IDE0055 (0 violations), 2,849 unit tests pass, 3 E2E tests pass | M3 | Survey 3 |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | Controllers & Middleware Modernization | MOD-CTRL-01..09, MOD-MID-01..02 | none | PLANNED |
| 2 | Razor Components & Pages Modernization | MOD-COMP-01..04 | M1 | PLANNED |
| 3 | Final Verification & Test Hardening | MOD-VERIF-01 | M2 | PLANNED |

## Interface Contracts
- **Controller API Contracts**: Public action signatures, HTTP route templates, request/response DTO schemas, and HTTP status codes must remain 100% identical.
- **Blazor Component Public Parameters**: All `[Parameter]` properties, `EventCallback` signatures, and Blazor lifecycle methods must retain their public contracts.
- **Middleware Pipeline**: The request execution order, header manipulation, and cookie cleanup behavior must remain identical.
- **Zero Behavioral Regression**: Exact logical and functional equivalence must be maintained across all modernizations.

## Code Layout
- `SRNSMudApp/Controllers/` - Web API controllers (Push, Auth, Bounty, Contract, Item, etc.)
- `SRNSMudApp/Middlewares/` - ASP.NET Core custom middlewares
- `SRNSMudApp/Endpoints/` - Endpoint route definitions
- `SRNSMudApp/Program.cs` - Host configuration and middleware pipeline
- `SRNSMudApp/Components/Pages/` - Blazor Server pages
- `SRNSMudApp/Components/UI/` - Shared presentation components
- `SRNSMudApp/Components/Tag/` - Tag hierarchy and management components
- `SRNSMudApp/Components/Item/` - Item listing, detail, and creation components
- `SRNSMudApp/Components/Admin/` - Administrative UI components
- `SRNSMudApp.Client/` - Blazor WebAssembly client components and pages
- `SRNSMudApp.Tests/` - Unit tests
- `SRNSMudApp.E2ETests/` - End-to-end integration tests
