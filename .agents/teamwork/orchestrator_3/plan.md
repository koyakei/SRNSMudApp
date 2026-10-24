# Execution Plan: Comprehensive Inconsistency & Edge-Case Audit

## Objective
Audit the entire SRNSWebApp solution across all 5 major functional domains to discover discrepancies, domain model inconsistencies, edge-case contradictions, authorization gaps, and API/UI validation mismatches. Produce an authoritative, highly detailed report at `INCONSISTENCY_AUDIT_REPORT.md`.

## Phases & Steps

### Phase 1: Exploration & Domain Audits (Parallel Subagents)
- Step 1.1: Dispatch Explorer 1 for **Items, Quotes & Splits**
  - Scope: Item creation, edition, soft/hard deletion, split logic, quote references, cascade deletion/update integrity (replies, TagRelation), owner authorization guards, transaction rollbacks.
- Step 1.2: Dispatch Explorer 2 for **Tags, Bounties & Contracts**
  - Scope: Gratis, Mutual, Trade contract workflows (apply, approve, reject, withdraw), `RightAsset` burn/mint/consumption rules, `TagRelation` vs `TagWeightLedger` sync, ledger consistency.
- Step 1.3: Dispatch Explorer 3 for **Visibility & Authorization Controls**
  - Scope: `IsPrivate` and `TargetUserGroupId` propagation across quotes/splits/replies, authorization checks in Controllers vs Services vs Pages, anonymous/unauthorized access vulnerabilities.
- Step 1.4: Dispatch Explorer 4 for **Notifications & Timeline Events**
  - Scope: Event publishing across all operations (splits, contracts, items, replies), notification target audience vs privacy settings, TimelineEvent vs Notification type consistency, missing triggers.
- Step 1.5: Dispatch Explorer 5 for **Blazor Server vs WASM & API Validation Consistency**
  - Scope: Validation rules in DTOs vs FluentValidation/DataAnnotation vs Controller actions vs Client API services vs UI forms (length, nullability, status transitions, disabled buttons vs API checks).

### Phase 2: Result Aggregation & False Positive Elimination
- Step 2.1: Collect reports from all 5 domain explorers.
- Step 2.2: Cross-check findings against actual files and lines to verify factual accuracy and eliminate false positives.
- Step 2.3: Synthesize into comprehensive draft grouped by domain and categorized by severity (High, Medium, Low).

### Phase 3: Final Report Generation & Delivery
- Step 3.1: Dispatch Worker to write the finalized `INCONSISTENCY_AUDIT_REPORT.md` to project root.
- Step 3.2: Verify report completeness and adherence to Acceptance Criteria.
- Step 3.3: Send completion message and handoff report back to Sentinel.
