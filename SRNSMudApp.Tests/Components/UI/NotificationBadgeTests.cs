using System.Security.Claims;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Components.UI;

/// <summary>
///     <see cref="NotificationBadge" /> において、同一パス内でのクエリパラメータ変更時に
///     未読カウントの再問い合わせが発生しないことを検証するテスト。
/// </summary>
public sealed class NotificationBadgeTests : IAsyncLifetime
{
    private const string UserId = "test-user-1";
    private readonly BunitContext _ctx = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();

    public NotificationBadgeTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ctx.Services.AddAuthorizationCore();

        AuthenticationState authState = BunitTestSetup.CreateAuthState(UserId);
        Mock<AuthenticationStateProvider> authMock = new();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped(_ => authMock.Object);
        _ctx.Services.AddScoped(_ => _notificationServiceMock.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void WhenLocationChangedWithSamePathAndDifferentQuery_DoesNotRefreshUnreadCount()
    {
        // Arrange
        NavigationManager navMan = _ctx.Services.GetRequiredService<NavigationManager>();
        navMan.NavigateTo("http://localhost/Item/ItemList");

        _notificationServiceMock
            .Setup(s => s.GetUnreadCountAsync(UserId))
            .ReturnsAsync(2);

        AuthenticationState authState = BunitTestSetup.CreateAuthState(UserId);

        IRenderedComponent<NotificationBadge> cut = _ctx.Render<NotificationBadge>(parameters => parameters
            .AddCascadingValue(Task.FromResult(authState)));

        cut.WaitForState(() => cut.Markup.Contains("2"));

        // Act: 同一パスでクエリパラメータのみを変更（スクロール時のフォーカス変更を模倣）
        navMan.NavigateTo("http://localhost/Item/ItemList?focus=item:10");
        navMan.NavigateTo("http://localhost/Item/ItemList?focus=item:20");

        // Assert: 未読カウントの問い合わせは初期表示の1回のみで、クエリ変更では再実行されない
        _notificationServiceMock.Verify(s => s.GetUnreadCountAsync(UserId), Times.Once);
    }

    [Fact]
    public void WhenLocationChangedWithDifferentPath_RefreshesUnreadCount()
    {
        // Arrange
        NavigationManager navMan = _ctx.Services.GetRequiredService<NavigationManager>();
        navMan.NavigateTo("http://localhost/Item/ItemList");

        _notificationServiceMock
            .Setup(s => s.GetUnreadCountAsync(UserId))
            .ReturnsAsync(2);

        AuthenticationState authState = BunitTestSetup.CreateAuthState(UserId);

        IRenderedComponent<NotificationBadge> cut = _ctx.Render<NotificationBadge>(parameters => parameters
            .AddCascadingValue(Task.FromResult(authState)));

        cut.WaitForState(() => cut.Markup.Contains("2"));

        // Act: 異なるパスへ遷移
        navMan.NavigateTo("http://localhost/ItemDetail/10");

        // Assert: パスが変わったため再度未読件数が取得される
        _notificationServiceMock.Verify(s => s.GetUnreadCountAsync(UserId), Times.Exactly(2));
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}