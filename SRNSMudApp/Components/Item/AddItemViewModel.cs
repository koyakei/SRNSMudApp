using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json.Serialization;

using SRNSMudApp.Client.Services;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

namespace SRNSMudApp.Components.Item;

using Item = SRNSMudApp.Data.Item;
using Tag = SRNSMudApp.Data.Tag;
using UserGroup = SRNSMudApp.Data.UserGroup;

/// <summary>
///     Tribute.js の補完候補アイテム。
/// </summary>
public record MentionItem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("replacement")] string Replacement);

/// <summary>
///     AddItem コンポーネントの状態管理、データロード、タグ提案、リンク変換、投稿ロジックを集約する ViewModel。
///     UI（Blazor / bUnit）から切り離し、純粋な xUnit + Moq 単体テストを可能にする。
///     ItemVisibility や ItemPostDraft などの型表現により、
///     公開範囲や投稿下書きのドメイン制約を型安全に管理する。
/// </summary>
public class AddItemViewModel
{
    public const int MaxContentLength = 1000;

    private readonly IItemCardDataProvider _itemCardData;
    private readonly IUserGroupDataProvider _userGroupData;
    private readonly IUserDataProvider _userDataProvider;
    private readonly ILinkPreviewService _linkPreviewService;
    private readonly ITagSearchQueryService _tagSearchQueryService;
    private readonly ITagSuggestionService _tagSuggestionService;
    private readonly IInternalLinkConversionService _linkConversionService;
    private readonly IDoubleSubmissionGuard _submissionGuard;

    public AddItemViewModel(
        IItemCardDataProvider itemCardData,
        IUserGroupDataProvider userGroupData,
        IUserDataProvider userDataProvider,
        ILinkPreviewService linkPreviewService,
        ITagSearchQueryService tagSearchQueryService,
        ITagSuggestionService tagSuggestionService,
        IInternalLinkConversionService linkConversionService,
        IDoubleSubmissionGuard? submissionGuard = null)
    {
        _itemCardData = itemCardData ?? throw new ArgumentNullException(nameof(itemCardData));
        _userGroupData = userGroupData ?? throw new ArgumentNullException(nameof(userGroupData));
        _userDataProvider = userDataProvider ?? throw new ArgumentNullException(nameof(userDataProvider));
        _linkPreviewService = linkPreviewService ?? throw new ArgumentNullException(nameof(linkPreviewService));
        _tagSearchQueryService = tagSearchQueryService ?? throw new ArgumentNullException(nameof(tagSearchQueryService));
        _tagSuggestionService = tagSuggestionService ?? throw new ArgumentNullException(nameof(tagSuggestionService));
        _linkConversionService = linkConversionService ?? throw new ArgumentNullException(nameof(linkConversionService));
        _submissionGuard = submissionGuard ?? new DoubleSubmissionGuard();
    }

    public bool IsSubmitting => _submissionGuard.IsSubmitting;

    public Item? NewItem { get; set; }
    public Item? ParentItem { get; set; }
    public Item? LoadedParentItem { get; private set; }
    public Item? EffectiveParentItem => ParentItem ?? LoadedParentItem;
    public int? ParentItemId { get; set; }

    public bool IsPrivate { get; set; }
    public int? SelectedGroupId { get; set; }
    public IReadOnlyList<UserGroup> UserGroups { get; private set; } = [];

    public IReadOnlyList<ReplyTargetCandidate> TargetCandidates { get; private set; } = [];
    public HashSet<string> SelectedTargetUserIds { get; } = [];
    public HashSet<string> UnselectedTargetUserIds { get; } = [];
    public bool HasManuallyModifiedTargets { get; set; }

    public IReadOnlyList<SuggestedTag> SuggestedTags { get; set; } = [];
    public HashSet<int> ConfirmedSuggestedTagIds { get; } = [];
    public bool IsLoadingSuggestions { get; set; }
    public float CurrentCandidateThreshold { get; set; } = SuggestedTag.DefaultCandidateThreshold;
    public float? UserStrongThreshold { get; set; }
    public float? UserCandidateThreshold { get; set; }

    public bool IsLinkConversionEnabled { get; set; }
    public float LinkConversionThreshold { get; set; } = LinkConversionCandidate.DefaultAutoReplaceThreshold;
    public IReadOnlyList<LinkConversionCandidate> LinkAutoReplaceCandidates { get; set; } = [];
    public IReadOnlyList<LinkConversionCandidate> LinkManualCandidates { get; set; } = [];
    public IReadOnlyList<LinkConversionCandidate> ConfirmedLinkCandidates { get; set; } = [];
    public bool IsLoadingLinkCandidates { get; set; }

    public string CurrentUserId { get; private set; } = string.Empty;

    public bool CanSubmitCurrent => CanSubmit(NewItem?.Content);

    /// <summary>
    ///     ログインユーザーおよび親アイテム情報から状態を初期化する。
    /// </summary>
    public async Task InitializeAsync(ClaimsPrincipal user, Item? parentItem = null, int? parentItemId = null)
    {
        ParentItem = parentItem;
        ParentItemId = parentItemId;
        CurrentUserId = string.Empty;

        if (user.Identity is { IsAuthenticated: true })
        {
            CurrentUserId = user.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
            if (!string.IsNullOrEmpty(CurrentUserId))
            {
                UserGroups = await _userGroupData.GetUserGroupsForUserAsync(CurrentUserId);
                var appUser = await _userDataProvider.FindUserByIdAsync(CurrentUserId);
                if (appUser != null)
                {
                    var initialVisibility = DetermineInitialVisibility(EffectiveParentItem, appUser);
                    IsPrivate = initialVisibility.IsPrivate;
                    SelectedGroupId = initialVisibility.TargetUserGroupId;

                    if (appUser.TagSuggestionStrongThreshold.HasValue)
                    {
                        UserStrongThreshold = appUser.TagSuggestionStrongThreshold.Value;
                    }

                    if (appUser.TagSuggestionCandidateThreshold.HasValue)
                    {
                        UserCandidateThreshold = appUser.TagSuggestionCandidateThreshold.Value;
                        CurrentCandidateThreshold = UserCandidateThreshold.Value;
                    }

                    IsLinkConversionEnabled = appUser.IsLinkConversionEnabled;
                    if (appUser.LinkConversionThreshold.HasValue)
                    {
                        LinkConversionThreshold = appUser.LinkConversionThreshold.Value;
                    }
                }
            }
        }

        await LoadParentItemIfNeededAsync();
        ResetItem();
    }

    /// <summary>
    ///     親アイテムIDが渡されており ParentItem がロードされていない場合にロードする。
    /// </summary>
    public async Task LoadParentItemIfNeededAsync()
    {
        if (ParentItem == null && ParentItemId.HasValue && (LoadedParentItem == null || LoadedParentItem.Id != ParentItemId.Value))
        {
            LoadedParentItem = await _itemCardData.GetItemByIdAsync(ParentItemId.Value);
        }

        if (EffectiveParentItem is { IsPrivate: true })
        {
            var visibility = DetermineInitialVisibility(EffectiveParentItem, null);
            IsPrivate = visibility.IsPrivate;
            SelectedGroupId = visibility.TargetUserGroupId;
            if (NewItem != null)
            {
                NewItem.IsPrivate = IsPrivate;
                NewItem.TargetUserGroupId = SelectedGroupId;
            }
        }
    }

    /// <summary>
    ///     投稿用 Item オブジェクトおよびタグ・リンク候補をリセットする。
    /// </summary>
    public void ResetItem()
    {
        var parentId = EffectiveParentItem?.Id ?? ParentItemId;
        var rootId = EffectiveParentItem?.RootItemId ?? parentId;
        NewItem = CreateInitialItem(CurrentUserId, IsPrivate, SelectedGroupId, parentId, rootId);

        SuggestedTags = [];
        ConfirmedSuggestedTagIds.Clear();
        LinkAutoReplaceCandidates = [];
        LinkManualCandidates = [];
        ConfirmedLinkCandidates = [];
    }

    /// <summary>
    ///     本文中のメンションから通知先候補ユーザーを解決・更新する。
    /// </summary>
    public async Task UpdateTargetCandidatesAsync()
    {
        if (string.IsNullOrWhiteSpace(NewItem?.Content))
        {
            TargetCandidates = [];
            SelectedTargetUserIds.Clear();
            return;
        }

        var userIdsInText = ExtractMentionedUserIds(NewItem.Content, CurrentUserId);
        if (userIdsInText.Count == 0)
        {
            TargetCandidates = [];
            SelectedTargetUserIds.Clear();
            return;
        }

        var users = await _userDataProvider.GetUsersByIdsAsync(userIdsInText);
        TargetCandidates = (users ?? []).Select(u => new ReplyTargetCandidate(u.Id, u.UserName!)).ToList();

        SyncSelectedTargets(
            TargetCandidates.Select(c => c.Id),
            HasManuallyModifiedTargets,
            SelectedTargetUserIds,
            UnselectedTargetUserIds);
    }

    /// <summary>
    ///     通知先ユーザーの選択状態を手動トグルする。
    /// </summary>
    public void ApplyTargetToggle(string userId, bool isSelected)
    {
        HasManuallyModifiedTargets = true;
        ApplyTargetToggle(userId, isSelected, SelectedTargetUserIds, UnselectedTargetUserIds);
    }

    /// <summary>
    ///     タグ提案の閾値設定を更新し永続化する。
    /// </summary>
    public async Task UpdateTagSuggestionThresholdsAsync(float strong, float candidate)
    {
        UserStrongThreshold = strong;
        UserCandidateThreshold = candidate;
        CurrentCandidateThreshold = candidate;

        if (!string.IsNullOrEmpty(CurrentUserId))
        {
            await _userDataProvider.UpdateTagSuggestionThresholdsAsync(CurrentUserId, strong, candidate);
        }
    }

    /// <summary>
    ///     タグ提案を取得する。
    /// </summary>
    public async Task<IReadOnlyList<SuggestedTag>> SuggestTagsAsync(string queryText, float threshold, CancellationToken ct)
    {
        return await _tagSuggestionService.SuggestTagsAsync(queryText, threshold, ct);
    }

    /// <summary>
    ///     内部リンク候補を検出する。
    /// </summary>
    public async Task<InternalLinkConversionResult> DetectLinkCandidatesAsync(string content, float threshold, CancellationToken ct)
    {
        return await _linkConversionService.DetectLinkCandidatesAsync(content, threshold, ct);
    }

    /// <summary>
    ///     内部リンク変換の設定を更新し永続化する。
    /// </summary>
    public async Task UpdateLinkConversionSettingsAsync(float threshold)
    {
        LinkConversionThreshold = threshold;
        if (!string.IsNullOrEmpty(CurrentUserId))
        {
            await _userDataProvider.UpdateLinkConversionSettingsAsync(CurrentUserId, IsLinkConversionEnabled, threshold);
        }
    }

    /// <summary>
    ///     指定された URL のリンクプレビューを取得する。
    /// </summary>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "Blazor コンポーネントおよび ILinkPreviewService の仕様に合わせて string を受け取るため")]
    public Task<LinkPreviewData> GetPreviewAsync(string url) => _linkPreviewService.GetPreviewAsync(url);

    /// <summary>
    ///     Tribute.js 用にタグを部分一致検索する。
    /// </summary>
    public async Task<IEnumerable<MentionItem>> SearchTagsAsync(string query)
    {
        var tags = await _tagSearchQueryService.SearchTagsWithFallbackAsync(query);
        return tags.Select(FormatTagMentionItem);
    }

    /// <summary>
    ///     Tribute.js 用にユーザーを部分一致検索する。
    /// </summary>
    public async Task<IEnumerable<MentionItem>> SearchUsersAsync(string query)
    {
        var users = await _userDataProvider.SearchUsersAsync(query);
        return users.Select(FormatUserMentionItem);
    }

    /// <summary>
    ///     アイテムの投稿を保存する。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch generic exception types",
        Justification = "Blazor UI にエラーメッセージを表示するため汎用例外を捕捉する")]
    public async Task<SaveItemResult> SaveItemAsync(IEnumerable<Tag>? initialTags)
    {
        if (NewItem == null || !CanSubmit(NewItem.Content))
        {
            return new SaveItemResult(false, "入力内容を確認してください。");
        }

        var parentId = EffectiveParentItem?.Id ?? ParentItemId;
        var rootId = EffectiveParentItem?.RootItemId ?? parentId;
        var operationKey = ComputeItemSubmissionKey(
            CurrentUserId,
            NewItem.Content,
            parentId,
            rootId,
            IsPrivate,
            SelectedGroupId);

        if (!_submissionGuard.TryBeginSubmit(operationKey))
        {
            return new SaveItemResult(false, DoubleSubmissionGuard.DuplicateSubmissionMessage, IsDuplicate: true);
        }

        try
        {
            var initialIds = initialTags?.Select(t => t.Id) ?? [];

            (Item itemToSave, IReadOnlyList<int> allTagIds) = PrepareItemForSave(
                NewItem.Content,
                NewItem.OwnerId,
                IsPrivate,
                SelectedGroupId,
                SelectedTargetUserIds,
                initialIds,
                ConfirmedSuggestedTagIds,
                parentId,
                rootId);

            await _itemCardData.CreateItemAsync(itemToSave, allTagIds);

            if (itemToSave.IsDuplicate)
            {
                _submissionGuard.Complete(operationKey);
                ResetItem();
                return new SaveItemResult(false, DoubleSubmissionGuard.DuplicateSubmissionMessage, IsDuplicate: true);
            }

            _submissionGuard.Complete(operationKey);
            ResetItem();
            return new SaveItemResult(true, null);
        }
        catch (Exception ex)
        {
            _submissionGuard.Reset();
            return new SaveItemResult(false, ex.Message);
        }
    }

    /// <summary>
    ///     二重送信防止用の操作キーを生成する。
    /// </summary>
    public static string ComputeItemSubmissionKey(
        string? userId,
        string? content,
        int? parentItemId,
        int? rootItemId,
        bool isPrivate,
        int? selectedGroupId) =>
        $"item:{userId ?? ""}:{content?.Trim() ?? ""}:{parentItemId}:{rootItemId}:{isPrivate}:{selectedGroupId}";

    // --- 既存の静的メソッド群（ドメインロジック / 後方互換） ---

    /// <summary>
    ///     コンテンツが送信可能（空文字でなく、かつ文字数上限以下）かどうかを判定する。
    /// </summary>
    public static bool CanSubmit(string? content) =>
        !string.IsNullOrWhiteSpace(content) && content.Length <= MaxContentLength;

    /// <summary>
    ///     コンテンツが最大文字数（1000文字）を超えているかどうかを判定する。
    /// </summary>
    public static bool IsContentTooLong(string? content) =>
        (content?.Length ?? 0) > MaxContentLength;

    /// <summary>
    ///     初期化時のデフォルト Item オブジェクトを作成する。
    /// </summary>
    public static Item CreateInitialItem(
        string userId,
        ItemVisibility visibility,
        int? parentItemId = null,
        int? rootItemId = null) =>
        new()
        {
            Content = string.Empty,
            OwnerId = userId,
            IsPrivate = visibility.IsPrivate,
            TargetUserGroupId = visibility.TargetUserGroupId,
            ParentItemId = parentItemId,
            RootItemId = rootItemId ?? parentItemId
        };

    /// <summary>
    ///     初期化時のデフォルト Item オブジェクトを作成する（bool / int? 互換オーバーロード）。
    /// </summary>
    public static Item CreateInitialItem(
        string userId,
        bool isPrivateDefault,
        int? defaultGroupId,
        int? parentItemId = null,
        int? rootItemId = null) =>
        CreateInitialItem(userId, ItemVisibility.FromBooleans(isPrivateDefault, defaultGroupId), parentItemId, rootItemId);

    /// <summary>
    ///     親アイテムやユーザー設定に基づいて、新規作成・リプライ時の初期公開範囲を決定する。
    ///     親アイテムがプライベートモードの場合、親アイテムと同一のスコープ（グループ限定またはフォロワー限定）をデフォルトとする。
    ///     親アイテムがない、または公開の場合は、ユーザーのデフォルト設定に従う。
    /// </summary>
    public static ItemVisibility DetermineInitialVisibility(Item? parentItem, ApplicationUser? currentUser)
    {
        if (parentItem is { IsPrivate: true })
        {
            return ItemVisibility.FromBooleans(true, parentItem.TargetUserGroupId);
        }

        return currentUser is { IsPrivateModeDefault: true }
            ? ItemVisibility.FromBooleans(true, currentUser.DefaultPrivateUserGroupId)
            : ItemVisibility.Public();
    }

    /// <summary>
    ///     テキスト中の /User/UserDetail/{userId} 形式の URL からメンション先ユーザーIDを抽出する。
    ///     ログイン中のユーザー自身は宛先から除外される。
    /// </summary>
    public static IReadOnlyList<string> ExtractMentionedUserIds(string? content, string? currentUserId)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var urls = ItemCardViewModel.ExtractUrls(content);
        var userIdsInText = urls
            .Where(u => u.StartsWith("/User/UserDetail/", StringComparison.OrdinalIgnoreCase))
            .Select(u => u["/User/UserDetail/".Length..])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

        if (!string.IsNullOrEmpty(currentUserId))
        {
            userIdsInText.Remove(currentUserId);
        }

        return userIdsInText;
    }

    /// <summary>
    ///     手動トグル操作を反映した通知先ユーザーIDセットを計算する。
    /// </summary>
    public static void ApplyTargetToggle(
        string userId,
        bool isSelected,
        HashSet<string> selectedTargetUserIds,
        HashSet<string> unselectedTargetUserIds)
    {
        if (isSelected)
        {
            selectedTargetUserIds.Add(userId);
            unselectedTargetUserIds.Remove(userId);
        }
        else
        {
            selectedTargetUserIds.Remove(userId);
            unselectedTargetUserIds.Add(userId);
        }
    }

    /// <summary>
    ///     メンション候補リストが更新された際に、選択状態を同期する。
    /// </summary>
    public static void SyncSelectedTargets(
        IEnumerable<string> candidateUserIds,
        bool hasManuallyModified,
        HashSet<string> selectedTargetUserIds,
        IReadOnlySet<string> unselectedTargetUserIds)
    {
        foreach (var candidateId in candidateUserIds)
        {
            if (hasManuallyModified)
            {
                if (unselectedTargetUserIds.Contains(candidateId))
                {
                    selectedTargetUserIds.Remove(candidateId);
                }
                else
                {
                    selectedTargetUserIds.Add(candidateId);
                }
            }
            else
            {
                // デフォルト: 選択
                selectedTargetUserIds.Add(candidateId);
            }
        }
    }

    /// <summary>
    ///     保存用の ItemPostDraft ドメインモデルを作成する。
    /// </summary>
    public static ItemPostDraft CreateDraft(
        string content,
        string ownerId,
        ItemVisibility visibility,
        IEnumerable<string>? selectedTargetUserIds,
        IEnumerable<int>? initialTagIds,
        IEnumerable<int>? confirmedSuggestedTagIds,
        int? parentItemId = null,
        int? rootItemId = null)
    {
        var recipients = (selectedTargetUserIds ?? []).Distinct().ToList();
        var initial = initialTagIds ?? [];
        var suggested = confirmedSuggestedTagIds ?? [];
        var allTagIds = initial.Concat(suggested).Distinct().ToList();

        return new ItemPostDraft(content, ownerId, visibility, recipients, allTagIds, parentItemId, rootItemId);
    }

    /// <summary>
    ///     保存用の Item エンティティおよび付与する全タグIDリストを構築する。
    ///     ItemPostDraft を経由してドメインルールを適用する。
    /// </summary>
    public static (Item ItemToSave, IReadOnlyList<int> AllTagIds) PrepareItemForSave(
        string content,
        string ownerId,
        ItemVisibility visibility,
        IEnumerable<string>? selectedTargetUserIds,
        IEnumerable<int>? initialTagIds,
        IEnumerable<int>? confirmedSuggestedTagIds,
        int? parentItemId = null,
        int? rootItemId = null)
    {
        ItemPostDraft draft = CreateDraft(
            content, ownerId, visibility, selectedTargetUserIds, initialTagIds, confirmedSuggestedTagIds, parentItemId, rootItemId);

        return (draft.ToItemEntity(), draft.TagIds);
    }

    /// <summary>
    ///     レガシーな bool / int? 引数から ItemPostDraft を生成して保存用データを構築する互換オーバーロード。
    /// </summary>
    public static (Item ItemToSave, IReadOnlyList<int> AllTagIds) PrepareItemForSave(
        string content,
        string ownerId,
        bool isPrivate,
        int? selectedGroupId,
        IEnumerable<string>? selectedTargetUserIds,
        IEnumerable<int>? initialTagIds,
        IEnumerable<int>? confirmedSuggestedTagIds,
        int? parentItemId = null,
        int? rootItemId = null) =>
        PrepareItemForSave(
            content,
            ownerId,
            ItemVisibility.FromBooleans(isPrivate, selectedGroupId),
            selectedTargetUserIds,
            initialTagIds,
            confirmedSuggestedTagIds,
            parentItemId,
            rootItemId);

    /// <summary>
    ///     関連タグ提案の参考テキスト（クエリ文字列）を構築する。
    ///     リプライの場合は親アイテムの本文も含める。
    /// </summary>
    public static string BuildTagSuggestionQueryText(string? currentContent, string? parentContent)
    {
        var hasParent = !string.IsNullOrWhiteSpace(parentContent);
        var hasCurrent = !string.IsNullOrWhiteSpace(currentContent);

        return (hasParent, hasCurrent) switch
        {
            (true, true) => $"{parentContent!.Trim()}\n{currentContent!.Trim()}",
            (true, false) => parentContent!.Trim(),
            (false, true) => currentContent!.Trim(),
            _ => string.Empty
        };
    }

    /// <summary>
    ///     関連タグ提案の参考テキスト（クエリ文字列）を構築する（Item エンティティ オーバーロード）。
    /// </summary>
    public static string BuildTagSuggestionQueryText(string? currentContent, Item? parentItem) =>
        BuildTagSuggestionQueryText(currentContent, parentItem?.Content);

    /// <summary>
    ///     タグ検索結果を Tribute.js 用の MentionItem へ整形する。
    /// </summary>
    public static MentionItem FormatTagMentionItem(Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        var ownerName = tag.Owner?.UserName
            ?? (tag.IsSystem || tag.OwnerId == "system" ? "system" : (!string.IsNullOrEmpty(tag.OwnerId) ? tag.OwnerId : "unknown"));
        return new MentionItem($"{tag.Name} : {ownerName}", $"/TagDetail/{tag.Id}");
    }

    /// <summary>
    ///     ユーザー検索結果を Tribute.js 用の MentionItem へ整形する。
    /// </summary>
    public static MentionItem FormatUserMentionItem(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new MentionItem("@" + user.UserName, $"/User/UserDetail/{user.Id}");
    }
}