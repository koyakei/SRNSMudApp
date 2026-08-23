#region

using System;
using System.Security.Claims;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;

using MudBlazor.Services;

using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;

#endregion

namespace SRNSMudApp.Tests.TestSupport;

/// <summary>
///     bUnit テスト用の共通セットアップ。
///     これまで各テストクラスに重複していた BunitContext 初期化
///     (MudServices / 認証状態モック / InMemory DbContextFactory) を集約する。
/// </summary>
public static class BunitTestSetup
{
    public const string DefaultUserId = "test-user-id";

    /// <summary>
    ///     コンポーネントが依存するアプリ側サービスを登録する
    ///     (<see cref="IDialogLauncher" /> 実装と Tag/Item Card データプロバイダ実装)。
    ///     委譲先の MudBlazor サービスは AddMudServices() が、
    ///     データプロバイダの依存は AddInMemoryDbFactory() が登録するものを利用する。
    /// </summary>
    public static IServiceCollection AddSrnsComponentServices(this IServiceCollection services)
    {
        return services
            .AddScoped<IDialogLauncher, DialogLauncher>()
            .AddScoped<ITagCardDataProvider, TagCardDataProvider>()
            .AddScoped<IItemCardDataProvider, ItemCardDataProvider>()
            .AddScoped<IItemListDataProvider, ItemListDataProvider>()
            .AddScoped<ITagTreeDataProvider, TagTreeDataProvider>()
            .AddScoped<ITagTableDataProvider, TagTableDataProvider>()
            .AddScoped<IHomeDataProvider, HomeDataProvider>()
            .AddScoped<INotificationsDataProvider, NotificationsDataProvider>()
            .AddScoped<IImportTagDataProvider, ImportTagDataProvider>()
            .AddScoped<IItemDetailDataProvider, ItemDetailDataProvider>()
            .AddScoped<ITagDialogDataProvider, TagDialogDataProvider>()
            .AddScoped<ITagDetailDataProvider, TagDetailDataProvider>();
    }

    /// <summary>
    ///     認証テスト用の <see cref="AuthenticationState" /> を生成する。
    /// </summary>
    public static AuthenticationState CreateAuthState(string userId)
    {
        Claim[] claims = [new(ClaimTypes.NameIdentifier, userId), new(ClaimTypes.Name, userId)];
        ClaimsIdentity identity = new(claims, "TestAuthType");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    /// <summary>
    ///     MudBlazor サービスと、指定ユーザーで固定した認証状態モックを登録する。
    /// </summary>
    public static Mock<AuthenticationStateProvider> AddAuth(this IServiceCollection services, string userId = DefaultUserId)
    {
        _ = services.AddMudServices();

        AuthenticationState authState = CreateAuthState(userId);
        Mock<AuthenticationStateProvider> authMock = new();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        services.AddScoped(_ => authMock.Object);

        return authMock;
    }

    /// <summary>
    ///     テストごとに独立した EF Core InMemory データベースを持つ
    ///     <see cref="IDbContextFactory{ApplicationDbContext}" /> を登録する。
    /// </summary>
    public static IServiceCollection AddInMemoryDbFactory(this IServiceCollection services, string? dbName = null)
    {
        return services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
    }
}

/// <summary>
///     認証カスケード (<see cref="CascadingAuthenticationState" />) を提供するホストコンポーネント。
/// </summary>
public sealed class AuthHost : ComponentBase
{
    [Parameter] public RenderFragment ChildContent { get; set; } = _ => { };

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingAuthenticationState>(0);
        builder.AddAttribute(1, nameof(CascadingAuthenticationState.ChildContent), (RenderFragment)(b =>
        {
            b.AddContent(0, ChildContent);
        }));
        builder.CloseComponent();
    }
}
