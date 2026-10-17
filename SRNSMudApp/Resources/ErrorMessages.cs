using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Resources;

namespace SRNSMudApp.Resources;

/// <summary>
///     ErrorMessages.resx に対応する型安全なリソースアクセサークラス。
///     Resource Pattern に従い、ハードコード文字列の排除とローカライズをサポートする。
/// </summary>
[SuppressMessage("Performance", "CA1863:Use 'CompositeFormat'", Justification = "Resource strings can change dynamically with culture")]
public static class ErrorMessages
{
    private static readonly ResourceManager ResourceManager =
        new("SRNSMudApp.Resources.ErrorMessages", typeof(ErrorMessages).Assembly);

    public static string GetString(string name, CultureInfo? culture = null) =>
        ResourceManager.GetString(name, culture ?? CultureInfo.CurrentUICulture) ?? name;

    public static string InvalidTradeStatusTransition =>
        GetString(nameof(InvalidTradeStatusTransition));

    public static string FormatInvalidTradeStatusTransition(object fromStatus, object toStatus) =>
        string.Format(CultureInfo.CurrentCulture, InvalidTradeStatusTransition, fromStatus, toStatus);

    public static string InvalidReportStatusTransition =>
        GetString(nameof(InvalidReportStatusTransition));

    public static string FormatInvalidReportStatusTransition(object fromStatus, object toStatus) =>
        string.Format(CultureInfo.CurrentCulture, InvalidReportStatusTransition, fromStatus, toStatus);

    public static string ReportNotFound =>
        GetString(nameof(ReportNotFound));

    public static string FormatReportNotFound(object reportId) =>
        string.Format(CultureInfo.CurrentCulture, ReportNotFound, reportId);

    public static string UnsupportedReportStatus =>
        GetString(nameof(UnsupportedReportStatus));

    public static string NotAuthorizedToEdit =>
        GetString(nameof(NotAuthorizedToEdit));

    public static string NotAuthorizedToDelete =>
        GetString(nameof(NotAuthorizedToDelete));

    public static string LoginRequired =>
        GetString(nameof(LoginRequired));

    public static string TagAlreadyAdded =>
        GetString(nameof(TagAlreadyAdded));

    public static string SystemTagRetrievalFailed =>
        GetString(nameof(SystemTagRetrievalFailed));

    public static string ContractCancelSuccess =>
        GetString(nameof(ContractCancelSuccess));

    public static string ContractApproveSuccess =>
        GetString(nameof(ContractApproveSuccess));

    public static string RequestNotFound =>
        GetString(nameof(RequestNotFound));

    public static string NotAuthorizedToApprove =>
        GetString(nameof(NotAuthorizedToApprove));

    public static string NotAuthorizedToReject =>
        GetString(nameof(NotAuthorizedToReject));

    public static string RequestAlreadyProcessed =>
        GetString(nameof(RequestAlreadyProcessed));

    public static string TargetTagNotFound =>
        GetString(nameof(TargetTagNotFound));

    public static string InsufficientPermissionBalance =>
        GetString(nameof(InsufficientPermissionBalance));

    public static string FormatInsufficientPermissionBalance(object needed, object current) =>
        string.Format(CultureInfo.CurrentCulture, InsufficientPermissionBalance, needed, current);

    public static string SplitTextRequired =>
        GetString(nameof(SplitTextRequired));

    public static string SplitRequesterRequired =>
        GetString(nameof(SplitRequesterRequired));

    public static string SplitItemNotFound =>
        GetString(nameof(SplitItemNotFound));

    public static string SplitSelfItemNotAllowed =>
        GetString(nameof(SplitSelfItemNotAllowed));

    public static string SplitSelectedTextNotInContent =>
        GetString(nameof(SplitSelectedTextNotInContent));

    public static string SplitQuoteItemNotFound =>
        GetString(nameof(SplitQuoteItemNotFound));

    public static string SplitDuplicateRequestExists =>
        GetString(nameof(SplitDuplicateRequestExists));

    public static string SplitTargetItemRequired =>
        GetString(nameof(SplitTargetItemRequired));

    public static string OwnerUserRequired =>
        GetString(nameof(OwnerUserRequired));

    public static string NotAuthorizedToSplit =>
        GetString(nameof(NotAuthorizedToSplit));

    public static string SplitRequestNotProposedForApprove =>
        GetString(nameof(SplitRequestNotProposedForApprove));

    public static string FormatSplitRequestNotProposedForApprove(object status) =>
        string.Format(CultureInfo.CurrentCulture, SplitRequestNotProposedForApprove, status);

    public static string SplitOriginalItemNotFound =>
        GetString(nameof(SplitOriginalItemNotFound));

    public static string SplitOriginalItemContentChanged =>
        GetString(nameof(SplitOriginalItemContentChanged));

    public static string SplitRequestNotProposedForReject =>
        GetString(nameof(SplitRequestNotProposedForReject));

    public static string FormatSplitRequestNotProposedForReject(object status) =>
        string.Format(CultureInfo.CurrentCulture, SplitRequestNotProposedForReject, status);

    public static string NotAuthorizedToCancel =>
        GetString(nameof(NotAuthorizedToCancel));

    public static string SplitRequestNotProposedForCancel =>
        GetString(nameof(SplitRequestNotProposedForCancel));

    public static string FormatSplitRequestNotProposedForCancel(object status) =>
        string.Format(CultureInfo.CurrentCulture, SplitRequestNotProposedForCancel, status);
}