#region

using System;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Bunit;

using SRNSMudApp.Tests.TestSupport;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.Item;

public class ItemListAutocompleteTests : IAsyncDisposable
{
    private const string UserId = "user1-id";
    private const string TagName = "Item1_Tag";

    private readonly BunitContext _ctx;

    public ItemListAutocompleteTests()
    {
        _ctx = new BunitContext();

        _ = _ctx.Services.AddMudServices().AddSrnsComponentServices();

        // ItemList 配下の AddItem / ResourceList / AuthorizeView が認証カスケードを必要とする。
        // AuthorizeView のため bUnit の認可テストダブルを使用し、AddItem 用に NameIdentifier クレームを付与する
        Bunit.TestDoubles.BunitAuthorizationContext authorization = _ctx.AddAuthorization();
        authorization.SetAuthorized("user1");
        authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, UserId));

        // AddItem が DI 解決する UserManager をモックで差し込む
        var storeMock = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(storeMock.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);
        _ = _ctx.Services.AddScoped(_ => userManagerMock.Object);

        // 埋め込み生成は常に失敗させ、SearchTagsAndUsersAsync のテキスト検索フォールバック経路を固定化する
        var embeddingMock = new Mock<ITagEmbeddingService>();
        embeddingMock.Setup(s => s.GenerateEmbeddingAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("embedding disabled in tests"));
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

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    /// <summary>
    ///     検索語に対するサジェスト候補が「タグ名 @」形式で返ること、および
    ///     候補選択時に検索欄へ「タグ名 @」が反映されることを検証する。
    ///     （ItemListAutocompleteE2ETests/TwoStepAutocomplete_ShouldAppendAtSymbolAndFilterByUser の移行テスト）
    /// </summary>
    [Fact]
    public async Task Suggestion_IsTagPlusAtMark_AndSelectionFillsSearchBox()
    {
        // Arrange: ユーザーとタグを事前投入
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            _ = dbContext.Users.Add(new ApplicationUser { Id = UserId, UserName = "user1" });
            _ = dbContext.Tags.Add(new SRNSMudApp.Data.Tag { Name = TagName, OwnerId = UserId });
            _ = await dbContext.SaveChangesAsync();
        }

        IRenderedComponent<ItemList> cut = _ctx.Render<ItemList>();

        // 検索用オートコンプリート（T=string）を特定
        cut.WaitForState(() => cut.FindAll("input[placeholder='タグ名 または タグ名 @ユーザー名 で検索...']").Count > 0);
        IRenderedComponent<MudAutocomplete<string>> autocomplete =
            cut.FindComponents<MudAutocomplete<string>>().First();

        // Act 1: "Item1" 入力時のサジェスト候補を実DB経由で取得
        System.Collections.Generic.IEnumerable<string> suggestions =
            await autocomplete.Instance.SearchFunc!("Item1", CancellationToken.None);

        // Assert 1: 候補は「タグ名 @」形式そのもの（末尾にスペース付き @）
        string expected = TagName + " @";
        string actual = Assert.Single(suggestions);
        Assert.Equal(expected, actual);

        // Act 2: 候補選択を再現（MudAutocomplete は選択時に ValueChanged へ候補文字列を流す）
        await cut.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(expected));

        // Assert 2: 検索欄の値が「タグ名 @」になっている
        string? value = cut.Find("input[placeholder='タグ名 または タグ名 @ユーザー名 で検索...']")
            .GetAttribute("value");
        Assert.Equal(expected, value);
    }
}
