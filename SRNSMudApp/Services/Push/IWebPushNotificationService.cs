namespace SRNSMudApp.Services.Push;

using SRNSMudApp.Models.Push;

/// <summary>
/// Web Push API を用いたプッシュ通知配信サービスインターフェイス。
/// </summary>
public interface IWebPushNotificationService
{
    /// <summary>
    /// 登録済みの全クライアントに対してプッシュ通知を配信します。
    /// 失効したサブスクリプション（404/410）は自動的に削除されます。
    /// </summary>
    Task<PushSendResult> SendNotificationToAllAsync(PushNotificationPayload payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// 単一のサブスクリプション宛てにプッシュ通知を配信します。
    /// </summary>
    Task<bool> SendNotificationAsync(PushSubscriptionDto subscription, PushNotificationPayload payload, CancellationToken cancellationToken = default);
}

/// <summary>
/// プッシュ通知一括配信結果。
/// </summary>
public sealed record PushSendResult(int SucceededCount, int FailedCount, int ExpiredCount);