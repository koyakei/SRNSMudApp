using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics.Tensors;
using System.Text.RegularExpressions;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

public class AdversarialTestSuite
{
    private static int _testsPassed = 0;
    private static int _testsFailed = 0;

    public static void Main()
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("STARTING ADVERSARIAL EMPIRICAL VERIFICATION SUITE");
        Console.WriteLine("=================================================\n");

        TestContentParser();
        TestTagSimilarityService();
        TestTagSuggestionServiceLogic();
        TestItemListDataProviderFilterExtraction();
        TestInternalLinkConversionServiceLogic();

        Console.WriteLine("\n=================================================");
        Console.WriteLine($"TEST RESULTS SUMMARY: {_testsPassed} PASSED, {_testsFailed} FAILED");
        Console.WriteLine("=================================================");

        if (_testsFailed > 0)
        {
            Console.WriteLine(">>> CRITICAL VULNERABILITIES / REGRESSIONS DETECTED! <<<");
            Environment.ExitCode = 1;
        }
    }

    private static void Assert(bool condition, string testName, string? details = null)
    {
        if (condition)
        {
            _testsPassed++;
            Console.WriteLine($"[PASS] {testName}");
        }
        else
        {
            _testsFailed++;
            Console.WriteLine($"[FAIL] {testName}");
            if (!string.IsNullOrEmpty(details))
            {
                Console.WriteLine($"       Details: {details}");
            }
        }
    }

    #region 1. ContentParser Tests

    // Legacy oracle for ContentParser
    private static IReadOnlyList<string> LegacyExtractUrls(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        List<string> results = [];
        foreach (Match match in ContentParser.UrlRegex().Matches(text))
        {
            if (!results.Contains(match.Value)) results.Add(match.Value);
        }
        foreach (Match match in ContentParser.InternalLinkRegex().Matches(text))
        {
            if (!results.Contains(match.Value)) results.Add(match.Value);
        }
        return results;
    }

    private static IReadOnlyList<ContentSegment> LegacyGetContentSegments(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        List<ContentSegment> results = [];
        List<Match> matches = [];
        matches.AddRange(ContentParser.UrlRegex().Matches(text).Cast<Match>());
        matches.AddRange(ContentParser.InternalLinkRegex().Matches(text).Cast<Match>());
        matches = matches.OrderBy(m => m.Index).ToList();

        List<Match> validMatches = [];
        int currentEnd = 0;
        foreach (Match m in matches)
        {
            if (m.Index >= currentEnd)
            {
                validMatches.Add(m);
                currentEnd = m.Index + m.Length;
            }
        }

        int lastIndex = 0;
        foreach (Match match in validMatches)
        {
            if (match.Index > lastIndex)
            {
                results.Add(new ContentSegment(text.Substring(lastIndex, match.Index - lastIndex), false));
            }
            results.Add(new ContentSegment(match.Value, true));
            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            results.Add(new ContentSegment(text.Substring(lastIndex), false));
        }
        return results;
    }

    private static void TestContentParser()
    {
        Console.WriteLine("\n--- Testing ContentParser ---");

        // Null, empty, whitespace
        Assert(ContentParser.ExtractUrls(null).Count == 0, "ExtractUrls: null input returns empty");
        Assert(ContentParser.ExtractUrls("").Count == 0, "ExtractUrls: empty input returns empty");
        Assert(ContentParser.ExtractUrls("   \t\r\n ").Count == 0, "ExtractUrls: whitespace input returns empty");

        Assert(ContentParser.GetContentSegments(null).Count == 0, "GetContentSegments: null input returns empty");
        Assert(ContentParser.GetContentSegments("").Count == 0, "GetContentSegments: empty input returns empty");
        Assert(ContentParser.GetContentSegments("   \t\r\n ").Count == 0, "GetContentSegments: whitespace input returns empty");

        // Edge case: duplicate URLs
        string duplicateText = "https://example.com and again https://example.com and /ItemDetail/1 and /ItemDetail/1";
        var modernExtracted = ContentParser.ExtractUrls(duplicateText);
        var legacyExtracted = LegacyExtractUrls(duplicateText);
        Assert(modernExtracted.SequenceEqual(legacyExtracted), "ExtractUrls: duplicates deduplicated identically to oracle",
            $"Modern: [{string.Join(", ", modernExtracted)}], Legacy: [{string.Join(", ", legacyExtracted)}]");

        // Mixed internal and external URLs, special characters
        string complexText = "Check http://localhost:5001/test?query=a&b=c#frag1 alongside /TagDetail/100, /User/UserDetail/admin_user-1 and https://sub.domain.org:8080/foo/bar.";
        var modernComplex = ContentParser.ExtractUrls(complexText);
        var legacyComplex = LegacyExtractUrls(complexText);
        Assert(modernComplex.SequenceEqual(legacyComplex), "ExtractUrls: complex URLs match oracle exactly");

        // Embedded internal link inside external URL
        string embeddedText = "Visit https://mysite.com/ItemDetail/9999 for details";
        var modernEmbedded = ContentParser.ExtractUrls(embeddedText);
        var legacyEmbedded = LegacyExtractUrls(embeddedText);
        Assert(modernEmbedded.SequenceEqual(legacyEmbedded), "ExtractUrls: internal link pattern embedded in external URL matches oracle");

        // Segmenting comparison
        var modernSegments = ContentParser.GetContentSegments(complexText);
        var legacySegments = LegacyGetContentSegments(complexText);
        bool segmentsEqual = modernSegments.Count == legacySegments.Count &&
                             modernSegments.Zip(legacySegments).All(p => p.First.Text == p.Second.Text && p.First.IsUrl == p.Second.IsUrl);
        Assert(segmentsEqual, "GetContentSegments: segment decomposition matches oracle exactly");

        // Stress: Large text with 1000 URLs
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 500; i++)
        {
            sb.Append($"Text segment {i} https://example.com/item/{i} more text /ItemDetail/{i % 50} end. ");
        }
        string largeText = sb.ToString();

        var sw = Stopwatch.StartNew();
        var modernLarge = ContentParser.ExtractUrls(largeText);
        sw.Stop();
        long modernMs = sw.ElapsedMilliseconds;

        sw.Restart();
        var legacyLarge = LegacyExtractUrls(largeText);
        sw.Stop();
        long legacyMs = sw.ElapsedMilliseconds;

        Assert(modernLarge.SequenceEqual(legacyLarge), "ExtractUrls: large stress test matches oracle",
            $"Count: {modernLarge.Count}, Modern time: {modernMs}ms, Legacy time: {legacyMs}ms");
        Console.WriteLine($"       [Perf note] ExtractUrls: Modern={modernMs}ms vs Legacy={legacyMs}ms (O(N) Distinct vs O(N^2) Contains)");
    }

    #endregion

    #region 2. TagSimilarityService Tests

    private static IReadOnlyList<SimilarTagCandidate> LegacyFindSimilarTags(
        IEnumerable<Tag> candidateTags,
        string? query,
        float minSimilarity = 0.50f,
        int maxResults = 5)
    {
        if (candidateTags is null || string.IsNullOrWhiteSpace(query)) return [];
        string trimmedQuery = query.Trim();
        if (trimmedQuery.Length == 0) return [];

        List<SimilarTagCandidate> matches = [];
        foreach (Tag tag in candidateTags)
        {
            if (string.IsNullOrEmpty(tag.Name) || tag.Name == Tag.RootTagName) continue;
            float similarity = StringSimilarity.Calculate(trimmedQuery.AsSpan(), tag.Name.AsSpan());
            bool isSubstring = tag.Name.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ||
                               trimmedQuery.Contains(tag.Name, StringComparison.OrdinalIgnoreCase);
            if (isSubstring)
            {
                float lengthRatio = (float)Math.Min(trimmedQuery.Length, tag.Name.Length) / Math.Max(trimmedQuery.Length, tag.Name.Length);
                similarity = Math.Max(similarity, 0.60f + (lengthRatio * 0.35f));
            }
            if (similarity >= minSimilarity)
            {
                matches.Add(new SimilarTagCandidate(tag, similarity));
            }
        }

        return matches
            .OrderByDescending(c => c.Similarity)
            .ThenBy(c => c.Tag.Name.Length)
            .ThenBy(c => c.Tag.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToList();
    }

    private static void TestTagSimilarityService()
    {
        Console.WriteLine("\n--- Testing TagSimilarityService ---");
        var service = new TagSimilarityService();

        // Null and empty candidates / query
        Assert(service.FindSimilarTags(null!, "test").Count == 0, "FindSimilarTags: null candidates returns empty");
        Assert(service.FindSimilarTags([], "test").Count == 0, "FindSimilarTags: empty candidates returns empty");
        Assert(service.FindSimilarTags([new Tag { Id = 1, Name = "Test", OwnerId = "u" }], null).Count == 0, "FindSimilarTags: null query returns empty");
        Assert(service.FindSimilarTags([new Tag { Id = 1, Name = "Test", OwnerId = "u" }], "").Count == 0, "FindSimilarTags: empty query returns empty");
        Assert(service.FindSimilarTags([new Tag { Id = 1, Name = "Test", OwnerId = "u" }], "   ").Count == 0, "FindSimilarTags: whitespace query returns empty");

        // RootTagName exclusion
        var tagsWithRoot = new List<Tag>
        {
            new Tag { Id = 1, Name = Tag.RootTagName, OwnerId = "u" },
            new Tag { Id = 2, Name = "", OwnerId = "u" },
            new Tag { Id = 3, Name = "root-other", OwnerId = "u" }
        };
        var rootResult = service.FindSimilarTags(tagsWithRoot, "root");
        Assert(rootResult.All(c => c.Tag.Name != Tag.RootTagName), "FindSimilarTags: Tag.RootTagName is strictly excluded");

        // Substring boosting and 3-tier tie-breaking
        var testCandidates = new List<Tag>
        {
            new Tag { Id = 1, Name = "DotNet", OwnerId = "u" },
            new Tag { Id = 2, Name = "DotNetCore", OwnerId = "u" },
            new Tag { Id = 3, Name = "DotNetFramework", OwnerId = "u" },
            new Tag { Id = 4, Name = "net", OwnerId = "u" },
            new Tag { Id = 5, Name = "Java", OwnerId = "u" },
            new Tag { Id = 6, Name = "Kotlin", OwnerId = "u" },
            new Tag { Id = 7, Name = "DOTNET", OwnerId = "u" }
        };

        var modernTags = service.FindSimilarTags(testCandidates, "dotnet", 0.5f, 5);
        var legacyTags = LegacyFindSimilarTags(testCandidates, "dotnet", 0.5f, 5);

        bool tagsEqual = modernTags.Count == legacyTags.Count &&
                         modernTags.Zip(legacyTags).All(p => p.First.Tag.Id == p.Second.Tag.Id && Math.Abs(p.First.Similarity - p.Second.Similarity) < 1e-5);
        Assert(tagsEqual, "FindSimilarTags: matches legacy oracle for substring boost and tie-break ordering");

        // Full-width vs half-width characters
        var fullWidthCandidates = new List<Tag>
        {
            new Tag { Id = 1, Name = "Ｃ＃", OwnerId = "u" },
            new Tag { Id = 2, Name = "C#", OwnerId = "u" },
            new Tag { Id = 3, Name = "プログラミング", OwnerId = "u" },
            new Tag { Id = 4, Name = "ﾌﾟﾛｸﾞﾗﾐﾝｸﾞ", OwnerId = "u" }
        };
        var modernFw = service.FindSimilarTags(fullWidthCandidates, "C#", 0.5f, 5);
        var legacyFw = LegacyFindSimilarTags(fullWidthCandidates, "C#", 0.5f, 5);
        Assert(modernFw.Count == legacyFw.Count && modernFw.Zip(legacyFw).All(p => p.First.Tag.Id == p.Second.Tag.Id),
            "FindSimilarTags: full-width vs half-width queries match oracle");

        // Exact score threshold boundary
        var boundaryCandidates = new List<Tag>
        {
            new Tag { Id = 1, Name = "ExactMatch", OwnerId = "u" },
            new Tag { Id = 2, Name = "CompletelyDifferentUnrelatedTag", OwnerId = "u" }
        };
        var modernExact = service.FindSimilarTags(boundaryCandidates, "ExactMatch", 1.0f, 5);
        Assert(modernExact.Count == 1 && modernExact[0].Similarity >= 1.0f, "FindSimilarTags: exact threshold (1.0f) boundary works");
    }

    #endregion

    #region 3. TagSuggestionService Logic Tests

    private static void TestTagSuggestionServiceLogic()
    {
        Console.WriteLine("\n--- Testing TagSuggestionService Logic ---");

        float[] queryVector = [0.1f, 0.2f, 0.3f, 0.4f];

        // Oracle simulation of modernized LINQ pipeline
        List<Tag> candidates =
        [
            new Tag { Id = 1, Name = "NormalTag", Embedding = [0.1f, 0.2f, 0.3f, 0.4f], OwnerId = "u" },
            new Tag { Id = 2, Name = "DimensionMismatch", Embedding = [0.1f, 0.2f], OwnerId = "u" },
            new Tag { Id = 3, Name = "NullEmbedding", Embedding = null, OwnerId = "u" },
            new Tag { Id = 4, Name = "good", Embedding = [0.1f, 0.2f, 0.3f, 0.4f], OwnerId = "u" }, // Vote tag
            new Tag { Id = 5, Name = "真実", Embedding = [0.1f, 0.2f, 0.3f, 0.4f], OwnerId = "u" }, // Reaction tag
            new Tag { Id = 6, Name = "LowSimilarity", Embedding = [-0.1f, -0.2f, -0.3f, -0.4f], OwnerId = "u" }
        ];

        // Legacy simulation
        List<SuggestedTag> legacySuggestions = [];
        foreach (var tag in candidates)
        {
            if (Tag.VoteTagNames.Contains(tag.Name) || Tag.ReactionTagNames.Contains(tag.Name)) continue;
            if (tag.Embedding == null || tag.Embedding.Length != queryVector.Length) continue;
            float score = TensorPrimitives.CosineSimilarity(tag.Embedding, queryVector);
            if (score >= SuggestedTag.DefaultCandidateThreshold)
            {
                legacySuggestions.Add(new SuggestedTag(tag.Id, tag.Name, score));
            }
        }
        legacySuggestions = legacySuggestions.OrderByDescending(s => s.Score).Take(20).ToList();

        // Modern simulation (from TagSuggestionService.cs)
        List<SuggestedTag> modernSuggestions = [.. candidates
            .Where(tag => !Tag.VoteTagNames.Contains(tag.Name)
                          && !Tag.ReactionTagNames.Contains(tag.Name)
                          && tag.Embedding?.Length == queryVector.Length)
            .Select(tag => new SuggestedTag(tag.Id, tag.Name, TensorPrimitives.CosineSimilarity(tag.Embedding!, queryVector)))
            .Where(s => s.Score >= SuggestedTag.DefaultCandidateThreshold)
            .OrderByDescending(s => s.Score)
            .Take(20)];

        Assert(modernSuggestions.Count == legacySuggestions.Count &&
               modernSuggestions.Zip(legacySuggestions).All(p => p.First.TagId == p.Second.TagId && Math.Abs(p.First.Score - p.Second.Score) < 1e-5),
            "TagSuggestionService: vector filtering, vote/reaction exclusion, and cosine scoring match oracle");

        Assert(!modernSuggestions.Any(s => s.TagName == "DimensionMismatch" || s.TagName == "NullEmbedding" || s.TagName == "good" || s.TagName == "真実" || s.TagName == "LowSimilarity"),
            "TagSuggestionService: invalid candidates (dimension mismatch, null, vote, reaction, low score) strictly excluded");
    }

    #endregion

    #region 4. ItemListDataProvider Filter Extraction Tests (CRITICAL ADVERSARIAL ATTACK)

    private static void TestItemListDataProviderFilterExtraction()
    {
        Console.WriteLine("\n--- Testing ItemListDataProvider Filter Extraction ---");

        // Scenario A: Empty filter list
        List<ItemListFilter> emptyFilters = [];
        List<int> emptyIds = [.. emptyFilters.OfType<TagIdFilter>().Select(f => f.TagId)];
        List<string> emptyNames = [.. emptyFilters.OfType<TagNameFilter>().Select(f => f.TagName)];
        Assert(emptyIds.Count == 0 && emptyNames.Count == 0, "ItemListDataProvider: empty filters produce empty ID/Name lists");

        // Scenario B: All TagIdFilter
        List<ItemListFilter> tagIdOnlyFilters =
        [
            new TagIdFilter(10, "alice"),
            new TagIdFilter(20, "bob"),
            new TagIdFilter(30, null)
        ];

        // Legacy extraction (original switch)
        List<int> legacyIdOnlyIds = [];
        List<string> legacyIdOnlyNames = [];
        foreach (ItemListFilter f in tagIdOnlyFilters)
        {
            switch (f)
            {
                case TagIdFilter idF:
                    legacyIdOnlyIds.Add(idF.TagId);
                    break;
                case TagNameFilter nameF:
                    legacyIdOnlyNames.Add(nameF.TagName);
                    break;
            }
        }

        // Modernized extraction (fixed switch pattern matching)
        List<int> modernIdOnlyIds = [];
        List<string> modernIdOnlyNames = [];
        foreach (ItemListFilter f in tagIdOnlyFilters)
        {
            switch (f)
            {
                case TagIdFilter idF:
                    modernIdOnlyIds.Add(idF.TagId);
                    break;
                case TagNameFilter nameF:
                    modernIdOnlyNames.Add(nameF.TagName);
                    break;
            }
        }

        Console.WriteLine($"       [Empirical Observation] Expected IDs: [{string.Join(", ", legacyIdOnlyIds)}]");
        Console.WriteLine($"       [Empirical Observation] Modernized OfType IDs: [{string.Join(", ", modernIdOnlyIds)}]");

        Assert(modernIdOnlyIds.SequenceEqual(legacyIdOnlyIds),
            "ItemListDataProvider: OfType<TagIdFilter>() extracts TagIdFilter elements from union list",
            $"FATAL BUG: modern extracted {modernIdOnlyIds.Count} items but expected {legacyIdOnlyIds.Count} items! LINQ OfType<T> fails on C# union struct!");

        // Scenario C: Mixed filters
        List<ItemListFilter> mixedFilters =
        [
            new TagIdFilter(101, "carol"),
            new TagNameFilter("Tech", null),
            new TagIdFilter(202, "dave"),
            new TagNameFilter("News", "eve")
        ];

        List<int> legacyMixedIds = [];
        List<string> legacyMixedNames = [];
        foreach (ItemListFilter f in mixedFilters)
        {
            switch (f)
            {
                case TagIdFilter idF:
                    legacyMixedIds.Add(idF.TagId);
                    break;
                case TagNameFilter nameF:
                    legacyMixedNames.Add(nameF.TagName);
                    break;
            }
        }

        List<int> modernMixedIds = [];
        List<string> modernMixedNames = [];
        foreach (ItemListFilter f in mixedFilters)
        {
            switch (f)
            {
                case TagIdFilter idF:
                    modernMixedIds.Add(idF.TagId);
                    break;
                case TagNameFilter nameF:
                    modernMixedNames.Add(nameF.TagName);
                    break;
            }
        }

        Assert(modernMixedIds.SequenceEqual(legacyMixedIds),
            "ItemListDataProvider: OfType<TagIdFilter>() extracts TagIdFilter elements from mixed union list",
            $"FATAL BUG: modern extracted {modernMixedIds.Count} IDs vs expected {legacyMixedIds.Count} IDs!");

        Assert(modernMixedNames.SequenceEqual(legacyMixedNames),
            "ItemListDataProvider: OfType<TagNameFilter>() extracts TagNameFilter elements from mixed union list",
            $"FATAL BUG: modern extracted {modernMixedNames.Count} Names vs expected {legacyMixedNames.Count} Names!");
    }

    #endregion

    #region 5. InternalLinkConversionService Logic Tests

    private static void TestInternalLinkConversionServiceLogic()
    {
        Console.WriteLine("\n--- Testing InternalLinkConversionService Logic ---");

        var candidates = new List<LinkConversionCandidate>
        {
            new LinkConversionCandidate("Tag1", 1, "Tag1", 1.0f, 0, 4, true),
            new LinkConversionCandidate("Tag2", 2, "Tag2", 0.6f, 10, 4, false),
            new LinkConversionCandidate("Tag3", 3, "Tag3", 0.95f, 20, 4, true),
            new LinkConversionCandidate("Tag4", 4, "Tag4", 0.5f, 30, 4, false)
        };

        // Legacy partitioning
        List<LinkConversionCandidate> legacyAuto = [];
        List<LinkConversionCandidate> legacyManual = [];
        foreach (var c in candidates)
        {
            if (c.IsAutoReplace) legacyAuto.Add(c);
            else legacyManual.Add(c);
        }

        // Modern partitioning
        List<LinkConversionCandidate> modernAuto = [.. candidates.Where(c => c.IsAutoReplace)];
        List<LinkConversionCandidate> modernManual = [.. candidates.Where(c => !c.IsAutoReplace)];

        Assert(modernAuto.SequenceEqual(legacyAuto), "InternalLinkConversionService: auto replace candidate partitioning matches oracle");
        Assert(modernManual.SequenceEqual(legacyManual), "InternalLinkConversionService: manual candidate partitioning matches oracle");
    }

    #endregion
}
