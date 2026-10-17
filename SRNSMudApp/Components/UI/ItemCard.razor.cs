using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

using MudBlazor;

using SRNSMudApp.Components.Contract;
using SRNSMudApp.Components.Item;
using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Resources;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;

// IDE0010 / IDE0072: union 型・enum の網羅的 switch に対する「Populate switch」は、
// 全ケース列挙済み・default 併記済みでも解消されない解析器の誤検知のため抑制する。
#pragma warning disable IDE0010, IDE0072

namespace SRNSMudApp.Components.UI;

/// <summary>
///     ItemCard のコードビハインド。
///     マークアップ (.razor) 側は表示のみを担い、各操作は専用のコーディネーター
///     (<see cref="IItemCardVoteCoordinator" />, <see cref="IItemCardSplitCoordinator" />, <see cref="IItemCardTagCoordinator" />)
///     へ委譲して UI オーケストレーションに専念する（SRP 準拠）。
///     純粋な計算は <see cref="ItemCardViewModel" /> へ。
/// </summary>
public partial class ItemCard : IAsyncDisposable
{
    [Inject] private IItemCardVoteCoordinator VoteCoordinator { get; set; } = null!;
    [Inject] private IItemCardSplitCoordinator SplitCoordinator { get; set; } = null!;
    [Inject] private IItemCardTagCoordinator TagCoordinator { get; set; } = null!;
    [Inject] private IDialogLauncher DialogLauncher { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private ItemCardActionViewModel ActionViewModel { get; set; } = null!;
    [Inject] private ILinkPreviewService PreviewService { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;
    [CascadingParameter] private Task<AuthenticationState>? AuthStateTask { get; set; }

    [Parameter][EditorRequired] public Data.Item Item { get; set; } = null!;
    [Parameter] public EventCallback OnDataChanged { get; set; }
    [Parameter] public bool IsFocused { get; set; }
    [Parameter] public EventCallback<int> OnFocus { get; set; }
    /// <summary>カードクリック時のコールバック。未指定時は OnFocus にフォールバックする。</summary>
    [Parameter] public EventCallback<int> OnCardClick { get; set; }
    [Parameter] public bool EnableNavigation { get; set; } = true;
    [Parameter] public bool IsAdmin { get; set; }
    [Parameter] public IReadOnlyList<Data.Tag> AllTags { get; set; } = [];
    [Parameter] public IReadOnlyList<TagRelationToTag> AllTagRelationsToTags { get; set; } = [];
    [Parameter] public string CurrentUserId { get; set; } = "";
    [Parameter] public int? CurrentUserGoodTagId { get; set; }
    [Parameter] public int? CurrentUserBadTagId { get; set; }
    [Parameter] public int? CurrentUserShinjiTagId { get; set; }
    [Parameter] public int? CurrentUserZenTagId { get; set; }
    [Parameter] public int? CurrentUserBiTagId { get; set; }
    [Parameter] public EventCallback OnEnsureSystemTags { get; set; }
    [Parameter] public IReadOnlyList<TimelineEvent>? HighlightEvents { get; set; }

    /// <summary>
    ///     リプライ 1 件の描画テンプレート (ネストした ItemCard)。
    ///     ItemReplyThread への描画委譲に使用する。
    /// </summary>
    private RenderFragment<Data.Item> ReplyTemplate => reply => builder =>
    {
        builder.OpenComponent<ItemCard>(0);
        builder.AddAttribute(1, nameof(Item), reply);
        builder.AddAttribute(2, nameof(CurrentUserId), CurrentUserId);
        builder.AddAttribute(3, nameof(CurrentUserGoodTagId), CurrentUserGoodTagId);
        builder.AddAttribute(4, nameof(CurrentUserBadTagId), CurrentUserBadTagId);
        builder.AddAttribute(5, nameof(CurrentUserShinjiTagId), CurrentUserShinjiTagId);
        builder.AddAttribute(6, nameof(CurrentUserZenTagId), CurrentUserZenTagId);
        builder.AddAttribute(7, nameof(CurrentUserBiTagId), CurrentUserBiTagId);
        builder.AddAttribute(8, nameof(OnDataChanged),
            EventCallback.Factory.Create(this, LoadRepliesAsync));
        builder.AddAttribute(9, nameof(AllTags), AllTags);
        builder.AddAttribute(10, nameof(AllTagRelationsToTags), AllTagRelationsToTags);
        builder.AddAttribute(11, nameof(HighlightEvents), HighlightEvents);
        builder.AddAttribute(12, nameof(OnEnsureSystemTags), OnEnsureSystemTags);
        builder.AddAttribute(13, nameof(EnableNavigation), true);
        builder.AddAttribute(14, nameof(IsAdmin), _isAdmin);
        builder.CloseComponent();
    };

    private DotNetObjectReference<ItemCard>? _dotNetRef;
    private IReadOnlyList<TaggingRequestEntity> _taggingRequests = [];
    private IReadOnlyList<ItemSplitRequest> _pendingSplitRequests = [];
    private int _loadedItemId;
    private DateTime? _loadedItemUpdatedDate;
    private string? _loadedItemContent;
    private bool _isAdmin;

    private int _replyCount;
    private int _quoteCount;
    private bool _isRepliesExpanded;
    private HashSet<string> _optedOutUserIds = [];
    private IReadOnlyList<Data.Item> _replies = [];
    private string _newReplyContent = "";
    private bool _isSubmittingReply;
    private bool _isReplyPrivate;
    private HashSet<string> _selectedTargetUserIds = [];
    private readonly HashSet<string> _unselectedTargetUserIds = [];
    private bool _hasManuallyModifiedTargets;

    protected override async Task OnParametersSetAsync()
    {
        if (IsAdmin)
        {
            _isAdmin = true;
        }
        else if (AuthStateTask is not null)
        {
            AuthenticationState authState = await AuthStateTask;
            _isAdmin = authState.User.IsInRole("Admin");
        }

        if (_loadedItemId == Item.Id &&
            _loadedItemUpdatedDate == Item.UpdatedDate &&
            _loadedItemContent == Item.Content &&
            Item.Id > 0)
        {
            return;
        }

        _loadedItemId = Item.Id;
        _loadedItemUpdatedDate = Item.UpdatedDate;
        _loadedItemContent = Item.Content;
        _isReplyPrivate = Item.IsPrivate;
        _taggingRequests = await ActionViewModel.GetTaggingRequestsAsync(Item.Id);
        _pendingSplitRequests = await ActionViewModel.GetPendingSplitRequestsAsync(Item.Id);
        _quoteCount = await ActionViewModel.GetQuoteCountAsync(Item.Id);
        if (_isRepliesExpanded)
        {
            await LoadRepliesAsync();
        }
        else
        {
            _replyCount = await ActionViewModel.GetItemReplyCountAsync(Item.Id, Item.Replies?.Count ?? 0);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await (firstRender switch
        {
            true => HandleFirstRenderAsync(),
            false => Task.CompletedTask
        });
    }

    /// <summary>スクロールによるフォーカス検知 (IntersectionObserver) を初期化する。
    /// オーバーフロー検知は ItemCardContent 子コンポーネント側に集約。</summary>
    private async Task HandleFirstRenderAsync()
    {
        _dotNetRef = DotNetObjectReference.Create(this);
        try
        {
            await JS.InvokeVoidAsync("contentOverflowHelper.init", _dotNetRef);
        }
        catch (JSException)
        {
            // ignored
        }

        try
        {
            await JS.InvokeVoidAsync("contentOverflowHelper.initScrollObserver");
        }
        catch (JSException)
        {
            // ignored
        }

        try
        {
            await JS.InvokeVoidAsync("contentOverflowHelper.observeElements", $"#item-card-{Item.Id}");
        }
        catch (JSException)
        {
            // ignored
        }
    }

    [JSInvokable]
    public void OnElementFocusedByScroll(string elementId)
    {
        if (elementId == $"item-card-{Item.Id}")
        {
            _ = OnFocus.InvokeAsync(Item.Id);
        }
    }

    private async Task HandleCardClickAsync()
    {
        if (OnCardClick.HasDelegate)
        {
            await OnCardClick.InvokeAsync(Item.Id);
        }
        else if (OnFocus.HasDelegate)
        {
            await OnFocus.InvokeAsync(Item.Id);
        }
    }

    private string GetItemCardStyle() => ItemCardViewModel.GetItemCardStyle(IsFocused);

    // --- Tagging Contract Request Alerts ---
    private async Task CancelTaggingRequestAsync()
    {
        if (await ActionViewModel.CancelTaggingRequestAsync(Item.AsRequestOf, CurrentUserId))
        {
            await NotifyDataChangedAsync();
        }
    }

    private async Task ApproveTaggingRequestAsync()
    {
        if (await ActionViewModel.ApproveTaggingRequestAsync(Item.AsRequestOf, CurrentUserId))
        {
            await NotifyDataChangedAsync();
        }
    }

    // --- Reply Management ---
    private async Task ToggleRepliesAsync()
    {
        _isRepliesExpanded = !_isRepliesExpanded;
        await (_isRepliesExpanded switch
        {
            true => LoadRepliesAsync(),
            false => Task.CompletedTask
        });
    }

    private async Task LoadRepliesAsync()
    {
        _replies = await ActionViewModel.GetItemRepliesAsync(Item.Id);
        _replyCount = _replies.Count;
        var rootId = Item.RootItemId ?? Item.Id;
        _optedOutUserIds = await ActionViewModel.GetOptedOutUsersAsync(rootId);
        SyncSelectedTargets(GetReplyTargetCandidates());
    }

    private List<ReplyTargetCandidate> GetReplyTargetCandidates()
    {
        List<ReplyTargetCandidate> candidates = [];
        HashSet<string> seen = [];

        if (!string.IsNullOrEmpty(Item.OwnerId) && Item.OwnerId != CurrentUserId)
        {
            seen.Add(Item.OwnerId);
            candidates.Add(new ReplyTargetCandidate(Item.OwnerId, Item.Owner?.UserName ?? "オーナー"));
        }

        foreach (var reply in _replies)
        {
            if (!string.IsNullOrEmpty(reply.OwnerId) && reply.OwnerId != CurrentUserId &&
                seen.Add(reply.OwnerId))
            {
                candidates.Add(new ReplyTargetCandidate(reply.OwnerId, reply.Owner?.UserName ?? "ユーザー"));
            }
        }

        if (!string.IsNullOrWhiteSpace(_newReplyContent))
        {
            var urls = ItemCardViewModel.ExtractUrls(_newReplyContent);
            var mentionedUserIds = urls
                .Where(u => u.StartsWith("/User/UserDetail/", StringComparison.OrdinalIgnoreCase))
                .Select(u => u.Substring("/User/UserDetail/".Length))
                .Distinct();

            foreach (var uid in mentionedUserIds)
            {
                if (uid != CurrentUserId && seen.Add(uid))
                {
                    // For performance, we could look it up, but for now we just show a generic name if they are not in the replies list.
                    // Wait, we should look them up. Let's do it synchronously from a cache, or just use their ID as a placeholder?
                    // Actually, if we just use "@ユーザー", it's fine.
                    candidates.Add(new ReplyTargetCandidate(uid, "メンション先"));
                }
            }
        }

        return candidates;
    }

    private void SyncSelectedTargets(List<ReplyTargetCandidate> candidates)
    {
        if (!_hasManuallyModifiedTargets)
        {
            _selectedTargetUserIds = [.. candidates.Where(c => !_optedOutUserIds.Contains(c.Id)).Select(c => c.Id)];
        }
        else
        {
            foreach (var candidate in candidates)
            {
                if (!_unselectedTargetUserIds.Contains(candidate.Id) && !_selectedTargetUserIds.Contains(candidate.Id))
                {
                    _selectedTargetUserIds.Add(candidate.Id);
                }
            }
        }
    }

    private Task HandleTargetUserToggled((string UserId, bool IsSelected) args)
    {
        _hasManuallyModifiedTargets = true;
        if (args.IsSelected)
        {
            _selectedTargetUserIds.Add(args.UserId);
            _unselectedTargetUserIds.Remove(args.UserId);
        }
        else
        {
            _selectedTargetUserIds.Remove(args.UserId);
            _unselectedTargetUserIds.Add(args.UserId);
        }
        return Task.CompletedTask;
    }

    private async Task HandleNewReplyContentChanged(string value)
    {
        _newReplyContent = value;
        SyncSelectedTargets(GetReplyTargetCandidates());
        await Task.CompletedTask;
    }

    private Task HandlePrivateReplyChanged(bool isPrivate)
    {
        _isReplyPrivate = isPrivate;
        return Task.CompletedTask;
    }

    private async Task SubmitReplyAsync()
    {
        if (string.IsNullOrWhiteSpace(_newReplyContent) || string.IsNullOrEmpty(CurrentUserId))
        {
            return;
        }

        _isSubmittingReply = true;
        try
        {
            Data.Item? addedReply = await ActionViewModel.SubmitReplyAsync(
                Item,
                _newReplyContent,
                CurrentUserId,
                _isReplyPrivate,
                _selectedTargetUserIds);

            if (addedReply is not null)
            {
                _newReplyContent = "";
                _hasManuallyModifiedTargets = false;
                _unselectedTargetUserIds.Clear();
                _isReplyPrivate = Item.IsPrivate;
                _isRepliesExpanded = true;
                await LoadRepliesAsync();
            }
        }
        finally
        {
            _isSubmittingReply = false;
        }
    }

    // --- Voting & Reaction Logic (Delegated to VoteCoordinator) ---
    private int GetItemScore() => ItemCardViewModel.GetItemScore(Item.TagRelations);

    private bool IsItemUpvoted() => ItemCardViewModel.IsItemUpvoted(Item.TagRelations, CurrentUserId, CurrentUserGoodTagId);

    private bool IsItemDownvoted() => ItemCardViewModel.IsItemDownvoted(Item.TagRelations, CurrentUserId, CurrentUserGoodTagId);

    private async Task UpvoteItemAsync() => await ToggleItemVoteAsync(true);

    private async Task DownvoteItemAsync() => await ToggleItemVoteAsync(false);

    private async Task ToggleItemVoteAsync(bool isUpvote)
    {
        Func<Task>? ensureAsync = OnEnsureSystemTags.HasDelegate ? OnEnsureSystemTags.InvokeAsync : null;
        var success = await VoteCoordinator.ToggleVoteAsync(Item.Id, CurrentUserId, CurrentUserGoodTagId, isUpvote, ensureAsync);
        if (success)
        {
            await NotifyDataChangedAsync();
        }
    }

    private int GetReactionScore(string reactionTagName)
        => ItemCardViewModel.GetReactionScore(Item.TagRelations, reactionTagName);

    private bool IsItemReactionUpvoted(string reactionTagName) => reactionTagName switch
    {
        ReactionTagNames.Shinji => ItemCardViewModel.IsItemReactionUpvoted(Item.TagRelations, CurrentUserId, CurrentUserShinjiTagId, ReactionTagNames.Shinji),
        ReactionTagNames.Zen => ItemCardViewModel.IsItemReactionUpvoted(Item.TagRelations, CurrentUserId, CurrentUserZenTagId, ReactionTagNames.Zen),
        ReactionTagNames.Bi => ItemCardViewModel.IsItemReactionUpvoted(Item.TagRelations, CurrentUserId, CurrentUserBiTagId, ReactionTagNames.Bi),
        _ => false
    };

    private bool IsItemReactionDownvoted(string reactionTagName) => reactionTagName switch
    {
        ReactionTagNames.Shinji => ItemCardViewModel.IsItemReactionDownvoted(Item.TagRelations, CurrentUserId, CurrentUserShinjiTagId, ReactionTagNames.Shinji),
        ReactionTagNames.Zen => ItemCardViewModel.IsItemReactionDownvoted(Item.TagRelations, CurrentUserId, CurrentUserZenTagId, ReactionTagNames.Zen),
        ReactionTagNames.Bi => ItemCardViewModel.IsItemReactionDownvoted(Item.TagRelations, CurrentUserId, CurrentUserBiTagId, ReactionTagNames.Bi),
        _ => false
    };

    private async Task VoteReactionAsync((string ReactionTagName, int TargetWeight) args)
    {
        (string reactionTagName, int targetWeight) = args;
        var reactionTagId = reactionTagName switch
        {
            ReactionTagNames.Shinji => CurrentUserShinjiTagId,
            ReactionTagNames.Zen => CurrentUserZenTagId,
            ReactionTagNames.Bi => CurrentUserBiTagId,
            _ => null
        };

        if (string.IsNullOrEmpty(CurrentUserId))
        {
            _ = Snackbar.Add(ErrorMessages.LoginRequired, Severity.Warning);
            return;
        }

        bool isCurrentlyUpvoted = IsItemReactionUpvoted(reactionTagName);
        bool isCurrentlyDownvoted = IsItemReactionDownvoted(reactionTagName);
        bool isNewAddition = !isCurrentlyUpvoted && !isCurrentlyDownvoted;

        Func<Task>? ensureAsync = OnEnsureSystemTags.HasDelegate ? OnEnsureSystemTags.InvokeAsync : null;
        ItemVoteResult? voteResult = await VoteCoordinator.ToggleReactionAsync(
            Item.Id, CurrentUserId, reactionTagName, targetWeight, reactionTagId, AllTags, ensureAsync);

        if (voteResult is null)
        {
            return;
        }

        await NotifyDataChangedAsync();

        if (isNewAddition && voteResult.Action == ItemVoteAction.Added)
        {
            var parameters = new DialogParameters
            {
                [nameof(ReactionCommentDialog.ReactionTagName)] = reactionTagName
            };
            var options = new DialogOptions
            {
                CloseOnEscapeKey = true,
                MaxWidth = MaxWidth.Small,
                FullWidth = true,
                Position = DialogPosition.BottomRight,
                CloseButton = true,
                BackgroundClass = "reaction-toast-dialog-overlay"
            };
            IDialogReference dialog = await DialogLauncher.ShowAsync<ReactionCommentDialog>("リアクションを追加", parameters, options);
            DialogResult? result = await dialog.Result;
            if (result is not null && !result.Canceled && result.Data is ReactionCommentDialogResult { Saved: true } dialogResult)
            {
                if (!string.IsNullOrWhiteSpace(dialogResult.Comment))
                {
                    bool updated = await VoteCoordinator.UpdateReactionCommentAsync(voteResult.RelationId, CurrentUserId, dialogResult.Comment);
                    if (updated)
                    {
                        await NotifyDataChangedAsync();
                    }
                }
            }
        }
    }

    // --- Quote Dialogs ---
    private async Task OpenQuoteDialogAsync()
    {
        if (string.IsNullOrEmpty(CurrentUserId))
        {
            _ = Snackbar.Add("ログインが必要です。", Severity.Warning);
            return;
        }

        // 他人がownerのアイテムで本文の一部が選択されている場合、分割リクエスト兼引用投稿フローを実行する
        if (!((IOwnable)Item).IsOwnedBy(CurrentUserId))
        {
            string? selectedText = null;
            try
            {
                selectedText = await JS.InvokeAsync<string>("selectionHelper.getSelectedText");
            }
            catch (JSException)
            {
                // ignored
            }

            if (!string.IsNullOrWhiteSpace(selectedText))
            {
                string reqNormalizedContent = (Item.Content ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
                string reqNormalizedSelected = selectedText.Replace("\r\n", "\n", StringComparison.Ordinal);
                if (Item.Content != null && reqNormalizedContent.Contains(reqNormalizedSelected, StringComparison.Ordinal))
                {
                    ItemSplitRequest? request = await SplitCoordinator.RequestSplitSelectionAsync(Item, CurrentUserId);
                    if (request is not null)
                    {
                        await ReloadSplitRequestsAsync();
                        await NotifyDataChangedAsync();
                    }
                    return;
                }
            }
        }

        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        var parameters = new DialogParameters
        {
            ["QuotedItem"] = Item,
            ["CurrentUserId"] = CurrentUserId
        };
        IDialogReference dialog = await DialogLauncher.ShowAsync<QuoteItemDialog>("引用して投稿", parameters, options);
        DialogResult? result = await dialog.Result;

        switch (result)
        {
            case { Canceled: false, Data: Data.Item createdItem }:
                var targetUri = $"/Item/ItemList?focus={createdItem.Id}";
                var currentPath = new Uri(NavigationManager.Uri).AbsolutePath;
                var isOnItemList = currentPath.Equals("/Item/ItemList", StringComparison.OrdinalIgnoreCase);
                NavigationManager.NavigateTo(targetUri, forceLoad: isOnItemList);
                break;
            case { Canceled: false }:
                await NotifyDataChangedAsync();
                break;
        }
    }

    private async Task OpenShowQuotesDialogAsync()
    {
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Medium, FullWidth = true };
        var parameters = new DialogParameters
        {
            ["TargetItem"] = Item,
            ["CurrentUserId"] = CurrentUserId,
            ["AllTags"] = AllTags,
            ["AllTagRelationsToTags"] = AllTagRelationsToTags
        };
        _ = await DialogLauncher.ShowAsync<QuotedItemListDialog>("引用された投稿一覧", parameters, options);
    }

    // --- Edit / Delete / Report Logic ---
    private async Task EditItemAsync()
    {
        if (Item.OwnerId != CurrentUserId)
        {
            _ = Snackbar.Add(ErrorMessages.NotAuthorizedToEdit, Severity.Error);
            return;
        }

        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        var parameters = new DialogParameters { ["Item"] = Item };
        IDialogReference dialog = await DialogLauncher.ShowAsync<ItemEditDialog>("アイテムの編集", parameters, options);
        DialogResult? result = await dialog.Result;

        switch (result)
        {
            case { Canceled: false }:
                await NotifyDataChangedAsync();
                _ = Snackbar.Add("アイテムを更新しました。", Severity.Success);
                break;
        }
    }

    private async Task DeleteItemAsync()
    {
        if (await ActionViewModel.DeleteItemAsync(Item.Id, CurrentUserId, Item.OwnerId))
        {
            await NotifyDataChangedAsync();
        }
    }

    private async Task AdminForceDeleteItemAsync()
    {
        if (!_isAdmin)
        {
            _ = Snackbar.Add("管理者権限が必要です。", Severity.Error);
            return;
        }

        var success = await ActionViewModel.DeleteItemByAdminAsync(Item.Id, CurrentUserId, _isAdmin);
        if (success)
        {
            await NotifyDataChangedAsync();
            _ = Snackbar.Add("【管理者】アイテムを強制削除しました。", Severity.Success);
        }
        else
        {
            _ = Snackbar.Add("アイテムの強制削除に失敗しました。", Severity.Error);
        }
    }

    private async Task AdminToggleHideItemAsync()
    {
        if (!_isAdmin)
        {
            _ = Snackbar.Add("管理者権限が必要です。", Severity.Error);
            return;
        }

        var newIsHidden = !Item.IsAdminHidden;
        var success = await ActionViewModel.SetAdminHiddenAsync(Item.Id, newIsHidden, null, CurrentUserId, _isAdmin);
        if (success)
        {
            Item.IsAdminHidden = newIsHidden;
            await NotifyDataChangedAsync();
            var message = newIsHidden ? "【管理者】アイテムを強制非公開化しました。" : "【管理者】アイテムの非公開を解除しました。";
            _ = Snackbar.Add(message, newIsHidden ? Severity.Warning : Severity.Success);
        }
        else
        {
            _ = Snackbar.Add("アイテムの非公開状態の変更に失敗しました。", Severity.Error);
        }
    }

    private async Task ReportItemAsync()
    {
        if (string.IsNullOrEmpty(CurrentUserId))
        {
            _ = Snackbar.Add("通報するにはログインが必要です。", Severity.Warning);
            return;
        }

        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        var parameters = new DialogParameters
        {
            [nameof(ReportContentDialog.TargetType)] = ReportTargetType.Item,
            [nameof(ReportContentDialog.ItemId)] = Item.Id,
            [nameof(ReportContentDialog.TargetContent)] = Item.Content,
            [nameof(ReportContentDialog.TargetOwnerName)] = Item.Owner?.UserName
        };

        _ = await DialogLauncher.ShowAsync<ReportContentDialog>("不適切な投稿を通報", parameters, options);
    }

    private async Task ToggleConversationOptOutAsync()
    {
        if (string.IsNullOrEmpty(CurrentUserId)) return;

        var rootId = Item.RootItemId ?? Item.Id;
        await ActionViewModel.ToggleConversationOptOutAsync(rootId, CurrentUserId);
    }

    // --- Text Split Logic (Delegated to SplitCoordinator) ---
    private async Task SplitSelectionAsync()
    {
        var success = await SplitCoordinator.SplitSelectionAsync(Item, CurrentUserId);
        if (success)
        {
            await NotifyDataChangedAsync();
        }
    }

    private async Task RequestSplitSelectionAsync()
    {
        ItemSplitRequest? request = await SplitCoordinator.RequestSplitSelectionAsync(Item, CurrentUserId);
        if (request is not null)
        {
            await ReloadSplitRequestsAsync();
            await NotifyDataChangedAsync();
        }
    }

    private async Task ApproveSplitRequestAsync(ItemSplitRequest request)
    {
        var success = await SplitCoordinator.ApproveSplitRequestAsync(Item, request, CurrentUserId);
        if (success)
        {
            await ReloadSplitRequestsAsync();
            await NotifyDataChangedAsync();
        }
    }

    private async Task RejectSplitRequestAsync(ItemSplitRequest request)
    {
        var success = await SplitCoordinator.RejectSplitRequestAsync(Item, request, CurrentUserId);
        if (success)
        {
            await ReloadSplitRequestsAsync();
            await NotifyDataChangedAsync();
        }
    }

    private async Task CancelSplitRequestAsync(ItemSplitRequest request)
    {
        var success = await SplitCoordinator.CancelSplitRequestAsync(request, CurrentUserId);
        if (success)
        {
            await ReloadSplitRequestsAsync();
            await NotifyDataChangedAsync();
        }
    }

    private async Task ReloadSplitRequestsAsync()
    {
        _pendingSplitRequests = await ActionViewModel.GetPendingSplitRequestsAsync(Item.Id);
    }

    // --- Tag Operations (Delegated to TagCoordinator) ---
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "データベース一時切断時にUIがクラッシュするのを防ぐため")]
    private async Task OnAddTagClicked()
    {
        try
        {
            TagAddOutcome outcome = await TagCoordinator.PromptAndAddTagAsync(Item, CurrentUserId);
            switch (outcome)
            {
                case TagAddOutcome.AddedDirectly:
                    await NotifyDataChangedAsync();
                    break;
                case TagAddOutcome.ContractProposed:
                    _taggingRequests = await ActionViewModel.GetTaggingRequestsAsync(Item.Id);
                    StateHasChanged();
                    await NotifyDataChangedAsync();
                    break;
                case TagAddOutcome.None:
                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            _ = Snackbar.Add("タグ追加処理中にエラーが発生しました: " + ex.Message, Severity.Error);
        }
    }

    /// <summary>親へデータ変更を通知する（デリゲート未接続なら何もしない）。</summary>
    private async Task NotifyDataChangedAsync()
    {
        await (OnDataChanged.HasDelegate switch
        {
            true => OnDataChanged.InvokeAsync(),
            false => Task.CompletedTask
        });
    }

    private void OpenTagDiagram()
    {
        NavigationManager.NavigateTo($"/tag-diagram?itemId={Item.Id}");
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (_dotNetRef is not null)
        {
            try
            {
                await JS.InvokeVoidAsync("contentOverflowHelper.removeDotNetRef", _dotNetRef);
            }
            catch (JSDisconnectedException)
            {
                // ignored
            }
            catch (TaskCanceledException)
            {
                // ignored
            }
            catch (JSException)
            {
                // ignored
            }
            _dotNetRef.Dispose();
        }
    }
}