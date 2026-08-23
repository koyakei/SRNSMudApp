using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using MudBlazor;

using SRNSMudApp.Components.Contract;
using SRNSMudApp.Components.Item;
using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Services;
using SRNSMudApp.Services.Dialogs;

// IDE0010 / IDE0072: union 型・enum の網羅的 switch に対する「Populate switch」は、
// 全ケース列挙済み・default 併記済みでも解消されない解析器の誤検知のため抑制する。
#pragma warning disable IDE0010, IDE0072

namespace SRNSMudApp.Components.UI;

using Item = SRNSMudApp.Data.Item;
// 兄弟名前空間 SRNSMudApp.Components.Tag / .Item が同名型と解決されるため、
// エイリアスを名前空間の内側に置く
using Tag = SRNSMudApp.Data.Tag;

/// <summary>
///     ItemCard のコードビハインド。
///     マークアップ (.razor) 側は表示のみを担い、JS 連携・返信・投票・ダイアログ起動などの
///     UI オーケストレーションはこちらに集約する。純粋な計算は <see cref="ItemCardViewModel" /> へ。
/// </summary>
public partial class ItemCard : IAsyncDisposable
{
    [Inject] private IItemTagService ItemTagService { get; set; } = null!;
    [Inject] private IDialogLauncher DialogLauncher { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private TaggingContractService TaggingContractService { get; set; } = null!;
    [Inject] private IItemCardDataProvider ItemCardData { get; set; } = null!;

    [Parameter][EditorRequired] public Item Item { get; set; } = null!;
    [Parameter] public EventCallback OnDataChanged { get; set; }
    [Parameter] public bool IsFocused { get; set; }
    [Parameter] public EventCallback<int> OnFocus { get; set; }
    [Parameter] public IReadOnlyList<Tag> AllTags { get; set; } = [];
    [Parameter] public IReadOnlyList<TagRelationToTag> AllTagRelationsToTags { get; set; } = [];
    [Parameter] public string CurrentUserId { get; set; } = "";
    [Parameter] public int? CurrentUserGoodTagId { get; set; }
    [Parameter] public int? CurrentUserBadTagId { get; set; }
    [Parameter] public EventCallback OnEnsureSystemTags { get; set; }
    [Parameter] public IReadOnlyList<TimelineEvent>? HighlightEvents { get; set; }

    /// <summary>
    ///     リプライ 1 件の描画テンプレート (ネストした ItemCard)。
    ///     ItemReplyThread への描画委譲に使用する。
    /// </summary>
    private RenderFragment<Item> ReplyTemplate => reply => builder =>
    {
        builder.OpenComponent<ItemCard>(0);
        builder.AddAttribute(1, nameof(Item), reply);
        builder.AddAttribute(2, nameof(CurrentUserId), CurrentUserId);
        builder.AddAttribute(3, nameof(CurrentUserGoodTagId), CurrentUserGoodTagId);
        builder.AddAttribute(4, nameof(CurrentUserBadTagId), CurrentUserBadTagId);
        builder.AddAttribute(5, nameof(OnDataChanged),
            EventCallback.Factory.Create(this, LoadRepliesAsync));
        builder.AddAttribute(6, nameof(AllTags), AllTags);
        builder.AddAttribute(7, nameof(AllTagRelationsToTags), AllTagRelationsToTags);
        builder.AddAttribute(8, nameof(HighlightEvents), HighlightEvents);
        builder.AddAttribute(9, nameof(OnEnsureSystemTags), OnEnsureSystemTags);
        builder.CloseComponent();
    };

    private DotNetObjectReference<ItemCard>? _dotNetRef;
    private List<TaggingRequestEntity> _taggingRequests = [];

    private bool _isRepliesExpanded;
    private List<Item> _replies = [];
    private string _newReplyContent = "";
    private bool _isSubmittingReply;

    protected override async Task OnParametersSetAsync()
    {
        _taggingRequests = await ItemTagService.GetTaggingRequestsForItemAsync(Item.Id);
        await (_isRepliesExpanded switch
        {
            true => LoadRepliesAsync(),
            false => Task.CompletedTask
        });
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
            OnFocus.InvokeAsync(Item.Id);
        }
    }

    private string GetItemCardStyle() => ItemCardViewModel.GetItemCardStyle(IsFocused);

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "UI 層で発生した例外の内容をユーザーへ通知するために広く捕捉する")]
    private async Task CancelTaggingRequestAsync()
    {
        if (Item.AsRequestOf is null)
        {
            return;
        }
        try
        {
            await TaggingContractService.CancelContractAsync(Item.AsRequestOf.Id, CurrentUserId);
            Item.AsRequestOf.Status = TradeStatus.Canceled;
            Snackbar.Add("リクエストを取り下げました。", Severity.Success);
            await NotifyDataChangedAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"エラー: {ex.Message}", Severity.Error);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "UI 層で発生した例外の内容をユーザーへ通知するために広く捕捉する")]
    private async Task ApproveTaggingRequestAsync()
    {
        if (Item.AsRequestOf is null)
        {
            return;
        }
        try
        {
            await TaggingContractService.AcceptContractAsync(Item.AsRequestOf.Id, CurrentUserId);
            Item.AsRequestOf.Status = TradeStatus.Executed;
            Snackbar.Add("リクエストを承認しました。", Severity.Success);
            await NotifyDataChangedAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"エラー: {ex.Message}", Severity.Error);
        }
    }

    private async Task ToggleRepliesAsync()
    {
        _isRepliesExpanded = !_isRepliesExpanded;
        await ((_isRepliesExpanded && _replies.Count == 0) switch
        {
            true => LoadRepliesAsync(),
            false => Task.CompletedTask
        });
    }

    private async Task LoadRepliesAsync() => _replies = await ItemTagService.GetItemRepliesAsync(Item.Id);

    private async Task SubmitReplyAsync()
    {
        if (string.IsNullOrWhiteSpace(_newReplyContent) || string.IsNullOrEmpty(CurrentUserId))
        {
            return;
        }

        _isSubmittingReply = true;
        try
        {
            var addedReply = await ItemTagService.AddItemReplyAsync(Item.Id, _newReplyContent, CurrentUserId);
            if (addedReply is not null)
            {
                _newReplyContent = "";
                await LoadRepliesAsync();
            }
        }
        finally
        {
            _isSubmittingReply = false;
        }
    }

    // --- Voting Logic ---
    private int GetItemScore() => ItemCardViewModel.GetItemScore(Item.TagRelations);

    private bool IsItemUpvoted() => ItemCardViewModel.IsItemUpvoted(Item.TagRelations, CurrentUserId, CurrentUserGoodTagId);

    private bool IsItemDownvoted() => ItemCardViewModel.IsItemDownvoted(Item.TagRelations, CurrentUserId, CurrentUserGoodTagId);

    private async Task UpvoteItemAsync() => await ToggleItemVoteAsync(true);

    private async Task DownvoteItemAsync() => await ToggleItemVoteAsync(false);

    private async Task ToggleItemVoteAsync(bool isUpvote)
    {
        if (string.IsNullOrEmpty(CurrentUserId))
        {
            Snackbar.Add("ログインが必要です。", Severity.Warning);
            return;
        }

        await (OnEnsureSystemTags.HasDelegate switch
        {
            true => OnEnsureSystemTags.InvokeAsync(),
            false => Task.CompletedTask
        });

        if (!(CurrentUserGoodTagId.HasValue))
        {
            Snackbar.Add("システムタグの取得に失敗しました。", Severity.Error);
            return;
        }

        var targetWeight = isUpvote ? 1 : -1;
        var goodTagId = CurrentUserGoodTagId.Value;

        ItemVoteResult result = await ItemCardData.ToggleItemVoteAsync(Item.Id, CurrentUserId, goodTagId, targetWeight);

        TagRelation? existingRelation = Item.TagRelations.FirstOrDefault(tr => tr.Id == result.RelationId);
        switch (result.Action)
        {
            case ItemVoteAction.Removed when existingRelation != null:
                Item.TagRelations.Remove(existingRelation);
                break;
            case ItemVoteAction.Updated when existingRelation != null:
                existingRelation.Weight = result.Weight;
                break;
            case ItemVoteAction.Added:
                {
                    Item.TagRelations ??= [];
                    Item.TagRelations.Add(new TagRelation
                    {
                        Id = result.RelationId,
                        ItemId = Item.Id,
                        TagId = goodTagId,
                        OwnerId = CurrentUserId,
                        Weight = result.Weight
                    });
                    break;
                }
        }

        await NotifyDataChangedAsync();
    }

    // --- Edit/Delete Logic ---
    private async Task EditItemAsync()
    {
        if (Item.OwnerId != CurrentUserId)
        {
            Snackbar.Add("投稿者本人ではないため、編集する権限がありません。", Severity.Error);
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
                Snackbar.Add("アイテムを更新しました。", Severity.Success);
                break;
        }
    }

    private async Task DeleteItemAsync()
    {
        if (Item.OwnerId != CurrentUserId)
        {
            Snackbar.Add("投稿者本人ではないため、操作する権限がありません。", Severity.Error);
            return;
        }

        await ItemCardData.DeleteItemAsync(Item.Id);
        await NotifyDataChangedAsync();

        Snackbar.Add("アイテムを削除しました。", Severity.Success);
    }

    // --- Tag Operations ---
    private async Task OnAddTagClicked()
    {
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Large, FullWidth = true };
        IDialogReference dialog = await DialogLauncher.ShowAsync<TagAddDialog>("タグの追加", options);
        DialogResult? result = await dialog.Result;

        switch (result)
        {
            case { Canceled: false, Data: Tag selectedTag }:
                await AddTagToItemAsync(selectedTag);
                break;
        }
    }

    private async Task AddTagToItemAsync(Tag selectedTag)
    {
        Tag? tagFromDb = await ItemCardData.GetTagWithOwnerAsync(selectedTag.Id);
        if (tagFromDb is null)
        {
            return;
        }

        if (tagFromDb.OwnerId != CurrentUserId)
        {
            await ProposeTaggingContractAsync(tagFromDb);
            return;
        }

        var alreadyExists = Item.TagRelations?.Any(tr => tr.TagId == selectedTag.Id) ?? false;
        if (alreadyExists)
        {
            Snackbar.Add("このタグは既に追加されています。", Severity.Warning);
            return;
        }

        await ExecuteAddTagToItemAsync(selectedTag, tagFromDb);
    }

    private async Task ProposeTaggingContractAsync(Tag tagFromDb)
    {
        var parameters = new DialogParameters<ProposeContractDialog>
        {
            { x => x.TargetItem, Item },
            { x => x.RequestedTag, tagFromDb },
            { x => x.WeightDelta, 1 }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Medium, FullWidth = true };
        IDialogReference dialog =
            await DialogLauncher.ShowAsync<ProposeContractDialog>("コントラクトの提案", parameters, options);
        DialogResult? result = await dialog.Result;

        switch (result)
        {
            case { Canceled: false }:
                _taggingRequests = await ItemTagService.GetTaggingRequestsForItemAsync(Item.Id);
                StateHasChanged();
                await NotifyDataChangedAsync();
                break;
        }
    }

    private async Task ExecuteAddTagToItemAsync(Tag selectedTag, Tag tagFromDb)
    {
        TagRelation? newRelation =
            await ItemCardData.AddFreeTagRelationAsync(Item.Id, selectedTag.Id, CurrentUserId);
        if (newRelation is not null)
        {
            Item.TagRelations ??= [];
            newRelation.Tag = tagFromDb;
            Item.TagRelations.Add(newRelation);
        }

        Snackbar.Add("タグを追加しました。", Severity.Success);
        await NotifyDataChangedAsync();
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

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (_dotNetRef is not null)
        {
            try
            {
                await JS.InvokeVoidAsync("contentOverflowHelper.removeDotNetRef", _dotNetRef);
            }
            catch (JSException)
            {
                // ignored
            }
            _dotNetRef.Dispose();
        }
    }
}