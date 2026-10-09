namespace SRNSMudApp.Components.Tag;

using System.Security.Claims;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;

using MudBlazor;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

/// <summary>
///     タグ追加ダイアログコンポーネントのコードビハインド。
///     既存タグ検索・選択、または親タグ配下への新規子タグ作成タブのUIインタラクションを制御する。
///     ドメイン操作・検索処理は <see cref="TagAddViewModel"/> に委譲する。
/// </summary>
public partial class TagAddDialog : ComponentBase
{
    [Inject] private TagAddViewModel ViewModel { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState> AuthStateTask { get; set; } = default!;
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter] public Tag? DefaultParentTag { get; set; }

    private int _activeTabIndex;
    private int? _activeTreeTagId;

    protected override async Task OnInitializedAsync()
    {
        await ViewModel.InitializeAsync();
    }

    protected override void OnParametersSet()
    {
        if (DefaultParentTag is null)
        {
            return;
        }

        ViewModel.ParentTag = DefaultParentTag;
        _activeTabIndex = 1;
    }

    private void OnActiveTabIndexChanged(int newIndex)
    {
        _activeTabIndex = newIndex;
        if (_activeTabIndex == 1 && ViewModel.ParentTag == null && ViewModel.SelectedTag != null)
        {
            ViewModel.ParentTag = ViewModel.SelectedTag;
        }
    }

    private void SelectParentAndOpenCreateTab(Tag tag)
    {
        ViewModel.ParentTag = tag;
        _activeTabIndex = 1;
    }

    private void SelectExistingCandidate(Tag tag)
    {
        ViewModel.SelectSimilarTag(tag);
        _activeTabIndex = 0;
    }

    private Task<IEnumerable<Tag>> SearchParentTagsAsync(string value, CancellationToken token)
    {
        return Task.FromResult(ViewModel.SearchParentTags(value));
    }

    private void ToggleTree(int tagId)
    {
        _activeTreeTagId = _activeTreeTagId == tagId ? null : tagId;
    }

    private async Task SearchAsync()
    {
        await ViewModel.SearchAsync();
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await SearchAsync();
        }
    }

    private void SubmitSearch()
    {
        MudDialog.Close(DialogResult.Ok(ViewModel.SelectedTag));
    }

    private async Task SubmitCreateChildAsync()
    {
        var authState = await AuthStateTask;
        var userId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var isAdmin = authState.User.IsInRole("Admin");

        var createdTag = await ViewModel.CreateChildTagAsync(userId, isAdmin);
        if (createdTag != null)
        {
            MudDialog.Close(DialogResult.Ok(createdTag));
        }
    }

    private void Cancel()
    {
        MudDialog.Cancel();
    }
}