namespace SRNSMudApp.Tests.Components.UI;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Moq;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

using Xunit;

public class TaggingRequestThreadViewModelTests
{
    private readonly Mock<IItemReplyService> _replyServiceMock = new();
    private readonly Mock<IHomeDataProvider> _homeDataMock = new();
    private readonly Mock<ITaggingRequestActions> _requestActionsMock = new();

    private TaggingRequestThreadViewModel CreateViewModel()
    {
        return new TaggingRequestThreadViewModel(
            _replyServiceMock.Object,
            _homeDataMock.Object,
            _requestActionsMock.Object);
    }

    [Fact]
    public async Task InitializeAsync_SortsRepliesByDateAndLoadsTags()
    {
        var reply1 = new Item { Id = 1, OwnerId = "u1", Content = "First", CreatedDate = DateTime.UtcNow.AddMinutes(-10) };
        var reply2 = new Item { Id = 2, OwnerId = "u2", Content = "Second", CreatedDate = DateTime.UtcNow.AddMinutes(-5) };

        var request = new TaggingRequestEntity
        {
            Id = 10,
            OwnerId = "u1",
            Replies = [reply2, reply1] // 逆順で渡す
        };

        var tags = new List<Tag> { new() { Id = 1, Name = "Tag1", OwnerId = "u1" } };
        var relations = new List<TagRelationToTag>();
        _homeDataMock.Setup(h => h.GetTagsAndRelationsAsync())
            .ReturnsAsync((tags, relations));

        var vm = CreateViewModel();

        // Act
        await vm.InitializeAsync(request, "u1");

        // Assert
        Assert.Same(request, vm.TaggingRequest);
        Assert.Equal("u1", vm.CurrentUserId);
        Assert.Equal(2, vm.Replies.Count);
        Assert.Equal(1, vm.Replies[0].Id); // 日付昇順でソートされている
        Assert.Equal(2, vm.Replies[1].Id);
        Assert.Same(tags, vm.AllTags);
        Assert.Same(relations, vm.AllTagRelationsToTags);
    }

    [Fact]
    public void CanSubmitReply_ValidatesConditions()
    {
        var vm = CreateViewModel();

        // 初期状態（メッセージ空、ユーザーなし）
        Assert.False(vm.CanSubmitReply);

        vm.NewReplyMessage = "Hello";
        Assert.False(vm.CanSubmitReply); // ユーザーなし

        // リフレクションや初期化でセット
        var request = new TaggingRequestEntity { Id = 1, OwnerId = "u1" };
        _ = vm.InitializeAsync(request, "u1");

        Assert.True(vm.CanSubmitReply);

        vm.NewReplyMessage = "   ";
        Assert.False(vm.CanSubmitReply);
    }

    [Fact]
    public async Task SubmitReplyAsync_WhenValid_AddsReplyAndClearsMessage()
    {
        var request = new TaggingRequestEntity { Id = 10, OwnerId = "u1", Replies = [] };
        var vm = CreateViewModel();
        _homeDataMock.Setup(h => h.GetTagsAndRelationsAsync())
            .ReturnsAsync(([], []));

        await vm.InitializeAsync(request, "u1");
        vm.NewReplyMessage = "Great point!";

        var createdReply = new Item { Id = 99, OwnerId = "u1", Content = "Great point!" };
        _replyServiceMock.Setup(s => s.AddReplyToRequestAsync(10, "u1", "Great point!"))
            .ReturnsAsync(createdReply);

        // Act
        var result = await vm.SubmitReplyAsync();

        // Assert
        Assert.Same(createdReply, result);
        Assert.Contains(createdReply, vm.Replies);
        Assert.Contains(createdReply, request.Replies);
        Assert.Equal(string.Empty, vm.NewReplyMessage);
        Assert.False(vm.IsSubmitting);
    }

    [Fact]
    public async Task SubmitReplyAsync_WhenInvalid_ReturnsNull()
    {
        var vm = CreateViewModel();
        vm.NewReplyMessage = "";

        // Act
        var result = await vm.SubmitReplyAsync();

        // Assert
        Assert.Null(result);
        _replyServiceMock.Verify(s => s.AddReplyToRequestAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ApproveRequestAsync_DelegatesToRequestActions()
    {
        var request = new TaggingRequestEntity { Id = 5, OwnerId = "u1" };
        var vm = CreateViewModel();
        _homeDataMock.Setup(h => h.GetTagsAndRelationsAsync())
            .ReturnsAsync(([], []));

        await vm.InitializeAsync(request, "u1");

        _requestActionsMock.Setup(a => a.ApproveAsync(5, "u1"))
            .ReturnsAsync(true);

        // Act
        var result = await vm.ApproveRequestAsync();

        // Assert
        Assert.True(result);
        _requestActionsMock.Verify(a => a.ApproveAsync(5, "u1"), Times.Once);
    }

    [Fact]
    public async Task RejectRequestAsync_DelegatesToRequestActions()
    {
        var request = new TaggingRequestEntity { Id = 5, OwnerId = "u1" };
        var vm = CreateViewModel();
        _homeDataMock.Setup(h => h.GetTagsAndRelationsAsync())
            .ReturnsAsync(([], []));

        await vm.InitializeAsync(request, "u1");

        _requestActionsMock.Setup(a => a.RejectViaDialogAsync(5, "u1"))
            .ReturnsAsync(true);

        // Act
        var result = await vm.RejectRequestAsync();

        // Assert
        Assert.True(result);
        _requestActionsMock.Verify(a => a.RejectViaDialogAsync(5, "u1"), Times.Once);
    }

    [Fact]
    public void CanApprove_DelegatesToRequestActions()
    {
        var request = new TaggingRequestEntity { Id = 5, OwnerId = "u1" };
        var vm = CreateViewModel();
        _requestActionsMock.Setup(a => a.CanApprove(request, "u1"))
            .Returns(true);

        _ = vm.InitializeAsync(request, "u1");

        // Act & Assert
        Assert.True(vm.CanApprove);
        _requestActionsMock.Verify(a => a.CanApprove(request, "u1"), Times.Once);
    }

    [Fact]
    public async Task SubmitReplyAsync_WhenDuplicate_ReturnsReplyWithoutAddingToRepliesOrClearingInput()
    {
        var request = new TaggingRequestEntity { Id = 10, OwnerId = "u1", Replies = [] };
        var vm = CreateViewModel();
        _homeDataMock.Setup(h => h.GetTagsAndRelationsAsync())
            .ReturnsAsync(([], []));

        await vm.InitializeAsync(request, "u1");
        vm.NewReplyMessage = "Duplicate message";

        var duplicateReply = new Item { Id = 99, OwnerId = "u1", Content = "Duplicate message", IsDuplicate = true };
        _replyServiceMock.Setup(s => s.AddReplyToRequestAsync(10, "u1", "Duplicate message"))
            .ReturnsAsync(duplicateReply);

        // Act
        var result = await vm.SubmitReplyAsync();

        // Assert
        Assert.Same(duplicateReply, result);
        Assert.Empty(vm.Replies);
        Assert.Empty(request.Replies);
        Assert.Equal("Duplicate message", vm.NewReplyMessage);
        Assert.False(vm.IsSubmitting);
    }

    [Fact]
    public async Task ApproveRequestAsync_WhenAlreadySubmitting_ReturnsFalseImmediately()
    {
        var request = new TaggingRequestEntity { Id = 5, OwnerId = "u1" };
        var vm = CreateViewModel();
        _homeDataMock.Setup(h => h.GetTagsAndRelationsAsync())
            .ReturnsAsync(([], []));

        await vm.InitializeAsync(request, "u1");

        var tcs = new TaskCompletionSource<bool>();
        _requestActionsMock.Setup(a => a.ApproveAsync(5, "u1"))
            .Returns(tcs.Task);

        // Act: 最初の承認リクエストを実行中にする
        var firstTask = vm.ApproveRequestAsync();
        Assert.True(vm.IsSubmitting);

        // 実行中の二重送信呼び出し
        var secondResult = await vm.ApproveRequestAsync();
        Assert.False(secondResult);

        // 最初のタスクを完了
        tcs.SetResult(true);
        var firstResult = await firstTask;
        Assert.True(firstResult);
        Assert.False(vm.IsSubmitting);

        _requestActionsMock.Verify(a => a.ApproveAsync(5, "u1"), Times.Once);
    }

    [Fact]
    public async Task RejectRequestAsync_WhenAlreadySubmitting_ReturnsFalseImmediately()
    {
        var request = new TaggingRequestEntity { Id = 5, OwnerId = "u1" };
        var vm = CreateViewModel();
        _homeDataMock.Setup(h => h.GetTagsAndRelationsAsync())
            .ReturnsAsync(([], []));

        await vm.InitializeAsync(request, "u1");

        var tcs = new TaskCompletionSource<bool>();
        _requestActionsMock.Setup(a => a.RejectViaDialogAsync(5, "u1"))
            .Returns(tcs.Task);

        // Act: 最初の却下リクエストを実行中にする
        var firstTask = vm.RejectRequestAsync();
        Assert.True(vm.IsSubmitting);

        // 実行中の二重送信呼び出し
        var secondResult = await vm.RejectRequestAsync();
        Assert.False(secondResult);

        // 最初のタスクを完了
        tcs.SetResult(true);
        var firstResult = await firstTask;
        Assert.True(firstResult);
        Assert.False(vm.IsSubmitting);

        _requestActionsMock.Verify(a => a.RejectViaDialogAsync(5, "u1"), Times.Once);
    }
}