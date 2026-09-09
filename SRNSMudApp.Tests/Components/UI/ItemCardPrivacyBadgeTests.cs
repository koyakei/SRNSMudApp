// Components/UI/ItemCardPrivacyBadgeTests.cs
#region

using Bunit;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Tests.TestSupport;

#endregion

namespace SRNSMudApp.Tests.Components.UI;

/// <summary>
///     ItemCardContent および ItemCard におけるプライベートモードバッジ（フォロワー限定、グループ限定）の表示テスト。
/// </summary>
public sealed class ItemCardPrivacyBadgeTests : IAsyncLifetime
{
    private const string UserId = "test-user-id";
    private readonly BunitContext _ctx = new();

    public ItemCardPrivacyBadgeTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices().AddMockSrnsServices();

        var storeMock = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(storeMock.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);
        _ctx.Services.AddScoped(_ => userManagerMock.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void ItemCardContent_WhenNotPrivate_DoesNotRenderPrivacyBadge()
    {
        IRenderedComponent<ItemCardContent> cut = _ctx.Render<ItemCardContent>(parameters => parameters
            .Add(p => p.ItemId, 1)
            .Add(p => p.Content, "Public Item Content")
            .Add(p => p.IsPrivate, false));

        Assert.DoesNotContain("フォロワー限定", cut.Markup);
        Assert.DoesNotContain("グループ:", cut.Markup);
    }

    [Fact]
    public void ItemCardContent_WhenPrivateFollowersOnly_RendersFollowersBadge()
    {
        IRenderedComponent<ItemCardContent> cut = _ctx.Render<ItemCardContent>(parameters => parameters
            .Add(p => p.ItemId, 2)
            .Add(p => p.Content, "Followers Only Content")
            .Add(p => p.IsPrivate, true)
            .Add(p => p.TargetUserGroupName, (string?)null));

        Assert.Contains("フォロワー限定", cut.Markup);
    }

    [Fact]
    public void ItemCardContent_WhenPrivateGroupOnly_RendersGroupBadge()
    {
        IRenderedComponent<ItemCardContent> cut = _ctx.Render<ItemCardContent>(parameters => parameters
            .Add(p => p.ItemId, 3)
            .Add(p => p.Content, "Group Only Content")
            .Add(p => p.IsPrivate, true)
            .Add(p => p.TargetUserGroupName, "開発チーム"));

        Assert.Contains("グループ: 開発チーム", cut.Markup);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}