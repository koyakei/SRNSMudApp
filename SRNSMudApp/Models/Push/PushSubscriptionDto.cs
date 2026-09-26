namespace SRNSMudApp.Models.Push;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

/// <summary>
/// ブラウザの pushManager.subscribe() から送信されるサブスクリプション情報 DTO。
/// </summary>
public sealed record PushSubscriptionDto(
    [property: JsonPropertyName("endpoint")] string Endpoint,
    [property: JsonPropertyName("keys")] PushSubscriptionKeysDto Keys
);

/// <summary>
/// Web Push 暗号化キーペア情報。
/// </summary>
public sealed record PushSubscriptionKeysDto(
    [property: JsonPropertyName("p256dh")] string P256Dh,
    [property: JsonPropertyName("auth")] string Auth
);

/// <summary>
/// プッシュ通知配信ペイロードモデル。
/// </summary>
[SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "JSON payload serialized to Web Push client is a string URL")]
[SuppressMessage("Design", "CA1054:URI parameters should not be strings", Justification = "JSON payload serialized to Web Push client is a string URL")]
public sealed record PushNotificationPayload(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("icon")] string? Icon = null,
    [property: JsonPropertyName("url")] string? Url = null
);

/// <summary>
/// 購読解除リクエスト DTO。
/// </summary>
public sealed record PushUnsubscribeRequest(
    [property: JsonPropertyName("endpoint")] string Endpoint
);