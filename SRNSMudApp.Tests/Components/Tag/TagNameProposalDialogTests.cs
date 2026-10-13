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
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

using TagEntity = SRNSMudApp.Data.Tag;

namespace SRNSMudApp.Tests.Components.Tag;

/// <summary>
/// <see cref="TagNameProposalDialog"/> の描画、新しいタグ名の提案送信、およびバリデーションエラー表示を検証する bUnit テスト。
/// </summary>
public sealed class TagNameProposalDialogTests : IAsyncLifetime
{
    private const string ProposerUserId = "proposer-user";

    private readonly BunitContext _ctx = new();
    private readonly Mock<ITagNameProposalService> _proposalServiceMock = new();
    private readonly Mock<ISnackbar> _snackbarMock = new();

    public TagNameProposalDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _proposalServiceMock.Object);
        _ = _ctx.Services.AddScoped(_ => _snackbarMock.Object);
        _ = _ctx.Services.AddAuthorizationCore();
        _ = _ctx.Services.AddAuth(ProposerUserId);
        _ = _ctx.Render<MudPopoverProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public async Task Render_DisplaysCurrentTagName()
    {
        // Arrange
        var tag = new TagEntity
        {
            Id = 20,
            Name = "OldTagName",
            OwnerId = "owner-user"
        };

        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<TagNameProposalDialog>
        {
            { x => x.Tag, tag }
        };

        // Act
        _ = await dialogService.ShowAsync<TagNameProposalDialog>("タグ名の編集提案", parameters);

        // Assert
        host.WaitForState(() => host.Markup.Contains("OldTagName"));
        Assert.Contains("提案する新しいタグ名", host.Markup);
        Assert.Contains("提案を送信", host.Markup);
    }

    [Fact]
    public async Task Submit_WithValidProposedName_CallsProposeNameAsyncAndClosesDialog()
    {
        // Arrange
        var tag = new TagEntity
        {
            Id = 20,
            Name = "OldTagName",
            OwnerId = "owner-user"
        };
        var proposal = new TagNameProposal
        {
            Id = 5,
            TagId = 20,
            OwnerId = ProposerUserId,
            RequesterUserId = ProposerUserId,
            ProposedName = "NewTagName",
            Reason = "Better naming"
        };

        _ = _proposalServiceMock
            .Setup(s => s.ProposeNameAsync(
                20,
                "NewTagName",
                "Better naming",
                ProposerUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Success<TagNameProposal>(proposal));

        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<TagNameProposalDialog>
        {
            { x => x.Tag, tag }
        };

        IDialogReference dialogRef = await dialogService.ShowAsync<TagNameProposalDialog>("タグ名の編集提案", parameters);
        host.WaitForState(() => host.FindComponents<MudTextField<string>>().Count >= 3);

        // Act: [0]=現在のタグ名(ReadOnly), [1]=提案する新しいタグ名, [2]=提案理由
        var textFields = host.FindComponents<MudTextField<string>>();
        await host.InvokeAsync(() => textFields[1].Instance.ValueChanged.InvokeAsync("NewTagName"));
        await host.InvokeAsync(() => textFields[2].Instance.ValueChanged.InvokeAsync("Better naming"));

        IElement submitBtn = host.FindAll("button").First(b => b.TextContent.Contains("提案を送信"));
        await host.InvokeAsync(() => submitBtn.Click());

        // Assert
        DialogResult? result = await dialogRef.Result.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(result);
        Assert.False(result.Canceled);
        _proposalServiceMock.Verify(
            s => s.ProposeNameAsync(
                20,
                "NewTagName",
                "Better naming",
                ProposerUserId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _snackbarMock.Verify(
            s => s.Add("名前変更提案を送信しました。", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task Submit_WhenNameIsEmpty_ShowsValidationErrorSnackbarAndDoesNotCallService()
    {
        // Arrange
        var tag = new TagEntity
        {
            Id = 20,
            Name = "OldTagName",
            OwnerId = "owner-user"
        };

        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<TagNameProposalDialog>
        {
            { x => x.Tag, tag }
        };

        _ = await dialogService.ShowAsync<TagNameProposalDialog>("タグ名の編集提案", parameters);
        host.WaitForState(() => host.FindComponents<MudTextField<string>>().Count >= 3);

        var textFields = host.FindComponents<MudTextField<string>>();
        await host.InvokeAsync(() => textFields[1].Instance.ValueChanged.InvokeAsync("   "));

        // Act
        IElement submitBtn = host.FindAll("button").First(b => b.TextContent.Contains("提案を送信"));
        await host.InvokeAsync(() => submitBtn.Click());

        // Assert
        _snackbarMock.Verify(
            s => s.Add("タグ名は必須です", Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string>()),
            Times.Once);
        _proposalServiceMock.Verify(
            s => s.ProposeNameAsync(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
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
