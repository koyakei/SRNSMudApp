using SRNSMudApp.Data;

namespace SRNSMudApp.Services;

/// <summary>
///     タグ追加操作の結果。
/// </summary>
public enum TagAddOutcome
{
    /// <summary>操作なし、キャンセル、または失敗。</summary>
    None,

    /// <summary>直接タグがアイテムへ付与された。</summary>
    AddedDirectly,

    /// <summary>タグ付けコントラクトの提案が送信された。</summary>
    ContractProposed
}

/// <summary>
///     ItemCard におけるタグ追加およびコントラクト提案モーダル操作を調整するコーディネーター。
/// </summary>
public interface IItemCardTagCoordinator
{
    /// <summary>
    ///     タグ追加ダイアログを表示し、ユーザー選択に応じて直接付与またはコントラクト提案を実行する。
    /// </summary>
    Task<TagAddOutcome> PromptAndAddTagAsync(Item item, string currentUserId);
}