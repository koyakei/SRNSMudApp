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
using SRNSMudApp.Tests.TestSupport;

namespace SRNSMudApp.Tests.Components.Tag;

public sealed class TagAddDialogTests : IAsyncLifetime
{
    private readonly BunitContext _ctx = new();
    private readonly Mock<ITagDialogDataProvider> _dialogDataMock = new();
    private readonly IRenderedComponent<MudPopoverProvider> _popoverProvider;

    public TagAddDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _dialogDataMock.Object);
        _ = _ctx.Services.AddAuthorizationCore();
        _ = _ctx.Services.AddAuth("user-1");
        _popoverProvider = _ctx.Render<MudPopoverProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task DialogContent_ContainsChildTagCreationTab()
    {
        _dialogDataMock.Setup(d => d.GetAllTagsAsync()).ReturnsAsync([]);

        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        _ = await dialogService.ShowAsync<TagAddDialog>("タグの追加");

        host.WaitForState(() => host.Markup.Contains("タグを検索"));

        // タブが存在することを確認
        Assert.Contains("既存タグから選択", host.Markup);
        Assert.Contains("子タグを新規作成", host.Markup);
        Assert.Contains("選択して追加", host.Markup);
        Assert.Contains("キャンセル", host.Markup);
    }

    [Fact]
    public async Task ShowWithDefaultParentTag_OpensCreateChildTab_AndPreselectsParent()
    {
        var parentTag = new SRNSMudApp.Data.Tag
        {
            Id = 10,
            Name = "親タグ",
            Content = "親の内容",
            OwnerId = "user-1",
            CachedWeight = 5
        };

        _dialogDataMock.Setup(d => d.GetAllTagsAsync()).ReturnsAsync([parentTag]);

        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters { [nameof(TagAddDialog.DefaultParentTag)] = parentTag };

        _ = await dialogService.ShowAsync<TagAddDialog>("タグの追加", parameters);

        host.WaitForState(() => host.Markup.Contains("子タグ名"));

        IRenderedComponent<MudAutocomplete<SRNSMudApp.Data.Tag>> autocomplete =
            host.FindComponent<MudAutocomplete<SRNSMudApp.Data.Tag>>();
        Assert.Equal(parentTag, autocomplete.Instance.Value);
        Assert.Contains("子タグを作成して追加", host.Markup);
        Assert.DoesNotContain("選択して追加", host.Markup);
    }

    [Fact]
    public async Task SearchTags_AndSelectTag_ReturnsSelectedTag()
    {
        var sampleTag = new SRNSMudApp.Data.Tag
        {
            Id = 1,
            Name = "テストタグ",
            Content = "テスト内容",
            OwnerId = "user-1",
            CachedWeight = 3
        };

        _dialogDataMock.Setup(d => d.GetAllTagsAsync()).ReturnsAsync([]);
        _dialogDataMock.Setup(d => d.SearchTagsAsync("テスト"))
            .ReturnsAsync([sampleTag]);

        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        IDialogReference dialog = await dialogService.ShowAsync<TagAddDialog>("タグの追加");

        host.WaitForState(() => host.Markup.Contains("タグを検索"));

        IRenderedComponent<MudTextField<string>> input = host.FindComponent<MudTextField<string>>();
        await host.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync("テスト"));

        IElement searchButton = host.FindAll("button").First(b => b.TextContent.Trim() == "検索");
        searchButton.Click();

        host.WaitForState(() => host.Markup.Contains("テストタグ"));

        IElement submitButton = host.FindAll("button").First(b => b.TextContent.Trim() == "選択して追加");
        submitButton.Click();

        DialogResult? result = await dialog.Result.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(result);
        Assert.False(result.Canceled);
        Assert.Equal(sampleTag, result.Data);
    }

    [Fact]
    public async Task SearchTags_AndToggleTree_OpensScrollableTagTreePopoverContent()
    {
        var sampleTag = new SRNSMudApp.Data.Tag
        {
            Id = 1,
            Name = "テストタグ",
            Content = "テスト内容",
            OwnerId = "user-1",
            CachedWeight = 3
        };

        _dialogDataMock.Setup(d => d.GetAllTagsAsync()).ReturnsAsync([sampleTag]);
        _dialogDataMock.Setup(d => d.SearchTagsAsync("テスト"))
            .ReturnsAsync([sampleTag]);

        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        _ = await dialogService.ShowAsync<TagAddDialog>("タグの追加");

        host.WaitForState(() => host.Markup.Contains("タグを検索"));

        IRenderedComponent<MudTextField<string>> input = host.FindComponent<MudTextField<string>>();
        await host.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync("テスト"));

        IElement searchButton = host.FindAll("button").First(b => b.TextContent.Trim() == "検索");
        searchButton.Click();

        host.WaitForState(() => host.Markup.Contains("テストタグ"));

        IElement treeButton = host.FindAll("button").First(b => b.GetAttribute("title") == "タグツリーを表示");
        treeButton.Click();

        host.WaitForState(() => host.FindComponent<MudPopover>().Instance.Open);
        IRenderedComponent<MudPopover> popover = host.FindComponent<MudPopover>();
        Assert.True(popover.Instance.Open);

        _popoverProvider.WaitForState(() => _popoverProvider.Markup.Contains("tag-tree-popover-content"));
        IRenderedComponent<TagTreePopoverContent> popoverContent = _popoverProvider.FindComponent<TagTreePopoverContent>();
        Assert.Contains("overflow-y: auto", popoverContent.Markup);
        Assert.Contains("overscroll-behavior: contain", popoverContent.Markup);
        Assert.Contains("max-height", popoverContent.Markup);
    }

    [Fact]
    public async Task CreateChildTag_SetsParentTagId_AndReturnsCreatedTag()
    {
        var parentTag = new SRNSMudApp.Data.Tag
        {
            Id = 10,
            Name = "親タグ",
            Content = "親の内容",
            OwnerId = "user-1",
            CachedWeight = 5
        };

        _dialogDataMock.Setup(d => d.GetAllTagsAsync()).ReturnsAsync([parentTag]);
        _dialogDataMock.Setup(d => d.FindTagByNameAsync("新しい子タグ")).ReturnsAsync((SRNSMudApp.Data.Tag?)null);
        _dialogDataMock.Setup(d => d.CreateTagAsync(It.IsAny<SRNSMudApp.Data.Tag>())).Returns(Task.CompletedTask);

        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        IDialogReference dialog = await dialogService.ShowAsync<TagAddDialog>("タグの追加");

        host.WaitForState(() => host.Markup.Contains("子タグを新規作成"));

        // 「子タグを新規作成」タブをクリック
        IElement childTab = host.FindAll(".mud-tab").First(t => t.TextContent.Contains("子タグを新規作成"));
        childTab.Click();

        host.WaitForState(() => host.Markup.Contains("親タグ"));

        // 親タグをセット
        IRenderedComponent<MudAutocomplete<SRNSMudApp.Data.Tag>> autocomplete =
            host.FindComponent<MudAutocomplete<SRNSMudApp.Data.Tag>>();
        await host.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(parentTag));

        // 子タグ名を入力
        IRenderedComponent<MudTextField<string>> childNameField =
            host.FindComponents<MudTextField<string>>().First(f => f.Instance.Label == "子タグ名");
        await host.InvokeAsync(() => childNameField.Instance.ValueChanged.InvokeAsync("新しい子タグ"));

        // 内容を入力
        IRenderedComponent<MudTextField<string>> childContentField =
            host.FindComponents<MudTextField<string>>().First(f => f.Instance.Label == "内容");
        await host.InvokeAsync(() => childContentField.Instance.ValueChanged.InvokeAsync("子タグ詳細"));

        // 「子タグを作成して追加」ボタンをクリック
        IElement submitButton = host.FindAll("button").First(b => b.TextContent.Trim() == "子タグを作成して追加");
        submitButton.Click();

        DialogResult? result = await dialog.Result.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(result);
        Assert.False(result.Canceled);

        var createdTag = Assert.IsType<SRNSMudApp.Data.Tag>(result.Data);
        Assert.Equal("新しい子タグ", createdTag.Name);
        Assert.Equal("子タグ詳細", createdTag.Content);
        Assert.Equal(parentTag.Id, createdTag.ParentTagId);

        _dialogDataMock.Verify(d => d.CreateTagAsync(It.Is<SRNSMudApp.Data.Tag>(
            t => t.Name == "新しい子タグ" && t.ParentTagId == parentTag.Id)), Times.Once);
    }

    [Fact]
    public async Task CancelButton_CancelsDialog()
    {
        _dialogDataMock.Setup(d => d.GetAllTagsAsync()).ReturnsAsync([]);

        IRenderedComponent<DialogHost> host = _ctx.Render<DialogHost>();
        IDialogService dialogService = _ctx.Services.GetRequiredService<IDialogService>();

        IDialogReference dialog = await dialogService.ShowAsync<TagAddDialog>("タグの追加");

        host.WaitForState(() => host.Markup.Contains("キャンセル"));

        IElement cancelButton = host.FindAll("button").First(b => b.TextContent.Trim() == "キャンセル");
        cancelButton.Click();

        DialogResult? result = await dialog.Result.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(result);
        Assert.True(result.Canceled);
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