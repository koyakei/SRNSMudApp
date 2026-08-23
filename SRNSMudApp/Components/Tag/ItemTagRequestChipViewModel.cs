using SRNSMudApp.Data;

namespace SRNSMudApp.Components.Tag;

// 親名前空間 Tag より先に Data.Item 型を解決させるため、エイリアスを置く
using Item = SRNSMudApp.Data.Item;

/// <summary>ItemTagRequestChip の表示状態。</summary>
public sealed record ItemTagRequestChipState(bool Visible, bool IsAdd, int ReplyCount);

/// <summary>
///     ItemTagRequestChip コンポーネントに含まれる可視性・種別・リプライ件数の計算ロジックを
///     切り出した ViewModel。DB / JS への依存を持たないため、bUnit を使わずに xUnit で直接単体テストできる。
/// </summary>
public static class ItemTagRequestChipViewModel
{
    /// <summary>
    ///     リクエスト状態からチップの表示状態を計算する。
    ///     Executed / Canceled（Proposed 以外）は非表示。
    ///     すでに関連付けが存在する追加リクエスト、または関連付けが存在しない削除リクエストも
    ///     非表示（放置されているリクエスト対策）。
    /// </summary>
    public static ItemTagRequestChipState Compute(TaggingRequestEntity request, Item item)
    {
        // Proposed 以外（Executed / Canceled）は非表示
        if (request.Status is not TradeStatus.Proposed)
        {
            return new ItemTagRequestChipState(Visible: false, IsAdd: false, ReplyCount: 0);
        }

        var isAdd = request.RequestType == TaggingRequestType.Add;
        var hasTag = item.TagRelations?.Any(tr => tr.TagId == request.RequestedTagId) ?? false;

        // すでに関連付けが存在する追加リクエスト、または関連付けが存在しない削除リクエストは
        // 非表示（放置されているリクエスト対策）
        return (isAdd, hasTag) switch
        {
            (true, true) => new ItemTagRequestChipState(Visible: false, IsAdd: true, ReplyCount: 0),
            (false, false) => new ItemTagRequestChipState(Visible: false, IsAdd: false, ReplyCount: 0),
            _ => new ItemTagRequestChipState(
                Visible: true,
                IsAdd: isAdd,
                ReplyCount: request.Replies?.Count ?? 0)
        };
    }
}