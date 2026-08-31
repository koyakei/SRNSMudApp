using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Components.Tag;

public sealed class ItemTagTableTests : IAsyncLifetime
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<IUserDataProvider> _userDataProviderMock = new();

    public ItemTagTableTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices().AddMockSrnsServices();

        _ctx.Services.AddScoped(_ => _userDataProviderMock.Object);

        _ctx.Services.AddAuthorizationCore();
        _ctx.Services.AddAuth("user-1");
        _ctx.Render<MudPopoverProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => _ctx.DisposeAsync().AsTask();

    [Fact]
    public void Render_DisplaysUserSearchBoxAndRequestButton()
    {
        var item = new SRNSMudApp.Data.Item { Id = 1, Content = "テストアイテム", OwnerId = "user-1" };
        var tags = new List<SRNSMudApp.Data.Tag>();
        var tagRelations = new List<TagRelation>();

        var cut = _ctx.Render<ItemTagTable>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, "user-1")
            .Add(p => p.TagRelations, tagRelations)
            .Add(p => p.AllTags, tags)
        );

        // ユーザー検索ボックスと送信ボタンが存在することを確認
        Assert.Contains("タグ付与依頼:", cut.Markup);
        Assert.Contains("依頼先のユーザーを検索...", cut.Markup);
        Assert.Contains("リクエストを送信", cut.Markup);
    }
}
