namespace SRNSMudApp.Tests.Components.Tag;

using AngleSharp.Dom;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

using Xunit;

public sealed class TagCreateChildDialogTests : IAsyncLifetime
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<ITagSearchQueryService> _queryServiceMock = new();

    public TagCreateChildDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _queryServiceMock.Object);
        _ = _ctx.Services.AddAuthorizationCore();
        _ = _ctx.Services.AddAuth("user-1");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task WhenTypingSimilarName_ShowsSimilarCandidateAlertAndChips()
    {
        var existingTag = new Tag
        {
            Id = 10,
            Name = "TypeScript",
            Content = "AltJS",
            OwnerId = "user-1",
            CachedWeight = 1
        };

        _queryServiceMock.Setup(d => d.GetAllTagsAsync()).ReturnsAsync([existingTag]);

        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        _ = await dialogService.ShowAsync<TagCreateChildDialog>("子タグの追加");

        host.WaitForState(() => host.Markup.Contains("タグ名"));

        IRenderedComponent<MudTextField<string>> nameField =
            host.FindComponents<MudTextField<string>>().First(f => f.Instance.Label == "タグ名");
        await host.InvokeAsync(() => nameField.Instance.ValueChanged.InvokeAsync("Type"));

        host.WaitForState(() => host.Markup.Contains("similar-tags-alert"));
        Assert.Contains("類似する既存タグ", host.Markup);
        Assert.Contains("TypeScript", host.Markup);
    }

    [Fact]
    public async Task ClickingSimilarTagChip_AppliesNameToField()
    {
        var existingTag = new Tag
        {
            Id = 10,
            Name = "TypeScript",
            Content = "AltJS",
            OwnerId = "user-1",
            CachedWeight = 1
        };

        _queryServiceMock.Setup(d => d.GetAllTagsAsync()).ReturnsAsync([existingTag]);

        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        IDialogReference dialog = await dialogService.ShowAsync<TagCreateChildDialog>("子タグの追加");

        host.WaitForState(() => host.Markup.Contains("タグ名"));

        IRenderedComponent<MudTextField<string>> nameField =
            host.FindComponents<MudTextField<string>>().First(f => f.Instance.Label == "タグ名");
        await host.InvokeAsync(() => nameField.Instance.ValueChanged.InvokeAsync("Type"));

        host.WaitForState(() => host.Markup.Contains("similar-tags-alert"));

        IElement chip = host.Find($@"[data-testid=""similar-tag-chip-{existingTag.Id}""]");
        chip.Click();

        // 追加ボタンをクリックして、反映された名前で確定できるか確認
        IElement submitButton = host.FindAll("button").First(b => b.TextContent.Trim() == "追加");
        submitButton.Click();

        DialogResult? result = await dialog.Result.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(result);
        Assert.False(result.Canceled);

        var data = Assert.IsType<TagCreateChildDialog.Result>(result.Data);
        Assert.Equal("TypeScript", data.Name);
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    private sealed class DialogHost : ComponentBase
    {
        [Parameter] public RenderFragment ChildContent { get; set; } = _ => { };

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<Microsoft.AspNetCore.Components.Authorization.CascadingAuthenticationState>(0);
            builder.AddAttribute(1, nameof(Microsoft.AspNetCore.Components.Authorization.CascadingAuthenticationState.ChildContent), (RenderFragment)(b =>
            {
                b.OpenComponent<MudDialogProvider>(0);
                b.CloseComponent();
                b.AddContent(1, ChildContent);
            }));
            builder.CloseComponent();
        }
    }
}