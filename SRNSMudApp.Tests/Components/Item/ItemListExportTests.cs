#region

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.Item;

/// <summary>
///     JSONエクスポート機能のテスト。ブラウザのダウンロード動作ではなく、
///     JS interop（window.downloadFileFromText）に渡されるJSON文字列の中身を検証する。
///     （ItemListExportE2ETests.ExportToJson_ShouldIncludeLinkPreview の移行テスト）
/// </summary>
public class ItemListExportTests : IAsyncDisposable
{
    private const string UserId = "export-user-id";

    private readonly BunitContext _ctx;

    public ItemListExportTests()
    {
        _ctx = new BunitContext();
        _ = _ctx.Services.AddMudServices();

        // ItemList 配下の AddItem / ResourceList / AuthorizeView が認証カスケードを必要とする
        Bunit.TestDoubles.BunitAuthorizationContext authorization = _ctx.AddAuthorization();
        authorization.SetAuthorized("export_user");
        authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, UserId));

        var storeMock = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(storeMock.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);
        _ = _ctx.Services.AddScoped(_ => userManagerMock.Object);

        var embeddingMock = new Mock<ITagEmbeddingService>();
        _ = _ctx.Services.AddScoped(_ => embeddingMock.Object);

        // ItemCard が DI 解決する TaggingContractService
        _ctx.Services.AddScoped<TaggingContractService>();

        // ItemCard 配下のコンポーネントが DI 解決する IItemTagService
        Mock<IItemTagService> itemTagMock = new();
        _ = itemTagMock.Setup(s => s.GetTaggingRequestsForItemAsync(It.IsAny<int>()))
            .ReturnsAsync([]);
        _ = itemTagMock.Setup(s => s.GetItemRepliesAsync(It.IsAny<int>()))
            .ReturnsAsync([]);
        _ctx.Services.AddScoped(_ => itemTagMock.Object);

        // LinkPreviewService が実際にHTTPフェッチしても固定HTMLを返すハンドラ経由にする
        _ = _ctx.Services.AddSingleton(new LinkPreviewService(new HttpClient(new FakeLinkPreviewHandler())));

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
    ///     URL を含むアイテムを JSON エクスポートすると、出力に LinkPreview（タイトル付き）が
    ///     含まれることを検証する。
    /// </summary>
    [Fact]
    public async Task ExportToJson_IncludesLinkPreview()
    {
        // Arrange: URL を含むアイテムを投入
        IDbContextFactory<ApplicationDbContext> dbFactory =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using (ApplicationDbContext dbContext = await dbFactory.CreateDbContextAsync())
        {
            _ = dbContext.Users.Add(new ApplicationUser { Id = UserId, UserName = "export_user" });
            _ = dbContext.Items.Add(new SRNSMudApp.Data.Item
            {
                Content = $"This is a test item with a URL: https://example.com {Guid.NewGuid()}",
                OwnerId = UserId
            });
            _ = await dbContext.SaveChangesAsync();
        }

        IRenderedComponent<ItemList> cut = _ctx.Render<ItemList>();

        cut.WaitForState(() => cut.Markup.Contains("item-card-"));

        // JS ダウンロード呼び出しを記録対象にする
        var downloadJs = _ctx.JSInterop.SetupVoid("window.downloadFileFromText", _ => true);

        // Act: 「JSONエクスポート」ボタン押下
        cut.FindAll("button").First(b => b.TextContent.Contains("JSONエクスポート")).Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(downloadJs.Invocations));

        string json = Assert.IsType<string>(downloadJs.Invocations.Single().Arguments[1]);

        // Assert: JSON に example.com の LinkPreview（タイトル非空）が含まれる
        List<ExportItemDto>? exportList =
            JsonSerializer.Deserialize<List<ExportItemDto>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        Assert.NotNull(exportList);
        Assert.NotEmpty(exportList!);

            System.Collections.Generic.List<ExportLinkPreviewDto> previews = exportList
            .Where(i => i.LinkPreviews is { Count: > 0 })
            .SelectMany(i => i.LinkPreviews)
            .Where(lp => lp.Url.Contains("example.com"))
            .ToList();

        Assert.NotEmpty(previews);
        Assert.All(previews, lp => Assert.False(string.IsNullOrWhiteSpace(lp.Title)));
    }

    private class ExportItemDto
    {
        public string Content { get; set; } = string.Empty;
        public List<ExportLinkPreviewDto> LinkPreviews { get; set; } = [];
    }

    private class ExportLinkPreviewDto
    {
        public string Url { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
    }

    /// <summary>
    ///     あらゆる GET リクエストに対して title 付き HTML を返すフェイクハンドラ。
    /// </summary>
    private sealed class FakeLinkPreviewHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "<html><head><title>Example Title</title></head><body><p>example body text</p></body></html>",
                    System.Text.Encoding.UTF8,
                    "text/html")
            };
            return Task.FromResult(response);
        }
    }
}
