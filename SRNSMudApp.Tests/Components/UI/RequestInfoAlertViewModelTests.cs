using MudBlazor;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;

using Xunit;

namespace SRNSMudApp.Tests.Components.UI;

/// <summary>
///     RequestInfoAlertViewModel の単体テスト。
///     ステータステキスト・カラー・ラベル文言・アイコン種別の全分岐を bUnit なしで検証する。
/// </summary>
public class RequestInfoAlertViewModelTests
{
    // --- StatusText ---

    [Theory]
    [InlineData(TradeStatus.Executed, "処理済み")]
    [InlineData(TradeStatus.Canceled, "取り下げ済み")]
    [InlineData(TradeStatus.Rejected, "却下済み")]
    [InlineData(TradeStatus.Proposed, "承認待ち")]
    public void StatusText_MapsAllStatuses(TradeStatus status, string expected)
    {
        Assert.Equal(expected, RequestInfoAlertViewModel.StatusText(status));
    }

    [Fact]
    public void StatusText_NullOrUnknown_ReturnsUnknown()
    {
        Assert.Equal("不明", RequestInfoAlertViewModel.StatusText(null));
        Assert.Equal("不明", RequestInfoAlertViewModel.StatusText((TradeStatus)999));
    }

    // --- StatusColor ---

    [Theory]
    [InlineData(TradeStatus.Executed, Color.Success)]
    [InlineData(TradeStatus.Canceled, Color.Default)]
    [InlineData(TradeStatus.Rejected, Color.Error)]
    [InlineData(TradeStatus.Proposed, Color.Warning)]
    public void StatusColor_MapsAllStatuses(TradeStatus status, Color expected)
    {
        Assert.Equal(expected, RequestInfoAlertViewModel.StatusColor(status));
    }

    [Fact]
    public void StatusColor_NullOrUnknown_ReturnsDefault()
    {
        Assert.Equal(Color.Default, RequestInfoAlertViewModel.StatusColor(null));
        Assert.Equal(Color.Default, RequestInfoAlertViewModel.StatusColor((TradeStatus)999));
    }

    // --- RequestTypeLabel ---

    [Theory]
    [InlineData(TaggingRequestType.Add, 1, "タグ追加リクエスト +1")]
    [InlineData(TaggingRequestType.Add, 3, "タグ追加リクエスト +3")]
    // 注意: 元ロジックでは DecreaseWeight は1番目の switch のデフォルトアームに流れるため
    // 「タグ削除」ではなく「タグ追加」ラベル + "-N" サフィックスになる（等価維持のため修正しない）
    [InlineData(TaggingRequestType.DecreaseWeight, 2, "タグ追加リクエスト -2")]
    [InlineData(TaggingRequestType.Remove, null, "タグ削除リクエスト")]
    public void RequestTypeLabel_GeneratesLabelWithWeight(
        TaggingRequestType? requestType, int? proposedWeight, string expected)
    {
        Assert.Equal(
            expected,
            RequestInfoAlertViewModel.RequestTypeLabel(requestType, proposedWeight));
    }

    [Fact]
    public void RequestTypeLabel_NullRequestType_ReturnsAddLabelWithoutSuffix()
    {
        // 元ロジック: null はどちらの switch もデフォルトアームへ流れる
        Assert.Equal(
            "タグ追加リクエスト",
            RequestInfoAlertViewModel.RequestTypeLabel(null, null));
    }

    // --- ResolveIconKind ---

    [Theory]
    [InlineData(true, false, false, TradeStatus.Proposed, RequestInfoIconKind.Cancel)]
    [InlineData(true, true, true, TradeStatus.Proposed, RequestInfoIconKind.Cancel)]
    [InlineData(false, true, true, TradeStatus.Proposed, RequestInfoIconKind.ApproveAndReject)]
    [InlineData(false, true, false, TradeStatus.Proposed, RequestInfoIconKind.Approve)]
    [InlineData(false, false, true, TradeStatus.Proposed, RequestInfoIconKind.Reject)]
    [InlineData(false, false, false, TradeStatus.Canceled, RequestInfoIconKind.Canceled)]
    [InlineData(false, false, false, TradeStatus.Executed, RequestInfoIconKind.Executed)]
    [InlineData(false, false, false, TradeStatus.Proposed, RequestInfoIconKind.Default)]
    public void ResolveIconKind_CoversAllBranches(
        bool canCancel, bool canApprove, bool canReject,
        TradeStatus status, RequestInfoIconKind expected)
    {
        Assert.Equal(
            expected,
            RequestInfoAlertViewModel.ResolveIconKind(canCancel, canApprove, canReject, status));
    }
}
