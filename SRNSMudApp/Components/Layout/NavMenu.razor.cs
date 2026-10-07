using System;
using System.Security.Claims;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace SRNSMudApp.Components.Layout;

/// <summary>
///     ナビゲーションメニューのコードビハインドコンポーネント。
///     通知数の購読・更新ロジックは <see cref="NavMenuViewModel"/> に委譲する。
/// </summary>
public sealed partial class NavMenu : ComponentBase, IDisposable
{
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;
    [Inject] private IWebHostEnvironment Env { get; set; } = null!;
    [Inject] private NavMenuViewModel ViewModel { get; set; } = null!;
    [Inject] private ILogger<NavMenu> Logger { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

    private string? _currentUserId;

    protected override async Task OnInitializedAsync()
    {
        AuthenticationState authState = await AuthStateTask;
        _currentUserId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        ViewModel.StateChanged += OnViewModelStateChanged;
        NavigationManager.LocationChanged += OnLocationChanged;

        string baseRelative = NavigationManager.ToBaseRelativePath(NavigationManager.Uri);
        var uri = new Uri(NavigationManager.Uri, UriKind.RelativeOrAbsolute);
        await ViewModel.InitializeAsync(_currentUserId, uri, baseRelative);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "ロケーション変更ハンドラー内の例外でプロセスが停止しないよう保護するため")]
    private async void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        try
        {
            string baseRelative = NavigationManager.ToBaseRelativePath(e.Location);
            await ViewModel.HandleLocationChangedAsync(e.Location, baseRelative);
        }
        catch (Exception ex)
        {
            LogLocationChangedFailed(Logger, ex);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "ロケーション変更時の通知数更新に失敗しました。")]
    private static partial void LogLocationChangedFailed(ILogger logger, Exception ex);

    private void OnViewModelStateChanged(object? sender, EventArgs e)
    {
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    ///     ナビゲーションおよび通知イベントの購読を解除し、リソースを解放します。
    /// </summary>
    public void Dispose()
    {
        NavigationManager.LocationChanged -= OnLocationChanged;
        ViewModel.StateChanged -= OnViewModelStateChanged;
        ViewModel.Dispose();
        GC.SuppressFinalize(this);
    }
}