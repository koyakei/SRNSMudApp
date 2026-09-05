using SRNSMudApp.Data;

namespace SRNSMudApp.Models;

/// <summary>
///     通知一覧の生成に必要な未加工エンティティ群を保持する DTO。
///     NotificationsDataProvider から NotificationService へ生データを受け渡すことで、
///     サービス層から DbContext への直接アクセスを排除し、責務を分離する。
/// </summary>
public record NotificationRawData(
    IReadOnlyList<TaggingRequestEntity> TagRequests,
    IReadOnlyList<Item> ItemReplies,
    IReadOnlyList<TaggingRequestEntity> RejectedRequests,
    IReadOnlyList<TaggingRequestEntity> ApprovedRequests,
    IReadOnlyList<Item> RequestReplies,
    IReadOnlyList<NotificationReadState> ReadStates);