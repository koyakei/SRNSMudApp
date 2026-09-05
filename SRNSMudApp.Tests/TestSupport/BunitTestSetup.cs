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

namespace SRNSMudApp.Tests.TestSupport;

/// <summary>
///     bUnit テスト用の共通セットアップ。
///     これまで各テストクラスに重複していた BunitContext 初期化
///     (MudServices / 認証状態モック / MSSQL DbContextFactory) を集約する。
/// </summary>
public static class BunitTestSetup
{
    public const string DefaultUserId = "test-user-id";

    static BunitTestSetup()
    {
        // 実DB (SQL Server) を使うため InMemory 比で待ち時間が伸びる。
        // 既定の 1 秒ではタイムアウトするため延長する。
        BunitContext.DefaultWaitTimeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    ///     コンポーネントが依存するアプリ側サービスを登録する
    ///     (<see cref="IDialogLauncher" /> 実装と Tag/Item Card データプロバイダ実装)。
    /// </summary>
    public static IServiceCollection AddSrnsComponentServices(this IServiceCollection services)
    {
        // ItemCard 経由で UrlPreviewCard のプレビュー取得に使用される。
        // 個別テストで Fake ハンドラ付きインスタンスを登録する場合は上書きされる
        _ = services.AddHttpClient();

        return services
            .AddSingleton<LinkPreviewService>()
            .AddScoped<ITaggingRequestActions, TaggingRequestActions>()
            .AddScoped<ISystemTagEnsurer, SystemTagEnsurer>()
            .AddScoped<IDialogLauncher, DialogLauncher>()
            .AddScoped<ITagCardDataProvider, TagCardDataProvider>()
            .AddScoped<IItemCardDataProvider, ItemCardDataProvider>()
            .AddScoped<IItemListDataProvider, ItemListDataProvider>()
            .AddScoped<IItemListExportService, ItemListExportService>()
            .AddScoped<ITagTreeDataProvider, TagTreeDataProvider>()
            .AddScoped<ITagTableDataProvider, TagTableDataProvider>()
            .AddScoped<IHomeDataProvider, HomeDataProvider>()
            .AddScoped<INotificationsDataProvider, NotificationsDataProvider>()
            .AddScoped<IImportTagDataProvider, ImportTagDataProvider>()
            .AddScoped<IItemDetailDataProvider, ItemDetailDataProvider>()
            .AddScoped<ITagDialogDataProvider, TagDialogDataProvider>()
            .AddScoped<ITagDetailDataProvider, TagDetailDataProvider>()
            .AddScoped<IContractDataProvider, ContractDataProvider>()
            .AddScoped<IUserDataProvider, UserDataProvider>()
            .AddScoped<IAdminDataProvider, AdminDataProvider>();
    }

    /// <summary>
    ///     コンポーネントテスト用に、全 DataProvider および Service の Mock インスタンスを登録する。
    ///     DB 接続やマイグレーションなしにミリ秒単位で bUnit コンポーネントテストを実行できる。
    /// </summary>
    public static IServiceCollection AddMockSrnsServices(this IServiceCollection services)
    {
        _ = services.AddHttpClient();

        var dummyOptions = new DbContextOptions<ApplicationDbContext>();
        var dummyContext = new ApplicationDbContext(dummyOptions);

        return services
            .AddSingleton<LinkPreviewService>()
            .AddScoped(_ => new Mock<IItemTagService>().Object)
            .AddScoped(_ => new Mock<ITaggingService>().Object)
            .AddScoped(_ => new Mock<ITagEmbeddingService>().Object)
            .AddScoped(_ => new Mock<INotificationService>().Object)
            .AddScoped(_ => new Mock<ITaggingContractService>().Object)
            .AddScoped(_ => new Mock<ITaggingRequestActions>().Object)
            .AddScoped(_ => new Mock<ISystemTagEnsurer>().Object)
            .AddScoped(_ => new Mock<IDialogLauncher>().Object)
            .AddScoped(_ => new Mock<ITagCardDataProvider>().Object)
            .AddScoped(_ => new Mock<IItemCardDataProvider>().Object)
            .AddScoped(_ => new Mock<IItemListDataProvider>().Object)
            .AddScoped(_ => new Mock<IItemListExportService>().Object)
            .AddScoped(_ => new Mock<ITagTreeDataProvider>().Object)
            .AddScoped(_ => new Mock<ITagTableDataProvider>().Object)
            .AddScoped(_ => new Mock<IHomeDataProvider>().Object)
            .AddScoped(_ => new Mock<INotificationsDataProvider>().Object)
            .AddScoped(_ => new Mock<IImportTagDataProvider>().Object)
            .AddScoped(_ => new Mock<IItemDetailDataProvider>().Object)
            .AddScoped(_ => new Mock<ITagDialogDataProvider>().Object)
            .AddScoped(_ => new Mock<ITagDetailDataProvider>().Object)
            .AddScoped(_ => new Mock<IContractDataProvider>().Object)
            .AddScoped(_ => new Mock<IUserDataProvider>().Object)
            .AddScoped(_ => new Mock<IAdminDataProvider>().Object);
    }

    /// <summary>
    ///     認証テスト用の <see cref="AuthenticationState" /> を生成する。
    /// </summary>
    public static AuthenticationState CreateAuthState(string userId, params string[] roles)
    {
        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, userId),
            ..roles.Select(r => new Claim(ClaimTypes.Role, r))
        ];
        ClaimsIdentity identity = new(claims, "TestAuthType");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    /// <summary>
    ///     MudBlazor サービスと、指定ユーザー（任意でロール）で固定した認証状態モックを登録する。
    /// </summary>
    public static Mock<AuthenticationStateProvider> AddAuth(
        this IServiceCollection services,
        string userId = DefaultUserId,
        params string[] roles)
    {
        _ = services.AddMudServices();

        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, userId),
            ..roles.Select(r => new Claim(ClaimTypes.Role, r))
        ];
        ClaimsIdentity identity = new(claims, "TestAuthType");
        AuthenticationState authState = new(new ClaimsPrincipal(identity));

        Mock<AuthenticationStateProvider> authMock = new();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        services.AddScoped(_ => authMock.Object);

        return authMock;
    }

    /// <summary>
    ///     テストごとに作成済みのMSSQLデータベース（<see cref="MsSqlTestDatabase"/> で用意したもの）に
    ///     接続する <see cref="IDbContextFactory{ApplicationDbContext}" /> および <see cref="ApplicationDbContext"/> を登録する。
    /// </summary>
    public static IServiceCollection AddMsSqlDbFactory(this IServiceCollection services, string connectionString)
    {
        _ = services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(connectionString, sqlOptions => sqlOptions.UseHierarchyId())
                   .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)),
            ServiceLifetime.Scoped,
            ServiceLifetime.Singleton);

        return services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseSqlServer(connectionString, sqlOptions => sqlOptions.UseHierarchyId())
                   .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
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