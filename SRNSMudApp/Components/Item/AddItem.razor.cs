namespace SRNSMudApp.Components.Item;

using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

using MudBlazor;

using SRNSMudApp.Client.Services;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

using ItemEntity = SRNSMudApp.Data.Item;
using TagEntity = SRNSMudApp.Data.Tag;
using UserGroupEntity = SRNSMudApp.Data.UserGroup;

/// <summary>
///     新規アイテム投稿フォームコンポーネントのコードビハインド。
///     Tribute.js を利用したインラインサジェスト、関連タグ候補表示、内部リンク変換の UI インタラクションを制御する。
///     ドメイン操作・バリデーションは <see cref="AddItemViewModel"/> に委譲する。
/// </summary>
public partial class AddItem : ComponentBase, IAsyncDisposable
{
    private static readonly Action<ILogger, string, Exception?> LogJsError =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, nameof(AddItem)),
            "JS Interop Error while initializing Tribute: {Message}");

    [Inject] private AddItemViewModel ViewModel { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private ILogger<AddItem> Logger { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState> AuthenticationStateTask { get; set; } = null!;

    /// <summary>
    ///     初期状態で設定されるタグのリスト（任意）。
    /// </summary>
    [Parameter] public IEnumerable<TagEntity> InitialTags { get; set; } = [];

    /// <summary>
    ///     アイテムが追加された後に呼び出されるコールバック。
    /// </summary>
    [Parameter] public EventCallback OnItemAdded { get; set; }

    /// <summary>
    ///     リプライ先となる親アイテム（任意）。
    /// </summary>
    [Parameter] public ItemEntity? ParentItem { get; set; }

    /// <summary>
    ///     リプライ先となる親アイテムID（任意。ParentItem が指定されていない場合に使用）。
    /// </summary>
    [Parameter] public int? ParentItemId { get; set; }

    /// <summary>
    ///     textarea 要素の DOM ID。複数インスタンスを同時描画する場合に一意にする。
    /// </summary>
    [Parameter] public string TextareaId { get; set; } = "add-item-textarea";

    /// <summary>
    ///     フォーム要素の id 属性（外部の送信ボタンから form 属性で submit する場合に使用）。
    /// </summary>
    [Parameter] public string? FormId { get; set; }

    /// <summary>
    ///     内部の保存ボタンを非表示にするかどうか（ダイアログヘッダーなど外部にボタンを配置する場合）。
    /// </summary>
    [Parameter] public bool HideSubmitButton { get; set; }

    /// <summary>
    ///     送信中状態が変化した際に呼び出されるコールバック。
    /// </summary>
    [Parameter] public EventCallback<bool> IsSubmittingChanged { get; set; }

    [SupplyParameterFromForm] private ItemEntity? NewItem { get => ViewModel.NewItem; set => ViewModel.NewItem = value; }

    private bool IsPrivate { get => ViewModel.IsPrivate; set => ViewModel.IsPrivate = value; }
    private int? SelectedGroupId { get => ViewModel.SelectedGroupId; set => ViewModel.SelectedGroupId = value; }
    private IReadOnlyList<UserGroupEntity> UserGroups => ViewModel.UserGroups;

    private IReadOnlyList<ReplyTargetCandidate> TargetCandidates => ViewModel.TargetCandidates;
    private HashSet<string> SelectedTargetUserIds => ViewModel.SelectedTargetUserIds;

    private IReadOnlyList<SuggestedTag> SuggestedTags
    {
        get => ViewModel.SuggestedTags;
        set => ViewModel.SuggestedTags = value;
    }
    private bool IsLoadingSuggestions
    {
        get => ViewModel.IsLoadingSuggestions;
        set => ViewModel.IsLoadingSuggestions = value;
    }
    private CancellationTokenSource? _suggestionCts;
    private float? UserStrongThreshold => ViewModel.UserStrongThreshold;
    private float? UserCandidateThreshold => ViewModel.UserCandidateThreshold;

    private bool IsLinkConversionEnabled
    {
        get => ViewModel.IsLinkConversionEnabled;
        set => ViewModel.IsLinkConversionEnabled = value;
    }
    private float LinkConversionThreshold
    {
        get => ViewModel.LinkConversionThreshold;
        set => ViewModel.LinkConversionThreshold = value;
    }
    private IReadOnlyList<LinkConversionCandidate> LinkAutoReplaceCandidates
    {
        get => ViewModel.LinkAutoReplaceCandidates;
        set => ViewModel.LinkAutoReplaceCandidates = value;
    }
    private IReadOnlyList<LinkConversionCandidate> LinkManualCandidates
    {
        get => ViewModel.LinkManualCandidates;
        set => ViewModel.LinkManualCandidates = value;
    }
    private bool IsLoadingLinkCandidates
    {
        get => ViewModel.IsLoadingLinkCandidates;
        set => ViewModel.IsLoadingLinkCandidates = value;
    }
    private CancellationTokenSource? _linkConversionCts;

    private int? _lastParentItemId;

    protected override async Task OnInitializedAsync()
    {
        if (ViewModel.NewItem == null)
        {
            var authState = await AuthenticationStateTask;
            await ViewModel.InitializeAsync(authState.User, ParentItem, ParentItemId);
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        ViewModel.ParentItem = ParentItem;
        ViewModel.ParentItemId = ParentItemId;

        var effectiveId = ViewModel.EffectiveParentItem?.Id ?? ParentItemId;
        if (effectiveId != _lastParentItemId)
        {
            _lastParentItemId = effectiveId;
            await ViewModel.LoadParentItemIfNeededAsync();
        }

        if (ViewModel.EffectiveParentItem != null && ViewModel.SuggestedTags.Count == 0 && !ViewModel.IsLoadingSuggestions && string.IsNullOrWhiteSpace(ViewModel.NewItem?.Content))
        {
            _ = TriggerTagSuggestionsAsync(ViewModel.NewItem?.Content ?? string.Empty);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Tribute.js のクリア処理失敗は UI の致命的エラーではないため握りつぶす")]
    private async Task HandleValidSubmit()
    {
        if (ViewModel.IsSubmitting)
        {
            Snackbar.Add(DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
            return;
        }

        if (!ViewModel.CanSubmitCurrent)
        {
            return;
        }

        if (IsSubmittingChanged.HasDelegate)
        {
            await IsSubmittingChanged.InvokeAsync(true);
        }

        try
        {
            var result = await ViewModel.SaveItemAsync(InitialTags);
            if (result.IsDuplicate)
            {
                Snackbar.Add(DoubleSubmissionGuard.DuplicateSubmissionMessage, Severity.Info);
                return;
            }

            if (result.Success)
            {
                try
                {
                    await JS.InvokeVoidAsync("tributeInterop.clear", TextareaId);
                }
                catch (Exception)
                {
                    // ignored
                }

                Snackbar.Add("アイテムが正常に保存されました。", Severity.Success);

                if (OnItemAdded.HasDelegate)
                {
                    await OnItemAdded.InvokeAsync();
                }
                else
                {
                    NavigationManager.NavigateTo(NavigationManager.Uri, true);
                }
            }
            else
            {
                Snackbar.Add($"エラー: {result.ErrorMessage}", Severity.Error);
            }
        }
        finally
        {
            if (IsSubmittingChanged.HasDelegate)
            {
                await IsSubmittingChanged.InvokeAsync(false);
            }
        }
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && e.MetaKey)
        {
            await HandleValidSubmit();
        }
    }

    private void HandleInvalidSubmit(EditContext context)
    {
        Snackbar.Add("入力内容を確認してください。", Severity.Warning);
    }

    private async Task HandleContentChanged(string value)
    {
        if (ViewModel.NewItem != null)
        {
            ViewModel.NewItem.Content = value;
        }
        await ViewModel.UpdateTargetCandidatesAsync();
        _ = TriggerTagSuggestionsAsync(value);

        if (ViewModel.IsLinkConversionEnabled)
        {
            _ = TriggerLinkCandidateDetectionAsync(value);
        }
    }

    private void HandleConfirmedSuggestedTagIdsChanged(IReadOnlyCollection<int> tagIds)
    {
        ViewModel.ConfirmedSuggestedTagIds.Clear();
        foreach (var id in tagIds)
        {
            ViewModel.ConfirmedSuggestedTagIds.Add(id);
        }
    }

    private async Task HandleThresholdsChanged((float Strong, float Candidate) thresholds)
    {
        await ViewModel.UpdateTagSuggestionThresholdsAsync(thresholds.Strong, thresholds.Candidate);

        if (!string.IsNullOrWhiteSpace(ViewModel.NewItem?.Content) || ViewModel.EffectiveParentItem != null)
        {
            await TriggerTagSuggestionsAsync(ViewModel.NewItem?.Content ?? string.Empty);
        }
    }

    private async Task TriggerTagSuggestionsAsync(string content)
    {
        if (_suggestionCts != null)
        {
            await _suggestionCts.CancelAsync();
            _suggestionCts.Dispose();
            _suggestionCts = null;
        }

        var queryText = AddItemViewModel.BuildTagSuggestionQueryText(content, ViewModel.EffectiveParentItem?.Content);

        if (string.IsNullOrWhiteSpace(queryText))
        {
            ViewModel.SuggestedTags = [];
            ViewModel.ConfirmedSuggestedTagIds.Clear();
            ViewModel.IsLoadingSuggestions = false;
            await InvokeAsync(StateHasChanged);
            return;
        }

        _suggestionCts = new CancellationTokenSource();
        var token = _suggestionCts.Token;

        try
        {
            ViewModel.IsLoadingSuggestions = true;
            await InvokeAsync(StateHasChanged);

            await Task.Delay(350, token);

            var suggestions = await ViewModel.SuggestTagsAsync(queryText, ViewModel.CurrentCandidateThreshold, token);

            if (!token.IsCancellationRequested)
            {
                ViewModel.SuggestedTags = suggestions;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                ViewModel.IsLoadingSuggestions = false;
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    private async Task TriggerLinkCandidateDetectionAsync(string content)
    {
        if (_linkConversionCts != null)
        {
            await _linkConversionCts.CancelAsync();
            _linkConversionCts.Dispose();
            _linkConversionCts = null;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            ViewModel.LinkAutoReplaceCandidates = [];
            ViewModel.LinkManualCandidates = [];
            ViewModel.ConfirmedLinkCandidates = [];
            ViewModel.IsLoadingLinkCandidates = false;
            await InvokeAsync(StateHasChanged);
            return;
        }

        _linkConversionCts = new CancellationTokenSource();
        var token = _linkConversionCts.Token;

        try
        {
            ViewModel.IsLoadingLinkCandidates = true;
            await InvokeAsync(StateHasChanged);

            await Task.Delay(500, token);

            var result = await ViewModel.DetectLinkCandidatesAsync(content, ViewModel.LinkConversionThreshold, token);

            if (!token.IsCancellationRequested)
            {
                var allCandidates = result.AutoReplaceCandidates.Concat(result.ManualCandidates).ToList();
                await JS.InvokeVoidAsync("tributeInterop.applyLinkCandidates", TextareaId, allCandidates);

                ViewModel.LinkAutoReplaceCandidates = result.AutoReplaceCandidates;
                ViewModel.LinkManualCandidates = result.ManualCandidates;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                ViewModel.IsLoadingLinkCandidates = false;
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    private async Task HandleLinkConversionThresholdChanged(float threshold)
    {
        await ViewModel.UpdateLinkConversionSettingsAsync(threshold);

        if (!string.IsNullOrWhiteSpace(ViewModel.NewItem?.Content))
        {
            await TriggerLinkCandidateDetectionAsync(ViewModel.NewItem.Content);
        }
    }

    private void HandleTargetToggled(string userId, bool isSelected)
    {
        ViewModel.ApplyTargetToggle(userId, isSelected);
    }

    // --- JS Interop Mentions Logic ---

    private DotNetObjectReference<AddItem>? _objRef;
    private bool _tributeInitialized;

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Tribute.js の初期化例外をログに記録し、UI描画の継続を保証するため")]
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_tributeInitialized && ViewModel.NewItem != null)
        {
            _tributeInitialized = true;
            _objRef = DotNetObjectReference.Create(this);
            try
            {
                await JS.InvokeVoidAsync("tributeInterop.init", TextareaId, _objRef);
            }
            catch (Exception ex)
            {
                LogJsError(Logger, ex.Message, ex);
            }
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
        await HandleValidSubmit();
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "非同期破棄時のクリーンアップ失敗をログに残さず握りつぶすため")]
    public async ValueTask DisposeAsync()
    {
        if (_suggestionCts != null)
        {
            try
            {
                await _suggestionCts.CancelAsync();
                _suggestionCts.Dispose();
            }
            catch
            {
            }
        }

        if (_linkConversionCts != null)
        {
            try
            {
                await _linkConversionCts.CancelAsync();
                _linkConversionCts.Dispose();
            }
            catch
            {
            }
        }

        if (_objRef != null)
        {
            try
            {
                await JS.InvokeVoidAsync("tributeInterop.destroy", TextareaId);
            }
            catch
            {
            }
            _objRef.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}