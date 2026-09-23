using SRNSMudApp.Components.Account;
using SRNSMudApp.Data;

namespace SRNSMudApp.Tests.Components.Account;

public class IdentityNoOpEmailSenderTests
{
    private readonly IdentityNoOpEmailSender _sender = new();
    private readonly ApplicationUser _user = new() { Id = "test-user-id", Email = "test@example.com" };

    [Fact]
    public async Task SendConfirmationLinkAsync_WithValidParameters_CompletesSuccessfully()
    {
        // Act
        Task task = _sender.SendConfirmationLinkAsync(_user, "test@example.com", "https://example.com/confirm");

        // Assert
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task SendConfirmationLinkAsync_WithNullUser_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _sender.SendConfirmationLinkAsync(null!, "test@example.com", "https://example.com/confirm"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendConfirmationLinkAsync_WithInvalidEmail_ThrowsArgumentException(string? email)
    {
        // Act & Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            _sender.SendConfirmationLinkAsync(_user, email!, "https://example.com/confirm"));
    }

    [Fact]
    public async Task SendPasswordResetLinkAsync_WithValidParameters_CompletesSuccessfully()
    {
        // Act
        Task task = _sender.SendPasswordResetLinkAsync(_user, "test@example.com", "https://example.com/reset");

        // Assert
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task SendPasswordResetCodeAsync_WithValidParameters_CompletesSuccessfully()
    {
        // Act
        Task task = _sender.SendPasswordResetCodeAsync(_user, "test@example.com", "123456");

        // Assert
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }
}
