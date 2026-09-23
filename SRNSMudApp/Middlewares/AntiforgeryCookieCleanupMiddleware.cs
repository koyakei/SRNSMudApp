#region

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

#endregion

namespace SRNSMudApp.Middlewares;

/// <summary>
/// devcontainer やコンテナの再作成、キーローテーション等により、
/// クライアントブラウザに残った復号不可能な古い Antiforgery Cookie を安全に検知し、
/// リクエストから除去してブラウザに削除指示を送信するミドルウェア。
/// これにより、DefaultAntiforgery 内で CryptographicException（The key was not found in the key ring）
/// がログにエラー出力されるのを防止する。
/// </summary>
public sealed partial class AntiforgeryCookieCleanupMiddleware
{
    private const string AntiforgeryPurpose = "Microsoft.AspNetCore.Antiforgery.AntiforgeryToken.v1";
    private readonly RequestDelegate _next;
    private readonly ILogger<AntiforgeryCookieCleanupMiddleware> _logger;

    public AntiforgeryCookieCleanupMiddleware(RequestDelegate next, ILogger<AntiforgeryCookieCleanupMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "復号不可能な古い Antiforgery Cookie ({CookieNames}) を検出しました。ブラウザから削除し、リクエストから除外します。")]
    private static partial void LogInvalidAntiforgeryCookie(ILogger logger, string cookieNames);

    public async Task InvokeAsync(HttpContext context)
    {
        AntiforgeryOptions? antiforgeryOptions = context.RequestServices.GetService<IOptions<AntiforgeryOptions>>()?.Value;
        string? cookieName = antiforgeryOptions?.Cookie?.Name;

        // リクエスト内の Antiforgery Cookie を特定
        // 以前のセッションや異なるアプリケーション名で発行された .AspNetCore.Antiforgery.* のCookieも含めて全て検査
        var antiforgeryCookies = context.Request.Cookies
            .Where(c => c.Key.StartsWith(".AspNetCore.Antiforgery.", StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(cookieName) && string.Equals(c.Key, cookieName, StringComparison.Ordinal)))
            .ToList();

        if (antiforgeryCookies.Count > 0)
        {
            IDataProtectionProvider? dataProtectionProvider = context.RequestServices.GetService<IDataProtectionProvider>();
            if (dataProtectionProvider is not null)
            {
                IDataProtector protector = dataProtectionProvider.CreateProtector(AntiforgeryPurpose);
                List<string>? invalidCookies = null;

                foreach (KeyValuePair<string, string> cookie in antiforgeryCookies)
                {
                    if (string.IsNullOrWhiteSpace(cookie.Value))
                    {
                        continue;
                    }

                    if (!TryUnprotect(protector, cookie.Value))
                    {
                        invalidCookies ??= [];
                        invalidCookies.Add(cookie.Key);
                    }
                }

                if (invalidCookies is not null)
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        var invalidCookieNames = string.Join(", ", invalidCookies);
                        LogInvalidAntiforgeryCookie(_logger, invalidCookieNames);
                    }

                    // ブラウザへクッキー削除レスポンスヘッダを発行（ルートパス指定と既定の双方で確実に消去）
                    CookieOptions deleteOptions = new()
                    {
                        Path = "/",
                        HttpOnly = true,
                        Secure = context.Request.IsHttps,
                        SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax
                    };

                    foreach (string name in invalidCookies)
                    {
                        context.Response.Cookies.Delete(name);
                        context.Response.Cookies.Delete(name, deleteOptions);
                    }

                    // 現在のリクエストから除外して後続の UseAntiforgery でのデシリアライズ失敗（CryptographicException）を防ぐ
                    Dictionary<string, string> filteredCookies = context.Request.Cookies
                        .Where(c => !invalidCookies.Contains(c.Key))
                        .ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);

                    var filteredCollection = new FilteredCookieCollection(filteredCookies);
                    context.Request.Cookies = filteredCollection;
                    context.Features.Set<IRequestCookiesFeature>(new RequestCookiesFeatureWrapper(filteredCollection));

                    // Cookie ヘッダーも同期
                    if (filteredCookies.Count > 0)
                    {
                        context.Request.Headers[HeaderNames.Cookie] = string.Join("; ", filteredCookies.Select(c => $"{c.Key}={c.Value}"));
                    }
                    else
                    {
                        _ = context.Request.Headers.Remove(HeaderNames.Cookie);
                    }
                }
            }
        }

        await _next(context);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "データ保護の任意の復号失敗（キー不一致、破損、形式不正など）を検知してfalseを返すため")]
    private static bool TryUnprotect(IDataProtector protector, string cookieValue)
    {
        try
        {
            byte[] decoded = WebEncoders.Base64UrlDecode(cookieValue);
            _ = protector.Unprotect(decoded);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private sealed class FilteredCookieCollection(IReadOnlyDictionary<string, string> cookies) : IRequestCookieCollection
    {
        public string? this[string key] => cookies.TryGetValue(key, out string? val) ? val : null;
        public int Count => cookies.Count;
        public ICollection<string> Keys => cookies.Keys.ToList();
        public bool ContainsKey(string key) => cookies.ContainsKey(key);
        public bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => cookies.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => cookies.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class RequestCookiesFeatureWrapper(IRequestCookieCollection cookies) : IRequestCookiesFeature
    {
        public IRequestCookieCollection Cookies { get; set; } = cookies;
    }
}

public static class AntiforgeryCookieCleanupMiddlewareExtensions
{
    public static IApplicationBuilder UseAntiforgeryCookieCleanup(this IApplicationBuilder app)
    {
        return app.UseMiddleware<AntiforgeryCookieCleanupMiddleware>();
    }
}