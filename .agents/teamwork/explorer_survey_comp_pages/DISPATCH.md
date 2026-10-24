## 2026-10-14T13:08:03Z
sender: f3f1e0a9-f165-4200-8f34-f761aec7f48c
priority: MESSAGE_PRIORITY_HIGH
content:
You are Survey Explorer 2 (explorer_survey_comp_pages).
Your working directory is: /workspaces/SRNSWebApp/.agents/teamwork/explorer_survey_comp_pages
You MUST read /workspaces/SRNSWebApp/.agents/teamwork/ORIGINAL_REQUEST.md (under header ## 2026-10-14T13:04:12Z) before starting work.
Also read /workspaces/SRNSWebApp/PROJECT.md, /workspaces/SRNSWebApp/AGENTS.md, /workspaces/SRNSWebApp/.agents/rules/mainRules.md, and skills at /workspaces/SRNSWebApp/.agents/skills/dotnet-best-practices/SKILL.md.

Mission:
Perform an exhaustive survey of all active C# code in Razor Components and Pages:
- Check SRNSMudApp/Components/Pages/ and any shared components in SRNSMudApp/Components/ (e.g., code-behind .razor.cs and @code blocks in .razor).
- Check SRNSMudApp.Client/Pages/ and SRNSMudApp.Client/Components/ or related client-side files.
Identify concrete opportunities for:
1. Modern C# features: pattern matching, switch expressions, target-typed new(), collection expressions [], null-checking idioms.
2. Declarative LINQ: converting imperative loops to declarative LINQ expressions where clarity and readability are improved without degrading performance.
3. Architectural rules compliance: ensure Blazor UI separation is maintained (no direct DB access or IDialogService misuse, async I/O, event unsubscription).
4. Roslynator and IDE analyzer warnings in these component files.

DO NOT modify any code. You are read-only.
Update your progress.md as you proceed.
Write a comprehensive handoff.md in your working directory (/workspaces/SRNSWebApp/.agents/teamwork/explorer_survey_comp_pages/handoff.md) listing:
- Catalog of all scanned files with line numbers
- Specific candidate blocks with before/after modernization proposals
- Analysis of Blazor lifecycle/rendering considerations
- Recommendations for Milestone decomposition
Notify the orchestrator with send_message when your handoff.md is ready.
