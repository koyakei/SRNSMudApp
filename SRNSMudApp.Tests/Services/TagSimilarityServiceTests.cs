namespace SRNSMudApp.Tests.Services;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

using Xunit;

public class TagSimilarityServiceTests
{
    private readonly TagSimilarityService _sut = new();

    [Fact]
    public void FindSimilarTags_WhenQueryIsNullOrWhiteSpace_ReturnsEmptyList()
    {
        var tags = new List<Tag>
        {
            new() { Id = 1, Name = "C#", OwnerId = "user-1" },
            new() { Id = 2, Name = "Java", OwnerId = "user-1" }
        };

        var nullResult = _sut.FindSimilarTags(tags, null);
        var emptyResult = _sut.FindSimilarTags(tags, "");
        var whitespaceResult = _sut.FindSimilarTags(tags, "   ");

        Assert.Empty(nullResult);
        Assert.Empty(emptyResult);
        Assert.Empty(whitespaceResult);
    }

    [Fact]
    public void FindSimilarTags_WhenCandidateTagsIsNull_ReturnsEmptyList()
    {
        var result = _sut.FindSimilarTags(null!, "test");

        Assert.Empty(result);
    }

    [Fact]
    public void FindSimilarTags_ExcludesRootTag()
    {
        var tags = new List<Tag>
        {
            new() { Id = 1, Name = Tag.RootTagName, OwnerId = "user-1" },
            new() { Id = 2, Name = "全て", OwnerId = "user-1" }
        };

        var result = _sut.FindSimilarTags(tags, "全て");

        Assert.Single(result);
        Assert.Equal("全て", result[0].Tag.Name);
    }

    [Fact]
    public void FindSimilarTags_ReturnsExactMatchWithScoreOne()
    {
        var tags = new List<Tag>
        {
            new() { Id = 1, Name = "Blazor", OwnerId = "user-1" },
            new() { Id = 2, Name = "React", OwnerId = "user-1" }
        };

        var result = _sut.FindSimilarTags(tags, "Blazor");

        Assert.NotEmpty(result);
        Assert.Equal("Blazor", result[0].Tag.Name);
        Assert.Equal(1.0f, result[0].Similarity);
    }

    [Fact]
    public void FindSimilarTags_BoostsSubstringMatches()
    {
        var tags = new List<Tag>
        {
            new() { Id = 1, Name = "タグラベル", OwnerId = "user-1" },
            new() { Id = 2, Name = "全く別の名前", OwnerId = "user-1" }
        };

        var result = _sut.FindSimilarTags(tags, "タグ");

        Assert.NotEmpty(result);
        Assert.Equal("タグラベル", result[0].Tag.Name);
        Assert.True(result[0].Similarity >= 0.60f);
    }

    [Fact]
    public void FindSimilarTags_OrdersBySimilarityDescending_AndRespectsMaxResults()
    {
        var tags = new List<Tag>
        {
            new() { Id = 1, Name = "TypeScript", OwnerId = "user-1" },
            new() { Id = 2, Name = "Type", OwnerId = "user-1" },
            new() { Id = 3, Name = "Typing", OwnerId = "user-1" },
            new() { Id = 4, Name = "Typo", OwnerId = "user-1" },
            new() { Id = 5, Name = "Typed", OwnerId = "user-1" },
            new() { Id = 6, Name = "Types", OwnerId = "user-1" }
        };

        var result = _sut.FindSimilarTags(tags, "Type", minSimilarity: 0.50f, maxResults: 3);

        Assert.Equal(3, result.Count);
        Assert.Equal("Type", result[0].Tag.Name);
        Assert.Equal(1.0f, result[0].Similarity);
        Assert.True(result[0].Similarity >= result[1].Similarity);
        Assert.True(result[1].Similarity >= result[2].Similarity);
    }
}