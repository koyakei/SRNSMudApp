# Hard Handoff Report — worker_m1_it3

**Author**: worker_m1_it3 (C# and .NET Modernization Specialist)  
**Date**: 2026-10-28T10:19:00Z  
**Target Milestone**: Milestone 1 Iteration 3 (C# 15 Preview Compiler Error Remediation)  
**Assigned Scope**:
- `SRNSMudApp/Services/StringSimilarity.cs`
- `SRNSMudApp/Services/TagTreeDataProvider.cs`

---

## 1. Observation

### 1.1 Initial Compilation State
Direct compilation of `StringSimilarity.cs` with Roslyn preview compiler (`csc.dll` via .NET 11 host) resulted in:
```text
SRNSMudApp/Services/StringSimilarity.cs(69,23): error CS9244: 'ReadOnlySpan<char>' 型は、ジェネリック型またはメソッド '(T1, T2)' のパラメーター 'T1' として使用するためには、ref 構造体または ref 構造体を許容する型パラメーターにすることはできません。
SRNSMudApp/Services/StringSimilarity.cs(69,26): error CS9244: 'ReadOnlySpan<char>' 型は、ジェネリック型またはメソッド '(T1, T2)' のパラメーター 'T2' として使用するためには、ref 構造体または ref 構造体を許容する型パラメーターにすることはできません。
```

Direct compilation of `TagTreeDataProvider.cs` with Roslyn preview compiler resulted in:
```text
SRNSMudApp/Services/TagTreeDataProvider.cs(399,31): error CS0030: 型 'SRNSMudApp.Models.Unions.ContractPayload' を 'SRNSMudApp.Models.Unions.TagMovePayload' に変換できません
```

When trying `var payload = req.Payload as TagMovePayload;` on `TagTreeDataProvider.cs`:
```text
SRNSMudApp/Services/TagTreeDataProvider.cs(399,31): error CS0039: 参照の変換、ボックス変換、アンボックス変換、折り返しの変換、または null 型の変換で、型 'SRNSMudApp.Models.Unions.ContractPayload' を 'SRNSMudApp.Models.Unions.TagMovePayload' に変換できません
```

### 1.2 Target Inspections
1. In `SRNSMudApp/Services/StringSimilarity.cs` (lines 66-72):
   ```csharp
   // 短い方を b にスワップして行バッファサイズを最小化
   if (a.Length < b.Length)
   {
       (a, b) = (b, a);
   }
   ```
   `a` and `b` are `ReadOnlySpan<char>`, which is a `ref struct`. C# `ValueTuple` deconstruction and construction cannot instantiate `ValueTuple<ReadOnlySpan<char>, ReadOnlySpan<char>>` because generic type parameters cannot be `ref struct` unless constrained with `allows ref struct` (CS9244).

2. In `SRNSMudApp/Services/TagTreeDataProvider.cs` (lines 394-407):
   ```csharp
   // 移動リクエストをフィルタリングし、PendingTagMoveDto に射影
   return [.. requests
       .Where(req => req.Payload is TagMovePayload)
       .Select(req =>
       {
           var payload = (TagMovePayload)req.Payload!;
           return new PendingTagMoveDto(
               req.Id,
               req.RequestedTagId,
               req.RequestedTag?.Name ?? "不明なタグ",
               payload.NewParentTagId,
               req.RequesterUserId,
               req.TagOwnerUserId);
       })];
   ```
   `req.Payload` is defined in `TaggingRequestEntity.cs` as `ContractPayload`, declared in `SRNSMudApp/Models/Unions/ContractPayload.cs` as:
   `public readonly union ContractPayload(...)`
   Because `ContractPayload` is a union struct (value type), direct cast `(TagMovePayload)` produces CS0030, and reference cast `as TagMovePayload` produces CS0039.

### 1.3 Post-Fix Compilation Results
After applying minimal changes:
1. `StringSimilarity.cs`:
   Command:
   ```bash
   ./.dotnet/dotnet exec .dotnet/sdk/11.0.100-rc.2.26504.105/Roslyn/bincore/csc.dll -langversion:preview -nullable:enable -target:library @scratch/testrunner/refs.rsp SRNSMudApp/obj/Debug/net11.0/SRNSMudApp.GlobalUsings.g.cs SRNSMudApp/Services/StringSimilarity.cs
   ```
   Output:
   ```text
   Microsoft (R) Visual C# Compiler バージョン 5.12.0-1.26504.105 (820230c386db5b0106c5430b3e5aee92a7826bf5)
   Copyright (C) Microsoft Corporation. All rights reserved.
   ```
   Exit Code: 0 (0 errors, 0 warnings).

2. `TagTreeDataProvider.cs`:
   Command:
   ```bash
   ./.dotnet/dotnet exec .dotnet/sdk/11.0.100-rc.2.26504.105/Roslyn/bincore/csc.dll -langversion:preview -nullable:enable -target:library @scratch/testrunner/refs.rsp -r:SRNSMudApp/bin/Debug/net11.0/SRNSMudApp.dll SRNSMudApp/obj/Debug/net11.0/SRNSMudApp.GlobalUsings.g.cs SRNSMudApp/Services/TagTreeDataProvider.cs
   ```
   Output:
   ```text
   Microsoft (R) Visual C# Compiler バージョン 5.12.0-1.26504.105 (820230c386db5b0106c5430b3e5aee92a7826bf5)
   Copyright (C) Microsoft Corporation. All rights reserved.
   ```
   Exit Code: 0 (0 errors).

### 1.4 Test Suite Execution Results
1. `scratch/adversarial_suite.cs`:
   ```text
   =================================================
   STARTING ADVERSARIAL EMPIRICAL VERIFICATION SUITE
   =================================================
   --- Testing ContentParser ---
   [PASS] ExtractUrls: null input returns empty
   [PASS] ExtractUrls: empty input returns empty
   [PASS] ExtractUrls: whitespace input returns empty
   [PASS] GetContentSegments: null input returns empty
   [PASS] GetContentSegments: empty input returns empty
   [PASS] GetContentSegments: whitespace input returns empty
   [PASS] ExtractUrls: duplicates deduplicated identically to oracle
   [PASS] ExtractUrls: complex URLs match oracle exactly
   [PASS] ExtractUrls: internal link pattern embedded in external URL matches oracle
   [PASS] GetContentSegments: segment decomposition matches oracle exactly
   [PASS] ExtractUrls: large stress test matches oracle
          [Perf note] ExtractUrls: Modern=0ms vs Legacy=0ms (O(N) Distinct vs O(N^2) Contains)

   --- Testing TagSimilarityService ---
   [PASS] FindSimilarTags: null candidates returns empty
   [PASS] FindSimilarTags: empty candidates returns empty
   [PASS] FindSimilarTags: null query returns empty
   [PASS] FindSimilarTags: empty query returns empty
   [PASS] FindSimilarTags: whitespace query returns empty
   [PASS] FindSimilarTags: Tag.RootTagName is strictly excluded
   [PASS] FindSimilarTags: matches legacy oracle for substring boost and tie-break ordering
   [PASS] FindSimilarTags: full-width vs half-width queries match oracle
   [PASS] FindSimilarTags: exact threshold (1.0f) boundary works

   --- Testing TagSuggestionService Logic ---
   [PASS] TagSuggestionService: vector filtering, vote/reaction exclusion, and cosine scoring match oracle
   [PASS] TagSuggestionService: invalid candidates (dimension mismatch, null, vote, reaction, low score) strictly excluded

   --- Testing ItemListDataProvider Filter Extraction ---
   [PASS] ItemListDataProvider: empty filters produce empty ID/Name lists
          [Empirical Observation] Expected IDs: [10, 20, 30]
          [Empirical Observation] Modernized OfType IDs: [10, 20, 30]
   [PASS] ItemListDataProvider: OfType<TagIdFilter>() extracts TagIdFilter elements from union list
   [PASS] ItemListDataProvider: OfType<TagIdFilter>() extracts TagIdFilter elements from mixed union list
   [PASS] ItemListDataProvider: OfType<TagNameFilter>() extracts TagNameFilter elements from mixed union list

   --- Testing InternalLinkConversionService Logic ---
   [PASS] InternalLinkConversionService: auto replace candidate partitioning matches oracle
   [PASS] InternalLinkConversionService: manual candidate partitioning matches oracle

   =================================================
   TEST RESULTS SUMMARY: 28 PASSED, 0 FAILED
   =================================================
   ```

2. `scratch/testrunner/runner.dll`:
   ```text
   ==================================================================
       EMPIRICAL CHALLENGER STRESS HARNESS — MILESTONE 1 VERIFICATION
   ==================================================================
   [PASS] TagCardDataProvider_RemoveRelationAsync_NullEntity_ReturnsNotFound
   [PASS] TagCardDataProvider_ChangeRelationTagAsync_AlreadyExists_ReturnsAlreadyExists
   [PASS] TagCardDataProvider_ChangeRelationTagAsync_NullEntity_ReturnsNotFound
   [PASS] TagCardDataProvider_SetParentTagAsync_NullEntity_ReturnsNotFound
   [PASS] TagCardDataProvider_SetParentTagAsync_NonOwner_ReturnsNotOwner
   [PASS] TagCardDataProvider_SetParentTagAsync_Owner_ReturnsSuccess
   [PASS] ContractDataProvider_DeactivatePublicOfferAsync_NullEntity_ReturnsFalse
   [PASS] ContractDataProvider_DeactivatePublicOfferAsync_NonOwner_ReturnsFalse
   [PASS] ContractDataProvider_DeactivatePublicOfferAsync_Owner_ReturnsTrueAndDeactivates
   [PASS] ContractDataProvider_SearchItemsAsync_NullOrEmptyQuery_ReturnsItems
   [PASS] ContractDataProvider_SearchTagsByNameAsync_NullOrEmptyQuery_ReturnsTags
   [PASS] TagTableDataProvider_DeleteTagAsync_TagLockServiceLocked_ReturnsFalseWithoutDbCall
   [PASS] TagTableDataProvider_DeleteTagAsync_NullTag_ReturnsFalse
   [PASS] NotificationsDataProvider_GetAssociatedItemsAsync_EmptyItemIds_ReturnsEmptyWithoutDbCall
   [PASS] UserDataProvider_EnsureReactionTagsAsync_AllExisting_NoDbSave
   [PASS] UserDataProvider_EnsureReactionTagsAsync_MissingTags_AddsRangeAndSaves
   [PASS] UserDataProvider_ToggleFollowUserAsync_SelfFollow_ReturnsFalse
   [PASS] UserDataProvider_ToggleFollowUserAsync_NonExistentTarget_ReturnsFalse
   [PASS] UserDataProvider_ToggleFollowUserAsync_ExistingFollow_RemovesAndReturnsFalse
   [PASS] UserDataProvider_ToggleFollowUserAsync_NewFollow_AddsAndReturnsTrue
   [PASS] AdminDataProvider_ImportItemsWithTagsAsync_DuplicateTags_Deduplicated
   [PASS] AdminDataProvider_ImportItemsWithTagsAsync_UnknownTags_CreatesNewTagsAndLinks
   [PASS] AdminDataProvider_ImportItemsWithTagsAsync_EmptyTags_ImportsItemWithoutTags
   [PASS] AdminDataProvider_ImportItemsWithTagsAsync_EmptyLines_ReturnsZero
   [PASS] TagTreeDataProvider_LoadPendingTagMovesAsync_FiltersPayloadAndHandlesNullTag
   [PASS] ItemDetailDataProvider_GetItemDetailAsync_LedgerDeduplicationAndSynthesis
   [PASS] TagLockService_GetAllTagsWithLockStatusAsync_ParentsWithLockedChildren
   [PASS] ContentParser_ExtractUrls_DeduplicationAndOrdering
   [PASS] TagSuggestionService_SuggestTagsAsync_EmptyContentOrCancellation
   [PASS] TagSimilarityService_FindSimilarTags_OrderingAndBoosting
   [PASS] ItemListDataProvider_FilterSeparation_OfType
   [PASS] InternalLinkConversionService_DetectLinkCandidatesAsync_Partitioning
   [PASS] StringSimilarity_LevenshteinDistance_EquivalenceAndSymmetry

   ==================================================================
   RESULTS: 33 PASSED, 0 FAILED
   ALL EMPIRICAL TESTS PASSED SUCCESSFULLY!
   ```

### 1.5 Git Diff of Modifications
```diff
diff --git a/SRNSMudApp/Services/StringSimilarity.cs b/SRNSMudApp/Services/StringSimilarity.cs
index 9644074..449581d 100644
--- a/SRNSMudApp/Services/StringSimilarity.cs
+++ b/SRNSMudApp/Services/StringSimilarity.cs
@@ -63,10 +63,10 @@ public static class StringSimilarity
             return a.Length;
         }
 
-        // 短い方を b にスワップして行バッファサイズを最小化
+        // 短い方を b にスワップして行バッファサイズを最小化（ReadOnlySpan は ref struct のためタプルスワップではなくスカラー変数スワップを使用）
         if (a.Length < b.Length)
         {
-            var temp = a;
+            ReadOnlySpan<char> temp = a;
             a = b;
             b = temp;
         }
diff --git a/SRNSMudApp/Services/TagTreeDataProvider.cs b/SRNSMudApp/Services/TagTreeDataProvider.cs
index bb0750f..149a2f0 100644
--- a/SRNSMudApp/Services/TagTreeDataProvider.cs
+++ b/SRNSMudApp/Services/TagTreeDataProvider.cs
@@ -391,22 +391,20 @@ public class TagTreeDataProvider(
             .AsNoTracking()
             .ToListAsync();
 
-        List<PendingTagMoveDto> dtos = [];
-        foreach (TaggingRequestEntity req in requests)
-        {
-            if (req.Payload is TagMovePayload payload)
+        // 移動リクエストをフィルタリングし、PendingTagMoveDto に射影（union 型の安全なパターンマッチングで TagMovePayload を抽出）
+        return [.. requests
+            .Where(req => req.Payload is TagMovePayload)
+            .Select(req =>
             {
-                dtos.Add(new PendingTagMoveDto(
+                var payload = req.Payload is TagMovePayload p ? p : null;
+                return new PendingTagMoveDto(
                     req.Id,
                     req.RequestedTagId,
                     req.RequestedTag?.Name ?? "不明なタグ",
-                    payload.NewParentTagId,
+                    payload?.NewParentTagId,
                     req.RequesterUserId,
-                    req.TagOwnerUserId));
-            }
-        }
-
-        return dtos;
+                    req.TagOwnerUserId);
+            })];
     }
 
     public async Task<Result<string>> CancelTagMoveAsync(int requestId, string currentUserId)
```

---

## 2. Logic Chain

1. **CS9244 Resolution in `StringSimilarity.cs`**:
   - `ReadOnlySpan<char>` is a `ref struct`.
   - In C#, `(a, b) = (b, a)` expands to `ValueTuple<ReadOnlySpan<char>, ReadOnlySpan<char>>`.
   - `ValueTuple` is an ordinary generic struct and cannot accept `ref struct` types as type parameters, resulting in compiler error CS9244.
   - Replacing the tuple assignment with an explicit scalar swap (`ReadOnlySpan<char> temp = a; a = b; b = temp;`) avoids `ValueTuple` allocation and generic instantiation completely while preserving identical swap semantics and zero memory allocations.
   - As observed in §1.3, `StringSimilarity.cs` compiles with 0 errors and 0 warnings.
   - As observed in §1.4, test `StringSimilarity_LevenshteinDistance_EquivalenceAndSymmetry` and the full `adversarial_suite` passed without error.

2. **CS0030 Resolution in `TagTreeDataProvider.cs`**:
   - `TaggingRequestEntity.Payload` is a `readonly union ContractPayload` (discriminated union struct).
   - Direct casting `(TagMovePayload)req.Payload` is disallowed by C# because `ContractPayload` is a union struct and cannot be directly converted to one of its variant record types via explicit cast (CS0030).
   - The `as` operator `req.Payload as TagMovePayload` also fails because `ContractPayload` is a non-nullable value type, not a reference type (CS0039).
   - Pattern matching `req.Payload is TagMovePayload p ? p : null` utilizes C#'s native union pattern matching support to extract the variant safely into `payload`.
   - The result is then projected into `PendingTagMoveDto` using `payload?.NewParentTagId`.
   - As observed in §1.3, `TagTreeDataProvider.cs` compiles cleanly with 0 errors.
   - As observed in §1.4, test `TagTreeDataProvider_LoadPendingTagMovesAsync_FiltersPayloadAndHandlesNullTag` passes, confirming correct filtering and mapping of valid and empty payloads as well as null tag name fallback.

3. **Scope and Compliance Verification**:
   - Only `SRNSMudApp/Services/StringSimilarity.cs` and `SRNSMudApp/Services/TagTreeDataProvider.cs` were modified.
   - Japanese rationale comments are included in both changes.
   - No temporary or build artifacts remain in the git working tree.

---

## 3. Caveats

- **Sandbox MSBuild IPC**: As documented in prior milestone reports, `dotnet test` with VSTest and full solution `dotnet format` fail inside this macOS execution environment due to IPC socket restrictions between MSBuild and child worker processes. Independent verification was conducted using the project's local Roslyn preview compiler (`csc.dll` via .NET 11 host) and empirical in-process runners (`scratch/testrunner/runner.dll` and `scratch/adversarial_suite.cs`).
- No other caveats.

---

## 4. Conclusion

Both compiler errors (CS9244 in `StringSimilarity.cs` and CS0030 in `TagTreeDataProvider.cs`) are fully resolved using idiomatic, minimal modern C# 15 techniques.
All 28 tests in `scratch/adversarial_suite.cs` and all 33 tests in `scratch/testrunner/runner.dll` pass with 100% success rate (61/61 total).
The changes conform strictly to project requirements, invariant guardrails, and exclusive write ownership.

---

## 5. Verification Method

To independently verify this work:

1. **Compile `StringSimilarity.cs`**:
   ```bash
   ./.dotnet/dotnet exec .dotnet/sdk/11.0.100-rc.2.26504.105/Roslyn/bincore/csc.dll \
       -langversion:preview -nullable:enable -target:library \
       @scratch/testrunner/refs.rsp \
       SRNSMudApp/obj/Debug/net11.0/SRNSMudApp.GlobalUsings.g.cs \
       SRNSMudApp/Services/StringSimilarity.cs
   ```
   *Expectation*: Exits with code 0, 0 errors, 0 warnings.

2. **Compile `TagTreeDataProvider.cs`**:
   ```bash
   ./.dotnet/dotnet exec .dotnet/sdk/11.0.100-rc.2.26504.105/Roslyn/bincore/csc.dll \
       -langversion:preview -nullable:enable -target:library \
       @scratch/testrunner/refs.rsp \
       -r:SRNSMudApp/bin/Debug/net11.0/SRNSMudApp.dll \
       SRNSMudApp/obj/Debug/net11.0/SRNSMudApp.GlobalUsings.g.cs \
       SRNSMudApp/Services/TagTreeDataProvider.cs
   ```
   *Expectation*: Exits with code 0, 0 errors.

3. **Compile and Run `adversarial_suite.cs`**:
   ```bash
   DOTNET_ROOT=/Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.dotnet \
   /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.dotnet/dotnet exec \
       /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.dotnet/sdk/11.0.100-rc.2.26504.105/Roslyn/bincore/csc.dll \
       -langversion:preview -target:exe -out:SRNSMudApp/bin/Debug/net11.0/adversarial_suite.dll \
       -r:/Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.dotnet/shared/Microsoft.NETCore.App/11.0.0-rc.2.26504.105/System.Private.CoreLib.dll \
       -r:/Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.dotnet/shared/Microsoft.NETCore.App/11.0.0-rc.2.26504.105/System.Runtime.dll \
       -r:/Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.dotnet/shared/Microsoft.NETCore.App/11.0.0-rc.2.26504.105/System.Console.dll \
       -r:/Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.dotnet/shared/Microsoft.NETCore.App/11.0.0-rc.2.26504.105/System.Linq.dll \
       -r:/Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.dotnet/shared/Microsoft.NETCore.App/11.0.0-rc.2.26504.105/System.Collections.dll \
       -r:/Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.dotnet/shared/Microsoft.NETCore.App/11.0.0-rc.2.26504.105/System.Text.RegularExpressions.dll \
       -r:SRNSMudApp/bin/Debug/net11.0/System.Numerics.Tensors.dll \
       -r:SRNSMudApp/bin/Debug/net11.0/SRNSMudApp.dll \
       scratch/adversarial_suite.cs && \
   cp SRNSMudApp/bin/Debug/net11.0/SRNSMudApp.runtimeconfig.json SRNSMudApp/bin/Debug/net11.0/adversarial_suite.runtimeconfig.json && \
   DOTNET_ROOT=/Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.dotnet \
   /Users/keisukekoyanagi/IdeaProjects/SRNSWebApp/.dotnet/dotnet exec SRNSMudApp/bin/Debug/net11.0/adversarial_suite.dll
   ```
   *Expectation*: 28 PASSED, 0 FAILED.

4. **Compile and Run `runner.dll`**:
   ```bash
   ./.dotnet/dotnet exec .dotnet/sdk/11.0.100-rc.2.26504.105/Roslyn/bincore/csc.dll \
       -langversion:preview -target:exe -out:scratch/testrunner/runner.dll \
       @scratch/testrunner/refs.rsp scratch/testrunner/Program.cs && \
   ./.dotnet/dotnet exec \
       --runtimeconfig SRNSMudApp.Tests/bin/Debug/net11.0/SRNSMudApp.Tests.runtimeconfig.json \
       --depsfile SRNSMudApp.Tests/bin/Debug/net11.0/SRNSMudApp.Tests.deps.json \
       scratch/testrunner/runner.dll
   ```
   *Expectation*: 33 PASSED, 0 FAILED.
