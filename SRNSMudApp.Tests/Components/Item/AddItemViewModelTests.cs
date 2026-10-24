namespace SRNSMudApp.Tests.Components.Item;

using System.Security.Claims;

using Moq;

using SRNSMudApp.Components.Item;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

using Item = SRNSMudApp.Data.Item;
using Tag = SRNSMudApp.Data.Tag;
using UserGroup = SRNSMudApp.Data.UserGroup;

/// <summary>
///     AddItemViewModel の純粋な単体テスト。
///     ItemVisibility（型安全な公開範囲）、ItemPostDraft（保存データ）、
///     およびメンション抽出・通知先同期・バリデーションの振る舞いを検証する。
/// </summary>
public class AddItemViewModelTests
{
    [Fact]
    public void ItemVisibility_EnforcesScopeConstraintsAtTypeLevel()
    {
        // PublicScope: グループIDを持てない
        ItemVisibility publicScope = ItemVisibility.Public();
        Assert.False(publicScope.IsPrivate);
        Assert.Null(publicScope.TargetUserGroupId);

        // PrivateFollowersScope: フォロワー限定
        ItemVisibility followersScope = ItemVisibility.PrivateFollowers();
        Assert.True(followersScope.IsPrivate);
        Assert.Null(followersScope.TargetUserGroupId);

        // PrivateGroupScope: 特定グループ限定
        ItemVisibility groupScope = ItemVisibility.PrivateGroup(42);
        Assert.True(groupScope.IsPrivate);
        Assert.Equal(42, groupScope.TargetUserGroupId);

        // FromBooleans によるレガシー引数からの型変換
        Assert.IsType<PublicItemScope>(ItemVisibility.FromBooleans(false, 42)); // 非プライベートならGroupIdは無視
        Assert.IsType<PrivateGroupItemScope>(ItemVisibility.FromBooleans(true, 42));
        Assert.IsType<PrivateFollowersItemScope>(ItemVisibility.FromBooleans(true, null));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("新体道をやる時に哲学を", true)]
    public void CanSubmit_ValidatesContentCorrectly(string? content, bool expected)
    {
        var result = AddItemViewModel.CanSubmit(content);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CanSubmit_WhenExceedingMaxLength_ReturnsFalse()
    {
        var tooLongContent = new string('あ', AddItemViewModel.MaxContentLength + 1);
        Assert.False(AddItemViewModel.CanSubmit(tooLongContent));
        Assert.True(AddItemViewModel.IsContentTooLong(tooLongContent));

        var exactMaxContent = new string('あ', AddItemViewModel.MaxContentLength);
        Assert.True(AddItemViewModel.CanSubmit(exactMaxContent));
        Assert.False(AddItemViewModel.IsContentTooLong(exactMaxContent));
    }

    [Fact]
    public void CreateInitialItem_WithItemVisibility_SetsFieldsCorrectly()
    {
        var item = AddItemViewModel.CreateInitialItem("user-1", ItemVisibility.PrivateGroup(42));

        Assert.Equal(string.Empty, item.Content);
        Assert.Equal("user-1", item.OwnerId);
        Assert.True(item.IsPrivate);
        Assert.Equal(42, item.TargetUserGroupId);
    }

    [Fact]
    public void ExtractMentionedUserIds_ExtractsUserIdsAndExcludesCurrentUser()
    {
        var content = "新体道 /User/UserDetail/shintaido_sensei と 哲学 /User/UserDetail/tetsugaku_fan そして /User/UserDetail/me";
        var result = AddItemViewModel.ExtractMentionedUserIds(content, "me");

        Assert.Equal(2, result.Count);
        Assert.Contains("shintaido_sensei", result);
        Assert.Contains("tetsugaku_fan", result);
        Assert.DoesNotContain("me", result);
    }

    [Fact]
    public void ExtractMentionedUserIds_WithNoMentionsOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(AddItemViewModel.ExtractMentionedUserIds("", "me"));
        Assert.Empty(AddItemViewModel.ExtractMentionedUserIds(null, "me"));
        Assert.Empty(AddItemViewModel.ExtractMentionedUserIds("新体道をやる時に哲学を", "me"));
    }

    [Fact]
    public void ApplyTargetToggle_UpdatesSelectedAndUnselectedSets()
    {
        HashSet<string> selected = ["shintaido_sensei"];
        HashSet<string> unselected = [];

        // メンション先のチェックを外す
        AddItemViewModel.ApplyTargetToggle("shintaido_sensei", false, selected, unselected);
        Assert.DoesNotContain("shintaido_sensei", selected);
        Assert.Contains("shintaido_sensei", unselected);

        // メンション先を再度チェックする
        AddItemViewModel.ApplyTargetToggle("shintaido_sensei", true, selected, unselected);
        Assert.Contains("shintaido_sensei", selected);
        Assert.DoesNotContain("shintaido_sensei", unselected);
    }

    [Fact]
    public void SyncSelectedTargets_WhenNotManuallyModified_SelectsAll()
    {
        List<string> candidates = ["shintaido_sensei", "tetsugaku_fan"];
        HashSet<string> selected = [];
        HashSet<string> unselected = [];

        AddItemViewModel.SyncSelectedTargets(candidates, false, selected, unselected);

        Assert.Equal(2, selected.Count);
        Assert.Contains("shintaido_sensei", selected);
        Assert.Contains("tetsugaku_fan", selected);
    }

    [Fact]
    public void SyncSelectedTargets_WhenManuallyModified_RespectsUnselectedSet()
    {
        List<string> candidates = ["shintaido_sensei", "tetsugaku_fan", "aikido_fan"];
        HashSet<string> selected = ["shintaido_sensei"];
        HashSet<string> unselected = ["tetsugaku_fan"];

        AddItemViewModel.SyncSelectedTargets(candidates, true, selected, unselected);

        Assert.Contains("shintaido_sensei", selected);
        Assert.DoesNotContain("tetsugaku_fan", selected);
        Assert.Contains("aikido_fan", selected); // 新たに追加されたメンションはデフォルト選択
    }

    [Fact]
    public void CreateDraft_And_ToItemEntity_GeneratesEntityWithRecipientsAndMergedTags()
    {
        var content = "新体道をやる時に哲学を";
        var ownerId = "author-1";
        ItemVisibility visibility = ItemVisibility.PrivateGroup(99);
        List<string> selectedRecipients = ["shintaido_sensei", "tetsugaku_fan"];
        List<int> initialTags = [10, 20]; // 10: 新体道, 20: 哲学
        List<int> suggestedTags = [20, 30]; // 20: 哲学 (重複), 30: 瞑想

        ItemPostDraft draft = AddItemViewModel.CreateDraft(
            content,
            ownerId,
            visibility,
            selectedRecipients,
            initialTags,
            suggestedTags);

        Assert.Equal(content, draft.Content);
        Assert.Equal(ownerId, draft.OwnerId);
        Assert.Equal(visibility, draft.Visibility);
        Assert.Equal([10, 20, 30], draft.TagIds);

        Item entity = draft.ToItemEntity();
        Assert.Equal(content, entity.Content);
        Assert.True(entity.IsPrivate);
        Assert.Equal(99, entity.TargetUserGroupId);
        Assert.NotNull(entity.NotificationRecipients);
        Assert.Equal(2, entity.NotificationRecipients.Count);
        Assert.Contains(entity.NotificationRecipients, r => r.RecipientUserId == "shintaido_sensei");
        Assert.Contains(entity.NotificationRecipients, r => r.RecipientUserId == "tetsugaku_fan");
    }

    [Fact]
    public void PrepareItemForSave_WhenPublic_ClearsTargetGroupIdAtTypeLevel()
    {
        (Item item, _) = AddItemViewModel.PrepareItemForSave(
            "新体道をやる時に哲学を（全体公開）",
            "author-1",
            ItemVisibility.Public(),
            selectedTargetUserIds: [],
            initialTagIds: [],
            confirmedSuggestedTagIds: []);

        Assert.False(item.IsPrivate);
        Assert.Null(item.TargetUserGroupId);
    }

    [Fact]
    public void FormatTagMentionItem_FormatsTagWithUserOrSystemCorrectly()
    {
        var userTag = new Tag
        {
            Id = 10,
            Name = "新体道",
            OwnerId = "user-1",
            Owner = new ApplicationUser { Id = "user-1", UserName = "Koyanagi" }
        };

        var systemTag = new Tag
        {
            Id = 20,
            Name = "good",
            OwnerId = "system",
            IsSystem = true
        };

        var itemUser = AddItemViewModel.FormatTagMentionItem(userTag);
        Assert.Equal("新体道 : Koyanagi", itemUser.Name);
        Assert.Equal("/TagDetail/10", itemUser.Replacement);

        var itemSystem = AddItemViewModel.FormatTagMentionItem(systemTag);
        Assert.Equal("good : system", itemSystem.Name);
        Assert.Equal("/TagDetail/20", itemSystem.Replacement);
    }

    [Fact]
    public void FormatUserMentionItem_FormatsUserCorrectly()
    {
        var user = new ApplicationUser { Id = "u-42", UserName = "Koyanagi" };
        var item = AddItemViewModel.FormatUserMentionItem(user);

        Assert.Equal("@Koyanagi", item.Name);
        Assert.Equal("/User/UserDetail/u-42", item.Replacement);
    }

    [Theory]
    [InlineData("親アイテムの本文", "返信の本文", "親アイテムの本文\n返信の本文")]
    [InlineData("  親アイテムの本文  ", "  返信の本文  ", "親アイテムの本文\n返信の本文")]
    [InlineData("親アイテムの本文", null, "親アイテムの本文")]
    [InlineData("親アイテムの本文", "", "親アイテムの本文")]
    [InlineData("親アイテムの本文", "   ", "親アイテムの本文")]
    [InlineData(null, "返信の本文", "返信の本文")]
    [InlineData("", "返信の本文", "返信の本文")]
    [InlineData("   ", "返信の本文", "返信の本文")]
    [InlineData(null, null, "")]
    [InlineData("", "", "")]
    [InlineData("   ", "   ", "")]
    public void BuildTagSuggestionQueryText_FormatsQueryCorrectly(string? parentContent, string? currentContent, string expected)
    {
        var result = AddItemViewModel.BuildTagSuggestionQueryText(currentContent, parentContent);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void BuildTagSuggestionQueryText_WithItemEntity_ExtractsParentContent()
    {
        var parentItem = new Item { Content = "哲学と武道の関係", OwnerId = "parent-user" };
        var result = AddItemViewModel.BuildTagSuggestionQueryText("新体道における技法", parentItem);
        Assert.Equal("哲学と武道の関係\n新体道における技法", result);

        var nullParentResult = AddItemViewModel.BuildTagSuggestionQueryText("新体道における技法", (Item?)null);
        Assert.Equal("新体道における技法", nullParentResult);
    }

    [Fact]
    public void CreateDraft_And_PrepareItemForSave_PreservesParentItemIdAndRootItemId()
    {
        // Act
        var draft = AddItemViewModel.CreateDraft(
            "返信テスト",
            "user-1",
            ItemVisibility.Public(),
            ["user-2"],
            [1],
            [2],
            parentItemId: 100,
            rootItemId: 50);

        (Item itemToSave, IReadOnlyList<int> tagIds) = AddItemViewModel.PrepareItemForSave(
            "返信テスト",
            "user-1",
            ItemVisibility.Public(),
            ["user-2"],
            [1],
            [2],
            parentItemId: 100,
            rootItemId: 50);

        // Assert
        Assert.Equal(100, draft.ParentItemId);
        Assert.Equal(50, draft.RootItemId);

        Assert.Equal(100, itemToSave.ParentItemId);
        Assert.Equal(50, itemToSave.RootItemId);
        Assert.Equal("返信テスト", itemToSave.Content);
        Assert.Contains(1, tagIds);
        Assert.Contains(2, tagIds);
    }

    [Fact]
    public void CreateInitialItem_WithParentAndRootItemId_SetsPropertiesCorrectly()
    {
        var item = AddItemViewModel.CreateInitialItem("user-1", ItemVisibility.Public(), parentItemId: 77, rootItemId: 11);
        Assert.Equal(77, item.ParentItemId);
        Assert.Equal(11, item.RootItemId);

        // rootItemId 省略時は parentItemId がデフォルト
        var itemDefaultRoot = AddItemViewModel.CreateInitialItem("user-1", ItemVisibility.Public(), parentItemId: 77);
        Assert.Equal(77, itemDefaultRoot.ParentItemId);
        Assert.Equal(77, itemDefaultRoot.RootItemId);
    }

    [Fact]
    public void DetermineInitialVisibility_WhenParentItemIsPrivateWithGroup_ReturnsPrivateGroupWithSameGroupId()
    {
        var parentItem = new Item
        {
            Id = 10,
            OwnerId = "owner-1",
            IsPrivate = true,
            TargetUserGroupId = 42
        };
        var currentUser = new ApplicationUser { Id = "user-1", IsPrivateModeDefault = false };

        ItemVisibility result = AddItemViewModel.DetermineInitialVisibility(parentItem, currentUser);

        Assert.True(result.IsPrivate);
        Assert.Equal(42, result.TargetUserGroupId);
        var groupScope = Assert.IsType<PrivateGroupItemScope>(result);
        Assert.Equal(42, groupScope.GroupId);
    }

    [Fact]
    public void DetermineInitialVisibility_WhenParentItemIsPrivateWithFollowersOnly_ReturnsPrivateFollowers()
    {
        var parentItem = new Item
        {
            Id = 10,
            OwnerId = "owner-1",
            IsPrivate = true,
            TargetUserGroupId = null
        };
        var currentUser = new ApplicationUser { Id = "user-1", IsPrivateModeDefault = false };

        ItemVisibility result = AddItemViewModel.DetermineInitialVisibility(parentItem, currentUser);

        Assert.True(result.IsPrivate);
        Assert.Null(result.TargetUserGroupId);
        Assert.IsType<PrivateFollowersItemScope>(result);
    }

    [Fact]
    public void DetermineInitialVisibility_WhenParentItemIsPublic_UsesCurrentUserDefaultSetting()
    {
        var parentItem = new Item
        {
            Id = 10,
            OwnerId = "owner-1",
            IsPrivate = false,
            TargetUserGroupId = null
        };
        var userWithPrivateGroup = new ApplicationUser
        {
            Id = "user-1",
            IsPrivateModeDefault = true,
            DefaultPrivateUserGroupId = 99
        };

        ItemVisibility result = AddItemViewModel.DetermineInitialVisibility(parentItem, userWithPrivateGroup);

        Assert.True(result.IsPrivate);
        Assert.Equal(99, result.TargetUserGroupId);

        var normalUser = new ApplicationUser
        {
            Id = "user-2",
            IsPrivateModeDefault = false
        };
        ItemVisibility publicResult = AddItemViewModel.DetermineInitialVisibility(parentItem, normalUser);
        Assert.False(publicResult.IsPrivate);
        Assert.IsType<PublicItemScope>(publicResult);
    }

    [Fact]
    public void DetermineInitialVisibility_WhenParentItemIsNull_UsesCurrentUserDefaultSetting()
    {
        var userWithPrivateFollowers = new ApplicationUser
        {
            Id = "user-1",
            IsPrivateModeDefault = true,
            DefaultPrivateUserGroupId = null
        };

        ItemVisibility result = AddItemViewModel.DetermineInitialVisibility(null, userWithPrivateFollowers);
        Assert.True(result.IsPrivate);
        Assert.Null(result.TargetUserGroupId);
        Assert.IsType<PrivateFollowersItemScope>(result);

        ItemVisibility nullUserResult = AddItemViewModel.DetermineInitialVisibility(null, null);
        Assert.False(nullUserResult.IsPrivate);
        Assert.IsType<PublicItemScope>(nullUserResult);
    }

    // --- インスタンス版 AddItemViewModel のテスト ---

    private readonly Mock<IItemCardDataProvider> _itemCardDataMock = new();
    private readonly Mock<IUserGroupDataProvider> _userGroupDataMock = new();
    private readonly Mock<IUserDataProvider> _userDataProviderMock = new();
    private readonly Mock<ILinkPreviewService> _linkPreviewServiceMock = new();
    private readonly Mock<ITagSearchQueryService> _tagSearchQueryServiceMock = new();
    private readonly Mock<ITagSuggestionService> _tagSuggestionServiceMock = new();
    private readonly Mock<IInternalLinkConversionService> _linkConversionServiceMock = new();

    private AddItemViewModel CreateSut() =>
        new(
            _itemCardDataMock.Object,
            _userGroupDataMock.Object,
            _userDataProviderMock.Object,
            _linkPreviewServiceMock.Object,
            _tagSearchQueryServiceMock.Object,
            _tagSuggestionServiceMock.Object,
            _linkConversionServiceMock.Object);

    private static ClaimsPrincipal CreatePrincipal(string? userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task InitializeAsync_WhenAuthenticatedUser_LoadsGroupsAndSettings()
    {
        var sut = CreateSut();
        var user = CreatePrincipal("user-1");
        var groups = new List<UserGroup> { new() { Id = 1, Name = "Group1", OwnerId = "user-1" } };
        var appUser = new ApplicationUser
        {
            Id = "user-1",
            IsPrivateModeDefault = true,
            DefaultPrivateUserGroupId = 1,
            TagSuggestionStrongThreshold = 0.85f,
            TagSuggestionCandidateThreshold = 0.65f,
            IsLinkConversionEnabled = true,
            LinkConversionThreshold = 0.75f
        };

        _userGroupDataMock.Setup(m => m.GetUserGroupsForUserAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(groups);
        _userDataProviderMock.Setup(m => m.FindUserByIdAsync("user-1"))
            .ReturnsAsync(appUser);

        await sut.InitializeAsync(user);

        Assert.Equal("user-1", sut.CurrentUserId);
        Assert.Equal(groups, sut.UserGroups);
        Assert.True(sut.IsPrivate);
        Assert.Equal(1, sut.SelectedGroupId);
        Assert.Equal(0.85f, sut.UserStrongThreshold);
        Assert.Equal(0.65f, sut.UserCandidateThreshold);
        Assert.Equal(0.65f, sut.CurrentCandidateThreshold);
        Assert.True(sut.IsLinkConversionEnabled);
        Assert.Equal(0.75f, sut.LinkConversionThreshold);
        Assert.NotNull(sut.NewItem);
        Assert.Equal("user-1", sut.NewItem.OwnerId);
    }

    [Fact]
    public async Task InitializeAsync_WhenUnauthenticated_SetsEmptyDefaults()
    {
        var sut = CreateSut();
        var user = CreatePrincipal(null);

        await sut.InitializeAsync(user);

        Assert.Equal(string.Empty, sut.CurrentUserId);
        Assert.Empty(sut.UserGroups);
        Assert.False(sut.IsPrivate);
        Assert.NotNull(sut.NewItem);
        Assert.Equal(string.Empty, sut.NewItem.OwnerId);
    }

    [Fact]
    public async Task LoadParentItemIfNeededAsync_WhenParentIdProvided_LoadsParentAndInheritsPrivate()
    {
        var sut = CreateSut();
        var parentItem = new Item
        {
            Id = 99,
            Content = "Parent content",
            OwnerId = "other-user",
            IsPrivate = true,
            TargetUserGroupId = 5
        };

        _itemCardDataMock.Setup(m => m.GetItemByIdAsync(99))
            .ReturnsAsync(parentItem);

        sut.ParentItemId = 99;
        await sut.LoadParentItemIfNeededAsync();

        Assert.Equal(parentItem, sut.LoadedParentItem);
        Assert.Equal(parentItem, sut.EffectiveParentItem);
        Assert.True(sut.IsPrivate);
        Assert.Equal(5, sut.SelectedGroupId);
    }

    [Fact]
    public async Task UpdateTargetCandidatesAsync_ExtractsMentionsAndResolvesUsers()
    {
        var sut = CreateSut();
        await sut.InitializeAsync(CreatePrincipal("me"));
        sut.NewItem!.Content = "Hello /User/UserDetail/target-1 and /User/UserDetail/me";

        var targetUser = new ApplicationUser { Id = "target-1", UserName = "target_user" };
        _userDataProviderMock.Setup(m => m.GetUsersByIdsAsync(It.Is<IEnumerable<string>>(ids => ids.Contains("target-1"))))
            .ReturnsAsync([targetUser]);

        await sut.UpdateTargetCandidatesAsync();

        Assert.Single(sut.TargetCandidates);
        Assert.Equal("target-1", sut.TargetCandidates[0].Id);
        Assert.Contains("target-1", sut.SelectedTargetUserIds);
        Assert.DoesNotContain("me", sut.SelectedTargetUserIds);
    }

    [Fact]
    public async Task UpdateTargetCandidatesAsync_WhenContentEmpty_ClearsCandidates()
    {
        var sut = CreateSut();
        await sut.InitializeAsync(CreatePrincipal("me"));
        sut.NewItem!.Content = string.Empty;

        await sut.UpdateTargetCandidatesAsync();

        Assert.Empty(sut.TargetCandidates);
        Assert.Empty(sut.SelectedTargetUserIds);
    }

    [Fact]
    public void ApplyTargetToggle_UpdatesSelectionAndManualFlag()
    {
        var sut = CreateSut();
        Assert.False(sut.HasManuallyModifiedTargets);

        sut.ApplyTargetToggle("u1", true);
        Assert.True(sut.HasManuallyModifiedTargets);
        Assert.Contains("u1", sut.SelectedTargetUserIds);
        Assert.DoesNotContain("u1", sut.UnselectedTargetUserIds);

        sut.ApplyTargetToggle("u1", false);
        Assert.DoesNotContain("u1", sut.SelectedTargetUserIds);
        Assert.Contains("u1", sut.UnselectedTargetUserIds);
    }

    [Fact]
    public async Task UpdateTagSuggestionThresholdsAsync_UpdatesPropertiesAndCallsProvider()
    {
        var sut = CreateSut();
        await sut.InitializeAsync(CreatePrincipal("user-1"));

        await sut.UpdateTagSuggestionThresholdsAsync(0.9f, 0.7f);

        Assert.Equal(0.9f, sut.UserStrongThreshold);
        Assert.Equal(0.7f, sut.UserCandidateThreshold);
        Assert.Equal(0.7f, sut.CurrentCandidateThreshold);
        _userDataProviderMock.Verify(m => m.UpdateTagSuggestionThresholdsAsync("user-1", 0.9f, 0.7f), Times.Once);
    }

    [Fact]
    public async Task SuggestTagsAsync_DelegatesToService()
    {
        var sut = CreateSut();
        var expected = new List<SuggestedTag> { new(1, "Tag1", 0.8f) };
        _tagSuggestionServiceMock.Setup(s => s.SuggestTagsAsync("test", 0.5f, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await sut.SuggestTagsAsync("test", 0.5f, CancellationToken.None);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task DetectLinkCandidatesAsync_DelegatesToService()
    {
        var sut = CreateSut();
        var expected = new InternalLinkConversionResult([], []);
        _linkConversionServiceMock.Setup(s => s.DetectLinkCandidatesAsync("content", 0.8f, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await sut.DetectLinkCandidatesAsync("content", 0.8f, CancellationToken.None);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task UpdateLinkConversionSettingsAsync_UpdatesPropertyAndCallsProvider()
    {
        var sut = CreateSut();
        await sut.InitializeAsync(CreatePrincipal("user-1"));
        sut.IsLinkConversionEnabled = true;

        await sut.UpdateLinkConversionSettingsAsync(0.85f);

        Assert.Equal(0.85f, sut.LinkConversionThreshold);
        _userDataProviderMock.Verify(m => m.UpdateLinkConversionSettingsAsync("user-1", true, 0.85f), Times.Once);
    }

    [Fact]
    public async Task GetPreviewAsync_DelegatesToService()
    {
        var sut = CreateSut();
        var preview = new LinkPreviewData { Url = "https://example.com", Title = "Example" };
        _linkPreviewServiceMock.Setup(p => p.GetPreviewAsync("https://example.com"))
            .ReturnsAsync(preview);

        var result = await sut.GetPreviewAsync("https://example.com");

        Assert.Equal(preview, result);
    }

    [Fact]
    public async Task SearchTagsAsync_ReturnsFormattedMentionItems()
    {
        var sut = CreateSut();
        var tags = new List<Tag> { new() { Id = 10, Name = "TestTag", OwnerId = "system", IsSystem = true } };
        _tagSearchQueryServiceMock.Setup(s => s.SearchTagsWithFallbackAsync("query"))
            .ReturnsAsync(tags);

        var result = (await sut.SearchTagsAsync("query")).ToList();

        Assert.Single(result);
        Assert.Equal("TestTag : system", result[0].Name);
        Assert.Equal("/TagDetail/10", result[0].Replacement);
    }

    [Fact]
    public async Task SearchUsersAsync_ReturnsFormattedMentionItems()
    {
        var sut = CreateSut();
        var users = new List<ApplicationUser> { new() { Id = "u1", UserName = "alice" } };
        _userDataProviderMock.Setup(u => u.SearchUsersAsync("al"))
            .ReturnsAsync(users);

        var result = (await sut.SearchUsersAsync("al")).ToList();

        Assert.Single(result);
        Assert.Equal("@alice", result[0].Name);
        Assert.Equal("/User/UserDetail/u1", result[0].Replacement);
    }

    [Fact]
    public async Task SaveItemAsync_WhenValid_CallsCreateItemAndReturnsSuccess()
    {
        var sut = CreateSut();
        await sut.InitializeAsync(CreatePrincipal("user-1"));
        sut.NewItem!.Content = "Valid content";

        var initialTags = new List<Tag> { new() { Id = 101, Name = "Tag101", OwnerId = "user-1" } };
        sut.ConfirmedSuggestedTagIds.Add(202);

        var (success, errorMessage) = await sut.SaveItemAsync(initialTags);

        Assert.True(success);
        Assert.Null(errorMessage);
        _itemCardDataMock.Verify(m => m.CreateItemAsync(
            It.Is<Item>(i => i.Content == "Valid content" && i.OwnerId == "user-1"),
            It.Is<IReadOnlyList<int>>(tags => tags.Contains(101) && tags.Contains(202))),
            Times.Once);
        Assert.Equal(string.Empty, sut.NewItem.Content);
    }

    [Fact]
    public async Task SaveItemAsync_WhenContentEmpty_ReturnsFalse()
    {
        var sut = CreateSut();
        await sut.InitializeAsync(CreatePrincipal("user-1"));
        sut.NewItem!.Content = string.Empty;

        var (success, errorMessage) = await sut.SaveItemAsync(null);

        Assert.False(success);
        Assert.NotNull(errorMessage);
        _itemCardDataMock.Verify(m => m.CreateItemAsync(It.IsAny<Item>(), It.IsAny<IReadOnlyList<int>>()), Times.Never);
    }

    [Fact]
    public async Task SaveItemAsync_WhenProviderThrows_ReturnsFalseWithExceptionMessage()
    {
        var sut = CreateSut();
        await sut.InitializeAsync(CreatePrincipal("user-1"));
        sut.NewItem!.Content = "Valid content";

        _itemCardDataMock.Setup(m => m.CreateItemAsync(It.IsAny<Item>(), It.IsAny<IReadOnlyList<int>>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        var (success, errorMessage) = await sut.SaveItemAsync(null);

        Assert.False(success);
        Assert.Equal("DB error", errorMessage);
    }

    [Fact]
    public async Task SaveItemAsync_WhenConcurrentOrRapidCalls_ReturnsDuplicateResultAndCallsProviderOnce()
    {
        var sut = CreateSut();
        await sut.InitializeAsync(CreatePrincipal("user-1"));
        sut.NewItem!.Content = "Valid content";

        var tcs = new TaskCompletionSource<bool>();
        _itemCardDataMock.Setup(m => m.CreateItemAsync(It.IsAny<Item>(), It.IsAny<IReadOnlyCollection<int>?>()))
            .Returns(tcs.Task);

        // 1回目の送信を開始（非同期処理中）
        var firstSaveTask = sut.SaveItemAsync(null);

        // 2回目の送信を即座に呼び出し（ガードにより抑止される）
        var secondResult = await sut.SaveItemAsync(null);

        Assert.False(secondResult.Success);
        Assert.True(secondResult.IsDuplicate);
        Assert.Equal(SRNSMudApp.Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, secondResult.ErrorMessage);

        // 1回目の処理を完了
        tcs.SetResult(true);
        var firstResult = await firstSaveTask;

        Assert.True(firstResult.Success);
        Assert.False(firstResult.IsDuplicate);

        _itemCardDataMock.Verify(m => m.CreateItemAsync(It.IsAny<Item>(), It.IsAny<IReadOnlyCollection<int>?>()), Times.Once);
    }

    [Fact]
    public async Task SaveItemAsync_WhenProviderMarksDuplicate_ReturnsSaveItemResultWithIsDuplicateTrue()
    {
        var sut = CreateSut();
        await sut.InitializeAsync(CreatePrincipal("user-1"));
        sut.NewItem!.Content = "Duplicate on server";

        _itemCardDataMock.Setup(m => m.CreateItemAsync(It.IsAny<Item>(), It.IsAny<IReadOnlyCollection<int>?>()))
            .Callback<Item, IReadOnlyCollection<int>?>((item, _) =>
            {
                item.Id = 123;
                item.IsDuplicate = true;
            })
            .Returns(Task.CompletedTask);

        var result = await sut.SaveItemAsync(null);

        Assert.False(result.Success);
        Assert.True(result.IsDuplicate);
        Assert.Equal(SRNSMudApp.Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, result.ErrorMessage);
    }
}