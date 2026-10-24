namespace SRNSMudApp.Components.UI;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

/// <summary>
///     タグ付けリクエストのスレッドダイアログ (TaggingRequestThreadDialog) の状態管理および
///     リプライ投稿・承認・却下ロジックを担う ViewModel。
/// </summary>
public sealed class TaggingRequestThreadViewModel
{
    private readonly IItemReplyService _itemReplyService;
    private readonly IHomeDataProvider _homeData;
    private readonly ITaggingRequestActions _requestActions;

    public TaggingRequestThreadViewModel(
        IItemReplyService itemReplyService,
        IHomeDataProvider homeData,
        ITaggingRequestActions requestActions)
    {
        _itemReplyService = itemReplyService;
        _homeData = homeData;
        _requestActions = requestActions;
    }

    private readonly List<Item> _replies = [];

    public TaggingRequestEntity? TaggingRequest { get; private set; }
    public IReadOnlyList<Item> Replies => _replies;
    public string NewReplyMessage { get; set; } = string.Empty;
    public bool IsSubmitting { get; private set; }
    public string? CurrentUserId { get; private set; }
    public IReadOnlyList<Tag> AllTags { get; private set; } = [];
    public IReadOnlyList<TagRelationToTag> AllTagRelationsToTags { get; private set; } = [];

    public bool CanSubmitReply =>
        !string.IsNullOrWhiteSpace(NewReplyMessage) &&
        !string.IsNullOrEmpty(CurrentUserId) &&
        !IsSubmitting;

    public bool CanApprove =>
        TaggingRequest != null &&
        _requestActions.CanApprove(TaggingRequest, CurrentUserId);

    /// <summary>
    ///     リクエスト、現在のユーザーID、およびタグ情報を初期化する。
    /// </summary>
    public async Task InitializeAsync(TaggingRequestEntity taggingRequest, string? currentUserId)
    {
        TaggingRequest = taggingRequest;
        CurrentUserId = currentUserId;

        _replies.Clear();
        if (taggingRequest?.Replies != null)
        {
            _replies.AddRange(taggingRequest.Replies.OrderBy(r => r.CreatedDate));
        }

        var (allTags, relations) = await _homeData.GetTagsAndRelationsAsync();
        AllTags = allTags;
        AllTagRelationsToTags = relations;
    }

    /// <summary>
    ///     リクエストへのリプライを送信する。
    /// </summary>
    public async Task<Item?> SubmitReplyAsync()
    {
        if (!CanSubmitReply || TaggingRequest == null || string.IsNullOrEmpty(CurrentUserId))
        {
            return null;
        }

        IsSubmitting = true;
        try
        {
            var message = NewReplyMessage.Trim();
            var newReply = await _itemReplyService.AddReplyToRequestAsync(TaggingRequest.Id, CurrentUserId, message);
            if (newReply is not null)
            {
                if (newReply.IsDuplicate)
                {
                    return newReply;
                }

                _replies.Add(newReply);
                NewReplyMessage = string.Empty;

                if (!TaggingRequest.Replies.Contains(newReply))
                {
                    TaggingRequest.Replies.Add(newReply);
                }
            }

            return newReply;
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    /// <summary>
    ///     リクエストを承認する。
    /// </summary>
    public async Task<bool> ApproveRequestAsync()
    {
        if (IsSubmitting || TaggingRequest == null || CurrentUserId == null)
        {
            return false;
        }

        IsSubmitting = true;
        try
        {
            return await _requestActions.ApproveAsync(TaggingRequest.Id, CurrentUserId);
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    /// <summary>
    ///     リクエストを却下する。
    /// </summary>
    public async Task<bool> RejectRequestAsync()
    {
        if (IsSubmitting || TaggingRequest == null || CurrentUserId == null)
        {
            return false;
        }

        IsSubmitting = true;
        try
        {
            return await _requestActions.RejectViaDialogAsync(TaggingRequest.Id, CurrentUserId);
        }
        finally
        {
            IsSubmitting = false;
        }
    }
}