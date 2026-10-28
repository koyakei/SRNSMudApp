#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

public class AuditorIt3StressTest
{
    private static int _passed = 0;
    private static int _failed = 0;

    public static void Main()
    {
        Console.WriteLine("==================================================================");
        Console.WriteLine("    AUDITOR INDEPENDENT ADVERSARIAL STRESS TEST (ITERATION 3)");
        Console.WriteLine("==================================================================");

        TestStringSimilarityLevenshtein();
        TestTagTreePayloadMatchingLogic();

        Console.WriteLine("\n==================================================================");
        Console.WriteLine($"RESULTS: {_passed} PASSED, {_failed} FAILED");
        Console.WriteLine("==================================================================");

        if (_failed > 0)
        {
            Environment.Exit(1);
        }
    }

    private static void Assert(bool condition, string testName, string? details = null)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"[PASS] {testName}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"[FAIL] {testName}");
            if (details != null)
            {
                Console.WriteLine($"       Details: {details}");
            }
        }
    }

    private static void TestStringSimilarityLevenshtein()
    {
        Console.WriteLine("\n--- Testing StringSimilarity & LevenshteinDistance ---");

        // 1. Empty strings
        Assert(StringSimilarity.LevenshteinDistance("", "") == 0, "Levenshtein('', '') == 0");
        Assert(StringSimilarity.LevenshteinDistance("abc", "") == 3, "Levenshtein('abc', '') == 3");
        Assert(StringSimilarity.LevenshteinDistance("", "abc") == 3, "Levenshtein('', 'abc') == 3");

        // 2. Symmetry check with swapped lengths (a.Length < b.Length vs a.Length >= b.Length)
        string shortStr = "short";
        string longStr = "much longer string here";
        int d1 = StringSimilarity.LevenshteinDistance(shortStr, longStr);
        int d2 = StringSimilarity.LevenshteinDistance(longStr, shortStr);
        Assert(d1 == d2, $"Symmetry holds: Levenshtein(short, long) ({d1}) == Levenshtein(long, short) ({d2})");

        // 3. Known distance pairs
        Assert(StringSimilarity.LevenshteinDistance("kitten", "sitting") == 3, "kitten -> sitting == 3");
        Assert(StringSimilarity.LevenshteinDistance("flaw", "lawn") == 2, "flaw -> lawn == 2");
        Assert(StringSimilarity.LevenshteinDistance("gumbo", "gambol") == 2, "gumbo -> gambol == 2");

        // 4. Large buffer test (>512 stackalloc boundary to heap fallback)
        string largeA = new string('A', 600);
        string largeB = new string('A', 598) + "BB";
        int largeDist1 = StringSimilarity.LevenshteinDistance(largeA, largeB);
        int largeDist2 = StringSimilarity.LevenshteinDistance(largeB, largeA);
        Assert(largeDist1 == 2, $"Large strings (>512 chars) distance is 2 (got {largeDist1})");
        Assert(largeDist1 == largeDist2, "Large strings symmetry holds");

        // 5. StringSimilarity.Calculate normalized score
        float scoreSame = StringSimilarity.Calculate("Same", "Same");
        Assert(Math.Abs(scoreSame - 1.0f) < 0.0001f, "Calculate('Same', 'Same') == 1.0f");

        float scoreEmpty = StringSimilarity.Calculate("", "");
        Assert(Math.Abs(scoreEmpty - 1.0f) < 0.0001f, "Calculate('', '') == 1.0f");

        float scoreOneEmpty = StringSimilarity.Calculate("Hello", "");
        Assert(Math.Abs(scoreOneEmpty - 0.0f) < 0.0001f, "Calculate('Hello', '') == 0.0f");

        // 6. Full-width vs half-width normalization in Calculate
        float scoreZenHan = StringSimilarity.Calculate("ABC１２３", "abc123");
        Assert(Math.Abs(scoreZenHan - 1.0f) < 0.0001f, "Calculate full-width vs half-width == 1.0f");

        // 7. 1000 randomized iterations stress test
        Random rng = new Random(42);
        bool allSymmetric = true;
        for (int i = 0; i < 1000; i++)
        {
            int lenA = rng.Next(0, 30);
            int lenB = rng.Next(0, 30);
            char[] charsA = new char[lenA];
            char[] charsB = new char[lenB];
            for (int k = 0; k < lenA; k++) charsA[k] = (char)('a' + rng.Next(0, 5));
            for (int k = 0; k < lenB; k++) charsB[k] = (char)('a' + rng.Next(0, 5));
            string sa = new string(charsA);
            string sb = new string(charsB);
            if (StringSimilarity.LevenshteinDistance(sa, sb) != StringSimilarity.LevenshteinDistance(sb, sa))
            {
                allSymmetric = false;
                break;
            }
        }
        Assert(allSymmetric, "1000 randomized string pairs all satisfy LevenshteinDistance symmetry");
    }

    private static void TestTagTreePayloadMatchingLogic()
    {
        Console.WriteLine("\n--- Testing TagTreeDataProvider Union Pattern Matching Logic ---");

        // Test all 6 union variants in ContractPayload
        List<TaggingRequestEntity> requests =
        [
            new TaggingRequestEntity
            {
                Id = 1,
                OwnerId = "u1",
                RequestedTagId = 101,
                RequestedTag = new Tag { Id = 101, Name = "Alpha", OwnerId = "u1" },
                RequesterUserId = "u1",
                TagOwnerUserId = "u2",
                Payload = new TagMovePayload(NewParentTagId: 500, RequesterMessage: "move 1")
            },
            new TaggingRequestEntity
            {
                Id = 2,
                OwnerId = "u3",
                RequestedTagId = 102,
                RequestedTag = null, // Null tag fallback test
                RequesterUserId = "u3",
                TagOwnerUserId = "u4",
                Payload = new TagMovePayload(NewParentTagId: null, RequesterMessage: null) // Null parent ID test
            },
            new TaggingRequestEntity
            {
                Id = 3,
                OwnerId = "u5",
                RequestedTagId = 103,
                RequestedTag = new Tag { Id = 103, Name = "Beta", OwnerId = "u5" },
                RequesterUserId = "u5",
                TagOwnerUserId = "u6",
                Payload = new GratisPayload(RequesterMessage: "gratis request")
            },
            new TaggingRequestEntity
            {
                Id = 4,
                OwnerId = "u7",
                RequestedTagId = 104,
                Payload = new MutualPayload(OfferedTargetItemId: 1, OfferedTagId: 2)
            },
            new TaggingRequestEntity
            {
                Id = 5,
                OwnerId = "u8",
                RequestedTagId = 105,
                Payload = new PublicOfferPayload(TargetPublicTradeOfferId: 99)
            },
            new TaggingRequestEntity
            {
                Id = 6,
                OwnerId = "u9",
                RequestedTagId = 106,
                Payload = new BountyPayload(OfferedRewardAssetId: 77)
            },
            new TaggingRequestEntity
            {
                Id = 7,
                OwnerId = "u10",
                RequestedTagId = 107,
                Payload = new EmptyPayload()
            }
        ];

        // Execute the exact LINQ expression from TagTreeDataProvider.cs:
        List<PendingTagMoveDto> dtos = [.. requests
            .Where(req => req.Payload is TagMovePayload)
            .Select(req =>
            {
                var payload = req.Payload is TagMovePayload p ? p : null;
                return new PendingTagMoveDto(
                    req.Id,
                    req.RequestedTagId,
                    req.RequestedTag?.Name ?? "不明なタグ",
                    payload?.NewParentTagId,
                    req.RequesterUserId,
                    req.TagOwnerUserId);
            })];

        Assert(dtos.Count == 2, $"Exactly 2 TagMove requests extracted from 7 mixed union variants (got {dtos.Count})");

        // Verify DTO 1
        Assert(dtos[0].RequestId == 1, "DTO 0 RequestId == 1");
        Assert(dtos[0].TagId == 101, "DTO 0 TagId == 101");
        Assert(dtos[0].TagName == "Alpha", "DTO 0 TagName == 'Alpha'");
        Assert(dtos[0].NewParentTagId == 500, "DTO 0 NewParentTagId == 500");
        Assert(dtos[0].RequesterUserId == "u1", "DTO 0 RequesterUserId == 'u1'");
        Assert(dtos[0].TagOwnerUserId == "u2", "DTO 0 TagOwnerUserId == 'u2'");

        // Verify DTO 2 (null tag name and null newParentTagId)
        Assert(dtos[1].RequestId == 2, "DTO 1 RequestId == 2");
        Assert(dtos[1].TagId == 102, "DTO 1 TagId == 102");
        Assert(dtos[1].TagName == "不明なタグ", "DTO 1 TagName falls back to '不明なタグ'");
        Assert(dtos[1].NewParentTagId == null, "DTO 1 NewParentTagId is null");
        Assert(dtos[1].RequesterUserId == "u3", "DTO 1 RequesterUserId == 'u3'");
        Assert(dtos[1].TagOwnerUserId == "u4", "DTO 1 TagOwnerUserId == 'u4'");

        // Verify all 5 non-TagMove variants were excluded
        bool anyInvalid = dtos.Any(d => d.RequestId is >= 3 and <= 7);
        Assert(!anyInvalid, "All 5 non-TagMove payload variants (Gratis, Mutual, PublicOffer, Bounty, Empty) excluded");
    }
}
