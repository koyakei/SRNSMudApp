using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

using SRNSMudApp.Services;

namespace SRNSMudApp.Components.Layout;

/// <summary>
///     NavMenu の通知バッジ数およびナビゲーション状態を管理する ViewModel。
///     通知変更イベントの購読と未読カウント計算を UI から分離する。
/// </summary>
public sealed class NavMenuViewModel : IDisposable
{
    private readonly INotificationService _notificationService;
    private string? _currentUserId;
    private string? _lastBasePath;

    /// <summary>
    ///     <see cref="NavMenuViewModel"/> クラスの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="notificationService">通知ドメインサービス。</param>
    /// <exception cref="ArgumentNullException"><paramref name="notificationService"/> が null の場合にスローされます。</exception>
    public NavMenuViewModel(INotificationService notificationService)
    {
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _notificationService.NotificationsChanged += OnNotificationsChanged;
    }

    /// <summary>
    ///     現在の未読通知数を取得します。
    /// </summary>
    public int UnreadNotificationCount { get; private set; }

    /// <summary>
    ///     現在のベース相対 URL を取得します。
    /// </summary>
    [SuppressMessage("Design", "CA1056:UriPropertiesShouldNotBeStrings", Justification = "Relative URL string for Blazor NavigationManager")]
    public string? CurrentUrl { get; private set; }

    /// <summary>
    ///     状態変更時に発火するイベント。
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    ///     ユーザー ID および初期 URI を用いて ViewModel を初期化します。
    /// </summary>
    /// <param name="currentUserId">現在のユーザー ID。</param>
    /// <param name="initialUri">初期 URI。</param>
    /// <param name="baseRelativePath">ベース相対パス。</param>
    /// <returns>非同期タスク。</returns>
    public Task InitializeAsync(string? currentUserId, Uri initialUri, string baseRelativePath)
    {
        ArgumentNullException.ThrowIfNull(initialUri);
        return InitializeInternalAsync(currentUserId, initialUri.IsAbsoluteUri ? initialUri.AbsolutePath : initialUri.ToString().Split('?')[0], baseRelativePath);
    }

    /// <summary>
    ///     ユーザー ID および初期 URI 文字列を用いて ViewModel を初期化します。
    /// </summary>
    /// <param name="currentUserId">現在のユーザー ID。</param>
    /// <param name="initialUri">初期 URI 文字列。</param>
    /// <param name="baseRelativePath">ベース相対パス。</param>
    /// <returns>非同期タスク。</returns>
    public Task InitializeAsync(string? currentUserId, string initialUri, string baseRelativePath)
    {
        ArgumentNullException.ThrowIfNull(initialUri);
        var uri = new Uri(initialUri, UriKind.RelativeOrAbsolute);
        return InitializeInternalAsync(currentUserId, uri.IsAbsoluteUri ? uri.AbsolutePath : initialUri.Split('?')[0], baseRelativePath);
    }

    private async Task InitializeInternalAsync(string? currentUserId, string lastBasePath, string baseRelativePath)
    {
        _currentUserId = currentUserId;
        CurrentUrl = baseRelativePath;
        _lastBasePath = lastBasePath;

        await UpdateUnreadCountAsync();
    }

    /// <summary>
    ///     URL ロケーション変更を処理し、必要に応じて未読数を更新して状態通知を発火します。
    /// </summary>
    /// <param name="location">新しいロケーション。</param>
    /// <param name="baseRelativePath">ベース相対パス。</param>
    /// <returns>非同期タスク。</returns>
    public async Task HandleLocationChangedAsync(string location, string baseRelativePath)
    {
        CurrentUrl = baseRelativePath;
        var uri = new Uri(location, UriKind.RelativeOrAbsolute);
        string absolutePath = uri.IsAbsoluteUri ? uri.AbsolutePath : location.Split('?')[0];

        if (string.Equals(_lastBasePath, absolutePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastBasePath = absolutePath;
        await UpdateUnreadCountAsync();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    [SuppressMessage("Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "非同期イベント購読ハンドラー内で例外が発生してもプロセスが異常終了しないよう保護するため")]
    private async void OnNotificationsChanged(object? sender, EventArgs e)
    {
        try
        {
            await UpdateUnreadCountAsync();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            // 非同期イベント購読ハンドラー内で例外が発生してもプロセスが異常終了しないよう保護
        }
    }

    /// <summary>
    ///     現在通知ページを表示中かどうかを判定します。
    /// </summary>
    /// <returns>通知ページの場合は true、それ以外は false。</returns>
    public bool IsNotificationsPage()
    {
        if (CurrentUrl == null)
        {
            return false;
        }
        return CurrentUrl.Trim('/').Equals("notifications", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     未読通知数を非同期に最新化します。
    /// </summary>
    /// <returns>非同期タスク。</returns>
    public async Task UpdateUnreadCountAsync()
    {
        if (!string.IsNullOrEmpty(_currentUserId))
        {
            if (IsNotificationsPage())
            {
                UnreadNotificationCount = 0;
            }
            else
            {
                UnreadNotificationCount = await _notificationService.GetUnreadCountAsync(_currentUserId);
            }
        }
        else
        {
            UnreadNotificationCount = 0;
        }
    }

    /// <summary>
    ///     通知サービスのイベント購読を解除し、リソースを解放します。
    /// </summary>
    public void Dispose()
    {
        _notificationService.NotificationsChanged -= OnNotificationsChanged;
        GC.SuppressFinalize(this);
    }
}