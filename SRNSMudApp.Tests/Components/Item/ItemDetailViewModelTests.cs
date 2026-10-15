using System.Security.Claims;

using Moq;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Components.Item;

/// <summary>
///     ItemDetailViewModel の単体テスト。
///     bUnit を一切使わず、高速かつ決定論的に状態変化・サービス呼び出しを検証する。
/// </summary>
public sealed class ItemDetailViewModelTests
{
    private const string UserId = "user-test-id";
    private readonly Mock<IItemDetailDataProvider> _detailDataMock = new();
    private readonly Mock<ITaggingContractService> _contractServiceMock = new();
    private readonly Mock<ITaggingService> _taggingServiceMock = new();
    private readonly Mock<IItemReplyService> _replyServiceMock = new();
    private readonly Mock<ISystemTagEnsurer> _ensurerMock = new();
    private readonly Mock<ITagWeightLedgerService> _tagWeightLedgerServiceMock = new();
    private readonly Mock<IItemReactionService> _reactionServiceMock = new();

    private readonly ItemDetailViewModel _sut;

    public ItemDetailViewModelTests()
    {
        _sut = new ItemDetailViewModel(
            _detailDataMock.Object,
            _contractServiceMock.Object,
            _taggingServiceMock.Object,
            _replyServiceMock.Object,
            _ensurerMock.Object,
            _tagWeightLedgerServiceMock.Object,
            _reactionServiceMock.Object);

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, UserId) };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        _sut.SetUserContext(new ClaimsPrincipal(identity));
    }

    [Fact]
    public async Task LoadDataAsync_WhenDataFound_SetsPageStateLoaded()
    {
        const int itemId = 10;
        var item = new SRNSMudApp.Data.Item { Id = itemId, Content = "Test item content", OwnerId = UserId };
        var pageData = new ItemDetailPageData(item, [], [], []);

        _detailDataMock.Setup(d => d.GetItemDetailAsync(itemId, UserId))
            .ReturnsAsync(pageData);

        await _sut.LoadDataAsync(itemId);

        var loaded = Assert.IsType<Loaded<ItemDetail.ItemDetailData>>(_sut.PageState.Value);
        Assert.Equal(itemId, loaded.Data.Item.Id);
    }

    [Fact]
    public async Task LoadDataAsync_WhenDataNull_SetsPageStateEmpty()
    {
        const int itemId = 999;
        _detailDataMock.Setup(d => d.GetItemDetailAsync(itemId, UserId))
            .ReturnsAsync((ItemDetailPageData?)null);

        await _sut.LoadDataAsync(itemId);

        _ = Assert.IsType<Empty>(_sut.PageState.Value);
    }

    [Fact]
    public async Task SubmitReplyAsync_WhenValid_CallsReplyService()
    {
        const int itemId = 10;
        const string replyText = "New reply comment";
        var createdReply = new SRNSMudApp.Data.Item { Id = 101, Content = replyText, ParentItemId = itemId, OwnerId = UserId };

        _replyServiceMock.Setup(r => r.AddItemReplyAsync(
            itemId,
            replyText,
            UserId,
            It.IsAny<IEnumerable<string>>(),
            false,
            null))
            .ReturnsAsync(createdReply);

        SRNSMudApp.Data.Item? result = await _sut.SubmitReplyAsync(itemId, replyText, isReplyPrivate: false);

        Assert.NotNull(result);
        Assert.Equal(101, result.Id);
        _replyServiceMock.Verify(r => r.AddItemReplyAsync(
            itemId,
            replyText,
            UserId,
            It.IsAny<IEnumerable<string>>(),
            false,
            null), Times.Once);
    }

    [Fact]
    public async Task RejectRequestAsync_WhenCalled_RemovesRequestFromLoadedState()
    {
        const int itemId = 10;
        var item = new SRNSMudApp.Data.Item { Id = itemId, OwnerId = UserId };
        var request = new TaggingRequestEntity { Id = 55, OwnerId = UserId, RequesterUserId = UserId };
        _detailDataMock.Setup(d => d.GetItemDetailAsync(itemId, UserId))
            .ReturnsAsync(new ItemDetailPageData(item, [], [], []));
        _contractServiceMock.Setup(c => c.GetRequestsByItemIdAsync(itemId))
            .ReturnsAsync([request]);

        await _sut.LoadDataAsync(itemId);

        bool success = await _sut.RejectRequestAsync(request, "Rejected by test");

        Assert.True(success);
        _taggingServiceMock.Verify(t => t.RejectRequestAsync(55, UserId, "Rejected by test"), Times.Once);
        var loaded = Assert.IsType<Loaded<ItemDetail.ItemDetailData>>(_sut.PageState.Value);
        Assert.DoesNotContain(request, loaded.Data.Requests);
    }

    [Fact]
    public async Task AddLedgerCommentAsync_WhenCalled_DelegatesToService()
    {
        const int ledgerId = 12;
        const string comment = "テストコメント";

        await _sut.AddLedgerCommentAsync(ledgerId, comment);

        _tagWeightLedgerServiceMock.Verify(s => s.AddCommentAsync(It.Is<TagWeightLedger>(l => l.Id == ledgerId), comment, UserId), Times.Once);
    }

    [Fact]
    public async Task AddLedgerTagAsync_WhenCalled_DelegatesToService()
    {
        const int ledgerId = 12;
        const int tagId = 34;

        await _sut.AddLedgerTagAsync(ledgerId, tagId);

        _tagWeightLedgerServiceMock.Verify(s => s.AddTagAsync(It.Is<TagWeightLedger>(l => l.Id == ledgerId), tagId, UserId), Times.Once);
    }

    [Fact]
    public async Task VoteLedgerReactionAsync_EnsuresCommentItemAndReactionTag_ThenTogglesReaction()
    {
        const int ledgerId = 15;
        const string reactionTagName = ReactionTagNames.Shinji;
        const int targetWeight = 1;
        var commentItem = new SRNSMudApp.Data.Item { Id = 100, OwnerId = UserId };
        var reactionTag = new SRNSMudApp.Data.Tag { Id = 200, Name = reactionTagName, OwnerId = UserId };
        var expectedResult = new ItemVoteResult(ItemVoteAction.Added, 300, 1);

        _tagWeightLedgerServiceMock.Setup(s => s.EnsureCommentItemAsync(It.Is<TagWeightLedger>(l => l.Id == ledgerId), UserId))
            .ReturnsAsync(commentItem);
        _reactionServiceMock.Setup(s => s.EnsureReactionTagAsync(UserId, reactionTagName))
            .ReturnsAsync(reactionTag);
        _reactionServiceMock.Setup(s => s.ToggleItemReactionAsync(commentItem.Id, UserId, reactionTag.Id, targetWeight, null))
            .ReturnsAsync(expectedResult);

        ItemVoteResult? result = await _sut.VoteLedgerReactionAsync(ledgerId, reactionTagName, targetWeight);

        Assert.NotNull(result);
        Assert.Equal(ItemVoteAction.Added, result.Action);
        Assert.Equal(300, result.RelationId);
        _tagWeightLedgerServiceMock.Verify(s => s.EnsureCommentItemAsync(It.Is<TagWeightLedger>(l => l.Id == ledgerId), UserId), Times.Once);
        _reactionServiceMock.Verify(s => s.EnsureReactionTagAsync(UserId, reactionTagName), Times.Once);
        _reactionServiceMock.Verify(s => s.ToggleItemReactionAsync(commentItem.Id, UserId, reactionTag.Id, targetWeight, null), Times.Once);
    }

    [Fact]
    public async Task UpdateLedgerReactionCommentAsync_WhenCalled_DelegatesToReactionService()
    {
        const int relationId = 300;
        const string comment = "リアクション時のコメント";

        _reactionServiceMock.Setup(s => s.UpdateTagRelationCommentAsync(relationId, UserId, comment))
            .ReturnsAsync(true);

        bool result = await _sut.UpdateLedgerReactionCommentAsync(relationId, comment);

        Assert.True(result);
        _reactionServiceMock.Verify(s => s.UpdateTagRelationCommentAsync(relationId, UserId, comment), Times.Once);
    }
}