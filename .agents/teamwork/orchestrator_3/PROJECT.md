# Project: Comprehensive Inconsistency & Edge-Case Audit (SRNSWebApp)

## Architecture & Scope
SRNSWebApp is a Blazor Web App (.NET 11 / C# 15) using MudBlazor, ASP.NET Core Identity, Entity Framework Core, with client-server interactions across Blazor Server and Blazor WebAssembly.
This project investigates specifications and code inconsistencies across 5 core domains:
1. Items, Quotes & Split Logic
2. Tags, Bounties & Contracts (Gratis, Mutual, Trade)
3. Visibility & Authorization Rules (IsPrivate, TargetUserGroupId)
4. Notifications & Timeline Events
5. Blazor Server vs WASM Consistency & API Validations

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|--------------|--------|
| M1 | Domain 1 Audit: Items, Quotes & Splits | Lifecycle, cascade integrity, rollback, owner checks | none | IN_PROGRESS |
| M2 | Domain 2 Audit: Tags, Bounties & Contracts | Contract states, RightAsset burn/transfer, ledger sync | none | IN_PROGRESS |
| M3 | Domain 3 Audit: Visibility & Authorization | IsPrivate, group propagation, endpoint auth | none | IN_PROGRESS |
| M4 | Domain 4 Audit: Notifications & Events | Event generation, recipients, Notification vs Timeline | none | IN_PROGRESS |
| M5 | Domain 5 Audit: Blazor Server vs WASM | DTOs, validations, status transitions, UI vs API | none | IN_PROGRESS |
| M6 | Cross-Domain Synthesis & Anti-False-Positive | Verify findings, filter false positives, structure report | M1, M2, M3, M4, M5 | PLANNED |
| M7 | Final Report Delivery | Compile INCONSISTENCY_AUDIT_REPORT.md at project root | M6 | PLANNED |
