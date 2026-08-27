using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;

namespace SRNSMudApp.Tests.Components.Tag;

using Item = SRNSMudApp.Data.Item;
// 親名前空間の下にある namespace Tag / Item より先に Data 側の型を解決させるため、
// エイリアスを名前空間の内側に置く
using Tag = SRNSMudApp.Data.Tag;

/// <summary>
///     ItemTagRequestChipViewModel の単体テスト。
///     可視性・種別・リプライ件数の分岐を bUnit なしで網羅的に検証する。
/// </summary>
public class ItemTagRequestChipViewModelTests
{
    private static TaggingRequestEntity CreateRequest(
        TradeStatus status = TradeStatus.Proposed,
        TaggingRequestType type = TaggingRequestType.Add,
        List<Item>? replies = null) =>
        new()
        {
            Id = 1,
            OwnerId = "owner",
            RequesterUserId = "requester",
            Status = status,
            RequestType = type,
            RequestedTagId = 10,
            RequestedTag = new Tag { Id = 10, Name = "target-tag", OwnerId = "owner" },
            Replies = replies!
        };

    private static Item CreateItem(params int[] tagIds)
    {
        var item = new Item { Id = 1, Content = "item", OwnerId = "owner" };
        foreach (var tagId in tagIds)
        {
            item.TagRelations.Add(new TagRelation
            {
                Id = tagId * 100,
                ItemId = 1,
                TagId = tagId,
                OwnerId = "owner",
                Weight = 1
            });
        }

        return item;
    }

    [Fact]
    public void Compute_NonProposedStatus_ReturnsInvisible()
    {
        foreach (var status in new[] { TradeStatus.Executed, TradeStatus.Rejected, TradeStatus.Canceled })
        {
            var state = ItemTagRequestChipViewModel.Compute(CreateRequest(status: status), CreateItem());

            Assert.False(state.Visible);
        }
    }

    [Fact]
    public void Compute_AddRequestWithExistingTag_ReturnsInvisible()
    {
        // 放置された追加リクエスト: 対象タグがすでに付けられている
        var state = ItemTagRequestChipViewModel.Compute(
            CreateRequest(type: TaggingRequestType.Add), CreateItem(10));

        Assert.False(state.Visible);
    }

    [Fact]
    public void Compute_RemoveRequestWithoutTag_ReturnsInvisible()
    {
        // 放置された削除リクエスト: 対象タグが存在しない
        var state = ItemTagRequestChipViewModel.Compute(
            CreateRequest(type: TaggingRequestType.Remove), CreateItem());

        Assert.False(state.Visible);
    }

    [Fact]
    public void Compute_ValidAddRequestWithoutTag_ReturnsVisibleAndAdd()
    {
        var state = ItemTagRequestChipViewModel.Compute(
            CreateRequest(type: TaggingRequestType.Add), CreateItem());

        Assert.True(state.Visible);
        Assert.True(state.IsAdd);
    }

    [Fact]
    public void Compute_ValidRemoveRequestWithTagged_ReturnsVisibleAndNotAdd()
    {
        var state = ItemTagRequestChipViewModel.Compute(
            CreateRequest(type: TaggingRequestType.Remove), CreateItem(10));

        Assert.True(state.Visible);
        Assert.False(state.IsAdd);
    }

    [Fact]
    public void Compute_WithReplies_CountsReplies()
    {
        List<Item> replies =
        [
            new() { Id = 100, Content = "reply-1", OwnerId = "u1", ParentItemId = 1 },
            new() { Id = 101, Content = "reply-2", OwnerId = "u2", ParentItemId = 1 }
        ];

        var state = ItemTagRequestChipViewModel.Compute(
            CreateRequest(replies: replies), CreateItem());

        Assert.Equal(2, state.ReplyCount);
    }

    [Fact]
    public void Compute_WithNullReplies_ReplyCountIsZero()
    {
        var state = ItemTagRequestChipViewModel.Compute(
            CreateRequest(replies: null), CreateItem());

        Assert.Equal(0, state.ReplyCount);
    }

    [Fact]
    public void Compute_WithNullTagRelations_TreatsAsNoTag_DoesNotThrow()
    {
        // Item.TagRelations が null でも例外を投げず hasTag == false 相当として扱う
        var item = new Item { Id = 1, Content = "item", OwnerId = "owner" };
        item.TagRelations = null!;

        var exception = Record.Exception(() =>
            _ = ItemTagRequestChipViewModel.Compute(
                CreateRequest(type: TaggingRequestType.Add), item));

        Assert.Null(exception);

        var state = ItemTagRequestChipViewModel.Compute(
            CreateRequest(type: TaggingRequestType.Add), item);

        Assert.True(state.Visible);
    }
}