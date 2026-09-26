#pragma warning disable CA1848, CA1873

namespace SRNSMudApp.Services.Push;

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SRNSMudApp.Models.Push;

using WebPush;

/// <summary>
/// WebPush ライブラリを用いて VAPID 署名付きプッシュ通知を送信するドメインサービス実装。
/// </summary>
public sealed class WebPushNotificationService(
    IPushSubscriptionStore subscriptionStore,
    IOptions<VapidOptions> vapidOptions,
    ILogger<WebPushNotificationService> logger) : IWebPushNotificationService
{
    private readonly IPushSubscriptionStore _subscriptionStore = subscriptionStore ?? throw new ArgumentNullException(nameof(subscriptionStore));
    private readonly VapidOptions _vapidOptions = vapidOptions?.Value ?? throw new ArgumentNullException(nameof(vapidOptions));
    private readonly ILogger<WebPushNotificationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Individual client push failure should not abort remaining notifications")]
    public async Task<PushSendResult> SendNotificationToAllAsync(PushNotificationPayload payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var subscriptions = await _subscriptionStore.GetAllAsync(cancellationToken);
        if (subscriptions.Count == 0)
        {
            _logger.LogInformation("プッシュ通知送信対象のサブスクリプションが存在しません。");
            return new PushSendResult(0, 0, 0);
        }

        int succeeded = 0;
        int failed = 0;
        int expired = 0;

        foreach (var sub in subscriptions)
        {
            try
            {
                bool success = await SendNotificationAsync(sub, payload, cancellationToken);
                if (success)
                {
                    succeeded++;
                }
                else
                {
                    failed++;
                }
            }
            catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
            {
                expired++;
                _logger.LogWarning("サブスクリプションが無効化・失効しているため削除します: {Endpoint}", sub.Endpoint);
                await _subscriptionStore.RemoveAsync(sub.Endpoint, cancellationToken);
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "プッシュ通知送信中にエラーが発生しました: {Endpoint}", sub.Endpoint);
            }
        }

        _logger.LogInformation("プッシュ通知配信完了: 成功={Succeeded}, 失敗={Failed}, 失効削除={Expired}", succeeded, failed, expired);
        return new PushSendResult(succeeded, failed, expired);
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Catching unexpected exceptions during client send to log and return false")]
    public async Task<bool> SendNotificationAsync(PushSubscriptionDto subscription, PushNotificationPayload payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(payload);

        if (string.IsNullOrWhiteSpace(_vapidOptions.PublicKey) || string.IsNullOrWhiteSpace(_vapidOptions.PrivateKey))
        {
            throw new InvalidOperationException("VAPIDキー（PublicKey / PrivateKey）が設定されていません。");
        }

        using var client = new WebPushClient();
        var vapidDetails = new VapidDetails(
            _vapidOptions.Subject,
            _vapidOptions.PublicKey,
            _vapidOptions.PrivateKey);

        var pushSubscription = new PushSubscription(
            subscription.Endpoint,
            subscription.Keys.P256Dh,
            subscription.Keys.Auth);

        string jsonPayload = JsonSerializer.Serialize(payload);

        try
        {
            await client.SendNotificationAsync(pushSubscription, jsonPayload, vapidDetails, cancellationToken);
            _logger.LogDebug("プッシュ通知を送信しました: {Endpoint}", subscription.Endpoint);
            return true;
        }
        catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
        {
            // 上位の一括配信処理でハンドリングできるようにリスロー
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "プッシュ通知の個別送信に失敗しました: {Endpoint}", subscription.Endpoint);
            return false;
        }
    }
}