#region

using SRNSMudApp.Data;
using SRNSMudApp.Services;

#endregion

namespace SRNSMudApp.Components.Tag;

/// <summary>
///     汎用タグエディタ (GenericTagEditor.razor) のタグ追加・削除操作を担当する ViewModel。
/// </summary>
public sealed class GenericTagEditorViewModel
{
    private readonly IDirectTaggingService _taggingService;

    public GenericTagEditorViewModel(ITaggingService taggingService)
    {
        _taggingService = taggingService ?? throw new ArgumentNullException(nameof(taggingService));
    }

    public int NewTagId { get; set; }
    public bool IsProcessing { get; private set; }

    public bool CanAdd => !IsProcessing && NewTagId > 0;

    /// <summary>
    ///     対象エンティティに指定したタグIDを付与します。
    /// </summary>
    public async Task<bool> AddTagAsync<TItem>(int targetEntityId, CancellationToken cancellationToken = default)
        where TItem : class, IDirectTaggable
    {
        if (NewTagId <= 0 || IsProcessing)
        {
            return false;
        }

        IsProcessing = true;
        try
        {
            await _taggingService.AddTagAsync<TItem>(targetEntityId, NewTagId);
            NewTagId = 0;
            return true;
        }
        finally
        {
            IsProcessing = false;
        }
    }

    /// <summary>
    ///     対象エンティティから指定したタグIDの紐付けを解除します。
    /// </summary>
    public async Task<bool> RemoveTagAsync<TItem>(int targetEntityId, int tagId, CancellationToken cancellationToken = default)
        where TItem : class, IDirectTaggable
    {
        if (tagId <= 0 || IsProcessing)
        {
            return false;
        }

        IsProcessing = true;
        try
        {
            await _taggingService.RemoveTagAsync<TItem>(targetEntityId, tagId);
            return true;
        }
        finally
        {
            IsProcessing = false;
        }
    }
}