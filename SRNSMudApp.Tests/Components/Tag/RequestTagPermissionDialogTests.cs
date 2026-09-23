using System.Security.Claims;

using AngleSharp.Dom;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

using TagEntity = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Tests.Components.Tag;

public sealed class RequestTagPermissionDialogTests : IAsyncDisposable
{
    private const string CurrentUserId = "test-user-me";
    private readonly BunitContext _ctx = new();
    private readonly Mock<IRightAssetDataProvider> _rightAssetDataProviderMock = new();
    private readonly Mock<ISnackbar> _snackbarMock = new();

    public RequestTagPermissionDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddAuthorizationCore();
        _ = _ctx.Services.AddAuth(CurrentUserId);
        _ = _ctx.Services.AddScoped(_ => _rightAssetDataProviderMock.Object);
        _ = _ctx.Services.AddScoped(_ => _snackbarMock.Object);
        _ = _ctx.Render<MudPopoverProvider>();

        _ = _rightAssetDataProviderMock.Setup(p => p.GetAvailableRightAssetsForUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public async Task RequestTagPermissionDialog_RendersTagInfo_AndPresetsTargetUser()
    {
        // Arrange
        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var tag = new TagEntity
        {
            Id = 10,
            Name = "Solana",
            OwnerId = "owner-alice",
            Owner = new ApplicationUser { Id = "owner-alice", UserName = "Alice" }
        };

        var holders = new List<RightAssetHolderSummary>
        {
            new("owner-alice", "Alice", 50, 1, 0, 0, DateTime.UtcNow),
            new("user-bob", "Bob", 30, 1, 0, 0, DateTime.UtcNow),
            new(CurrentUserId, "Me", 10, 1, 0, 0, DateTime.UtcNow)
        };

        var myAssets = new List<UserAvailableRightAssetDto>
        {
            new(101, 20, "Ethereum", 15)
        };

        _ = _rightAssetDataProviderMock.Setup(p => p.GetAvailableRightAssetsForUserAsync(CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(myAssets);

        var parameters = new DialogParameters<RequestTagPermissionDialog>
        {
            { x => x.RequestedTag, tag },
            { x => x.PresetTargetUserId, "user-bob" },
            { x => x.AvailableHolders, holders }
        };

        // Act
        _ = await dialogService.ShowAsync<RequestTagPermissionDialog>(
            "操作権限のリクエスト",
            parameters,
            new DialogOptions());

        // Assert: ダイアログコンテンツがレンダリングされる
        host.WaitForAssertion(() =>
        {
            Assert.Contains("「Solana」の操作権限リクエスト", host.Markup);
            Assert.Contains("このタグの操作権限（RightAsset）を保有者から譲渡・交換してもらうリクエストを送信します", host.Markup);
        });
    }

    [Fact]
    public async Task RequestTagPermissionDialog_WhenSubmitted_CallsDataProvider_AndClosesDialog()
    {
        // Arrange
        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var tag = new TagEntity
        {
            Id = 10,
            Name = "Solana",
            OwnerId = "owner-alice"
        };

        var holders = new List<RightAssetHolderSummary>
        {
            new("user-bob", "Bob", 30, 1, 0, 0, DateTime.UtcNow)
        };

        _ = _rightAssetDataProviderMock.Setup(p => p.GetAvailableRightAssetsForUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _ = _rightAssetDataProviderMock.Setup(p => p.SubmitPermissionRequestAsync(
                It.IsAny<string>(),
                It.IsAny<TagPermissionRequestDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<bool>(true));

        var parameters = new DialogParameters<RequestTagPermissionDialog>
        {
            { x => x.RequestedTag, tag },
            { x => x.PresetTargetUserId, "user-bob" },
            { x => x.AvailableHolders, holders }
        };

        // Act
        IDialogReference dialogRef = await dialogService.ShowAsync<RequestTagPermissionDialog>("操作権限のリクエスト", parameters);

        host.WaitForAssertion(() =>
        {
            IElement? submitBtn = host.FindAll("[data-testid='submit-request-button']").FirstOrDefault();
            Assert.NotNull(submitBtn);
        });

        IElement submitButton = host.Find("[data-testid='submit-request-button']");
        await host.InvokeAsync(() => submitButton.Click());

        // Assert: SubmitPermissionRequestAsync が呼ばれたこと
        _rightAssetDataProviderMock.Verify(p => p.SubmitPermissionRequestAsync(
            CurrentUserId,
            It.Is<TagPermissionRequestDto>(r => r.RequestedTagId == 10 && r.TargetUserId == "user-bob"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RequestTagPermissionDialog_WhenCancelClicked_CancelsDialog()
    {
        // Arrange
        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        var tag = new TagEntity
        {
            Id = 10,
            Name = "Solana",
            OwnerId = "owner-alice"
        };

        var parameters = new DialogParameters<RequestTagPermissionDialog>
        {
            { x => x.RequestedTag, tag },
            { x => x.AvailableHolders, [] }
        };

        // Act
        IDialogReference dialogRef = await dialogService.ShowAsync<RequestTagPermissionDialog>("操作権限のリクエスト", parameters);

        host.WaitForAssertion(() =>
        {
            IElement? cancelBtn = host.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("キャンセル"));
            Assert.NotNull(cancelBtn);
        });

        IElement cancelButton = host.FindAll("button").First(b => b.TextContent.Contains("キャンセル"));
        await host.InvokeAsync(() => cancelButton.Click());

        DialogResult? result = await dialogRef.Result;
        Assert.NotNull(result);
        Assert.True(result.Canceled);
    }

    private sealed class DialogHost : ComponentBase
    {
        [Parameter] public RenderFragment ChildContent { get; set; } = _ => { };

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, nameof(CascadingAuthenticationState.ChildContent), (RenderFragment)(b =>
            {
                b.OpenComponent<MudDialogProvider>(0);
                b.CloseComponent();
                b.AddContent(1, ChildContent);
            }));
            builder.CloseComponent();
        }
    }
}