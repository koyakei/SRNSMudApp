using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Push;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services.Push;

// IDE0010 / IDE0072: union 型・enum の網羅的 switch に対する「Populate switch」は、
// 全ケース列挙済み・default 併記済みでも解消されない解析器の誤検知のため抑制する。
#pragma warning disable IDE0010, IDE0072

namespace SRNSMudApp.Services;

/// <summary>
///     通知の DTO 構築および集約ロジックを担当するドメインサービス。
///     データアクセスは INotificationsDataProvider に委譲し、本クラスは純粋なビジネス/変換ロジックに集中する。
/// </summary>
public class NotificationService(
    INotificationsDataProvider dataProvider,
    IWebPushNotificationService? webPushService = null) : INotificationService
{
    private readonly INotificationsDataProvider _dataProvider =
        dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
    private readonly IWebPushNotificationService? _webPushService = webPushService;

    public async Task<IReadOnlyList<NotificationDto>> GetUserNotificationsAsync(string userId)
    {
        NotificationRawData raw = await _dataProvider.GetNotificationRawDataAsync(userId);

        // 純粋な変換は宣言的に構築し、最後にソートする (mainRules: 再代入撲滅 / modern-csharp: Prefer LINQ)
        IEnumerable<NotificationDto> notifications =
            BuildTagRequestNotifications(raw.TagRequests, raw.ReadStates)
                .Concat(BuildRejectedRequestNotifications(raw.RejectedRequests, raw.ReadStates))
                .Concat(BuildApprovedRequestNotifications(raw.ApprovedRequests, raw.ReadStates))
                .Concat(BuildReplyNotifications(raw.ItemReplies, raw.ReadStates, "ItemReply", userId))
                .Concat(BuildReplyNotifications(raw.RequestReplies, raw.ReadStates, "RequestReply", userId))
                .Concat(BuildReportResolvedNotifications(raw.ResolvedReports ?? [], raw.ReadStates))
                .Concat(BuildSplitRequestNotifications(raw.SplitRequests ?? [], raw.ReadStates))
                .Concat(BuildResolvedSplitNotifications(raw.ResolvedSplitRequests ?? [], raw.ReadStates))
                .Concat(BuildTagContentProposalNotifications(raw.TagContentProposals ?? [], raw.ReadStates))
                .Concat(BuildResolvedTagContentProposalNotifications(raw.ResolvedTagContentProposals ?? [], raw.ReadStates))
                .Concat(BuildTagNameProposalNotifications(raw.TagNameProposals ?? [], raw.ReadStates))
                .Concat(BuildResolvedTagNameProposalNotifications(raw.ResolvedTagNameProposals ?? [], raw.ReadStates))
                .Concat(BuildTagRelationCommentNotifications(raw.TagRelationComments ?? [], raw.ReadStates))
                .Concat(BuildTagPermissionRequestNotifications(raw.TagPermissionRequests ?? [], raw.ReadStates))
                .Concat(BuildResolvedTagPermissionNotifications(raw.ResolvedTagPermissionRequests ?? [], raw.ReadStates));

        return [.. notifications.OrderByDescending(n => n.CreatedAt)];
    }

    public async Task<int> GetUnreadCountAsync(string userId)
    {
        IReadOnlyList<NotificationDto> notifications = await GetUserNotificationsAsync(userId);
        return notifications.Count(n => !n.IsRead);
    }

    public event EventHandler? NotificationsChanged;

    public void NotifyNotificationsChanged() => NotificationsChanged?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc />
    public async Task NotifyUserAsync(string userId, string title, string message, string? url = "/notifications", CancellationToken cancellationToken = default)
    {
        NotifyNotificationsChanged();

        if (_webPushService != null && !string.IsNullOrWhiteSpace(userId))
        {
            var payload = new PushNotificationPayload(
                Title: title,
                Body: message,
                Icon: "/images/icons/icon-192x192.png",
                Url: url ?? "/notifications");

            _ = await _webPushService.SendNotificationToUserAsync(userId, payload, cancellationToken);
        }
    }

    public async Task MarkAsReadAsync(string userId, int sourceId, string sourceType)
    {
        await _dataProvider.MarkAsReadAsync(userId, sourceId, sourceType);
        NotificationsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task MarkAllAsReadAsync(string userId)
    {
        IReadOnlyList<NotificationDto> notifications = await GetUserNotificationsAsync(userId);
        List<(int SourceId, string SourceType)> unreadItems = [.. notifications
            .Where(n => !n.IsRead)
            .Select(n => (n.SourceId, n.Kind.SourceType))];

        if (unreadItems.Count > 0)
        {
            await _dataProvider.MarkAllAsReadAsync(userId, unreadItems);
            NotificationsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal static string GetRequestTypeLabel(TaggingRequestType? type) => type switch
    {
        TaggingRequestType.Add => "追加",
        TaggingRequestType.DecreaseWeight => "削除",
        TaggingRequestType.Remove => "削除",
        TaggingRequestType.Move => "移動",
        _ => "不明"
    };

    internal static bool IsRead(IReadOnlyList<NotificationReadState> readStates, int sourceId, string sourceType) =>
        readStates.Any(rs => rs.SourceId == sourceId && rs.SourceType == sourceType);

    /// <summary>自分宛てのタグ付けリクエストから通知 DTO を生成する。</summary>
    internal static IEnumerable<NotificationDto> BuildTagRequestNotifications(
        IEnumerable<TaggingRequestEntity> requests,
        IReadOnlyList<NotificationReadState> readStates) =>
        requests.Select(req => new NotificationDto
        {
            SourceId = req.Id,
            Kind = new TagRequestNotification(
                RequestId: req.Id,
                RequestType: req.RequestType,
                TargetItemId: req.TargetItemId,
                TargetTagName: req.RequestedTag?.Name,
                TargetTagId: req.RequestedTagId,
                ProposedWeight: req.ProposedWeight,
                Status: req.Status
            ),
            Message = $"{req.RequestedTag?.Name ?? "不明なタグ"}の{GetRequestTypeLabel(req.RequestType)}リクエストが届いています。",
            CreatedAt = new DateTimeOffset(req.CreatedDate, TimeSpan.Zero),
            TargetUrl = req.RequestType == TaggingRequestType.Move
                ? new RelativeUrl($"/tag-tree?tagId={req.RequestedTagId}")
                : new RelativeUrl($"/ItemDetail/{req.TargetItemId}"),
            IsRead = IsRead(readStates, req.Id, "TagRequest"),
            ActorName = "システム",
            AssociatedItemId = req.TargetItemId,
            HighlightTagId = req.RequestedTagId
        });

    /// <summary>リクエスタ自身の却下済みリクエストから通知 DTO を生成する。</summary>
    internal static IEnumerable<NotificationDto> BuildRejectedRequestNotifications(
        IEnumerable<TaggingRequestEntity> requests,
        IReadOnlyList<NotificationReadState> readStates) =>
        requests.Select(req =>
        {
            var rejectReason = req.Rejection is RejectionReason r && !string.IsNullOrWhiteSpace(r.Reason)
                ? r.Reason
                : null;
            var commentMsg = rejectReason is not null ? $"\n理由: {rejectReason}" : "";
            return new NotificationDto
            {
                SourceId = req.Id,
                Kind = new RequestRejectedNotification(
                    RequestId: req.Id,
                    TagName: req.RequestedTag?.Name,
                    RequestType: req.RequestType,
                    RejectComment: rejectReason ?? "",
                    TargetItemId: req.TargetItemId,
                    TargetTagId: req.RequestedTagId
                ),
                Message = $"あなたの{req.RequestedTag?.Name ?? "不明なタグ"}の{GetRequestTypeLabel(req.RequestType)}リクエストが却下されました。{commentMsg}",
                CreatedAt = new DateTimeOffset(req.UpdatedDate, TimeSpan.Zero),
                TargetUrl = req.RequestType == TaggingRequestType.Move
                    ? new RelativeUrl($"/tag-tree?tagId={req.RequestedTagId}")
                    : new RelativeUrl($"/ItemDetail/{req.TargetItemId}"),
                IsRead = IsRead(readStates, req.Id, "RequestRejected"),
                ActorName = "システム",
                AssociatedItemId = req.TargetItemId,
                HighlightTagId = req.RequestedTagId
            };
        });

    /// <summary>リクエスタ自身の承認済みリクエストから通知 DTO を生成する。</summary>
    internal static IEnumerable<NotificationDto> BuildApprovedRequestNotifications(
        IEnumerable<TaggingRequestEntity> requests,
        IReadOnlyList<NotificationReadState> readStates) =>
        requests.Select(req => new NotificationDto
        {
            SourceId = req.Id,
            Kind = new RequestApprovedNotification(
                RequestId: req.Id,
                TagName: req.RequestedTag?.Name,
                RequestType: req.RequestType,
                TargetItemId: req.TargetItemId,
                TargetTagId: req.RequestedTagId
            ),
            // UpdatedDate を実行時刻の近似として使用する
            Message = $"あなたの{req.RequestedTag?.Name ?? "不明なタグ"}の{GetRequestTypeLabel(req.RequestType)}リクエストが承認されました。",
            CreatedAt = new DateTimeOffset(req.UpdatedDate, TimeSpan.Zero),
            TargetUrl = req.RequestType == TaggingRequestType.Move
                ? new RelativeUrl($"/tag-tree?tagId={req.RequestedTagId}")
                : new RelativeUrl($"/ItemDetail/{req.TargetItemId}"),
            IsRead = IsRead(readStates, req.Id, "RequestApproved"),
            ActorName = "システム",
            AssociatedItemId = req.TargetItemId,
            HighlightTagId = req.RequestedTagId
        });

    /// <summary>リプライ / リクエスト返信から通知 DTO を生成する。</summary>
    internal static IEnumerable<NotificationDto> BuildReplyNotifications(
        IEnumerable<Item> replies, IReadOnlyList<NotificationReadState> readStates, string sourceType, string? currentUserId = null) =>
        replies.Select(reply =>
        {
            var ownerName = reply.Owner?.UserName ?? "不明なユーザー";
            var isRequestReply = sourceType == "RequestReply";
            // 対応するクエリで ID != 0 が保証されているため既定値は使用されない (CS8629 回避)
            var relatedItemId = isRequestReply
                ? reply.TaggingRequestEntityId.GetValueOrDefault()
                : reply.ParentItemId.GetValueOrDefault();

            var targetLabel = isRequestReply
                ? "リクエスト"
                : (currentUserId != null && reply.ParentItem?.OwnerId != currentUserId ? "参加しているアイテム" : "アイテム");

            var targetItemId = isRequestReply ? relatedItemId : reply.Id;

            return new NotificationDto
            {
                SourceId = reply.Id,
                Kind = isRequestReply
                    ? new RequestReplyNotification(
                        ReplyItemId: reply.Id,
                        RequestId: relatedItemId,
                        ActorName: ownerName
                    )
                    : new ItemReplyNotification(
                        ReplyItemId: reply.Id,
                        ParentItemId: relatedItemId,
                        ActorName: ownerName
                    ),
                Message = $"{ownerName}さんがあなたの{targetLabel}に{(isRequestReply ? "返信" : "リプライ")}しました。",
                CreatedAt = new DateTimeOffset(reply.CreatedDate, TimeSpan.Zero),
                TargetUrl = new RelativeUrl($"/ItemDetail/{targetItemId}"),
                IsRead = IsRead(readStates, reply.Id, sourceType),
                ActorName = ownerName,
                AssociatedItemId = reply.Id
            };
        });

    public static IEnumerable<NotificationDto> BuildReportResolvedNotifications(
        IReadOnlyList<ContentReport> reports,
        IReadOnlyList<NotificationReadState> readStates)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(readStates);

        return reports.Select(report =>
        {
            var targetLabel = report.TargetType == ReportTargetType.Item ? "アイテム" : "タグ";
            var statusLabel = report.Status switch
            {
                ReportStatus.ActionTaken => "処置（削除等）が完了しました",
                ReportStatus.Reviewed => "確認が完了しました（違反なし）",
                ReportStatus.Dismissed => "対応不要（却下）と判断されました",
                _ => "審査が完了しました"
            };

            var message = $"通報いただいた{targetLabel}について、管理者の{statusLabel}。";
            if (!string.IsNullOrWhiteSpace(report.ResolutionNote))
            {
                message += $" (メモ: {report.ResolutionNote})";
            }

            var targetUrl = (report.TargetType == ReportTargetType.Item && report.ItemId.HasValue)
                ? new RelativeUrl($"/ItemDetail/{report.ItemId.Value}")
                : (report.TargetType == ReportTargetType.Tag && report.TagId.HasValue)
                    ? new RelativeUrl($"/TagDetail/{report.TagId.Value}")
                    : new RelativeUrl("#");

            return new NotificationDto
            {
                SourceId = report.Id,
                Kind = new ReportResolvedNotification(
                    report.Id,
                    report.Status,
                    report.ResolutionNote,
                    report.TargetType),
                Message = message,
                CreatedAt = new DateTimeOffset(report.HandledDate ?? report.UpdatedDate, TimeSpan.Zero),
                TargetUrl = targetUrl,
                IsRead = IsRead(readStates, report.Id, "ReportResolved"),
                ActorName = "管理者"
            };
        });
    }

    public static IEnumerable<NotificationDto> BuildSplitRequestNotifications(
        IReadOnlyList<ItemSplitRequest> requests,
        IReadOnlyList<NotificationReadState> readStates)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(readStates);

        return requests.Select(request =>
        {
            var requesterName = request.RequesterUser?.UserName ?? "ユーザー";
            var snippet = request.SelectedText.Length > 20
                ? $"{request.SelectedText[..20]}..."
                : request.SelectedText;
            var message = $"{requesterName} さんからアイテムのテキスト分割リクエストが届いています: 「{snippet}」";

            return new NotificationDto
            {
                SourceId = request.Id,
                Kind = new ItemSplitRequestNotification(
                    request.Id,
                    request.OriginalItemId,
                    requesterName,
                    request.SelectedText,
                    request.Status),
                Message = message,
                CreatedAt = new DateTimeOffset(request.CreatedDate, TimeSpan.Zero),
                TargetUrl = new RelativeUrl($"/ItemDetail/{request.OriginalItemId}"),
                IsRead = IsRead(readStates, request.Id, "ItemSplitRequest"),
                ActorName = requesterName,
                AssociatedItemId = request.OriginalItemId
            };
        });
    }

    public static IEnumerable<NotificationDto> BuildResolvedSplitNotifications(
        IReadOnlyList<ItemSplitRequest> requests,
        IReadOnlyList<NotificationReadState> readStates)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(readStates);

        return requests.Select(request =>
        {
            var ownerName = request.OwnerUser?.UserName ?? "所有者";
            var snippet = request.SelectedText.Length > 20
                ? $"{request.SelectedText[..20]}..."
                : request.SelectedText;

            var isApproved = request.Status == TradeStatus.Executed;
            var message = isApproved
                ? $"{ownerName} さんがアイテム分割リクエスト（「{snippet}」）を承認しました。"
                : $"{ownerName} さんがアイテム分割リクエスト（「{snippet}」）を却下しました。";

            if (!isApproved && !string.IsNullOrWhiteSpace(request.RejectReason))
            {
                message += $" (理由: {request.RejectReason})";
            }

            var sourceType = isApproved ? "ItemSplitApproved" : "ItemSplitRejected";
            NotificationType kind = isApproved
                ? new ItemSplitApprovedNotification(request.Id, request.OriginalItemId, request.CreatedItemId ?? 0)
                : new ItemSplitRejectedNotification(request.Id, request.OriginalItemId, request.RejectReason);

            var targetUrl = isApproved && request.CreatedItemId.HasValue
                ? new RelativeUrl($"/ItemDetail/{request.CreatedItemId.Value}")
                : new RelativeUrl($"/ItemDetail/{request.OriginalItemId}");

            return new NotificationDto
            {
                SourceId = request.Id,
                Kind = kind,
                Message = message,
                CreatedAt = new DateTimeOffset(request.UpdatedDate, TimeSpan.Zero),
                TargetUrl = targetUrl,
                IsRead = IsRead(readStates, request.Id, sourceType),
                ActorName = ownerName,
                AssociatedItemId = isApproved && request.CreatedItemId.HasValue
                    ? request.CreatedItemId.Value
                    : request.OriginalItemId
            };
        });
    }

    public static IEnumerable<NotificationDto> BuildTagContentProposalNotifications(
        IReadOnlyList<TagContentProposal> proposals,
        IReadOnlyList<NotificationReadState> readStates)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        ArgumentNullException.ThrowIfNull(readStates);

        return proposals.Select(proposal =>
        {
            var requesterName = proposal.RequesterUser?.UserName ?? "ユーザー";
            var tagName = proposal.Tag?.Name ?? "不明なタグ";
            var snippet = proposal.ProposedContent.Length > 20
                ? $"{proposal.ProposedContent[..20]}..."
                : proposal.ProposedContent;
            var message = $"{requesterName} さんからタグ「{tagName}」の編集提案が届いています: 「{snippet}」";

            return new NotificationDto
            {
                SourceId = proposal.Id,
                Kind = new TagContentProposalNotification(
                    proposal.Id,
                    proposal.TagId,
                    tagName,
                    requesterName,
                    proposal.ProposedContent,
                    proposal.Reason,
                    proposal.Status),
                Message = message,
                CreatedAt = new DateTimeOffset(proposal.CreatedDate, TimeSpan.Zero),
                TargetUrl = new RelativeUrl($"/TagDetail/{proposal.TagId}"),
                IsRead = IsRead(readStates, proposal.Id, "TagContentProposal"),
                ActorName = requesterName,
                HighlightTagId = proposal.TagId
            };
        });
    }

    public static IEnumerable<NotificationDto> BuildResolvedTagContentProposalNotifications(
        IReadOnlyList<TagContentProposal> proposals,
        IReadOnlyList<NotificationReadState> readStates)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        ArgumentNullException.ThrowIfNull(readStates);

        return proposals.Select(proposal =>
        {
            var ownerName = proposal.OwnerUser?.UserName ?? "所有者";
            var tagName = proposal.Tag?.Name ?? "タグ";
            var snippet = proposal.ProposedContent.Length > 20
                ? $"{proposal.ProposedContent[..20]}..."
                : proposal.ProposedContent;

            var isApproved = proposal.Status == TradeStatus.Executed;
            var message = isApproved
                ? $"{ownerName} さんがタグ「{tagName}」の編集提案（「{snippet}」）を承認しました。"
                : $"{ownerName} さんがタグ「{tagName}」の編集提案（「{snippet}」）を却下しました。";

            if (!isApproved && !string.IsNullOrWhiteSpace(proposal.RejectReason))
            {
                message += $" (理由: {proposal.RejectReason})";
            }

            var sourceType = isApproved ? "TagContentProposalApproved" : "TagContentProposalRejected";
            NotificationType kind = isApproved
                ? new TagContentProposalApprovedNotification(proposal.Id, proposal.TagId, tagName)
                : new TagContentProposalRejectedNotification(proposal.Id, proposal.TagId, tagName, proposal.RejectReason);

            return new NotificationDto
            {
                SourceId = proposal.Id,
                Kind = kind,
                Message = message,
                CreatedAt = new DateTimeOffset(proposal.UpdatedDate, TimeSpan.Zero),
                TargetUrl = new RelativeUrl($"/TagDetail/{proposal.TagId}"),
                IsRead = IsRead(readStates, proposal.Id, sourceType),
                ActorName = ownerName,
                HighlightTagId = proposal.TagId
            };
        });
    }

    public static IEnumerable<NotificationDto> BuildTagNameProposalNotifications(
        IReadOnlyList<TagNameProposal> proposals,
        IReadOnlyList<NotificationReadState> readStates)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        ArgumentNullException.ThrowIfNull(readStates);

        return proposals.Select(proposal =>
        {
            var requesterName = proposal.RequesterUser?.UserName ?? "ユーザー";
            var currentTagName = proposal.Tag?.Name ?? "不明なタグ";
            var message = $"{requesterName} さんからタグ「{currentTagName}」の名前変更提案（「{proposal.ProposedName}」）が届いています。";

            return new NotificationDto
            {
                SourceId = proposal.Id,
                Kind = new TagNameProposalNotification(
                    proposal.Id,
                    proposal.TagId,
                    currentTagName,
                    proposal.ProposedName,
                    requesterName,
                    proposal.Reason,
                    proposal.Status),
                Message = message,
                CreatedAt = new DateTimeOffset(proposal.CreatedDate, TimeSpan.Zero),
                TargetUrl = new RelativeUrl($"/TagDetail/{proposal.TagId}"),
                IsRead = IsRead(readStates, proposal.Id, "TagNameProposal"),
                ActorName = requesterName,
                HighlightTagId = proposal.TagId
            };
        });
    }

    public static IEnumerable<NotificationDto> BuildResolvedTagNameProposalNotifications(
        IReadOnlyList<TagNameProposal> proposals,
        IReadOnlyList<NotificationReadState> readStates)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        ArgumentNullException.ThrowIfNull(readStates);

        return proposals.Select(proposal =>
        {
            var ownerName = proposal.OwnerUser?.UserName ?? "所有者";
            var currentTagName = proposal.Tag?.Name ?? "タグ";

            var isApproved = proposal.Status == TradeStatus.Executed;
            var message = isApproved
                ? $"{ownerName} さんがタグ「{currentTagName}」の名前変更提案（「{proposal.ProposedName}」）を承認しました。"
                : $"{ownerName} さんがタグ「{currentTagName}」の名前変更提案（「{proposal.ProposedName}」）を却下しました。";

            if (!isApproved && !string.IsNullOrWhiteSpace(proposal.RejectReason))
            {
                message += $" (理由: {proposal.RejectReason})";
            }

            var sourceType = isApproved ? "TagNameProposalApproved" : "TagNameProposalRejected";
            NotificationType kind = isApproved
                ? new TagNameProposalApprovedNotification(proposal.Id, proposal.TagId, proposal.ProposedName)
                : new TagNameProposalRejectedNotification(proposal.Id, proposal.TagId, currentTagName, proposal.RejectReason);

            return new NotificationDto
            {
                SourceId = proposal.Id,
                Kind = kind,
                Message = message,
                CreatedAt = new DateTimeOffset(proposal.UpdatedDate, TimeSpan.Zero),
                TargetUrl = new RelativeUrl($"/TagDetail/{proposal.TagId}"),
                IsRead = IsRead(readStates, proposal.Id, sourceType),
                ActorName = ownerName,
                HighlightTagId = proposal.TagId
            };
        });
    }

    internal static IEnumerable<NotificationDto> BuildTagRelationCommentNotifications(
        IEnumerable<TagRelation> tagRelations,
        IReadOnlyList<NotificationReadState> readStates) =>
        tagRelations.Select(tr =>
        {
            var actorName = tr.Owner?.UserName ?? "誰か";
            var tagName = tr.Tag?.Name ?? "タグ";
            var comment = tr.Comment ?? string.Empty;

            return new NotificationDto
            {
                SourceId = tr.Id,
                Kind = new TagRelationCommentNotification(
                    tr.Id,
                    tr.ItemId,
                    tagName,
                    comment,
                    actorName),
                Message = $"{actorName} さんが「{tagName}」タグにコメントしました: {comment}",
                CreatedAt = new DateTimeOffset(tr.CreatedDate, TimeSpan.Zero),
                TargetUrl = new RelativeUrl($"/ItemDetail/{tr.ItemId}"),
                IsRead = IsRead(readStates, tr.Id, "TagRelationComment"),
                ActorName = actorName,
                AssociatedItemId = tr.ItemId
            };
        });

    internal static IEnumerable<NotificationDto> BuildTagPermissionRequestNotifications(
        IEnumerable<Item> items,
        IReadOnlyList<NotificationReadState> readStates) =>
        items.Select(item =>
        {
            var payload = RightAssetDataProvider.ParsePermissionPayload(item);
            var requesterName = item.Owner?.UserName ?? "ユーザー";
            var tagName = payload?.RequestedTagName ?? "タグ";
            var requestedAmount = payload?.RequestedAmount ?? 1;
            var offeredAssetId = payload?.OfferedRightAssetId;
            var offeredTagName = payload?.OfferedTagName;
            var offeredAmount = payload?.OfferedAmount ?? 0;
            var status = payload?.Status ?? TradeStatus.Proposed;
            var rejectReason = payload?.RejectReason;

            return new NotificationDto
            {
                SourceId = item.Id,
                Kind = new TagPermissionRequestNotification(
                    ItemId: item.Id,
                    RequestedTagId: payload?.RequestedTagId ?? 0,
                    RequestedTagName: tagName,
                    RequestedAmount: requestedAmount,
                    OfferedRightAssetId: offeredAssetId,
                    OfferedTagName: offeredTagName,
                    OfferedAmount: offeredAmount,
                    RequesterName: requesterName,
                    Message: payload?.Message,
                    Status: status,
                    RejectReason: rejectReason),
                Message = $"{requesterName} さんからタグ「{tagName}」の操作権限リクエスト ({requestedAmount}) が届いています。",
                CreatedAt = new DateTimeOffset(item.CreatedDate, TimeSpan.Zero),
                TargetUrl = payload?.RequestedTagId > 0
                    ? new RelativeUrl($"/TagDetail/{payload.RequestedTagId}")
                    : new RelativeUrl("/notifications"),
                IsRead = IsRead(readStates, item.Id, "TagPermissionRequest"),
                ActorName = requesterName,
                AssociatedItemId = item.Id,
                HighlightTagId = payload?.RequestedTagId > 0 ? payload.RequestedTagId : null
            };
        });

    internal static IEnumerable<NotificationDto> BuildResolvedTagPermissionNotifications(
        IEnumerable<Item> items,
        IReadOnlyList<NotificationReadState> readStates) =>
        items
            .Select(item => (Item: item, Payload: RightAssetDataProvider.ParsePermissionPayload(item)))
            .Where(x => x.Payload != null && x.Payload.Status != TradeStatus.Proposed)
            .Select(x =>
            {
                var item = x.Item;
                var payload = x.Payload!;
                var isApproved = payload.Status == TradeStatus.Executed;
                var tagName = payload.RequestedTagName;
                var sourceType = isApproved ? "TagPermissionApproved" : "TagPermissionRejected";
                var message = isApproved
                    ? $"あなたのタグ「{tagName}」の操作権限リクエストが承認されました。"
                    : $"あなたのタグ「{tagName}」の操作権限リクエストが却下されました。{(string.IsNullOrWhiteSpace(payload.RejectReason) ? "" : $"\n理由: {payload.RejectReason}")}";

                NotificationType kind = isApproved
                    ? new TagPermissionApprovedNotification(item.Id, payload.RequestedTagId, tagName, payload.RequestedAmount)
                    : new TagPermissionRejectedNotification(item.Id, payload.RequestedTagId, tagName, payload.RejectReason);

                return new NotificationDto
                {
                    SourceId = item.Id,
                    Kind = kind,
                    Message = message,
                    CreatedAt = new DateTimeOffset(item.UpdatedDate, TimeSpan.Zero),
                    TargetUrl = payload.RequestedTagId > 0
                        ? new RelativeUrl($"/TagDetail/{payload.RequestedTagId}")
                        : new RelativeUrl("/notifications"),
                    IsRead = IsRead(readStates, item.Id, sourceType),
                    ActorName = "システム",
                    AssociatedItemId = item.Id,
                    HighlightTagId = payload.RequestedTagId > 0 ? payload.RequestedTagId : null
                };
            });
}