# Dispatch — worker_m1_it3

- **Target Files**:
  - `SRNSMudApp/Services/StringSimilarity.cs`
  - `SRNSMudApp/Services/TagTreeDataProvider.cs`
- **Objective**:
  Fix two compile errors introduced during Milestone 1 Iteration 1 while maintaining Japanese explanatory comments:
  1. `SRNSMudApp/Services/StringSimilarity.cs:69`:
     Replace `(a, b) = (b, a);` with scalar swap:
     ```csharp
     ReadOnlySpan<char> temp = a;
     a = b;
     b = temp;
     ```
     Rationale: `ReadOnlySpan<char>` is a `ref struct` and cannot be a type argument of `ValueTuple` (CS9244).
  2. `SRNSMudApp/Services/TagTreeDataProvider.cs:394-408`:
     Inspect `req.Payload` type and `PendingTagMoveDto` constructor. Replace unsafe cast `(TagMovePayload)req.Payload!` with safe pattern matching:
     ```csharp
     return [.. requests
         .Where(req => req.Payload is TagMovePayload)
         .Select(req =>
         {
             var payload = req.Payload as TagMovePayload;
             return new PendingTagMoveDto(
                 req.Id,
                 req.RequestedTagId,
                 req.RequestedTag?.Name ?? "不明なタグ",
                 payload?.NewParentTagId,
                 req.RequesterUserId,
                 req.TagOwnerUserId);
         })];
     ```
     Verify that `NewParentTagId` is properly passed and resolves CS0030.
- **Verification**:
  1. Verify compilation of `StringSimilarity.cs` and `TagTreeDataProvider.cs`.
  2. Run the test harnesses:
     - `scratch/adversarial_suite.cs` (28 tests)
     - `scratch/testrunner/runner.dll` (33 tests)
  3. Confirm 0 compiler errors and 0 test failures.


## 2026-10-28T10:10:36Z
You are worker_m1_it3, a teamwork_worker specializing in C# and .NET modernization.
Your working directory is: /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/worker_m1_it3

MANDATORY FIRST STEPS:
1. Read /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/ORIGINAL_REQUEST.md before starting work.
2. Read /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/worker_m1_it3/DISPATCH.md.
3. Read /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/PROJECT.md and /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/AGENTS.md.

EXCLUSIVE WRITE OWNERSHIP:
You have exclusive write ownership of EXACTLY TWO files:
- SRNSMudApp/Services/StringSimilarity.cs
- SRNSMudApp/Services/TagTreeDataProvider.cs
Do NOT touch or modify any other files in the codebase.

TASKS:
1. In `SRNSMudApp/Services/StringSimilarity.cs`:
   Inspect lines 65-72. Line 69 currently has:
   `(a, b) = (b, a);`
   Because `a` and `b` are `ReadOnlySpan<char>` (ref struct), `ValueTuple` cannot take `ReadOnlySpan<char>` as a type argument, which causes compiler error CS9244.
   Fix this by replacing it with scalar variable swap:
   ```csharp
   // 短い方を b にスワップして行バッファサイズを最小化（ReadOnlySpan は ref struct のためタプルスワップではなくスカラー変数スワップを使用）
   ReadOnlySpan<char> temp = a;
   a = b;
   b = temp;
   ```
2. In `SRNSMudApp/Services/TagTreeDataProvider.cs`:
   Inspect lines 394–408 (`LoadPendingTagMovesAsync`). Line 399 currently has:
   `var payload = (TagMovePayload)req.Payload!;`
   Direct casting causes CS0030 if `req.Payload` is a discriminated union struct or polymorphic type.
   Inspect the definition of `TagMovePayload`, `req.Payload`, and `PendingTagMoveDto`.
   Safely extract `TagMovePayload` using pattern matching:
   ```csharp
   // 移動リクエストをフィルタリングし、PendingTagMoveDto に射影
   return [.. requests
       .Where(req => req.Payload is TagMovePayload)
       .Select(req =>
       {
           var payload = req.Payload as TagMovePayload;
           return new PendingTagMoveDto(
               req.Id,
               req.RequestedTagId,
               req.RequestedTag?.Name ?? "不明なタグ",
               payload?.NewParentTagId,
               req.RequesterUserId,
               req.TagOwnerUserId);
       })];
   ```
   (Or use `switch (req.Payload)` pattern matching if `TagMovePayload` is part of a union or struct).
   Ensure all compiler errors (CS0030) are resolved.

3. VERIFICATION:
   - Check compilation of `StringSimilarity.cs` and `TagTreeDataProvider.cs` using the local Roslyn preview compiler (`csc.dll` via .dotnet/dotnet exec).
   - Execute the adversarial suite (`scratch/adversarial_suite.cs`) and test runner (`scratch/testrunner/runner.dll`) to verify that all tests pass without errors.
   - Run `git diff` on both files to verify minimal, correct changes and Japanese comments explaining rationale.

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

OUTPUT REQUIREMENTS:
Write your hard handoff report to:
`/Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.agents/teamwork/worker_m1_it3/handoff.md`
Then use `send_message` to notify the orchestrator with your verdict, test results, and report location.
