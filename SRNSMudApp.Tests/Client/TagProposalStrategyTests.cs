namespace SRNSMudApp.Tests.Client;

using System;
using System.Threading;
using System.Threading.Tasks;

using Moq;

using SRNSMudApp.Client.Models.Api;
using SRNSMudApp.Client.Services;

using Xunit;

public class TagProposalStrategyTests
{
    private readonly Mock<ITagProposalApiClient> _apiClientMock = new();

    [Fact]
    public async Task TagNameProposalStrategy_WhenSubmitted_CallsProposeTagNameAsync()
    {
        // Arrange
        var strategy = new TagNameProposalStrategy();
        _apiClientMock
            .Setup(c => c.ProposeTagNameAsync(It.IsAny<ProposeTagNameRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TagNameProposalDto(1, 10, "old", "new", "u1", "user", "owner", "reason", "Pending", null, DateTimeOffset.UtcNow));

        // Act
        bool result = await strategy.SubmitAsync(_apiClientMock.Object, 10, "new", "reason", CancellationToken.None);

        // Assert
        Assert.True(result);
        _apiClientMock.Verify(c => c.ProposeTagNameAsync(
            It.Is<ProposeTagNameRequest>(r => r.TagId == 10 && r.ProposedName == "new" && r.Reason == "reason"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TagNameProposalStrategy_WhenNullClient_ThrowsArgumentNullException()
    {
        var strategy = new TagNameProposalStrategy();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            strategy.SubmitAsync(null!, 10, "new", null, CancellationToken.None));
    }

    [Fact]
    public async Task TagContentProposalStrategy_WhenSubmitted_CallsProposeTagContentAsync()
    {
        // Arrange
        var strategy = new TagContentProposalStrategy();
        _apiClientMock
            .Setup(c => c.ProposeTagContentAsync(It.IsAny<ProposeTagContentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TagContentProposalDto(1, 10, "old", "new desc", "u1", "user", "owner", null, "Pending", null, DateTimeOffset.UtcNow));

        // Act
        bool result = await strategy.SubmitAsync(_apiClientMock.Object, 10, "new desc", "   ", CancellationToken.None);

        // Assert
        Assert.True(result);
        _apiClientMock.Verify(c => c.ProposeTagContentAsync(
            It.Is<ProposeTagContentRequest>(r => r.TagId == 10 && r.ProposedContent == "new desc" && r.Reason == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TagContentProposalStrategy_WhenNullClient_ThrowsArgumentNullException()
    {
        var strategy = new TagContentProposalStrategy();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            strategy.SubmitAsync(null!, 10, "new desc", null, CancellationToken.None));
    }
}