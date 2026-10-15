## 2026-10-14T13:36:00Z
You are Worker 1 (worker_m1) responsible for implementing Milestone 1: Controllers & Middleware Modernization.

Your working directory is: /workspaces/SRNSWebApp/.agents/teamwork/worker_m1
You MUST read /workspaces/SRNSWebApp/.agents/teamwork/ORIGINAL_REQUEST.md (under header ## 2026-10-14T13:04:12Z) before starting work.
Also read:
- /workspaces/SRNSWebApp/.agents/teamwork/orchestrator_2/PROJECT.md
- /workspaces/SRNSWebApp/.agents/teamwork/explorer_survey_ctrl_mid/handoff.md (Detailed survey catalog with 17 concrete modernization candidates)
- /workspaces/SRNSWebApp/.agents/teamwork/explorer_survey_baseline/handoff.md (Baseline build, test, and analyzer report)
- Repository rules: /workspaces/SRNSWebApp/AGENTS.md, /workspaces/SRNSWebApp/.agents/rules/mainRules.md, and /workspaces/SRNSWebApp/.agents/skills/dotnet-best-practices/SKILL.md

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_preview_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Scope & Write Ownership:
You have exclusive write ownership over:
- `SRNSMudApp/Controllers/` (e.g., PushNotificationController, BountyController, ContractController, InvitationController, ItemController, ItemSplitController, LinkPreviewController, PublicOfferController, ReportController, RightAssetController, TagController, TagDiagramController, TagImportController, TagLockController, TagProposalController, UserController, UserGroupController)
- `SRNSMudApp/Middlewares/AntiforgeryCookieCleanupMiddleware.cs`
- `SRNSMudApp/Endpoints/SitemapEndpoints.cs`
- `SRNSMudApp/Program.cs` (pipeline and DI configuration)
DO NOT edit any Razor component files, which are reserved for Milestone 2.

Tasks to Implement:
Implement modern C# features and declarative LINQ as surveyed in `explorer_survey_ctrl_mid/handoff.md`:
1. PushNotificationController: Adopt property pattern matching for subscription validation and user identity claim extraction.
2. BountyController: Convert imperative DTO list construction to declarative LINQ `.Select(...)`; convert `Result<bool>` switch statement to switch expression.
3. ContractController: Convert verbose `Result<T>` switch statements with dead `default: break;` to clean C# switch expressions.
4. InvitationController: Modernize `MapToDto` with property pattern switch expression and target-typed `new()`.
5. ItemController: Convert imperative filter `foreach` loop to declarative LINQ `.Select(...).OfType<ItemListFilter>()`; convert `Result<string>` to switch expression; use target-typed `new()`.
6. ItemSplitController: Modernize 5 `Result<T>` switch statements to switch expressions.
7. LinkPreviewController & PublicOfferController: Adopt helper/method group with target-typed `new()`; modernize 3 `Result<bool>` switch statements to switch expressions.
8. ReportController & RightAssetController: Use ternary async expressions; convert `Result<RightAsset>` switch statement to switch expression with target-typed `new()`.
9. TagController, TagDiagramController, TagImportController, TagLockController, TagProposalController: Adopt property patterns, LINQ tuple projection in item ID parsing, async null-coalescing `?? await`, relational pattern matching `is < 0 or > 20`, and 8 `Result<T>` switch expressions.
10. AntiforgeryCookieCleanupMiddleware: Adopt primary constructor; convert cookie filtering loop to declarative LINQ `.Where(...).Select(...).ToList()`.
11. Program.cs & SitemapEndpoints.cs: Pattern matching with property extraction, target-typed `new()`.
12. Comply with Japanese comments for design rationale as required by repository rules.

Verification Requirements:
1. `dotnet build SRNSMudApp.sln`: Must succeed with 0 warnings, 0 errors.
2. `dotnet format --diagnostics IDE0055 --verify-no-changes`: Must exit with 0 violations.
3. `dotnet test SRNSMudApp.Tests/SRNSMudApp.Tests.csproj --filter "FullyQualifiedName~Controllers|FullyQualifiedName~Antiforgery"`: Must pass cleanly with 0 failures.
4. Run full test suite `dotnet test SRNSMudApp.Tests/SRNSMudApp.Tests.csproj`.

Deliverables:
Update your `progress.md` after each major step.
When complete, write `handoff.md` in `/workspaces/SRNSWebApp/.agents/teamwork/worker_m1/handoff.md` documenting:
- Files modified
- Changes implemented and rationale
- Verification commands executed with exact output
- Residual risks or notes for reviewers
Notify the orchestrator with `send_message` when your handoff.md is ready.
