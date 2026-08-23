#region

using System;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;

using AngleSharp.Dom;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.Item;

public class ItemListTagSearchTests : IAsyncDisposable
{
    private const string UserId = "tagtest-user-id";
    private const string TagName = "SearchTestTag";

    private readonly BunitContext _ctx;

    public ItemListTagSearchTests()
    {
        _ctx = new BunitContext();

        _ = _ctx.Services.AddMudServices().AddSrnsComponentServices();

        // ItemList 配下の AddItem / ResourceList / AuthorizeView が認証カスケードを必要とする。
        // AuthorizeView のため bUnit の認可テストダブルを使用し、AddItem 用に NameIdentifier クレームを付与する
        Bunit.TestDoubles.BunitAuthorizationContext authorization = _ctx.AddAuthorization();
        authorization.SetAuthorized("tagtest_user");
        authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, UserId));

        // AddItem が DI 解決する UserManager をモックで差し込む
        var storeMock = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(storeMock.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);
        _ = _ctx.Services.AddScoped(_ => userManagerMock.Object);

        var embeddingMock = new Mock<ITagEmbeddingService>();
        _ = _ctx.Services.AddScoped(_ => embeddingMock.Object);

        // LinkPreviewService は HttpClient を要求するのみのため素のインスタンスを登録
        _ = _ctx.Services.AddSingleton(new LinkPreviewService(new HttpClient()));

        var dbName = Guid.NewGuid().ToString();
        _ = _ctx.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped, ServiceLifetime.Singleton);
        _ = _ctx.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public async ValueTask DisposeAsync() => await _ctx.DisposeAsync();

    /// <summary>
    ///     タグ候補選択後に検索を実行すると、選択タグのチップが表示され、
    ///     URL のクエリへ f= パラメータが反映されることを検証する。
    ///     （ItemListTagSearchE2ETests/ClickingSuggestion_AddsTagChip の移行テスト。
    ///     URL からのフィルタ復元は ItemListQueryStateTests が担保済みのため、
    ///     本テストはチップ表示と URL クエリ生成を担当する）
    /// </summary>
    [Fact]
    public async Task ExecutingTagSearch_ShowsTagChip_AndReflectsFilterQueryParameter()
    {
        // Arrange: ユーザーとタグを事前投入
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            _ = dbContext.Users.Add(new ApplicationUser { Id = UserId, UserName = "tagtest_user" });
            _ = dbContext.Tags.Add(new SRNSMudApp.Data.Tag { Name = TagName, OwnerId = UserId });
            _ = await dbContext.SaveChangesAsync();
        }

        IRenderedComponent<ItemList> cut = _ctx.Render<ItemList>();

        cut.WaitForState(() => cut.FindAll("input[placeholder='タグ名 または タグ名 @ユーザー名 で検索...']").Count > 0);
        IRenderedComponent<MudAutocomplete<string>> autocomplete =
            cut.FindComponents<MudAutocomplete<string>>().First();

        // Act 1: 候補「SearchTestTag @」の選択を再現（検索欄へ反映される）
        await cut.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(TagName + " @"));

        // Act 2: 虫眼鏡アイコンの装飾ボタン押下で検索実行（OnAdornmentClick="ExecuteSearch"）
        autocomplete.Find(".mud-input-adornment button").Click();

        // Assert 1: 選択タグのチップが表示される
        cut.WaitForAssertion(() =>
        {
            IElement chip = cut.Find(".mud-chip");
            Assert.Contains(TagName, chip.TextContent);
        });

        // Assert 2: URL クエリに f= (タグフィルタ) が含まれる
        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        Assert.Contains("f=", navigationManager.Uri);
    }
}