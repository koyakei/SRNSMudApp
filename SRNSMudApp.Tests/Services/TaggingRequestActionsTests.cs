using Microsoft.EntityFrameworkCore;

using Moq;

using MudBlazor;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Commands;
using SRNSMudApp.Services.Dialogs;

namespace SRNSMudApp.Tests.Services;

/// <summary>
///     TaggingRequestActions の単体テスト。
///     承認権限判定と承認/却下フロー（ダイアログ結果の分岐含む）を検証する。
/// </summary>
public class TaggingRequestActionsTests
{
    private const string TagOwnerId = "tag-owner";
    private const string RequesterId = "requester";

    private readonly Mock<ITaggingContractService> _contractServiceMock = new();
    private readonly Mock<ITaggingService> _taggingServiceMock = new();
    private readonly Mock<IDialogLauncher> _dialogLauncherMock = new();
    private readonly Mock<IDialogReference> _dialogReferenceMock = new();
    private readonly Mock<ISnackbar> _snackbarMock = new();
    private readonly TaggingRequestActions _actions;

    public TaggingRequestActionsTests()
    {
        _actions = new TaggingRequestActions(
            new ApproveTaggingRequestHandler(_contractServiceMock.Object),
            new RejectTaggingRequestHandler(_taggingServiceMock.Object),
            _dialogLauncherMock.Object,
            _snackbarMock.Object);
    }

    private static TaggingRequestEntity CreateRequest(
        string contractType = "Gratis", TradeStatus status = TradeStatus.Proposed) =>
        new()
        {
            Id = 1,
            OwnerId = TagOwnerId,
            RequesterUserId = RequesterId,
            TagOwnerUserId = TagOwnerId,
            ContractType = contractType,
            Status = status
        };

    // --- CanApprove ---

    [Fact]
    public void CanApprove_NonProposedStatus_ReturnsFalse()
    {
        Assert.False(_actions.CanApprove(CreateRequest(status: TradeStatus.Executed), TagOwnerId));
        Assert.False(_actions.CanApprove(CreateRequest(status: TradeStatus.Canceled), TagOwnerId));
    }

    [Fact]
    public void CanApprove_GratisContract_OnlyTagOwnerCanApprove()
    {
        Assert.True(_actions.CanApprove(CreateRequest(), TagOwnerId));
        Assert.False(_actions.CanApprove(CreateRequest(), RequesterId));
    }

    [Fact]
    public void CanApprove_TriggerContract_OnlyRequesterCanApprove()
    {
        Assert.True(_actions.CanApprove(CreateRequest(contractType: "Trigger"), RequesterId));
        Assert.False(_actions.CanApprove(CreateRequest(contractType: "Trigger"), TagOwnerId));
    }

    [Fact]
    public void CanApprove_BountyContract_AnyoneCanApprove()
    {
        Assert.True(_actions.CanApprove(CreateRequest(contractType: "Bounty"), "anyone"));
    }

    // --- ApproveAsync ---

    [Fact]
    public async Task ApproveAsync_OnSuccess_ReturnsTrueAndShowsSnackbar()
    {
        _ = _contractServiceMock
            .Setup(s => s.AcceptContractAsync(1, TagOwnerId, It.IsAny<int?>()))
            .ReturnsAsync(new Success<string>("ok"));

        var result = await _actions.ApproveAsync(1, TagOwnerId);

        Assert.True(result);
        _snackbarMock.Verify(s => s.Add("リクエストを承認しました。", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ApproveAsync_OnException_ReturnsFalseAndShowsErrorSnackbar()
    {
        _ = _contractServiceMock
            .Setup(s => s.AcceptContractAsync(1, TagOwnerId, It.IsAny<int?>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await _actions.ApproveAsync(1, TagOwnerId);

        Assert.False(result);
        _snackbarMock.Verify(s => s.Add("承認に失敗しました: boom", Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ApproveAsync_OnContractFailure_ReturnsFalseAndShowsErrorSnackbar()
    {
        _ = _contractServiceMock
            .Setup(s => s.AcceptContractAsync(1, TagOwnerId, It.IsAny<int?>()))
            .ReturnsAsync(new Failure("権利アセットが不足しています。"));

        var result = await _actions.ApproveAsync(1, TagOwnerId);

        Assert.False(result);
        _snackbarMock.Verify(s => s.Add("承認に失敗しました: 権利アセットが不足しています。", Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    // --- RejectViaDialogAsync ---

    private void SetupDialog(DialogResult result)
    {
        _ = _dialogReferenceMock.Setup(r => r.Result).Returns(Task.FromResult<DialogResult?>(result));
        _ = _dialogLauncherMock
            .Setup(l => l.ShowAsync(
                typeof(RejectRequestDialog),
                "リクエストを却下",
                It.IsAny<DialogParameters?>(),
                It.IsAny<DialogOptions?>()))
            .ReturnsAsync(_dialogReferenceMock.Object);
    }

    [Fact]
    public async Task RejectViaDialogAsync_WhenCanceled_ReturnsFalseWithoutCallingService()
    {
        SetupDialog(DialogResult.Cancel());

        var result = await _actions.RejectViaDialogAsync(1, TagOwnerId);

        Assert.False(result);
        _taggingServiceMock.Verify(s => s.RejectRequestAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task RejectViaDialogAsync_WhenConfirmed_RejectsWithComment()
    {
        SetupDialog(DialogResult.Ok("bad request"));

        var result = await _actions.RejectViaDialogAsync(1, TagOwnerId);

        Assert.True(result);
        _taggingServiceMock.Verify(s => s.RejectRequestAsync(1, TagOwnerId, "bad request"), Times.Once);
        _snackbarMock.Verify(s => s.Add("リクエストを却下しました。", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RejectViaDialogAsync_WhenServiceThrows_ReturnsFalse()
    {
        SetupDialog(DialogResult.Ok<object?>(null));
        _ = _taggingServiceMock
            .Setup(s => s.RejectRequestAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await _actions.RejectViaDialogAsync(1, TagOwnerId);

        Assert.False(result);
        _snackbarMock.Verify(s => s.Add("却下に失敗しました: boom", Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task Handlers_NullParameters_ThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ApproveTaggingRequestHandler(null!));
        Assert.Throws<ArgumentNullException>(() => new RejectTaggingRequestHandler(null!));

        var approveHandler = new ApproveTaggingRequestHandler(_contractServiceMock.Object);
        await Assert.ThrowsAsync<ArgumentNullException>(() => approveHandler.HandleAsync(null!));

        var rejectHandler = new RejectTaggingRequestHandler(_taggingServiceMock.Object);
        await Assert.ThrowsAsync<ArgumentNullException>(() => rejectHandler.HandleAsync(null!));
    }

    [Fact]
    public void TaggingRequestActions_NullParameters_ThrowArgumentNullException()
    {
        var approveHandler = new Mock<ICommandHandler<ApproveTaggingRequestCommand, Result<string>>>().Object;
        var rejectHandler = new Mock<ICommandHandler<RejectTaggingRequestCommand, Result<bool>>>().Object;
        var launcher = new Mock<IDialogLauncher>().Object;
        var snackbar = new Mock<ISnackbar>().Object;

        Assert.Throws<ArgumentNullException>(() => new TaggingRequestActions(null!, rejectHandler, launcher, snackbar));
        Assert.Throws<ArgumentNullException>(() => new TaggingRequestActions(approveHandler, null!, launcher, snackbar));
        Assert.Throws<ArgumentNullException>(() => new TaggingRequestActions(approveHandler, rejectHandler, null!, snackbar));
        Assert.Throws<ArgumentNullException>(() => new TaggingRequestActions(approveHandler, rejectHandler, launcher, null!));
    }
}