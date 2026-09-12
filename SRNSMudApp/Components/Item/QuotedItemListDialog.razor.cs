using Microsoft.AspNetCore.Components;

using MudBlazor;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

namespace SRNSMudApp.Components.Item;

using DataItem = SRNSMudApp.Data.Item;
using DataTag = SRNSMudApp.Data.Tag;

/// <summary>
///     引用された投稿一覧ダイアログのコードビハインド。
///     引用元と引用投稿の取得、およびダイアログのクローズを担当する。
/// </summary>
public partial class QuotedItemListDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;

    [Inject] private IItemQuoteService ItemQuoteService { get; set; } = null!;

    [Parameter] public DataItem TargetItem { get; set; } = null!;
    [Parameter] public string CurrentUserId { get; set; } = "";
    [Parameter] public IReadOnlyList<DataTag> AllTags { get; set; } = [];
    [Parameter] public IReadOnlyList<TagRelationToTag> AllTagRelationsToTags { get; set; } = [];

    private DataItem? _sourceItem;
    private IReadOnlyList<DataItem> _quotedItems = [];
    private bool _isLoading = true;

    protected override async Task OnInitializedAsync()
    {
        await LoadQuotedItemsAsync();
    }

    private async Task LoadQuotedItemsAsync()
    {
        _isLoading = true;
        try
        {
            _sourceItem = await ItemQuoteService.GetSourceItemAsync(TargetItem.Id);
            _quotedItems = await ItemQuoteService.GetQuotedByItemsAsync(TargetItem.Id);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void Close()
    {
        MudDialog.Close(DialogResult.Ok(true));
    }
}