#region

using Moq;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

using TagEntity = SRNSMudApp.Data.Tag;

#endregion

namespace SRNSMudApp.Tests.Components.Tag;

public class TagNameProposalViewModelTests
{
    private readonly Mock<ITagNameProposalService> _proposalServiceMock = new();
    private readonly TagNameProposalViewModel _sut;

    public TagNameProposalViewModelTests()
    {
        _sut = new TagNameProposalViewModel(_proposalServiceMock.Object);
    }

    [Fact]
    public void Constructor_NullService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new TagNameProposalViewModel(null!));
    }

    [Fact]
    public void InitialState_IsConfiguredProperly()
    {
        Assert.Null(_sut.TargetTag);
        Assert.Empty(_sut.CurrentUserId);
        Assert.Empty(_sut.ProposedName);
        Assert.Null(_sut.Reason);
        Assert.False(_sut.IsSubmitting);
        Assert.False(_sut.CanSubmit);
    }

    [Fact]
    public void Initialize_SetsTargetTagAndUserIdAndInitialName()
    {
        // Arrange
        var tag = new TagEntity { Id = 10, Name = "TagName", OwnerId = "owner" };

        // Act
        _sut.Initialize(tag, "user-123");

        // Assert
        Assert.Same(tag, _sut.TargetTag);
        Assert.Equal("user-123", _sut.CurrentUserId);
        Assert.Equal("TagName", _sut.ProposedName);
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
    [InlineData("ValidName", null)]
    [InlineData("タグ名123", null)]
    [InlineData("tag-name_test", null)]
    [InlineData("", "タグ名は必須です")]
    [InlineData("   ", "タグ名は必須です")]
    [InlineData(null, "タグ名は必須です")]
    public void ValidateTagName_ValidatesBasics(string? name, string? expectedError)
    {
        string? result = TagNameProposalViewModel.ValidateTagName(name);
        Assert.Equal(expectedError, result);
    }

    [Fact]
    public void ValidateTagName_WhenExceeds100Chars_ReturnsLengthError()
    {
        string longName = new('a', 101);
        string? result = TagNameProposalViewModel.ValidateTagName(longName);
        Assert.Equal("タグ名は100文字以内で入力してください", result);
    }

    [Fact]
    public void ValidateTagName_WhenContainsSpecialCharacters_ReturnsCharacterError()
    {
        string? result = TagNameProposalViewModel.ValidateTagName("tag\ninvalid");
        Assert.Equal("タグ名に使用できない文字が含まれています", result);
    }

    [Theory]
    [InlineData("ValidName", "user-1", true, true)]
    [InlineData("", "user-1", true, false)]
    [InlineData("tag\ninvalid", "user-1", true, false)]
    [InlineData("ValidName", "", true, false)]
    [InlineData("ValidName", "user-1", false, false)]
    public void CanSubmit_ValidatesConditions(string name, string userId, bool hasTag, bool expected)
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

        _sut.ProposedName = name;

        // Assert
        Assert.Equal(expected, _sut.CanSubmit);
    }

    [Fact]
    public async Task SubmitAsync_WhenTargetTagNull_ReturnsFailure()
    {
        _sut.CurrentUserId = "user-1";
        _sut.ProposedName = "Name";

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
        _sut.ProposedName = "Name";

        var result = await _sut.SubmitAsync();

        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Contains("ログインユーザー情報が取得できませんでした", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task SubmitAsync_WhenValidationFails_ReturnsFailure()
    {
        var tag = new TagEntity { Id = 1, Name = "T", OwnerId = "owner" };
        _sut.Initialize(tag, "user-1");
        _sut.ProposedName = "tag\ninvalid";

        var result = await _sut.SubmitAsync();

        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Equal("タグ名に使用できない文字が含まれています", failure.ErrorMessage);
        }
    }

    [Fact]
    public async Task SubmitAsync_WhenValid_CallsServiceAndReturnsSuccess()
    {
        // Arrange
        var tag = new TagEntity { Id = 5, Name = "OldName", OwnerId = "owner" };
        _sut.Initialize(tag, "user-1");
        _sut.ProposedName = "  NewName  ";
        _sut.Reason = "  Typo fix  ";

        var proposal = new TagNameProposal
        {
            Id = 100,
            TagId = 5,
            ProposedName = "NewName",
            Reason = "Typo fix",
            RequesterUserId = "user-1",
            OwnerUserId = "owner",
            OwnerId = "owner"
        };

        _proposalServiceMock.Setup(s => s.ProposeNameAsync(
                5,
                "NewName",
                "Typo fix",
                "user-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<TagNameProposal>(proposal));

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Success<TagNameProposal>);
        if (result is Success<TagNameProposal> success)
        {
            Assert.Equal(100, success.Value.Id);
            Assert.Equal("NewName", success.Value.ProposedName);
        }

        Assert.False(_sut.IsSubmitting);
        _proposalServiceMock.Verify(s => s.ProposeNameAsync(5, "NewName", "Typo fix", "user-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_WhenReasonWhitespace_PassesNullReasonToService()
    {
        // Arrange
        var tag = new TagEntity { Id = 5, Name = "OldName", OwnerId = "owner" };
        _sut.Initialize(tag, "user-1");
        _sut.ProposedName = "NewName";
        _sut.Reason = "   ";

        var proposal = new TagNameProposal
        {
            Id = 101,
            TagId = 5,
            ProposedName = "NewName",
            Reason = null,
            RequesterUserId = "user-1",
            OwnerUserId = "owner",
            OwnerId = "owner"
        };

        _proposalServiceMock.Setup(s => s.ProposeNameAsync(
                5,
                "NewName",
                null,
                "user-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<TagNameProposal>(proposal));

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Success<TagNameProposal>);
        _proposalServiceMock.Verify(s => s.ProposeNameAsync(5, "NewName", null, "user-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_WhenServiceFails_ReturnsFailure()
    {
        // Arrange
        var tag = new TagEntity { Id = 5, Name = "OldName", OwnerId = "owner" };
        _sut.Initialize(tag, "user-1");
        _sut.ProposedName = "NewName";

        _proposalServiceMock.Setup(s => s.ProposeNameAsync(
                5,
                "NewName",
                null,
                "user-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Failure("同名のタグが既に存在します。"));

        // Act
        var result = await _sut.SubmitAsync();

        // Assert
        Assert.True(result is Failure);
        if (result is Failure failure)
        {
            Assert.Equal("同名のタグが既に存在します。", failure.ErrorMessage);
        }

        Assert.False(_sut.IsSubmitting);
    }

    [Fact]
    public async Task SubmitAsync_WhenAlreadySubmitting_ReturnsFailureWithDuplicateSubmissionMessage()
    {
        // Arrange
        var tag = new TagEntity { Id = 5, Name = "OldName", OwnerId = "owner" };
        _sut.Initialize(tag, "user-1");
        _sut.ProposedName = "NewName";

        var tcs = new TaskCompletionSource<Result<TagNameProposal>>();
        _proposalServiceMock.Setup(s => s.ProposeNameAsync(5, "NewName", null, "user-1", It.IsAny<CancellationToken>()))
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

        tcs.SetResult(new Success<TagNameProposal>(new TagNameProposal { OwnerId = "owner" }));
        var firstResult = await firstTask;
        Assert.True(firstResult is Success<TagNameProposal>);
        Assert.False(_sut.IsSubmitting);

        _proposalServiceMock.Verify(s => s.ProposeNameAsync(5, "NewName", null, "user-1", It.IsAny<CancellationToken>()), Times.Once);
    }
}