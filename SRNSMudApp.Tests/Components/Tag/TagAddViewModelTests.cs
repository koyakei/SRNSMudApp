using Moq;

using MudBlazor;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

using TagEntity = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Tests.Components.Tag;

public sealed class TagAddViewModelTests
{
    private readonly Mock<ITagSearchQueryService> _tagSearchQueryServiceMock = new();
    private readonly Mock<ITagCommandService> _tagCommandServiceMock = new();
    private readonly Mock<ITagLockService> _tagLockServiceMock = new();
    private readonly Mock<ITagSimilarityService> _tagSimilarityServiceMock = new();
    private readonly Mock<ISnackbar> _snackbarMock = new();
    private readonly TagAddViewModel _sut;

    public TagAddViewModelTests()
    {
        _sut = new TagAddViewModel(
            _tagSearchQueryServiceMock.Object,
            _tagCommandServiceMock.Object,
            _tagLockServiceMock.Object,
            _tagSimilarityServiceMock.Object,
            _snackbarMock.Object);
    }

    [Fact]
    public async Task InitializeAsync_LoadsAllTags()
    {
        var tags = new List<TagEntity>
        {
            new() { Id = 1, Name = "Tag1", OwnerId = "user-1" },
            new() { Id = 2, Name = "Tag2", OwnerId = "user-1" }
        };

        _tagSearchQueryServiceMock.Setup(s => s.GetAllTagsAsync()).ReturnsAsync(tags);

        await _sut.InitializeAsync();

        Assert.Equal(2, _sut.AllTags.Count);
        Assert.Equal("Tag1", _sut.AllTags[0].Name);
    }

    [Fact]
    public async Task SearchAsync_WhenSearchTextEmpty_ClearsResultsAndSelection()
    {
        _sut.SearchText = "  ";
        _sut.SelectedTag = new TagEntity { Id = 1, Name = "Old", OwnerId = "user-1" };

        await _sut.SearchAsync();

        Assert.Null(_sut.SearchResults);
        Assert.Null(_sut.SelectedTag);
    }

    [Fact]
    public async Task SearchAsync_WhenMultipleMatches_PopulatesResultsWithoutAutoSelect()
    {
        _sut.SearchText = "test";
        var results = new List<TagEntity>
        {
            new() { Id = 1, Name = "test1", OwnerId = "user-1" },
            new() { Id = 2, Name = "test2", OwnerId = "user-1" }
        };

        _tagSearchQueryServiceMock.Setup(s => s.SearchTagsAsync("test")).ReturnsAsync(results);

        await _sut.SearchAsync();

        Assert.NotNull(_sut.SearchResults);
        Assert.Equal(2, _sut.SearchResults.Count);
        Assert.Null(_sut.SelectedTag);
    }

    [Fact]
    public async Task SearchAsync_WhenSingleMatch_AutoSelectsTag()
    {
        _sut.SearchText = "single";
        var results = new List<TagEntity>
        {
            new() { Id = 42, Name = "single", OwnerId = "user-1" }
        };

        _tagSearchQueryServiceMock.Setup(s => s.SearchTagsAsync("single")).ReturnsAsync(results);

        await _sut.SearchAsync();

        Assert.NotNull(_sut.SearchResults);
        Assert.Single(_sut.SearchResults);
        Assert.NotNull(_sut.SelectedTag);
        Assert.Equal(42, _sut.SelectedTag.Id);
    }

    [Fact]
    public async Task SearchParentTags_FiltersByNameAndContent_CaseInsensitive()
    {
        var tags = new List<TagEntity>
        {
            new() { Id = 1, Name = "DotNet", Content = "Modern C#", OwnerId = "user-1" },
            new() { Id = 2, Name = "Rust", Content = "Systems programming", OwnerId = "user-1" },
            new() { Id = 3, Name = "Blazor", Content = "dotnet web UI", OwnerId = "user-1" }
        };

        _tagSearchQueryServiceMock.Setup(s => s.GetAllTagsAsync()).ReturnsAsync(tags);
        await _sut.InitializeAsync();

        var matches = _sut.SearchParentTags("dotnet").ToList();

        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, t => t.Name == "DotNet");
        Assert.Contains(matches, t => t.Name == "Blazor");
    }

    [Fact]
    public void SearchParentTags_WhenQueryEmpty_ReturnsUpTo15Tags()
    {
        var tags = Enumerable.Range(1, 20)
            .Select(i => new TagEntity { Id = i, Name = $"Tag{i}", Content = "", OwnerId = "user-1" })
            .ToList();

        typeof(TagAddViewModel)
            .GetProperty(nameof(TagAddViewModel.AllTags))!
            .SetValue(_sut, tags);

        var matches = _sut.SearchParentTags(null).ToList();

        Assert.Equal(15, matches.Count);
    }

    [Fact]
    public async Task CreateChildTagAsync_WhenTagNameEmpty_ShowsWarningAndReturnsNull()
    {
        _sut.NewTagName = "  ";
        _sut.ParentTag = new TagEntity { Id = 1, Name = "Parent", OwnerId = "user-1" };

        var result = await _sut.CreateChildTagAsync("user-1", isAdmin: false);

        Assert.Null(result);
        _snackbarMock.Verify(s => s.Add("タグ名は必須です。", Severity.Warning, null, null), Times.Once);
    }

    [Fact]
    public async Task CreateChildTagAsync_WhenParentTagNull_ShowsWarningAndReturnsNull()
    {
        _sut.NewTagName = "ChildTag";
        _sut.ParentTag = null;

        var result = await _sut.CreateChildTagAsync("user-1", isAdmin: false);

        Assert.Null(result);
        _snackbarMock.Verify(s => s.Add("親タグを選択してください。", Severity.Warning, null, null), Times.Once);
    }

    [Fact]
    public async Task CreateChildTagAsync_WhenUserEmpty_ShowsErrorAndReturnsNull()
    {
        _sut.NewTagName = "ChildTag";
        _sut.ParentTag = new TagEntity { Id = 1, Name = "Parent", OwnerId = "user-1" };

        var result = await _sut.CreateChildTagAsync("", isAdmin: false);

        Assert.Null(result);
        _snackbarMock.Verify(s => s.Add("ユーザーが見つかりません。", Severity.Error, null, null), Times.Once);
    }

    [Fact]
    public async Task CreateChildTagAsync_WhenNonAdminAndParentLocked_ShowsWarningAndReturnsNull()
    {
        _sut.NewTagName = "ChildTag";
        _sut.ParentTag = new TagEntity { Id = 10, Name = "LockedParent", OwnerId = "user-1" };

        _tagLockServiceMock
            .Setup(l => l.IsChildCreationRestrictedAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.CreateChildTagAsync("user-1", isAdmin: false);

        Assert.Null(result);
        _snackbarMock.Verify(s => s.Add(
            "選択された親タグ配下（または兄弟）はロックされているため子タグを作成できません。",
            Severity.Warning, null, null), Times.Once);
    }

    [Fact]
    public async Task CreateChildTagAsync_WhenAdminAndParentLocked_AllowsCreation()
    {
        _sut.NewTagName = "ChildTag";
        _sut.ParentTag = new TagEntity { Id = 10, Name = "LockedParent", OwnerId = "user-1" };

        _tagLockServiceMock
            .Setup(l => l.IsChildCreationRestrictedAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _tagSearchQueryServiceMock
            .Setup(s => s.FindTagByNameAsync("ChildTag"))
            .ReturnsAsync((TagEntity?)null);

        var result = await _sut.CreateChildTagAsync("admin-user", isAdmin: true);

        Assert.NotNull(result);
        Assert.Equal("ChildTag", result.Name);
        _tagCommandServiceMock.Verify(c => c.CreateTagAsync(It.Is<TagEntity>(t => t.Name == "ChildTag"), true), Times.Once);
    }

    [Fact]
    public async Task CreateChildTagAsync_WhenTagWithSameNameExists_ShowsErrorAndReturnsNull()
    {
        _sut.NewTagName = "ExistingTag";
        _sut.ParentTag = new TagEntity { Id = 1, Name = "Parent", OwnerId = "user-1" };

        _tagLockServiceMock
            .Setup(l => l.IsChildCreationRestrictedAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _tagSearchQueryServiceMock
            .Setup(s => s.FindTagByNameAsync("ExistingTag"))
            .ReturnsAsync(new TagEntity { Id = 99, Name = "ExistingTag", OwnerId = "user-2" });

        var result = await _sut.CreateChildTagAsync("user-1", isAdmin: false);

        Assert.Null(result);
        _snackbarMock.Verify(s => s.Add("同じ名前のタグが既に存在します。", Severity.Error, null, null), Times.Once);
    }

    [Fact]
    public async Task CreateChildTagAsync_WhenValid_CreatesTagAndReturnsIt()
    {
        _sut.NewTagName = "NewChild";
        _sut.NewTagContent = "Description";
        _sut.ParentTag = new TagEntity { Id = 5, Name = "Parent", OwnerId = "user-1" };

        _tagLockServiceMock
            .Setup(l => l.IsChildCreationRestrictedAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _tagSearchQueryServiceMock
            .Setup(s => s.FindTagByNameAsync("NewChild"))
            .ReturnsAsync((TagEntity?)null);

        var result = await _sut.CreateChildTagAsync("user-1", isAdmin: false);

        Assert.NotNull(result);
        Assert.Equal("NewChild", result.Name);
        Assert.Equal("Description", result.Content);
        Assert.Equal(5, result.ParentTagId);
        Assert.Equal("user-1", result.OwnerId);

        _tagCommandServiceMock.Verify(c => c.CreateTagAsync(
            It.Is<TagEntity>(t => t.Name == "NewChild" && t.ParentTagId == 5 && t.OwnerId == "user-1"), false), Times.Once);
    }

    [Fact]
    public async Task CreateChildTagAsync_WhenExceptionThrown_CatchesAndShowsError()
    {
        _sut.NewTagName = "NewChild";
        _sut.ParentTag = new TagEntity { Id = 5, Name = "Parent", OwnerId = "user-1" };

        _tagLockServiceMock
            .Setup(l => l.IsChildCreationRestrictedAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _tagSearchQueryServiceMock
            .Setup(s => s.FindTagByNameAsync("NewChild"))
            .ReturnsAsync((TagEntity?)null);

        _tagCommandServiceMock
            .Setup(c => c.CreateTagAsync(It.IsAny<TagEntity>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("DB failed"));

        var result = await _sut.CreateChildTagAsync("user-1", isAdmin: false);

        Assert.Null(result);
        _snackbarMock.Verify(s => s.Add(It.Is<string>(m => m.Contains("DB failed")), Severity.Error, null, null), Times.Once);
    }

    [Fact]
    public void SettingNewTagName_UpdatesSimilarTags()
    {
        var existingTag = new TagEntity { Id = 1, Name = "Existing", OwnerId = "user-1" };
        var candidate = new SRNSMudApp.Models.SimilarTagCandidate(existingTag, 0.8f);

        _tagSimilarityServiceMock
            .Setup(s => s.FindSimilarTags(It.IsAny<IEnumerable<TagEntity>>(), "Exist", 0.50f, 5))
            .Returns([candidate]);

        _sut.NewTagName = "Exist";

        Assert.Single(_sut.SimilarTags);
        Assert.Equal(candidate, _sut.SimilarTags[0]);
    }

    [Fact]
    public void SelectSimilarTag_SetsSelectedTagAndSearchState()
    {
        var tag = new TagEntity { Id = 10, Name = "TargetTag", OwnerId = "user-1" };

        _sut.SelectSimilarTag(tag);

        Assert.Equal(tag, _sut.SelectedTag);
        Assert.Equal("TargetTag", _sut.SearchText);
        Assert.NotNull(_sut.SearchResults);
        Assert.Single(_sut.SearchResults);
        Assert.Equal(tag, _sut.SearchResults[0]);
    }
}