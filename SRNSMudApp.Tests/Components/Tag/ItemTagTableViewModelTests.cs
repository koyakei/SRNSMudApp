using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;

namespace SRNSMudApp.Tests.Components.Tag;

/// <summary>
///     ItemTagTableViewModel の単体テスト。
///     TagSearchQuery を用いた 2 段階サジェストおよびテーブルフィルタ判定を検証する。
/// </summary>
public class ItemTagTableViewModelTests
{
    private static SRNSMudApp.Data.Tag CreateTag(int id = 1, string name = "CSharp", string ownerName = "alice") =>
        new()
        {
            Id = id,
            Name = name,
            OwnerId = $"user-{ownerName}",
            Owner = new ApplicationUser { Id = $"user-{ownerName}", UserName = ownerName },
            Content = $"Content for {name}"
        };

    private static TagRelation CreateTagRelation(int id, SRNSMudApp.Data.Tag tag, string ownerName = "alice") =>
        new()
        {
            Id = id,
            TagId = tag.Id,
            Tag = tag,
            OwnerId = $"user-{ownerName}",
            Owner = new ApplicationUser { Id = $"user-{ownerName}", UserName = ownerName },
            Weight = 1
        };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FilterFunc_WithEmptySearch_MatchesAll(string? search)
    {
        var relation = CreateTagRelation(1, CreateTag(1, "CSharp", "alice"));

        Assert.True(ItemTagTableViewModel.FilterFunc(relation, search));
    }

    [Fact]
    public void FilterFunc_WithTagNameSearch_MatchesTagOrUser()
    {
        var tag = CreateTag(1, "CSharp", "alice");
        var relation = CreateTagRelation(1, tag, "alice");

        Assert.True(ItemTagTableViewModel.FilterFunc(relation, "csharp"));
        Assert.True(ItemTagTableViewModel.FilterFunc(relation, "ali"));
        Assert.False(ItemTagTableViewModel.FilterFunc(relation, "python"));
    }

    [Fact]
    public void FilterFunc_WithIncompleteSearch_MatchesTag()
    {
        var tag = CreateTag(1, "CSharp", "alice");
        var relation = CreateTagRelation(1, tag, "alice");

        Assert.True(ItemTagTableViewModel.FilterFunc(relation, "CSharp @"));
        Assert.False(ItemTagTableViewModel.FilterFunc(relation, "Python @"));
    }

    [Fact]
    public void FilterFunc_WithTagWithUserSearch_MatchesBothTagAndUser()
    {
        var tag = CreateTag(1, "CSharp", "alice");
        var relation = CreateTagRelation(1, tag, "alice");

        Assert.True(ItemTagTableViewModel.FilterFunc(relation, "CSharp @alice"));
        Assert.True(ItemTagTableViewModel.FilterFunc(relation, "CSharp @ali"));
        Assert.False(ItemTagTableViewModel.FilterFunc(relation, "CSharp @bob"));
        Assert.False(ItemTagTableViewModel.FilterFunc(relation, "Python @alice"));
    }

    [Fact]
    public void GetSearchSuggestions_WithEmptyValue_ReturnsDistinctTagNamesWithAt()
    {
        var relations = new List<TagRelation>
        {
            CreateTagRelation(1, CreateTag(1, "CSharp", "alice")),
            CreateTagRelation(2, CreateTag(1, "CSharp", "bob")),
            CreateTagRelation(3, CreateTag(2, "Blazor", "charlie"))
        };

        var suggestions = ItemTagTableViewModel.GetSearchSuggestions(relations, "");

        Assert.Equal(["CSharp @", "Blazor @"], suggestions);
    }

    [Fact]
    public void GetSearchSuggestions_WithTagName_ReturnsFilteredTagNamesWithAt()
    {
        var relations = new List<TagRelation>
        {
            CreateTagRelation(1, CreateTag(1, "CSharp", "alice")),
            CreateTagRelation(2, CreateTag(2, "Blazor", "charlie")),
            CreateTagRelation(3, CreateTag(3, "Cloud", "dave"))
        };

        var suggestions = ItemTagTableViewModel.GetSearchSuggestions(relations, "c");

        Assert.Equal(["CSharp @", "Cloud @"], suggestions);
    }

    [Fact]
    public void GetSearchSuggestions_WithIncompleteSearch_ReturnsUserSuggestions()
    {
        var tagCSharp = CreateTag(1, "CSharp", "alice");
        var tagRelation1 = CreateTagRelation(1, tagCSharp, "alice");
        var tagRelation2 = new TagRelation
        {
            Id = 2,
            TagId = 1,
            Tag = tagCSharp,
            OwnerId = "user-bob",
            Owner = new ApplicationUser { Id = "user-bob", UserName = "bob" }
        };

        var relations = new List<TagRelation> { tagRelation1, tagRelation2 };

        var suggestions = ItemTagTableViewModel.GetSearchSuggestions(relations, "CSharp @");

        Assert.Contains("CSharp @alice", suggestions);
        Assert.Contains("CSharp @bob", suggestions);
    }

    [Fact]
    public void GetSearchSuggestions_WithIncompleteSearch_WhenNoUser_ReturnsTagNameWithAt()
    {
        var tag = new SRNSMudApp.Data.Tag { Id = 1, Name = "CSharp", OwnerId = "user-1" };
        var relation = new TagRelation { Id = 1, TagId = 1, Tag = tag, OwnerId = "user-1" };

        var suggestions = ItemTagTableViewModel.GetSearchSuggestions([relation], "CSharp @");

        Assert.Equal(["CSharp @"], suggestions);
    }

    [Fact]
    public void GetSearchSuggestions_WithTagWithUserSearch_FiltersUsers()
    {
        var tagCSharp = CreateTag(1, "CSharp", "alice");
        var tagRelation1 = CreateTagRelation(1, tagCSharp, "alice");
        var tagRelation2 = new TagRelation
        {
            Id = 2,
            TagId = 1,
            Tag = tagCSharp,
            OwnerId = "user-bob",
            Owner = new ApplicationUser { Id = "user-bob", UserName = "bob" }
        };

        var relations = new List<TagRelation> { tagRelation1, tagRelation2 };

        var suggestions = ItemTagTableViewModel.GetSearchSuggestions(relations, "CSharp @al");

        Assert.Equal(["CSharp @alice"], suggestions);
    }
}