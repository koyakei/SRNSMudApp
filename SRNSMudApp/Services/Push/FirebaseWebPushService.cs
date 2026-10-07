#pragma warning disable CA1848, CA1873

namespace SRNSMudApp.Services.Push;

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;

using FirebaseAdmin.Messaging;

using Google.Apis.Auth.OAuth2;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SRNSMudApp.Models.Push;

using WebPush;

/// <summary>
/// Web ブラウザ（W3C Web Push / RFC 8291 / RFC 8292 VAPID）および Firebase Cloud Messaging (FCM) へのプッシュ通知配信サービス実装。
/// Chrome, Edge, Safari, Firefox を含むすべての Web ブラウザに対して、Google FCM サーバー経由で確実に Web Push を到達させます。
/// </summary>
public sealed class FirebaseWebPushService : IWebPushNotificationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IPushSubscriptionStore _subscriptionStore;
    private readonly FirebaseOptions _firebaseOptions;
    private readonly VapidOptions _vapidOptions;
    private readonly ILogger<FirebaseWebPushService> _logger;
    private readonly FirebaseMessaging? _messaging;

    /// <summary>
    /// コンストラクタ。DI コンテナから設定・ストア・ロガーを注入します。
    /// </summary>
    public FirebaseWebPushService(
        IPushSubscriptionStore subscriptionStore,
        IOptions<FirebaseOptions> firebaseOptions,
        IOptions<VapidOptions> vapidOptions,
        ILogger<FirebaseWebPushService> logger,
        FirebaseMessaging? messaging = null)
    {
        _subscriptionStore = subscriptionStore ?? throw new ArgumentNullException(nameof(subscriptionStore));
        _firebaseOptions = firebaseOptions?.Value ?? throw new ArgumentNullException(nameof(firebaseOptions));
        _vapidOptions = vapidOptions?.Value ?? throw new ArgumentNullException(nameof(vapidOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (messaging != null)
        {
            _messaging = messaging;
        }
        else if (_firebaseOptions.IsConfigured)
        {
            _messaging = InitializeFirebaseMessaging(_firebaseOptions, _logger);
        }
        else
        {
            _messaging = null;
        }

        if (!_vapidOptions.IsValid)
        {
            _logger.LogWarning("VAPIDキー（PublicKey / PrivateKey）が有効に設定されていません。Web Push 通知が失敗する可能性があります。");
        }
    }

    /// <summary>
    /// Firebase Messaging が初期化済みかどうか。
    /// </summary>
    public bool IsUsingFirebase => _messaging != null;

    /// <inheritdoc />
    public async Task<PushSendResult> SendNotificationToAllAsync(PushNotificationPayload payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var subscriptions = await _subscriptionStore.GetAllAsync(cancellationToken);
        if (subscriptions.Count == 0)
        {
            _logger.LogInformation("プッシュ通知送信対象のサブスクリプションが存在しません。");
            return new PushSendResult(0, 0, 0);
        }

        return await SendToSubscriptionsAsync(subscriptions, payload, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PushSendResult> SendNotificationToUserAsync(string userId, PushNotificationPayload payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new PushSendResult(0, 0, 0);
        }

        var subscriptions = await _subscriptionStore.GetByUserIdAsync(userId, cancellationToken);
        if (subscriptions.Count == 0)
        {
            _logger.LogInformation("ユーザー {UserId} 宛てのプッシュ通知サブスクリプションが存在しません。", userId);
            return new PushSendResult(0, 0, 0);
        }

        return await SendToSubscriptionsAsync(subscriptions, payload, cancellationToken);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Individual client push failure should not abort remaining notifications")]
    private async Task<PushSendResult> SendToSubscriptionsAsync(
        IReadOnlyCollection<PushSubscriptionDto> subscriptions,
        PushNotificationPayload payload,
        CancellationToken cancellationToken)
    {
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
                _logger.LogError(ex, "プッシュ通知送信中に予期しないエラーが発生しました: {Endpoint}", sub.Endpoint);
            }
        }

        _logger.LogInformation("プッシュ通知配信完了: 成功={Succeeded}, 失敗={Failed}, 失効削除={Expired}",
            succeeded, failed, expired);
        return new PushSendResult(succeeded, failed, expired);
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Catching unexpected exceptions during client send to log and return false")]
    public async Task<bool> SendNotificationAsync(PushSubscriptionDto subscription, PushNotificationPayload payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(payload);

        string jsonPayload = JsonSerializer.Serialize(payload, JsonOptions);

        // 1. ネイティブ FCM 登録トークン（URL ではない単一トークン文字列）が渡された場合のみ Firebase Admin SDK を利用
        if (_messaging != null && IsNativeFcmToken(subscription.Endpoint))
        {
            return await SendViaFirebaseAdminAsync(subscription.Endpoint, payload, jsonPayload, cancellationToken);
        }

        // 2. ブラウザ W3C Web Push（Chrome/FCM, Edge, Safari, Firefox）への送信
        // Google FCM も含め、すべてのブラウザ Web Push は RFC 8292 VAPID プロトコルで送信します。
        return await SendViaWebPushClientAsync(subscription, jsonPayload, cancellationToken);
    }

    /// <summary>
    /// VAPID（RFC 8292）認証を用いて各ブラウザのエンドポイント（fcm.googleapis.com 含む）へ Web Push を直接送信します。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Catching unexpected exceptions during WebPush send to log and return false")]
    private async Task<bool> SendViaWebPushClientAsync(PushSubscriptionDto subscription, string jsonPayload, CancellationToken cancellationToken)
    {
        string privateKey = _vapidOptions.GetEffectivePrivateKey();
        if (string.IsNullOrWhiteSpace(_vapidOptions.PublicKey) || string.IsNullOrWhiteSpace(privateKey))
        {
            _logger.LogError("VAPIDキー（PublicKey または PrivateKey）が未設定です。プッシュ通知を送信できません。");
            return false;
        }

        if (subscription.Keys == null ||
            string.IsNullOrWhiteSpace(subscription.Keys.P256Dh) ||
            string.IsNullOrWhiteSpace(subscription.Keys.Auth))
        {
            _logger.LogError("サブスクリプションの暗号化キー (P256Dh/Auth) が不足しています: {Endpoint}", subscription.Endpoint);
            return false;
        }

        using var client = new WebPushClient();
        var vapidDetails = new VapidDetails(
            _vapidOptions.Subject,
            _vapidOptions.PublicKey,
            privateKey);

        var pushSubscription = new PushSubscription(
            subscription.Endpoint,
            subscription.Keys.P256Dh,
            subscription.Keys.Auth);

        try
        {
            await client.SendNotificationAsync(pushSubscription, jsonPayload, vapidDetails, cancellationToken);
            _logger.LogInformation("Web Push 通知送信成功: Endpoint={Endpoint}", subscription.Endpoint);
            return true;
        }
        catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
        {
            // 端末側で購読解除または失効済み (410 / 404) → 上位でストアから削除
            _logger.LogWarning("Web Push エンドポイントが失効しています (HTTP {StatusCode}): {Endpoint}", ex.StatusCode, subscription.Endpoint);
            throw;
        }
        catch (WebPushException ex)
        {
            _logger.LogError(ex, "Web Push 送信エラー (HTTP {StatusCode}): Message={Message}, Endpoint={Endpoint}",
                ex.StatusCode, ex.Message, subscription.Endpoint);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Web Push 送信中にエラーが発生しました: {Endpoint}", subscription.Endpoint);
            return false;
        }
    }

    /// <summary>
    /// ネイティブ端末用 FCM トークン宛てに Firebase Admin SDK で送信します。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Catching unexpected exceptions during FCM send")]
    private async Task<bool> SendViaFirebaseAdminAsync(string fcmToken, PushNotificationPayload payload, string jsonPayload, CancellationToken cancellationToken)
    {
        try
        {
            var message = new Message
            {
                Token = fcmToken,
                Notification = new Notification
                {
                    Title = payload.Title,
                    Body = payload.Body,
                    ImageUrl = payload.Icon
                },
                Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "payload", jsonPayload },
                    { "url", payload.Url ?? "/notifications" }
                }
            };

            string messageId = await _messaging!.SendAsync(message, cancellationToken);
            _logger.LogInformation("Firebase FCM 送信完了: MessageId={MessageId}", messageId);
            return true;
        }
        catch (FirebaseMessagingException ex) when (
            ex.MessagingErrorCode is MessagingErrorCode.Unregistered)
        {
            _logger.LogWarning("FCM トークンが無効化されています: {Token}", fcmToken);
            throw new WebPushException("FCM token is unregistered.", null, new HttpResponseMessage(HttpStatusCode.Gone));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firebase FCM 送信に失敗しました: {Token}", fcmToken);
            return false;
        }
    }

    /// <summary>
    /// 指定されたエンドポイントが W3C Web Push URL ではなく、ネイティブ FCM 登録トークンであるかを判定します。
    /// </summary>
    private static bool IsNativeFcmToken(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        // Web Push のエンドポイントは必ず http:// または https:// で始まります
        return !endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
               !endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Firebase Admin SDK の FirebaseApp を初期化して FirebaseMessaging インスタンスを返します。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Firebase initialization failure should not crash the app")]
#pragma warning disable CS0618
    private static FirebaseMessaging? InitializeFirebaseMessaging(FirebaseOptions options, ILogger logger)
    {
        try
        {
            FirebaseAdmin.FirebaseApp? existingApp = null;
            try
            {
                existingApp = FirebaseAdmin.FirebaseApp.DefaultInstance;
            }
            catch (Exception)
            {
                // DefaultInstance が存在しない場合は無視
            }

            if (existingApp == null)
            {
                GoogleCredential credential;

                string serviceAccountValue = options.ServiceAccountJson!;
                if (File.Exists(serviceAccountValue))
                {
                    credential = GoogleCredential.FromFile(serviceAccountValue)
                        .CreateScoped("https://www.googleapis.com/auth/firebase.messaging");
                    logger.LogInformation("Firebase サービスアカウントをファイルから初期化します: {Path}", serviceAccountValue);
                }
                else
                {
                    credential = GoogleCredential.FromJson(serviceAccountValue)
                        .CreateScoped("https://www.googleapis.com/auth/firebase.messaging");
                    logger.LogInformation("Firebase サービスアカウントを JSON 文字列から初期化します。");
                }

                var appOptions = new FirebaseAdmin.AppOptions
                {
                    Credential = credential,
                    ProjectId = options.ProjectId
                };

                _ = FirebaseAdmin.FirebaseApp.Create(appOptions);
                logger.LogInformation("Firebase アプリを初期化しました。");
            }

            return FirebaseMessaging.DefaultInstance;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Firebase アプリの初期化に失敗しました。VAPID 送信モードで動作します。");
            return null;
        }
    }
#pragma warning restore CS0618
}