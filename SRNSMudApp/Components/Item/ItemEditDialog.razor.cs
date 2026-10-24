namespace SRNSMudApp.Components.Item;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

using MudBlazor;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

/// <summary>
///     アイテム編集ダイアログコンポーネントのコードビハインド。
///     Tribute.js によるメンション補完初期化、内部リンクピル変換、入力検証、保存ダイアログ操作を制御する。
///     ドメイン操作・URL抽出・検索ロジックは <see cref="ItemEditViewModel"/> に委譲する。
/// </summary>
public partial class ItemEditDialog : ComponentBase, IAsyncDisposable
{
    private static readonly Action<ILogger, string, Exception?> LogJsError =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, nameof(ItemEditDialog)),
            "JS Interop Error while initializing Tribute: {Message}");

    [Inject] private ItemEditViewModel ViewModel { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private ILogger<ItemEditDialog> Logger { get; set; } = null!;

    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter] public Item Item { get; set; } = null!;

    private DotNetObjectReference<ItemEditDialog>? _objRef;
    private bool _tributeInitialized;

    protected override void OnInitialized()
    {
        ViewModel.Initialize(Item);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_tributeInitialized)
        {
            _tributeInitialized = true;
            _objRef = DotNetObjectReference.Create(this);
            try
            {
                // Init HTML content of the editor with parsed pills
                var initialHtml = ItemEditViewModel.ParsePillsToHtml(ViewModel.EditContent);
                await JS.InvokeVoidAsync("eval", $"document.getElementById('edit-item-textarea').innerHTML = {JsonSerializer.Serialize(initialHtml)}");

                await JS.InvokeVoidAsync("tributeInterop.init", "edit-item-textarea", _objRef);
            }
            catch (JSException ex)
            {
                LogJsError(Logger, ex.Message, ex);
            }
        }
    }

    /// <summary>
    /// テキスト内の内部リンク（/TagDetail/ や /User/UserDetail/）を contenteditable 用の pill HTML に変換する
    /// </summary>
    internal static string ParsePillsToHtml(string text) => ItemEditViewModel.ParsePillsToHtml(text);

    private void Cancel()
    {
        MudDialog.Cancel();
    }

    private async Task SaveAsync()
    {
        if (ViewModel.IsSubmitting)
        {
            Snackbar.Add(Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
            return;
        }

        var (success, errorMessage) = await ViewModel.SaveAsync();
        if (success)
        {
            MudDialog.Close(DialogResult.Ok(true));
        }
        else if (errorMessage == Client.Services.DoubleSubmissionGuard.DuplicateSubmissionMessage)
        {
            Snackbar.Add(errorMessage, Severity.Info);
        }
        else if (!string.IsNullOrEmpty(errorMessage))
        {
            Snackbar.Add(errorMessage, Severity.Warning);
        }
    }

    [JSInvokable]
    public async Task<IEnumerable<MentionItem>> SearchTags(string query)
    {
        return await ViewModel.SearchTagsAsync(query);
    }

    [JSInvokable]
    public async Task<IEnumerable<MentionItem>> SearchUsers(string query)
    {
        return await ViewModel.SearchUsersAsync(query);
    }

    [JSInvokable]
    public async Task SubmitForm()
    {
        await SaveAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_objRef != null)
        {
            try
            {
                await JS.InvokeVoidAsync("tributeInterop.destroy", "edit-item-textarea");
            }
            catch (JSException)
            {
            }

            _objRef.Dispose();
        }
    }
}