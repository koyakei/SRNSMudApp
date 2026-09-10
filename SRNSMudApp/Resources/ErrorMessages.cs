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
}