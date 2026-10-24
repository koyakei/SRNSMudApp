using System.Security.Claims;

using Bunit;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor;
using MudBlazor.Services;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Components.Item;

public sealed class AddItemTests : IAsyncLifetime
{
    private const string ExistingUserId = "test-user-id";
    private const string TestContent = "Test Item Content";

    private readonly BunitContext _ctx = new();
    private readonly Mock<IItemCardDataProvider> _itemCardDataMock = new();
    private readonly Mock<IUserDataProvider> _userDataProviderMock = new();
    private readonly Mock<ITagSearchQueryService> _tagSearchMock = new();
    private readonly Mock<ITagSuggestionService> _tagSuggestionMock = new();

    public AddItemTests()
    {
        _ = _ctx.Services.AddMudServices().AddMockSrnsServices();
        _ = _ctx.Services.AddScoped(_ => _itemCardDataMock.Object);

        Claim[] claims = [new(ClaimTypes.NameIdentifier, ExistingUserId), new(ClaimTypes.Name, "testuser")];
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var authState = new AuthenticationState(new ClaimsPrincipal(identity));
        _ = _ctx.Services.AddCascadingValue(_ => Task.FromResult(authState));
        _ = _ctx.Services.AddAuthorizationCore();

        _ = _ctx.Services.AddScoped(_ => _userDataProviderMock.Object);
        _ = _ctx.Services.AddScoped(_ => _tagSearchMock.Object);

        _tagSuggestionMock
            .Setup(s => s.SuggestTagsAsync(It.IsAny<string>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _ = _ctx.Services.AddScoped(_ => _tagSuggestionMock.Object);

        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Render<MudPopoverProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void Save_SubmitsForm_AndCallsCreateItemAsyncWithCorrectParameters()
    {
        var onItemAddedCalled = false;
        _ = _itemCardDataMock
            .Setup(d => d.CreateItemAsync(It.IsAny<SRNSMudApp.Data.Item>(), It.IsAny<IReadOnlyCollection<int>?>()))
            .Returns(Task.CompletedTask);

        IRenderedComponent<AddItem> cut = _ctx.Render<AddItem>(parameters => parameters
            .Add(p => p.OnItemAdded, () => onItemAddedCalled = true));

        cut.WaitForState(() => cut.FindAll("form").Count > 0);
        cut.Find("textarea").Input(TestContent);


        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            _itemCardDataMock.Verify(d => d.CreateItemAsync(
                It.Is<SRNSMudApp.Data.Item>(i => i.Content == TestContent && i.OwnerId == ExistingUserId),
                It.IsAny<IReadOnlyCollection<int>?>()), Times.Once);

            Assert.True(onItemAddedCalled);
        });
    }

    [Fact]
    public void CommandEnter_SavesItem()
    {
        _ = _itemCardDataMock
            .Setup(d => d.CreateItemAsync(It.IsAny<SRNSMudApp.Data.Item>(), It.IsAny<IReadOnlyCollection<int>?>()))
            .Returns(Task.CompletedTask);

        IRenderedComponent<AddItem> cut = _ctx.Render<AddItem>();

        cut.WaitForState(() => cut.FindAll("form").Count > 0);
        cut.Find("textarea").Input(TestContent);
        cut.Find("textarea").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs
        {
            Key = "Enter",
            MetaKey = true
        });

        cut.WaitForAssertion(() =>
        {
            _itemCardDataMock.Verify(d => d.CreateItemAsync(
                It.Is<SRNSMudApp.Data.Item>(i => i.Content == TestContent && i.OwnerId == ExistingUserId),
                It.IsAny<IReadOnlyCollection<int>?>()), Times.Once);
        });
    }

    [Fact]
    public void Input_UrlInContent_RendersUrlPreviewCardWithLoadPreview()
    {
        const string inputContent = "Check this out https://example.com";
        IRenderedComponent<AddItem> cut = _ctx.Render<AddItem>();

        cut.WaitForState(() => cut.FindAll("form").Count > 0);
        cut.Find("textarea").Input(inputContent);



        var previewCard = cut.FindComponent<SRNSMudApp.Components.UI.UrlPreviewCard>();
        Assert.NotNull(previewCard);
        Assert.Equal("https://example.com", previewCard.Instance.Url);
        Assert.NotNull(previewCard.Instance.LoadPreview);
    }

    [Fact]
    public void Save_WhenPrivateModeEnabled_CreatesItemWithIsPrivateTrue()
    {
        _ = _itemCardDataMock
            .Setup(d => d.CreateItemAsync(It.IsAny<SRNSMudApp.Data.Item>(), It.IsAny<IReadOnlyCollection<int>?>()))
            .Returns(Task.CompletedTask);

        IRenderedComponent<AddItem> cut = _ctx.Render<AddItem>();

        cut.WaitForState(() => cut.FindAll("form").Count > 0);
        cut.Find("textarea").Input(TestContent);



        // プライベートモードのスイッチ (MudSwitch) を ON に切り替える
        var switchInput = cut.Find("input[type='checkbox']");
        switchInput.Change(true);

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            _itemCardDataMock.Verify(d => d.CreateItemAsync(
                It.Is<SRNSMudApp.Data.Item>(i => i.Content == TestContent && i.OwnerId == ExistingUserId && i.IsPrivate),
                It.IsAny<IReadOnlyCollection<int>?>()), Times.Once);
        });
    }

    [Fact]
    public void Render_ContainsContentEditableEditor()
    {
        IRenderedComponent<AddItem> cut = _ctx.Render<AddItem>();

        cut.WaitForState(() => cut.FindAll("form").Count > 0);

        var editor = cut.Find("#add-item-textarea");
        Assert.NotNull(editor);
        Assert.Equal("true", editor.GetAttribute("contenteditable"));
        Assert.Equal("新しいアイテムのコンテンツを入力...", editor.GetAttribute("data-placeholder"));
    }

    [Fact]
    public void Input_InternalUrlInContent_DoesNotRenderBottomPreviewCards()
    {
        const string inputContent = "Check tag /TagDetail/1 and user /User/UserDetail/user1";
        IRenderedComponent<AddItem> cut = _ctx.Render<AddItem>();

        cut.WaitForState(() => cut.FindAll("form").Count > 0);
        cut.Find("textarea").Input(inputContent);

        // 内部リンクはエディタ内でインライン表示されるため、bottomの外部プレビューカードセクションには描画されない
        var previewCards = cut.FindComponents<SRNSMudApp.Components.UI.UrlPreviewCard>();
        Assert.Empty(previewCards);
    }

    [Fact]
    public async Task SearchTags_InvokesTagSearchQueryService_AndReturnsFormattedMentionItems()
    {
        var sampleTags = new List<SRNSMudApp.Data.Tag>
        {
            new()
            {
                Id = 10,
                Name = "C#",
                OwnerId = "owner-1",
                Owner = new ApplicationUser { Id = "owner-1", UserName = "Alice" }
            },
            new()
            {
                Id = 20,
                Name = "Blazor",
                OwnerId = "owner-2",
                Owner = new ApplicationUser { Id = "owner-2", UserName = "Bob" }
            },
            new()
            {
                Id = 30,
                Name = "SystemTag",
                OwnerId = "system",
                IsSystem = true
            }
        };
        _ = _tagSearchMock
            .Setup(s => s.SearchTagsWithFallbackAsync("test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleTags);

        IRenderedComponent<AddItem> cut = _ctx.Render<AddItem>();
        cut.WaitForState(() => cut.FindAll("form").Count > 0);

        var results = (await cut.Instance.SearchTags("test")).ToList();

        Assert.Equal(3, results.Count);
        Assert.Equal("C# : Alice", results[0].Name);
        Assert.Equal("/TagDetail/10", results[0].Replacement);
        Assert.Equal("Blazor : Bob", results[1].Name);
        Assert.Equal("/TagDetail/20", results[1].Replacement);
        Assert.Equal("SystemTag : system", results[2].Name);
        Assert.Equal("/TagDetail/30", results[2].Replacement);
    }

    [Fact]
    public async Task SearchUsers_InvokesUserDataProvider_AndReturnsFormattedMentionItems()
    {
        var sampleUsers = new List<ApplicationUser>
        {
            new() { Id = "user-1", UserName = "Alice" },
            new() { Id = "user-2", UserName = "Bob" }
        };
        _ = _userDataProviderMock
            .Setup(u => u.SearchUsersAsync("al", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleUsers);

        IRenderedComponent<AddItem> cut = _ctx.Render<AddItem>();
        cut.WaitForState(() => cut.FindAll("form").Count > 0);

        var results = (await cut.Instance.SearchUsers("al")).ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal("@Alice", results[0].Name);
        Assert.Equal("/User/UserDetail/user-1", results[0].Replacement);
        Assert.Equal("@Bob", results[1].Name);
        Assert.Equal("/User/UserDetail/user-2", results[1].Replacement);
    }

    [Fact]
    public async Task SubmitForm_WhenInvokedDirectly_SavesItem()
    {
        _ = _itemCardDataMock
            .Setup(d => d.CreateItemAsync(It.IsAny<SRNSMudApp.Data.Item>(), It.IsAny<IReadOnlyCollection<int>?>()))
            .Returns(Task.CompletedTask);

        IRenderedComponent<AddItem> cut = _ctx.Render<AddItem>();
        cut.WaitForState(() => cut.FindAll("form").Count > 0);

        cut.Find("textarea").Input(TestContent);

        // JS側から Cmd+Enter 等で呼ばれる SubmitForm() を直接テスト
        await cut.Instance.SubmitForm();

        _itemCardDataMock.Verify(d => d.CreateItemAsync(
            It.Is<SRNSMudApp.Data.Item>(i => i.Content == TestContent && i.OwnerId == ExistingUserId),
            It.IsAny<IReadOnlyCollection<int>?>()), Times.Once);
    }

    [Fact]
    public void Input_UserMentionInContent_UpdatesNotificationTargets_AndSavesRecipients()
    {
        var mentionedUser = new ApplicationUser { Id = "target-user-1", UserName = "TargetUser" };
        _ = _userDataProviderMock
            .Setup(u => u.GetUsersByIdsAsync(It.Is<IEnumerable<string>>(ids => ids.Contains("target-user-1"))))
            .ReturnsAsync([mentionedUser]);

        _ = _itemCardDataMock
            .Setup(d => d.CreateItemAsync(It.IsAny<SRNSMudApp.Data.Item>(), It.IsAny<IReadOnlyCollection<int>?>()))
            .Returns(Task.CompletedTask);

        IRenderedComponent<AddItem> cut = _ctx.Render<AddItem>();
        cut.WaitForState(() => cut.FindAll("form").Count > 0);

        // メンションを含むテキストを入力
        cut.Find("textarea").Input("Hello /User/UserDetail/target-user-1 please check");

        // 通知先パネルが表示されるのを待機
        cut.WaitForState(() => cut.FindAll(".mud-expand-panel").Count > 0);
        Assert.Contains("通知先 (1)", cut.Markup);

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            _itemCardDataMock.Verify(d => d.CreateItemAsync(
                It.Is<SRNSMudApp.Data.Item>(i =>
                    i.NotificationRecipients != null &&
                    i.NotificationRecipients.Count == 1 &&
                    i.NotificationRecipients.Any(r => r.RecipientUserId == "target-user-1")),
                It.IsAny<IReadOnlyCollection<int>?>()), Times.Once);
        });
    }

    [Fact]
    public void Save_WithConfirmedSuggestedTags_IncludesBothInitialAndSuggestedTagIds()
    {
        _tagSuggestionMock
            .Setup(s => s.SuggestTagsAsync(It.IsAny<string>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SRNSMudApp.Models.SuggestedTag(42, "C#", 0.90f)]);

        IReadOnlyCollection<int>? capturedTagIds = null;
        _ = _itemCardDataMock
            .Setup(d => d.CreateItemAsync(It.IsAny<SRNSMudApp.Data.Item>(), It.IsAny<IReadOnlyCollection<int>?>()))
            .Callback<SRNSMudApp.Data.Item, IReadOnlyCollection<int>?>((_, tagIds) => capturedTagIds = tagIds)
            .Returns(Task.CompletedTask);

        var initialTag = new SRNSMudApp.Data.Tag { Id = 7, Name = "InitialTag", OwnerId = ExistingUserId };

        IRenderedComponent<AddItem> cut = _ctx.Render<AddItem>(parameters => parameters
            .Add(p => p.InitialTags, [initialTag]));

        cut.WaitForState(() => cut.FindAll("form").Count > 0);
        cut.Find("textarea").Input("C# に関するメモ");

        // 提案パネルにタグが表示されるまで待機
        cut.WaitForState(() => cut.FindAll("[data-testid='strong-tag-chip-42']").Count > 0);

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(capturedTagIds);
            Assert.Contains(7, capturedTagIds);   // InitialTag
            Assert.Contains(42, capturedTagIds);  // 自動提案タグ
        });
    }

    [Fact]
    public void Initialize_WhenUserHasSavedThresholds_PassesThemToTagSuggestionPanel()
    {
        var appUser = new ApplicationUser
        {
            Id = ExistingUserId,
            UserName = "testuser",
            TagSuggestionStrongThreshold = 0.80f,
            TagSuggestionCandidateThreshold = 0.50f
        };
        _ = _userDataProviderMock
            .Setup(u => u.FindUserByIdAsync(ExistingUserId))
            .ReturnsAsync(appUser);

        IRenderedComponent<AddItem> cut = _ctx.Render<AddItem>();
        cut.WaitForState(() => cut.FindAll("form").Count > 0);

        var suggestionPanel = cut.FindComponent<TagSuggestionPanel>();
        Assert.NotNull(suggestionPanel);
        Assert.Equal(0.80f, suggestionPanel.Instance.StrongThreshold);
        Assert.Equal(0.50f, suggestionPanel.Instance.CandidateThreshold);
    }

    [Fact]
    public void ThresholdsChanged_CallsUpdateTagSuggestionThresholdsAsync()
    {
        _tagSuggestionMock
            .Setup(s => s.SuggestTagsAsync(It.IsAny<string>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SuggestedTag(1, "TestTag", 0.8f)]);

        _ = _userDataProviderMock
            .Setup(u => u.FindUserByIdAsync(ExistingUserId))
            .ReturnsAsync(new ApplicationUser { Id = ExistingUserId, UserName = "testuser" });

        _ = _userDataProviderMock
            .Setup(u => u.UpdateTagSuggestionThresholdsAsync(ExistingUserId, It.IsAny<float>(), It.IsAny<float>()))
            .Returns(Task.CompletedTask);

        IRenderedComponent<AddItem> cut = _ctx.Render<AddItem>();
        cut.WaitForState(() => cut.FindAll("form").Count > 0);

        cut.Find("textarea").Input("テストテキスト");
        cut.WaitForState(() => cut.FindAll("[data-testid='toggle-threshold-settings-button']").Count > 0);

        // パネルの設定展開ボタンを押す
        cut.Find("[data-testid='toggle-threshold-settings-button']").Click();

        // リセットボタン（またはスライダー操作）で閾値変更を発火
        var resetButton = cut.Find("[data-testid='reset-thresholds-button']");
        resetButton.Click();

        cut.WaitForAssertion(() =>
        {
            _userDataProviderMock.Verify(u => u.UpdateTagSuggestionThresholdsAsync(
                ExistingUserId,
                SuggestedTag.DefaultStrongThreshold,
                SuggestedTag.DefaultCandidateThreshold), Times.Once);
        });
    }

    [Fact]
    public void SuggestTags_WhenParentItemProvided_IncludesParentContentInQuery()
    {
        var parent = new SRNSMudApp.Data.Item
        {
            Id = 99,
            OwnerId = "parent-user",
            Content = "親アイテムの投稿内容"
        };

        _tagSuggestionMock
            .Setup(s => s.SuggestTagsAsync(It.IsAny<string>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SuggestedTag(1, "TestTag", 0.85f)]);

        IRenderedComponent<AddItem> cut = _ctx.Render<AddItem>(parameters => parameters
            .Add(p => p.ParentItem, parent));
        cut.WaitForState(() => cut.FindAll("form").Count > 0);

        cut.Find("textarea").Input("返信の投稿内容");

        cut.WaitForAssertion(() =>
        {
            _tagSuggestionMock.Verify(s => s.SuggestTagsAsync(
                It.Is<string>(q => q.Contains("親アイテムの投稿内容") && q.Contains("返信の投稿内容")),
                It.IsAny<float>(),
                It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        });
    }

    [Fact]
    public void SuggestTags_WhenParentItemProvidedInitially_TriggersSuggestionWithParentContent()
    {
        var parent = new SRNSMudApp.Data.Item
        {
            Id = 99,
            OwnerId = "parent-user",
            Content = "親アイテムの投稿内容"
        };

        _tagSuggestionMock
            .Setup(s => s.SuggestTagsAsync(It.IsAny<string>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SuggestedTag(1, "TestTag", 0.85f)]);

        IRenderedComponent<AddItem> cut = _ctx.Render<AddItem>(parameters => parameters
            .Add(p => p.ParentItem, parent));
        cut.WaitForState(() => cut.FindAll("form").Count > 0);

        cut.WaitForAssertion(() =>
        {
            _tagSuggestionMock.Verify(s => s.SuggestTagsAsync(
                "親アイテムの投稿内容",
                It.IsAny<float>(),
                It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        });
    }

    [Fact]
    public void Save_WhenParentItemProvided_SavesItemWithParentItemId()
    {
        var parent = new SRNSMudApp.Data.Item
        {
            Id = 42,
            OwnerId = "parent-user",
            RootItemId = 10,
            Content = "親アイテム"
        };

        SRNSMudApp.Data.Item? savedItem = null;
        _ = _itemCardDataMock
            .Setup(d => d.CreateItemAsync(It.IsAny<SRNSMudApp.Data.Item>(), It.IsAny<IReadOnlyCollection<int>?>()))
            .Callback<SRNSMudApp.Data.Item, IReadOnlyCollection<int>?>((item, _) => savedItem = item)
            .Returns(Task.CompletedTask);

        IRenderedComponent<AddItem> cut = _ctx.Render<AddItem>(parameters => parameters
            .Add(p => p.ParentItem, parent));
        cut.WaitForState(() => cut.FindAll("form").Count > 0);

        cut.Find("textarea").Input("リプライ本文");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(savedItem);
            Assert.Equal("リプライ本文", savedItem.Content);
            Assert.Equal(42, savedItem.ParentItemId);
            Assert.Equal(10, savedItem.RootItemId);
        });
    }

    [Fact]
    public void Save_WithLinkConversionEnabled_AppliesReplacementsBeforeSaving()
    {
        // Arrange
        var appUser = new ApplicationUser
        {
            Id = ExistingUserId,
            UserName = "testuser",
            IsLinkConversionEnabled = true, // リンク変換ON
            LinkConversionThreshold = 0.85f
        };
        _userDataProviderMock.Setup(u => u.FindUserByIdAsync(ExistingUserId)).ReturnsAsync(appUser);

        // リンク変換サービスのモック設定
        var conversionServiceMock = Mock.Get(_ctx.Services.GetRequiredService<IInternalLinkConversionService>());
        var candidates = new[] { new LinkConversionCandidate("C#", 42, "C#", 1.0f, 0, 2, true) };
        conversionServiceMock
            .Setup(s => s.DetectLinkCandidatesAsync("C# に関するメモ", 0.85f, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InternalLinkConversionResult(candidates, []));

        var capturedContent = string.Empty;
        _itemCardDataMock
            .Setup(d => d.CreateItemAsync(It.IsAny<SRNSMudApp.Data.Item>(), It.IsAny<IReadOnlyCollection<int>?>()))
            .Callback<SRNSMudApp.Data.Item, IReadOnlyCollection<int>?>((item, _) => capturedContent = item.Content)
            .Returns(Task.CompletedTask);

        IRenderedComponent<AddItem> cut = _ctx.Render<AddItem>();
        cut.WaitForState(() => cut.FindAll("form").Count > 0);

        // Act
        cut.Find("textarea").Input("C# に関するメモ");

        // リンク変換が非同期で行われるのを待つ (UI上でパネルが表示され、JS Interop に候補が渡されることを検証)
        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll("[data-testid='link-conversion-panel']"));
            conversionServiceMock.Verify(
                s => s.DetectLinkCandidatesAsync("C# に関するメモ", 0.85f, It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.Contains(
                _ctx.JSInterop.Invocations,
                inv => inv.Identifier == "tributeInterop.applyLinkCandidates");
        });

        cut.Find("form").Submit();

        // Assert
        cut.WaitForAssertion(() => Assert.Equal("C# に関するメモ", capturedContent));
    }

    [Fact]
    public void Initialize_LoadsLinkConversionSettings()
    {
        // Arrange
        var appUser = new ApplicationUser
        {
            Id = ExistingUserId,
            UserName = "testuser",
            IsLinkConversionEnabled = true,
            LinkConversionThreshold = 0.75f
        };
        _userDataProviderMock.Setup(u => u.FindUserByIdAsync(ExistingUserId)).ReturnsAsync(appUser);

        // Act
        IRenderedComponent<AddItem> cut = _ctx.Render<AddItem>();
        cut.WaitForState(() => cut.FindAll("form").Count > 0);

        // Assert
        // スイッチがONになっているか、UI上の要素で確認する
        var toggle = cut.Find("input[type='checkbox']");
        // Note: 2つ目のチェックボックスがLinkConversion用のMudSwitch (PrivateModeの次)
        var checkboxes = cut.FindAll("input[type='checkbox']");
        Assert.True(checkboxes.Count >= 2);
    }

    [Fact]
    public void Save_WhenDuplicateSubmission_ShowsSnackbarAndInvokesCreateItemOnce()
    {
        var snackbar = _ctx.Services.GetRequiredService<ISnackbar>();
        _ = _itemCardDataMock
            .Setup(d => d.CreateItemAsync(It.IsAny<SRNSMudApp.Data.Item>(), It.IsAny<IReadOnlyCollection<int>?>()))
            .Callback<SRNSMudApp.Data.Item, IReadOnlyCollection<int>?>((item, _) =>
            {
                item.IsDuplicate = true;
            })
            .Returns(Task.CompletedTask);

        IRenderedComponent<AddItem> cut = _ctx.Render<AddItem>();
        cut.WaitForState(() => cut.FindAll("form").Count > 0);
        cut.Find("textarea").Input(TestContent);

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(snackbar.ShownSnackbars, s => s.Message.ToString().Contains(SRNSMudApp.Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage));
        });
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }
}