using MudBlazor;

using SRNSMudApp.Data;

// IDE0072: null 許容 enum の switch 式は `_ =>` デフォルトアーム付きでも
// 「Populate switch」として検出される解析器の誤検知のため抑制する。
#pragma warning disable IDE0072

namespace SRNSMudApp.Components.UI;

/// <summary>アイコン領域の表示種別。</summary>
public enum RequestInfoIconKind
{
    Cancel,
    Approve,
    Reject,
    ApproveAndReject,
    Canceled,
    Executed,
    Default
}

/// <summary>
///     RequestInfoAlert コンポーネントに含まれるステータス表示テキスト・カラー・ラベル文言・
///     アイコン種別の生成ロジックを切り出した ViewModel。
///     DB / JS への依存を持たないため、bUnit を使わずに xUnit で直接単体テストできる。
/// </summary>
public static class RequestInfoAlertViewModel
{
    /// <summary>ステータスの表示テキストを返す。</summary>
    public static string StatusText(TradeStatus? status) => status switch
    {
        TradeStatus.Executed => "処理済み",
        TradeStatus.Canceled => "取り下げ済み",
        TradeStatus.Rejected => "却下済み",
        TradeStatus.Proposed => "承認待ち",
        _ => "不明"
    };

    /// <summary>ステータスに対応するチップのカラーを返す。</summary>
    public static Color StatusColor(TradeStatus? status) => status switch
    {
        TradeStatus.Executed => Color.Success,
        TradeStatus.Canceled => Color.Default,
        TradeStatus.Rejected => Color.Error,
        TradeStatus.Proposed => Color.Warning,
        _ => Color.Default
    };

    /// <summary>
    ///     リクエスト種別と提案ウェイトからラベル文言を生成する。
    /// </summary>
    public static string RequestTypeLabel(TaggingRequestType? requestType, int? proposedWeight)
    {
        var text = requestType switch
        {
            TaggingRequestType.Remove => "タグ削除リクエスト",
            _ => "タグ追加リクエスト"
        };

        return requestType switch
        {
            TaggingRequestType.Add => text + $" +{proposedWeight}",
            TaggingRequestType.DecreaseWeight => text + $" -{proposedWeight}",
            _ => text
        };
    }

    /// <summary>
    ///     操作可否フラグとステータスからアイコン領域の表示種別を解決する。
    ///     マークアップ側のタプルスイッチ ((CanCancel, CanApprove || CanReject, Status)) と等価。
    /// </summary>
    // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
    public static RequestInfoIconKind ResolveIconKind(
        bool canCancel, bool canApprove, bool canReject, TradeStatus? status)
    {
        return (canCancel, canApprove || canReject, status) switch
        {
            (true, _, _) => RequestInfoIconKind.Cancel,

            // 承認と却下の両方が可能な場合は両ボタンを並べて表示する
            (false, true, _) when canApprove && canReject => RequestInfoIconKind.ApproveAndReject,
            (false, true, _) when canApprove => RequestInfoIconKind.Approve,
            (false, true, _) when canReject => RequestInfoIconKind.Reject,

            (false, false, TradeStatus.Canceled) => RequestInfoIconKind.Canceled,
            (false, false, TradeStatus.Executed) => RequestInfoIconKind.Executed,
            _ => RequestInfoIconKind.Default
        };
    }
}