#region

using Moq;

using MudBlazor;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.Item;

/// <summary>
///     <see cref="ItemEditViewModel" /> の単体テスト。
///     アイテム編集の初期化、文字数カウント、Pill HTML 生成、URL 抽出、タグ/ユーザー検索、リンクプレビュー、保存処理を検証する。
/// </summary>
public sealed class ItemEditViewModelTests
{
    private readonly Mock<IItemReplyService> _mockItemReply = new();
    private readonly Mock<IItemCardDataProvider> _mockItemCardData = new();
    private readonly Mock<ITaggingContractService> _mockTaggingContract = new();
    private readonly Mock<ISnackbar> _mockSnackbar = new();
    private readonly Mock<ITagSearchQueryService> _mockTagSearch = new();
    private readonly Mock<IUserDataProvider> _mockUserData = new();
    private readonly Mock<ILinkPreviewService> _mockLinkPreview = new();
    private readonly ItemCardActionViewModel _actionViewModel;

    public ItemEditViewModelTests()
    {
        _actionViewModel = new ItemCardActionViewModel(
            _mockItemReply.Object,
            _mockItemCardData.Object,
            _mockTaggingContract.Object,
            new Mock<IItemTagService>().Object,
            new Mock<IItemSplitService>().Object,
            new Mock<IItemQuoteService>().Object,
            _mockSnackbar.Object);
    }

    private ItemEditViewModel CreateViewModel()
    {
        return new ItemEditViewModel(
            _actionViewModel,
            _mockTagSearch.Object,
            _mockUserData.Object,
            _mockLinkPreview.Object);
    }

    [Fact]
    public void Constructor_NullActionViewModel_ThrowsArgumentNullException()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new ItemEditViewModel(null!, _mockTagSearch.Object, _mockUserData.Object, _mockLinkPreview.Object));
    }

    [Fact]
    public void Constructor_NullTagSearchQueryService_ThrowsArgumentNullException()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new ItemEditViewModel(_actionViewModel, null!, _mockUserData.Object, _mockLinkPreview.Object));
    }

    [Fact]
    public void Constructor_NullUserDataProvider_ThrowsArgumentNullException()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new ItemEditViewModel(_actionViewModel, _mockTagSearch.Object, null!, _mockLinkPreview.Object));
    }

    [Fact]
    public void Constructor_NullLinkPreviewService_ThrowsArgumentNullException()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new ItemEditViewModel(_actionViewModel, _mockTagSearch.Object, _mockUserData.Object, null!));
    }

    [Fact]
    public async Task GetPreviewAsync_DelegatesToLinkPreviewService()
    {
        // Arrange
        const string testUrl = "https://example.com";
        var expectedData = new LinkPreviewData { Url = testUrl, Title = "Example" };
        _mockLinkPreview.Setup(s => s.GetPreviewAsync(testUrl)).ReturnsAsync(expectedData);

        var vm = CreateViewModel();

        // Act
        var actual = await vm.GetPreviewAsync(testUrl);

        // Assert
        Assert.Same(expectedData, actual);
        _mockLinkPreview.Verify(s => s.GetPreviewAsync(testUrl), Times.Once);
    }

    [Fact]
    public void InitialState_PropertiesDefaultCorrectly()
    {
        var vm = CreateViewModel();

        Assert.Null(vm.Item);
        Assert.Equal(string.Empty, vm.EditContent);
        Assert.Equal(0, vm.CharacterCount);
        Assert.False(vm.IsOverCharacterLimit);
        Assert.Empty(vm.ExternalUrls);
    }

    [Fact]
    public void Initialize_NullItem_ThrowsArgumentNullException()
    {
        var vm = CreateViewModel();

        _ = Assert.Throws<ArgumentNullException>(() => vm.Initialize(null!));
    }

    [Fact]
    public void Initialize_ValidItem_SetsItemAndContent()
    {
        var vm = CreateViewModel();
        var item = new SRNSMudApp.Data.Item { Id = 1, Content = "Initial Content", OwnerId = "user-1" };

        vm.Initialize(item);

        Assert.Same(item, vm.Item);
        Assert.Equal("Initial Content", vm.EditContent);
        Assert.Equal(15, vm.CharacterCount);
        Assert.False(vm.IsOverCharacterLimit);
    }

    [Theory]
    [InlineData(1000, false)]
    [InlineData(1001, true)]
    [InlineData(0, false)]
    public void IsOverCharacterLimit_EvaluatesBasedOn1000Chars(int length, bool expected)
    {
        var vm = CreateViewModel();
        vm.EditContent = new string('a', length);

        Assert.Equal(length, vm.CharacterCount);
        Assert.Equal(expected, vm.IsOverCharacterLimit);
    }

    [Fact]
    public async Task SaveAsync_WhenItemNull_ReturnsFailure()
    {
        var vm = CreateViewModel();

        var (success, errorMessage) = await vm.SaveAsync();

        Assert.False(success);
        Assert.Equal("対象アイテムが設定されていません。", errorMessage);
    }

    [Fact]
    public async Task SaveAsync_WhenValidItem_CallsActionViewModel()
    {
        var vm = CreateViewModel();
        var item = new SRNSMudApp.Data.Item { Id = 42, Content = "Old", OwnerId = "user-1" };
        vm.Initialize(item);
        vm.EditContent = "New content";

        _mockItemCardData.Setup(d => d.UpdateItemContentAsync(42, "New content"))
            .ReturnsAsync(true);

        var (success, errorMessage) = await vm.SaveAsync();

        Assert.True(success);
        Assert.Null(errorMessage);
        _mockItemCardData.Verify(d => d.UpdateItemContentAsync(42, "New content"), Times.Once);
    }

    [Fact]
    public async Task SearchTagsAsync_FormatsTagsAsMentionItems()
    {
        var vm = CreateViewModel();
        var tags = new List<SRNSMudApp.Data.Tag>
        {
            new() { Id = 1, Name = "C#", OwnerId = "user-1" },
            new() { Id = 2, Name = "Blazor", OwnerId = "user-1" }
        };
        _mockTagSearch.Setup(s => s.SearchTagsWithFallbackAsync("query", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tags);

        var results = (await vm.SearchTagsAsync("query")).ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal("#C#", results[0].Name);
        Assert.Equal("/TagDetail/1", results[0].Replacement);
        Assert.Equal("#Blazor", results[1].Name);
        Assert.Equal("/TagDetail/2", results[1].Replacement);
    }

    [Fact]
    public async Task SearchUsersAsync_FormatsUsersAsMentionItems()
    {
        var vm = CreateViewModel();
        var users = new List<ApplicationUser>
        {
            new() { Id = "u1", UserName = "Alice" },
            new() { Id = "u2", UserName = "Bob" }
        };
        _mockUserData.Setup(u => u.SearchUsersAsync("query", It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);

        var results = (await vm.SearchUsersAsync("query")).ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal("@Alice", results[0].Name);
        Assert.Equal("/User/UserDetail/u1", results[0].Replacement);
        Assert.Equal("@Bob", results[1].Name);
        Assert.Equal("/User/UserDetail/u2", results[1].Replacement);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Hello World", "Hello&nbsp;World")]
    [InlineData("Line 1\nLine 2", "Line&nbsp;1<br>Line&nbsp;2")]
    public void ParsePillsToHtml_NormalText_EscapesAndReplacesSpaces(string? input, string expected)
    {
        var actual = ItemEditViewModel.ParsePillsToHtml(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParsePillsToHtml_InternalLinks_ConvertedToPreviewPills()
    {
        var input = "Check /TagDetail/1 and /User/UserDetail/abc for details";
        var actual = ItemEditViewModel.ParsePillsToHtml(input);

        Assert.Contains("internal-link-preview-pill", actual, StringComparison.Ordinal);
        Assert.Contains("#タグ", actual, StringComparison.Ordinal);
        Assert.Contains("@ユーザー", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void GetExternalUrls_ExcludesInternalLinksAndReturnsExternal()
    {
        var text = "Visit https://example.com and check /TagDetail/1 or http://test.org";
        var urls = ItemEditViewModel.GetExternalUrls(text);

        Assert.Equal(2, urls.Count);
        Assert.Contains("https://example.com", urls);
        Assert.Contains("http://test.org", urls);
        Assert.DoesNotContain("/TagDetail/1", urls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetExternalUrls_EmptyOrWhitespace_ReturnsEmptyList(string? text)
    {
        var urls = ItemEditViewModel.GetExternalUrls(text);
        Assert.Empty(urls);
    }

    [Fact]
    public async Task SaveAsync_WhenDuplicateSubmission_ReturnsFailureWithDuplicateMessage()
    {
        var guard = new global::SRNSMudApp.Client.Services.DoubleSubmissionGuard();
        var vm = new ItemEditViewModel(
            _actionViewModel,
            _mockTagSearch.Object,
            _mockUserData.Object,
            _mockLinkPreview.Object,
            guard);

        var item = new SRNSMudApp.Data.Item { Id = 10, Content = "Content", OwnerId = "user-1" };
        vm.Initialize(item);
        vm.EditContent = "Edited";

        _mockItemCardData.Setup(d => d.UpdateItemContentAsync(10, "Edited"))
            .ReturnsAsync(true);

        var firstResult = await vm.SaveAsync();
        Assert.True(firstResult.Success);

        // 5秒以内の直近同一操作は重複防止される
        var secondResult = await vm.SaveAsync();
        Assert.False(secondResult.Success);
        Assert.Equal(global::SRNSMudApp.Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, secondResult.ErrorMessage);
    }
}