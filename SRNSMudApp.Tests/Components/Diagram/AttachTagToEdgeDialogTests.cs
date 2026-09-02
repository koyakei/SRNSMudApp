using Bunit;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Diagram;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

using TagEntity = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Tests.Components.Diagram;

public sealed class AttachTagToEdgeDialogTests : IAsyncDisposable
{
    private const string CurrentUserId = "test-user-1";
    private readonly BunitContext _ctx = new();
    private readonly Mock<ITagDiagramDataProvider> _dataProviderMock = new();

    public AttachTagToEdgeDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _dataProviderMock.Object);
        _ = _ctx.Render<MudPopoverProvider>();
    }

    [Fact]
    public async Task AttachTagToEdgeDialog_ShowsOwnerNoticeAndEnablesSubmit_WhenTagOwnerHasAutoIssuedAsset()
    {
        // Arrange
        var host = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var sourceTag = new TagEntity { Id = 1, Name = "Source", OwnerId = CurrentUserId };
        var targetTag = new TagEntity { Id = 2, Name = "Target", OwnerId = CurrentUserId };
        var edge = new TagEdge { Id = 100, SourceTagId = 1, TargetTagId = 2, SourceTag = sourceTag, TargetTag = targetTag, OwnerId = CurrentUserId };

        var ownerTag = new TagEntity { Id = 3, Name = "OwnerTag", OwnerId = CurrentUserId };
        var autoIssuedAsset = new RightAsset { Id = 501, OwnerId = CurrentUserId, TargetTagId = 3, Amount = 10, IsBurned = false };

        _ = _dataProviderMock.Setup(p => p.GetAvailableRightAssetsAsync(CurrentUserId, ownerTag.Id))
            .ReturnsAsync([autoIssuedAsset]);

        var parameters = new DialogParameters<AttachTagToEdgeDialog>
        {
            { x => x.Edge, edge },
            { x => x.CurrentUserId, CurrentUserId },
            { x => x.AvailableTags, new List<TagEntity> { ownerTag } }
        };

        // Act: ダイアログを開く
        var dialogRef = await dialogService.ShowAsync<AttachTagToEdgeDialog>("Edge へのタグ紐付け", parameters);
        host.WaitForState(() => host.Markup.Contains("対象 Edge:"));

        // タグを選択する（MudAutocomplete に値をバインド）
        var autocomplete = host.FindComponent<MudAutocomplete<TagEntity>>();
        await host.InvokeAsync(async () => await autocomplete.Instance.ValueChanged.InvokeAsync(ownerTag));

        // Assert: オーナー向け案内が表示され、アセットが自動選択されて送信ボタンが有効になること
        host.WaitForState(() => host.Markup.Contains("オーナーであるため"));
        Assert.Contains("タグ「OwnerTag」のオーナーであるため、RightAsset を利用できます", host.Markup);
        Assert.Contains("アセット ID: 501 (残量: 10)", host.Markup);

        var submitButton = host.FindAll("button").First(b => b.TextContent.Contains("紐付け実行"));
        Assert.False(submitButton.HasAttribute("disabled"));

        // 紐付け実行ボタンをクリック
        await host.InvokeAsync(() => submitButton.Click());

        var result = await dialogRef.Result;
        Assert.NotNull(result);
        Assert.False(result.Canceled);
        Assert.Equal((TagId: 3, RightAssetId: 501, Weight: 1), result.Data);
    }

    [Fact]
    public async Task AttachTagToEdgeDialog_ShowsWarning_WhenNonOwnerHasNoAssets()
    {
        // Arrange
        var host = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var sourceTag = new TagEntity { Id = 1, Name = "Source", OwnerId = CurrentUserId };
        var targetTag = new TagEntity { Id = 2, Name = "Target", OwnerId = CurrentUserId };
        var edge = new TagEdge { Id = 100, SourceTagId = 1, TargetTagId = 2, SourceTag = sourceTag, TargetTag = targetTag, OwnerId = CurrentUserId };

        var nonOwnerTag = new TagEntity { Id = 4, Name = "OtherTag", OwnerId = "other-user" };

        _ = _dataProviderMock.Setup(p => p.GetAvailableRightAssetsAsync(CurrentUserId, nonOwnerTag.Id))
            .ReturnsAsync([]);

        var parameters = new DialogParameters<AttachTagToEdgeDialog>
        {
            { x => x.Edge, edge },
            { x => x.CurrentUserId, CurrentUserId },
            { x => x.AvailableTags, new List<TagEntity> { nonOwnerTag } }
        };

        // Act: ダイアログを開く
        _ = await dialogService.ShowAsync<AttachTagToEdgeDialog>("Edge へのタグ紐付け", parameters);
        host.WaitForState(() => host.Markup.Contains("対象 Edge:"));

        // タグを選択する
        var autocomplete = host.FindComponent<MudAutocomplete<TagEntity>>();
        await host.InvokeAsync(async () => await autocomplete.Instance.ValueChanged.InvokeAsync(nonOwnerTag));

        // Assert: 警告が表示され、送信ボタンが disabled であること
        host.WaitForState(() => host.Markup.Contains("所有 RightAsset（未消費の権利）がありません"));
        var submitButton = host.FindAll("button").First(b => b.TextContent.Contains("紐付け実行"));
        Assert.True(submitButton.HasAttribute("disabled"));
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}