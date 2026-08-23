#region

using System;
using System.Security.Claims;
using System.Threading.Tasks;

using Bunit;

using SRNSMudApp.Tests.TestSupport;
using Bunit.TestDoubles;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.Tag;

// BunitContextの継承をやめ、IAsyncDisposableを実装します
public class ItemTagChipTests : IAsyncDisposable
{
    private readonly BunitContext _ctx;

    public ItemTagChipTests()
    {
        _ctx = new BunitContext();

        // 認証モック・MudServices・アプリ側サービスは BunitTestSetup に集約
        _ = _ctx.Services.AddAuth("test-user-id");
        _ = _ctx.Services.AddSrnsComponentServices();

        var dbName = Guid.NewGuid().ToString();
        _ = _ctx.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped, ServiceLifetime.Singleton);
        _ = _ctx.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        var itemTagServiceMock = new Mock<IItemTagService>();
        _ = _ctx.Services.AddScoped(sp => itemTagServiceMock.Object);

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // 非同期でBunitContextを破棄し、MudBlazorの非同期サービスの例外を防ぐ
    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void ItemTagChip_ShouldRenderTagNameAndWeight_ForTagRelation()
    {
        // Arrange
        var tag = new SRNSMudApp.Data.Tag { Id = 1, Name = "TestTag", OwnerId = "test-user-id" };
        var tagRelation = new TagRelation
        {
            Id = 1,
            TagId = 1,
            Tag = tag,
            ItemId = 1,
            Weight = 10,
            OwnerId = "test-user-id"
        };
        var item = new SRNSMudApp.Data.Item { Id = 1, Content = "Test Item", OwnerId = "test-user-id" };

        // Act
        // Render ではなく、最新の _ctx.Render<T>() を使用します
        IRenderedComponent<ItemTagChip> component = _ctx.Render<ItemTagChip>(parameters => parameters
            .Add(p => p.TagRelation, tagRelation)
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, "test-user-id")
        );

        // Assert
        Assert.Contains("TestTag", component.Markup);
        Assert.Contains("10", component.Markup);
        Assert.Contains("mud-icon", component.Markup); // Check for the presence of an icon
    }
}