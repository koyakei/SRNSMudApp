#region

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using Bunit;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.Tag;

/// <summary>
///     タグ追加ダイアログ経由のタグ付与と、チップの閉じるボタンによる削除
///     （EF Core トラッキング例外が発生しないことの回帰）を検証する。
///     （TagDeletionTrackingE2ETests の移行テスト。ダイアログの新規作成タブ操作は
///     IDialogLauncher のモックが新規作成済みタグを返す形で置き換えている）
/// </summary>
public class TagDeletionTrackingTests : IAsyncDisposable
{
    private const string UserId = "tracking-user-id";

    private readonly BunitContext _ctx;
    private readonly Mock<IDialogLauncher> _launcherMock = new();
    private readonly Mock<IDialogReference> _addTagDialogMock = new();
    private int _onDataChangedCount;
    private SRNSMudApp.Data.Tag? _ownedTag;

    public TagDeletionTrackingTests()
    {
        _ctx = new BunitContext();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddSrnsComponentServices();
        _ctx.Services.AddAuthorizationCore();

        AuthenticationState authState = BunitTestSetup.CreateAuthState(UserId);
        Mock<AuthenticationStateProvider> authMock = new();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped(_ => authMock.Object);

        var dbName = Guid.NewGuid().ToString();
        _ = _ctx.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        _ctx.Services.AddScoped<IItemTagService, ItemTagService>();
        _ctx.Services.AddScoped<TaggingContractService>();
        _ctx.Services.AddScoped<ITaggingService, TaggingService>();

        // タグ追加ダイアログは「自分所有の既存タグを返す」ようモック
        _ctx.Services.RemoveAll<IDialogLauncher>();
        _ctx.Services.AddSingleton(_ => _launcherMock.Object);
    }

    public async ValueTask DisposeAsync() => await _ctx.DisposeAsync();

    [Fact]
    public async Task AddTagViaDialog_ThenCloseChip_RemovesRelationWithoutTrackingException()
    {
        // Arrange: ユーザー所有のタグとアイテムをシードし、ダイアログ結果を設定
        (SRNSMudApp.Data.Item item, var tagName) = await SeedDataAsync();
        SetupAddTagDialogResult(item, tagName);

        IRenderedComponent<ItemCard> cut = RenderCard(item);

        // Act 1: 「タグを追加」ボタンでダイアログを開き、結果として新規タグを受け取る
        cut.Find("button[title='タグを追加']").Click();

        cut.WaitForState(() => cut.Markup.Contains(tagName));
        Assert.Contains(tagName, cut.Markup);

        // Act 2: チップの閉じるボタンで削除
        cut.Find(".mud-chip-close-button").Click();

        // Assert: DB からリレーションが削除され、例外が発生しない
        cut.WaitForAssertion(() =>
        {
            using ApplicationDbContext db =
                _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();
            Assert.Empty(db.TagRelations.Where(tr => tr.ItemId == item.Id).ToList());
        });

        // 実アプリでは OnDataChanged → 親のデータ再読込で新パラメータが渡るため、
        // ここでも明示的に再レンダリングしてチップ消失を検証する
        cut.Render();

        Assert.DoesNotContain(tagName, cut.Markup);
        Assert.Equal(2, _onDataChangedCount);
    }

    /// <summary>ダイアログ起動時に、指定タグ名のタグ (自分所有) を返すよう設定する。</summary>
    private void SetupAddTagDialogResult(SRNSMudApp.Data.Item item, string tagName)
    {
        var newTag = _ownedTag ?? throw new InvalidOperationException("Seed first");
        _ = _addTagDialogMock.Setup(r => r.Result).Returns(Task.FromResult<DialogResult?>(DialogResult.Ok(newTag)));
        _ = _launcherMock
            .Setup(l => l.ShowAsync(
                typeof(TagAddDialog),
                "タグの追加",
                It.IsAny<DialogParameters?>(),
                It.IsAny<DialogOptions?>()))
            .ReturnsAsync(_addTagDialogMock.Object);
    }

    private async Task<(SRNSMudApp.Data.Item Item, string TagName)> SeedDataAsync()
    {
        await using ApplicationDbContext db =
            _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();

        ApplicationUser user = new() { Id = UserId, UserName = UserId };
        _ = db.Users.Add(user);

        var tagName = $"TrackingTag_{Guid.NewGuid():N}";
        _ownedTag = new SRNSMudApp.Data.Tag { Name = tagName, OwnerId = UserId };
        _ = db.Tags.Add(_ownedTag);
        SRNSMudApp.Data.Item item = new() { Content = $"Tracking item {Guid.NewGuid():N}", OwnerId = UserId };
        _ = db.Items.Add(item);
        _ = await db.SaveChangesAsync();

        return (item, tagName);
    }

    private IRenderedComponent<ItemCard> RenderCard(SRNSMudApp.Data.Item item)
    {
        return _ctx.Render<ItemCard>(parameters => parameters
            .Add(p => p.Item, item)
            .Add(p => p.CurrentUserId, UserId)
            .Add(p => p.OnDataChanged, () =>
            {
                _onDataChangedCount++;
                return Task.CompletedTask;
            }));
    }
}