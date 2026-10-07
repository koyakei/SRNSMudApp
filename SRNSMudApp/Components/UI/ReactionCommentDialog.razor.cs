namespace SRNSMudApp.Components.UI;

using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;

using MudBlazor;

using SRNSMudApp.Models;

/// <summary>
///     リアクションコメント入力ダイアログコンポーネントのコードビハインド。
///     10秒自動消滅タイマーのカウントダウン、カーソルホバーによるタイマー停止、コメント入力・保存を制御する。
/// </summary>
public partial class ReactionCommentDialog : ComponentBase, IDisposable
{
    [CascadingParameter]
    public IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter]
    public string ReactionTagName { get; set; } = "リアクション";

    [Inject]
    private IServiceProvider ServiceProvider { get; set; } = null!;

    public ReactionCommentViewModel ViewModel { get; private set; } = null!;

    private CancellationTokenSource? _cts;
    private bool _disposed;

    protected override void OnInitialized()
    {
        ViewModel = ServiceProvider.GetService<ReactionCommentViewModel>() ?? new ReactionCommentViewModel();
        ViewModel.StateChanged += OnViewModelStateChanged;
        ViewModel.Completed += OnViewModelCompleted;

        _cts = new CancellationTokenSource();
        _ = ViewModel.StartCountdownAsync(_cts.Token);
    }



    private void OnViewModelStateChanged()
    {
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnViewModelCompleted(ReactionCommentDialogResult result)
    {
        if (_disposed)
        {
            return;
        }

        _ = InvokeAsync(() =>
        {
            if (!_disposed)
            {
                MudDialog.Close(DialogResult.Ok(result));
            }
        });
    }

    public void OnCursorEnter()
    {
        ViewModel.OnCursorEnter();
        _cts?.Cancel();
    }

    private void Save()
    {
        _cts?.Cancel();
        ViewModel.Save();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                ViewModel.StateChanged -= OnViewModelStateChanged;
                ViewModel.Completed -= OnViewModelCompleted;
                _cts?.Cancel();
                _cts?.Dispose();
            }

            _disposed = true;
        }
    }
}