using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using AngleSharp.Dom;

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
using SRNSMudApp.Tests.TestSupport;

using Xunit;

namespace SRNSMudApp.Tests.Components.Item;

/// <summary>
///     ItemDetail のタグチップで Weight を減らした際に、アクション列へ反映されることを検証する。
///     （ItemDetailTagWeightE2ETests の移行テスト）
/// </summary>
[Collection(MsSqlCollection.Name)]
public class ItemDetailTagWeightTests(MsSqlContainerFixture fixture) : IAsyncLifetime
{
    private const string UserId = "weight-user-id";
    private const string UserName = "weight_user";

    private readonly BunitContext _ctx = new();
    private MsSqlTestDatabase _testDb = null!;

    public async Task InitializeAsync()
    {
        _testDb = await MsSqlTestDatabase.CreateAsync(fixture.ConnectionString, nameof(ItemDetailTagWeightTests));

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddSrnsComponentServices();

        Bunit.TestDoubles.BunitAuthorizationContext authorization = _ctx.AddAuthorization();
        authorization.SetAuthorized(UserName);
        authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, UserId));

        var storeMock = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(storeMock.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);
        _ = _ctx.Services.AddScoped(_ => userManagerMock.Object);

        _ctx.Services.AddScoped<TaggingContractService>();

        // Weight 更新・リプライ取得は実サービスを使用する
        _ctx.Services.AddScoped<ITaggingService, TaggingService>();
        _ctx.Services.AddScoped<IItemTagService, ItemTagService>();

        _ctx.Services.AddMsSqlDbFactory(_testDb.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
        await _testDb.DisposeAsync();
    }

    [Fact]
    public async Task ClickingDecreaseWeightButton_ShowsUpdatedWeightInActionsColumn()
    {
        (var itemId, var tagName) = await SeedDataAsync();

        NavigationManager navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"http://localhost/ItemDetail/{itemId}");

        IRenderedComponent<ItemDetail> cut =
            _ctx.Render<ItemDetail>(parameters => parameters.Add(p => p.ItemId, itemId));

        cut.WaitForState(() => !cut.Markup.Contains("mud-progress-circular"));
        cut.WaitForState(() => cut.Markup.Contains(tagName));

        // Act: 該当タグの行にある「Weightを減らす」ボタンをクリック
        IElement decreaseButton = cut.FindAll("button[title='Weightを減らす']").First();
        decreaseButton.Click();

        // Assert: アクション列の Weight 表示が 0 → -1 に更新される
        cut.WaitForAssertion(() =>
        {
            IElement actionsCell = cut.FindAll("td[data-label='Actions']")
                .First(td => td.TextContent.Contains("-1"));
            Assert.Contains("-1", actionsCell.TextContent);
        });

        // DB 側も更新されていること
        await using ApplicationDbContext db =
            await _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync();
        TagRelation relation = await db.TagRelations.SingleAsync(tr => tr.ItemId == itemId);
        Assert.Equal(-1, relation.Weight);
    }

    private async Task<(int ItemId, string TagName)> SeedDataAsync()
    {
        await using ApplicationDbContext db =
            await _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync();

        ApplicationUser user = new() { Id = UserId, UserName = UserName };
        _ = db.Users.Add(user);

        var tagName = $"WeightTag_{Guid.NewGuid():N}";
        SRNSMudApp.Data.Tag tag = new() { Name = tagName, OwnerId = UserId };
        _ = db.Tags.Add(tag);

        SRNSMudApp.Data.Item item = new() { Content = $"Weight item {Guid.NewGuid():N}", OwnerId = UserId };
        _ = db.Items.Add(item);
        _ = await db.SaveChangesAsync();

        TagRelation relation = new()
        {
            ItemId = item.Id,
            TagId = tag.Id,
            OwnerId = UserId,
            Weight = 0
        };
        _ = db.TagRelations.Add(relation);
        _ = await db.SaveChangesAsync();

        return (item.Id, tagName);
    }
}