using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

using ItemEntity = SRNSMudApp.Data.Item;

namespace SRNSMudApp.Components.Diagram;

/// <summary>
///     Blazor.Diagrams 上で Item を表すカスタムノードモデル。
///     未展開時のコンパクト表示および展開時の ItemCard フル表示の双方向表示状態を管理する。
/// </summary>
public class ItemNode : NodeModel
{
    /// <summary>
    ///     ノードに関連付けられた Item エンティティを取得する。
    /// </summary>
    public ItemEntity Item { get; }

    /// <summary>
    ///     ダイアグラムの表示コンテキストに含まれるすべての Item 一覧を取得する。
    ///     内部リンク解決時の即時参照に使用される。
    /// </summary>
    public IReadOnlyList<ItemEntity> AllContextItems { get; }

    /// <summary>
    ///     ItemCard の展開表示中かどうかを示す値を取得または設定する。
    /// </summary>
    public bool IsExpanded { get; set; }

    /// <summary>
    ///     このノードを画面表示から消す（非表示にする）ことを要求するコールバック。
    /// </summary>
    public Action<ItemEntity>? RequestHideNode { get; set; }

    /// <summary>
    ///     <see cref="ItemNode"/> クラスの新しいインスタンスを初期化する。
    /// </summary>
    /// <param name="item">表示対象の Item エンティティ。</param>
    /// <param name="allContextItems">ダイアグラム上のコンテキストに含まれる Item 一覧。</param>
    /// <param name="position">ノードの初期配置座標（省略時は null）。</param>
    public ItemNode(ItemEntity item, IReadOnlyList<ItemEntity>? allContextItems = null, Point? position = null)
        : base(position)
    {
        ArgumentNullException.ThrowIfNull(item);
        Item = item;
        AllContextItems = allContextItems ?? [];
        _ = AddPort(PortAlignment.Left);
        _ = AddPort(PortAlignment.Right);
        _ = AddPort(PortAlignment.Top);
        _ = AddPort(PortAlignment.Bottom);
    }
}