namespace SRNSMudApp.Controllers;

using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

using SRNSMudApp.Models.Push;
using SRNSMudApp.Services.Push;

/// <summary>
/// Web Push 通知の購読管理および配信リクエストを処理する API コントローラー。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class PushNotificationController(
    IPushSubscriptionStore subscriptionStore,
    IWebPushNotificationService pushService,
    IOptions<VapidOptions> vapidOptions) : ControllerBase
{
    private readonly IPushSubscriptionStore _subscriptionStore = subscriptionStore ?? throw new ArgumentNullException(nameof(subscriptionStore));
    private readonly IWebPushNotificationService _pushService = pushService ?? throw new ArgumentNullException(nameof(pushService));
    private readonly VapidOptions _vapidOptions = vapidOptions?.Value ?? throw new ArgumentNullException(nameof(vapidOptions));

    /// <summary>
    /// クライアントが PushManager.subscribe() を呼び出す際に必要な VAPID 公開鍵を取得します。
    /// </summary>
    [HttpGet("vapid-public-key")]
    public IActionResult GetVapidPublicKey()
    {
        return Ok(new { publicKey = _vapidOptions.PublicKey });
    }

    /// <summary>
    /// フロントエンドから送信されたプッシュ購読情報（Endpoint, p256dh, auth, userId）を保存します。
    /// </summary>
    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] PushSubscriptionDto? subscription, CancellationToken cancellationToken)
    {
        // プロパティパターンマッチングによるサブスクリプション入力検証
        if (subscription is not { Endpoint: { Length: > 0 } endpoint, Keys: { P256Dh: { Length: > 0 } p256, Auth: { Length: > 0 } auth } }
            || string.IsNullOrWhiteSpace(endpoint)
            || string.IsNullOrWhiteSpace(p256)
            || string.IsNullOrWhiteSpace(auth))
        {
            return BadRequest(new { message = "無効なサブスクリプション情報です。" });
        }

        // SEC-01: 認証済みユーザーのIDを取得（ClaimTypes.NameIdentifier または "sub"）
        // 未認証ユーザーの場合はリクエストボディの UserId を任意に信用せず null とする（ユーザーIDのなりすまし登録を防止）
        string? authenticatedUserId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("sub")?.Value;
        string? userId = User is { Identity.IsAuthenticated: true } && !string.IsNullOrWhiteSpace(authenticatedUserId)
            ? authenticatedUserId
            : null;

        // SEC-01: クライアントがリクエストボディで指定した UserId を無条件に破棄し、
        // サーバー側で検証した userId（認証済みならクレーム値、未認証なら null）で DTO を無害化して保存する
        var sanitizedSubscription = subscription with { UserId = userId };

        await _subscriptionStore.AddOrUpdateAsync(sanitizedSubscription, userId, cancellationToken);
        return Ok(new { message = "サブスクリプションの登録に成功しました。", userId });
    }

    /// <summary>
    /// クライアントのプッシュ購読解除を処理します。
    /// </summary>
    [HttpPost("unsubscribe")]
    public async Task<IActionResult> Unsubscribe([FromBody] PushUnsubscribeRequest? request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Endpoint))
        {
            return BadRequest(new { message = "エンドポイントが指定されていません。" });
        }

        await _subscriptionStore.RemoveAsync(request.Endpoint, cancellationToken);
        return Ok(new { message = "サブスクリプションを解除しました。" });
    }

    /// <summary>
    /// 保存されている全購読者に対してプッシュ通知を送信します。管理者ロール（Admin）のみ実行可能です。
    /// </summary>
    [HttpPost("send")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SendNotification([FromBody] PushNotificationPayload? payload, CancellationToken cancellationToken)
    {
        if (payload is not { Title: { Length: > 0 } title, Body: { Length: > 0 } body }
            || string.IsNullOrWhiteSpace(title)
            || string.IsNullOrWhiteSpace(body))
        {
            return BadRequest(new { message = "タイトルと本文は必須です。" });
        }

        var result = await _pushService.SendNotificationToAllAsync(payload, cancellationToken);
        return Ok(new
        {
            message = "通知の配信処理が完了しました。",
            result.SucceededCount,
            result.FailedCount,
            result.ExpiredCount
        });
    }

    /// <summary>
    /// 現在のログインユーザーに対してテストプッシュ通知を送信します。
    /// </summary>
    [HttpPost("test-me")]
    public async Task<IActionResult> TestMe([FromBody] PushSubscriptionDto? fallbackSubscription, CancellationToken cancellationToken)
    {
        string? userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        PushNotificationPayload payload = new(
            Title: "SRNS テスト通知",
            Body: "PWA への Web Push 通知が正常に動作しています！",
            Icon: "/images/icons/icon-192x192.png",
            Url: "/notifications"
        );

        if (!string.IsNullOrWhiteSpace(userId))
        {
            var userResult = await _pushService.SendNotificationToUserAsync(userId, payload, cancellationToken);
            if (userResult.SucceededCount > 0)
            {
                return Ok(new { message = "テスト通知を送信しました。", result = userResult });
            }

            if (userResult.FailedCount > 0 || userResult.ExpiredCount > 0)
            {
                // ユーザー宛ての購読が存在するが送信失敗した場合
                if (string.IsNullOrWhiteSpace(fallbackSubscription?.Endpoint))
                {
                    return BadRequest(new { message = "通知送信に失敗しました。サブスクリプションが失効しているか、サーバー設定を確認してください。" });
                }
            }
        }

        // ユーザーに紐づくサブスクリプションが無いか未ログインで fallbackSubscription がある場合
        if (!string.IsNullOrWhiteSpace(fallbackSubscription?.Endpoint))
        {
            bool directSuccess = await _pushService.SendNotificationAsync(fallbackSubscription, payload, cancellationToken);
            if (directSuccess)
            {
                return Ok(new { message = "テスト通知を送信しました。" });
            }

            return BadRequest(new { message = "プッシュ通知の直接送信に失敗しました。VAPID鍵設定または端末の通知設定を確認してください。" });
        }

        return BadRequest(new { message = "送信先のサブスクリプションが見つかりません。通知を許可して購読してください。" });
    }
}