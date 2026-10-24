#region

using Moq;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

using TagEntity = SRNSMudApp.Data.Tag;

#endregion

namespace SRNSMudApp.Tests.Components.Tag;

public class TagContentProposalViewModelTests
{
    private readonly Mock<ITagContentProposalService> _proposalServiceMock = new();
    private readonly TagContentProposalViewModel _sut;

    public TagContentProposalViewModelTests()
    {
        _sut = new TagContentProposalViewModel(_proposalServiceMock.Object);
    }

    [Fact]
    public void Constructor_NullService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new TagContentProposalViewModel(null!));
    }

    [Fact]
    public void InitialState_IsConfiguredProperly()
    {
        Assert.Null(_sut.TargetTag);
        Assert.Empty(_sut.CurrentUserId);
        Assert.Empty(_sut.ProposedContent);
        Assert.Null(_sut.Reason);
        Assert.False(_sut.IsSubmitting);
        Assert.False(_sut.CanSubmit);
    }

    [Fact]
    public void Initialize_SetsTargetTagAndUserIdAndInitialContent()
    {
        // Arrange
        var tag = new TagEntity { Id = 10, Name = "TagName", Content = "Existing content", OwnerId = "owner" };

        // Act
        _sut.Initialize(tag, "user-123");

        // Assert
        Assert.Same(tag, _sut.TargetTag);
        Assert.Equal("user-123", _sut.CurrentUserId);
        Assert.Equal("Existing content", _sut.ProposedContent);
        Assert.Null(_sut.Reason);
        Assert.False(_sut.IsSubmitting);
        Assert.True(_sut.CanSubmit);
    }

    [Fact]
    public void Initialize_NullTag_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.Initialize(null!, "user-123"));
    }

    [Theory]
    [InlineData("valid content", "user-1", true, true)]
    [InlineData("", "user-1", true, false)]
    [InlineData("   ", "user-1", true, false)]
    [InlineData("valid content", "", true, false)]
    [InlineData("valid content", "   ", true, false)]
    [InlineData("valid content", "user-1", false, false)]
    public void CanSubmit_ValidatesConditions(string content, string userId, bool hasTag, bool expected)
    {
        // Arrange
        var tag = hasTag ? new TagEntity { Id = 1, Name = "T", OwnerId = "owner" } : null;
        if (tag != null)
        {
            _sut.Initialize(tag, userId);
        }
        else
        {
            _sut.CurrentUserId = userId;
        }

        _sut.ProposedContent = content;

        // Assert
        Assert.Equal(expected, _sut.CanSubmit);
    }

    [Fact]
    public async Task SubmitAsync_WhenTargetTagNull_ReturnsFailure()
    {
        _sut.CurrentUserId = "user-1";
        _sut.ProposedContent = "content";

        var result = await _sut.SubmitAsync();

        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("対象タグが指定されていません", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task SubmitAsync_WhenUserIdEmpty_ReturnsFailure()
    {
        var tag = new TagEntity { Id = 1, Name = "T", OwnerId = "owner" };
        _sut.Initialize(tag, "");
        _sut.ProposedContent = "content";

        var result = await _sut.SubmitAsync();

        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("ログインユーザー情報が取得できませんでした", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task SubmitAsync_WhenProposedContentEmpty_ReturnsFailure()
    {
        var tag = new TagEntity { Id = 1, Name = "T", OwnerId = "owner" };
        _sut.Initialize(tag, "user-1");
        _sut.ProposedContent = "   ";

        var result = await _sut.SubmitAsync();

        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("提案内容は必須です", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task SubmitAsync_WhenValid_CallsServiceAndReturnsSuccess()
    {
        // Arrange
        var tag = new TagEntity { Id = 5, Name = "Sample", Content = "Old", OwnerId = "owner" };
        _sut.Initialize(tag, "user-1");
        _sut.ProposedContent = "  New description  ";
        _sut.Reason = "  Improved wording  ";

        var proposal = new TagContentProposal
        {
            Id = 100,
            TagId = 5,
            ProposedContent = "New description",
            Reason = "Improved wording",
            RequesterUserId = "user-1",
            OwnerUserId = "owner",
            OwnerId = "owner"
        };

        _proposalServiceMock.Setup(s => s.ProposeContentAsync(
                5,
                "New description",
                "Improved wording",
                "user-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<TagContentProposal>(proposal));

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Success<TagContentProposal>);
        if (result is Success<TagContentProposal> success)
        {
            Assert.Equal(100, success.Value.Id);
            Assert.Equal("New description", success.Value.ProposedContent);
        }

        Assert.False(_sut.IsSubmitting);
        _proposalServiceMock.Verify(s => s.ProposeContentAsync(5, "New description", "Improved wording", "user-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_WhenReasonWhitespace_PassesNullReasonToService()
    {
        // Arrange
        var tag = new TagEntity { Id = 5, Name = "Sample", OwnerId = "owner" };
        _sut.Initialize(tag, "user-1");
        _sut.ProposedContent = "New description";
        _sut.Reason = "   ";

        var proposal = new TagContentProposal
        {
            Id = 101,
            TagId = 5,
            ProposedContent = "New description",
            Reason = null,
            RequesterUserId = "user-1",
            OwnerUserId = "owner",
            OwnerId = "owner"
        };

        _proposalServiceMock.Setup(s => s.ProposeContentAsync(
                5,
                "New description",
                null,
                "user-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<TagContentProposal>(proposal));

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Success<TagContentProposal>);
        _proposalServiceMock.Verify(s => s.ProposeContentAsync(5, "New description", null, "user-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_WhenServiceFails_ReturnsFailure()
    {
        // Arrange
        var tag = new TagEntity { Id = 5, Name = "Sample", OwnerId = "owner" };
        _sut.Initialize(tag, "user-1");
        _sut.ProposedContent = "New description";

        _proposalServiceMock.Setup(s => s.ProposeContentAsync(
                5,
                "New description",
                null,
                "user-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Failure("既に保留中の提案が存在します。"));

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Equal("既に保留中の提案が存在します。", failure.ErrorMessage);
        }

        Assert.False(_sut.IsSubmitting);
    }

    [Fact]
    public async Task SubmitAsync_WhenAlreadySubmitting_ReturnsFailureWithDuplicateSubmissionMessage()
    {
        // Arrange
        var tag = new TagEntity { Id = 5, Name = "Sample", OwnerId = "owner" };
        _sut.Initialize(tag, "user-1");
        _sut.ProposedContent = "New description";

        var tcs = new TaskCompletionSource<Result<TagContentProposal>>();
        _proposalServiceMock.Setup(s => s.ProposeContentAsync(5, "New description", null, "user-1", It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        // Act: 最初の送信タスクを開始
        var firstTask = _sut.SubmitAsync();
        Assert.True(_sut.IsSubmitting);

        // 二重送信
        var secondResult = await _sut.SubmitAsync();

        // Assert
        Assert.True(secondResult is Failure);
        if (secondResult is Failure failure)
        {
            Assert.Equal(SRNSMudApp.Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, failure.ErrorMessage);
        }

        tcs.SetResult(new Success<TagContentProposal>(new TagContentProposal { OwnerId = "owner" }));
        var firstResult = await firstTask;
        Assert.True(firstResult is Success<TagContentProposal>);
        Assert.False(_sut.IsSubmitting);

        _proposalServiceMock.Verify(s => s.ProposeContentAsync(5, "New description", null, "user-1", It.IsAny<CancellationToken>()), Times.Once);
    }
}