using Moq;

using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Commands;

namespace SRNSMudApp.Tests.Services;

public class TaggingRequestCommandsTests
{
    [Fact]
    public async Task ApproveTaggingRequestHandler_ShouldDelegateToTaggingContractService()
    {
        // Arrange
        var mockService = new Mock<ITaggingContractService>();
        mockService.Setup(s => s.AcceptContractAsync(42, "user-1", 10))
            .ReturnsAsync(new Success<string>("契約を承認しました。"));

        var handler = new ApproveTaggingRequestHandler(mockService.Object);

        // Act
        var result = await handler.HandleAsync(new ApproveTaggingRequestCommand(42, "user-1", 10));

        // Assert
        Assert.True(result is Success<string>);
        mockService.Verify(s => s.AcceptContractAsync(42, "user-1", 10), Times.Once);
    }

    [Fact]
    public async Task RejectTaggingRequestHandler_ShouldDelegateToTagRequestRejectionService()
    {
        // Arrange
        var mockRejectionService = new Mock<ITagRequestRejectionService>();
        mockRejectionService.Setup(s => s.RejectRequestAsync(42, "user-1", "Not suitable"))
            .Returns(Task.CompletedTask);

        var handler = new RejectTaggingRequestHandler(mockRejectionService.Object);

        // Act
        var result = await handler.HandleAsync(new RejectTaggingRequestCommand(42, "user-1", "Not suitable"));

        // Assert
        Assert.True(result is Success<bool>);
        mockRejectionService.Verify(s => s.RejectRequestAsync(42, "user-1", "Not suitable"), Times.Once);
    }
}