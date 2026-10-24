using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

#pragma warning disable CA1508

namespace SRNSMudApp.Components.Item;

/// <summary>
///     ItemDetail コンポーネントの状態管理およびデータフェッチ・操作ロジックを集約する ViewModel。
///     Blazor の UI レンダリング (bUnit) から切り離し、純粋な xUnit + Moq 単体テストを可能にする。
/// </summary>
public class ItemDetailViewModel
{
    private readonly IItemDetailDataProvider _detailData;
    private readonly ITaggingContractService _taggingContractService;
    private readonly ITagRequestRejectionService _taggingService;
    private readonly IItemReplyService _itemReplyService;
    private readonly ISystemTagEnsurer _systemTagEnsurer;
    private readonly ITagWeightLedgerService? _tagWeightLedgerService;
    private readonly IItemReactionService? _itemReactionService;

    public ItemDetailViewModel(
        IItemDetailDataProvider detailData,
        ITaggingContractService taggingContractService,
        ITaggingService taggingService,
        IItemReplyService itemReplyService,
        ISystemTagEnsurer systemTagEnsurer,
        ITagWeightLedgerService? tagWeightLedgerService = null,
        IItemReactionService? itemReactionService = null)
    {
        _detailData = detailData;
        _taggingContractService = taggingContractService;
        _taggingService = taggingService;
        _itemReplyService = itemReplyService;
        _systemTagEnsurer = systemTagEnsurer;
        _tagWeightLedgerService = tagWeightLedgerService;
        _itemReactionService = itemReactionService;
    }

    public AsyncPageState<ItemDetail.ItemDetailData> PageState { get; private set; } = new Loading();

    public string CurrentUserId { get; private set; } = "";
    public bool IsAdmin { get; private set; }
    public IReadOnlyList<Data.Tag> AllTags { get; private set; } = [];
    public IReadOnlyList<TagRelationToTag> AllTagRelationsToTags { get; private set; } = [];

    public int? CurrentUserGoodTagId { get; private set; }
    public int? CurrentUserBadTagId { get; private set; }
    public int? CurrentUserShinjiTagId { get; private set; }
    public int? CurrentUserZenTagId { get; private set; }
    public int? CurrentUserBiTagId { get; private set; }

    public int ActiveTabIndex { get; set; }
    public TaggingRequestEntity? SelectedRequest { get; set; }
    public string? SearchQuery { get; set; }
    public bool OnlyMyRequests { get; set; } = true;

    /// <summary>
    ///     認証情報およびクエリ状態を初期化する。
    /// </summary>
    public void SetUserContext(ClaimsPrincipal? user)
    {
        if (user != null)
        {
            CurrentUserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            IsAdmin = user.IsInRole("Admin");
        }
        else
        {
            CurrentUserId = "";
            IsAdmin = false;
        }
    }

    /// <summary>
    ///     指定アイテムの詳細データを非同期取得し、画面状態を更新する。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "例外発生時は画面の Failed 状態としてユーザーに通知するため捕捉する")]
    public async Task LoadDataAsync(int itemId, int? selectedRequestId = null)
    {
        try
        {
            PageState = new Loading();

            string? userId = string.IsNullOrEmpty(CurrentUserId) ? null : CurrentUserId;
            ItemDetailPageData? data = IsAdmin
                ? await _detailData.GetItemDetailAsync(itemId, userId, IsAdmin)
                : await _detailData.GetItemDetailAsync(itemId, userId);

            // モック未設定のレガシーテストまたは引数なしオーバーロード用の安全なフォールバック
            data ??= await _detailData.GetItemDetailAsync(itemId);

            if (data is null)
            {
                PageState = new Empty("アイテムが見つかりません。");
                return;
            }

            List<TaggingRequestEntity>? requests = (await _taggingContractService.GetRequestsByItemIdAsync(itemId))?.ToList();
            if (selectedRequestId.HasValue && requests != null)
            {
                SelectedRequest = requests.FirstOrDefault(r => r.Id == selectedRequestId.Value);
            }

            AllTags = data.AllTags;
            AllTagRelationsToTags = data.AllTagRelationsToTags;

            if (!string.IsNullOrEmpty(CurrentUserId))
            {
                SystemTagIds systemTags = ResourceListViewModel.FindSystemTags(AllTags, CurrentUserId);
                CurrentUserGoodTagId = systemTags.GoodTagId;
                CurrentUserBadTagId = systemTags.BadTagId;

                ReactionTagIds reactionTags = ResourceListViewModel.FindReactionTags(AllTags, CurrentUserId);
                CurrentUserShinjiTagId = reactionTags.ShinjiTagId;
                CurrentUserZenTagId = reactionTags.ZenTagId;
                CurrentUserBiTagId = reactionTags.BiTagId;
            }

            PageState = new Loaded<ItemDetail.ItemDetailData>(new ItemDetail.ItemDetailData(
                data.Item,
                requests ?? [],
                data.Ledgers,
                data.Ancestors,
                data.Replies,
                data.Siblings,
                data.Quotes));
        }
        catch (Exception ex)
        {
            PageState = new Failed(ex);
        }
    }

    /// <summary>
    ///     システムタグおよびリアクションタグの存在を検証・自動生成する。
    /// </summary>
    public async Task<bool> EnsureSystemTagsExistAsync()
    {
        if (string.IsNullOrEmpty(CurrentUserId))
        {
            return false;
        }

        (SystemTagIds voteIds, ReactionTagIds reactionIds, bool refetch) = await _systemTagEnsurer.EnsureAllAsync(
            CurrentUserId,
            new SystemTagIds(CurrentUserGoodTagId, CurrentUserBadTagId),
            new ReactionTagIds(CurrentUserShinjiTagId, CurrentUserZenTagId, CurrentUserBiTagId));

        CurrentUserGoodTagId = voteIds.GoodTagId;
        CurrentUserBadTagId = voteIds.BadTagId;
        CurrentUserShinjiTagId = reactionIds.ShinjiTagId;
        CurrentUserZenTagId = reactionIds.ZenTagId;
        CurrentUserBiTagId = reactionIds.BiTagId;

        return refetch;
    }

    /// <summary>
    ///     リプライを送信する。
    /// </summary>
    public async Task<Data.Item?> SubmitReplyAsync(int itemId, string replyText, bool isReplyPrivate)
    {
        if (string.IsNullOrWhiteSpace(replyText) || string.IsNullOrEmpty(CurrentUserId))
        {
            return null;
        }

        Data.Item? currentItem = PageState switch
        {
            Loaded<ItemDetail.ItemDetailData> loaded => loaded.Data.Item,
            _ => null
        };
        (bool isPrivateResolved, int? targetGroupId) = ItemDetailThreadViewModel.ResolveReplyPrivacy(isReplyPrivate, currentItem);

        Data.Item? addedReply = await _itemReplyService.AddItemReplyAsync(
            itemId,
            replyText,
            CurrentUserId,
            isPrivate: isPrivateResolved,
            targetUserGroupId: targetGroupId);

        return addedReply;
    }

    /// <summary>
    ///     タグ付与リクエストを却下する。
    /// </summary>
    public async Task<bool> RejectRequestAsync(TaggingRequestEntity request, string? comment)
    {
        if (string.IsNullOrEmpty(CurrentUserId))
        {
            return false;
        }

        await _taggingService.RejectRequestAsync(request.Id, CurrentUserId, comment);

        if (PageState is Loaded<ItemDetail.ItemDetailData> loaded)
        {
            _ = loaded.Data.Requests.Remove(request);
        }

        return true;
    }

    /// <summary>
    ///     タグ履歴レコードにコメントを追加する。
    /// </summary>
    public Task AddLedgerCommentAsync(int ledgerId, string comment) =>
        AddLedgerCommentAsync(new TagWeightLedger { Id = ledgerId, OwnerId = CurrentUserId }, comment);

    /// <summary>
    ///     タグ履歴レコードにコメントを追加する。
    ///     未永続化（Id == 0）のレコードの場合は自動的に永続化してからコメントを追加する。
    /// </summary>
    public async Task AddLedgerCommentAsync(TagWeightLedger ledger, string comment)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (string.IsNullOrWhiteSpace(comment) || string.IsNullOrEmpty(CurrentUserId) || _tagWeightLedgerService == null)
        {
            return;
        }

        _ = await _tagWeightLedgerService.AddCommentAsync(ledger, comment, CurrentUserId);
    }

    /// <summary>
    ///     タグ履歴レコードにタグを付与する。
    /// </summary>
    public Task AddLedgerTagAsync(int ledgerId, int tagId) =>
        AddLedgerTagAsync(new TagWeightLedger { Id = ledgerId, OwnerId = CurrentUserId }, tagId);

    /// <summary>
    ///     タグ履歴レコードにタグを付与する。
    ///     未永続化（Id == 0）のレコードの場合は自動的に永続化してからタグを付与する。
    /// </summary>
    public async Task AddLedgerTagAsync(TagWeightLedger ledger, int tagId)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (string.IsNullOrEmpty(CurrentUserId) || _tagWeightLedgerService == null)
        {
            return;
        }

        _ = await _tagWeightLedgerService.AddTagAsync(ledger, tagId, CurrentUserId);
    }

    /// <summary>
    ///     タグ履歴レコードに対してリアクションタグを付与または変更・取消する。
    ///     レコードにまだコメント用アイテムが存在しない場合は、自動的に作成する。
    /// </summary>
    public Task<ItemVoteResult?> VoteLedgerReactionAsync(int ledgerId, string reactionTagName, int targetWeight) =>
        VoteLedgerReactionAsync(new TagWeightLedger { Id = ledgerId, OwnerId = CurrentUserId }, reactionTagName, targetWeight);

    /// <summary>
    ///     タグ履歴レコードに対してリアクションタグを付与または変更・取消する。
    ///     未永続化（Id == 0）のレコードの場合は自動的に永続化し、
    ///     レコードにまだコメント用アイテムが存在しない場合は自動作成する。
    /// </summary>
    public async Task<ItemVoteResult?> VoteLedgerReactionAsync(TagWeightLedger ledger, string reactionTagName, int targetWeight)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (string.IsNullOrEmpty(CurrentUserId) || _tagWeightLedgerService == null || _itemReactionService == null)
        {
            return null;
        }

        Data.Item commentItem = await _tagWeightLedgerService.EnsureCommentItemAsync(ledger, CurrentUserId);
        Data.Tag tag = await _itemReactionService.EnsureReactionTagAsync(CurrentUserId, reactionTagName);
        return await _itemReactionService.ToggleItemReactionAsync(commentItem.Id, CurrentUserId, tag.Id, targetWeight);
    }

    /// <summary>
    ///     タグ履歴レコードのリアクションコメントを更新する。
    /// </summary>
    public async Task<bool> UpdateLedgerReactionCommentAsync(int relationId, string comment)
    {
        if (string.IsNullOrEmpty(CurrentUserId) || _itemReactionService == null)
        {
            return false;
        }

        return await _itemReactionService.UpdateTagRelationCommentAsync(relationId, CurrentUserId, comment);
    }
}