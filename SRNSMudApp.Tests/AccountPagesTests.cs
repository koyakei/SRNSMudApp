#region

using Bunit;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using SRNSMudApp.Components.Account;
using SRNSMudApp.Components.Account.Shared;

#endregion

namespace SRNSMudApp.Tests;

public class AccountPagesTests : TestContext
{
    public AccountPagesTests()
    {
        // 共通のモックや依存関係の設定
        var antiforgeryMock = new Mock<IAntiforgery>();
        _ = antiforgeryMock.Setup(a => a.GetTokens(It.IsAny<HttpContext>()))
            .Returns(new AntiforgeryTokenSet("dummy-request-token", "dummy-cookie-token", "form-field-name",
                "header-name"));
        _ = Services.AddSingleton(antiforgeryMock.Object);
        // NavigationManager は bUnit に標準で組み込まれている
    }

    [Fact]
    public void StatusMessage_RendersWithoutException_WhenHttpContextIsNull()
    {
        // Arrange
        // bUnit はデフォルトで HttpContext などの CascadingParameter を提供しないため、null になる

        // Act & Assert
        // 例外（NullReferenceException など）が発生しないことを確認する
        Exception? exception = Record.Exception(() => RenderComponent<StatusMessage>());
        Assert.Null(exception);
    }

    [Fact]
    public void PasskeySubmit_RendersWithoutException_WhenHttpContextIsNull()
    {
        // Arrange
        var testContext = new TestContext();

        // Mock AntiforgeryStateProvider
        // In bUnit, AntiforgeryStateProvider is not registered by default.
        // We can either mock it or just use Bunit.TestContext.Services to add a dummy implementation or use bUnit's built-in support if available.
        // However, for AntiforgeryStateProvider, the simplest is to just add it as a mock or use the underlying mechanism if possible.
        // Actually, since AntiforgeryStateProvider is an abstract class, we can mock it with Moq.
        var antiforgeryStateProviderMock = new Mock<AntiforgeryStateProvider>();
        _ = antiforgeryStateProviderMock.Setup(p => p.GetAntiforgeryToken())
            .Returns(new AntiforgeryRequestToken("RequestVerificationToken", "dummy-token"));

        _ = testContext.Services.AddSingleton(antiforgeryStateProviderMock.Object);

        // Act & Assert
        Exception? exception = Record.Exception(() => testContext.RenderComponent<PasskeySubmit>(parameters =>
            parameters
                .Add(p => p.Operation, PasskeyOperation.Request)
                .Add(p => p.Name, "test-name")
                .Add(p => p.ChildContent, "Submit")));

        Assert.Null(exception);
    }
}