using System.Diagnostics.CodeAnalysis;

namespace SRNSMudApp.Components.UI;

/// <summary>
///     UrlPreviewCard に含まれる純粋なロジック（テキストフラグメント解析）を切り出した ViewModel。
///     UI への依存を持たないため、bUnit を使わずに xUnit で直接単体テストできる。
/// </summary>
// IDE0010: union 型・enum の網羅的 switch に対する「Populate switch」は解析器の誤検知のため抑制する。
#pragma warning disable IDE0010

public static class UrlPreviewCardViewModel
{
    /// <summary>
    ///     URL の # :~:text= フラグメントから選択テキスト部分を抽出して返す。
    /// </summary>
    [SuppressMessage("Design", "CA1054:Change the type of parameter from string to System.Uri",
        Justification = "URL はフラグメント文字列操作の対象であり Uri 型へ変換する必要がない")]
    public static string? ExtractTextFragment(string url)
    {
        const string marker = "#:~:text=";
        var markerIndex = url.IndexOf(marker, StringComparison.Ordinal);

        return markerIndex switch
        {
            < 0 => null,
            _ => ProcessTextFragment(url, markerIndex, marker)
        };
    }

    private static string? ProcessTextFragment(string url, int markerIndex, string marker)
    {
        var raw = url[(markerIndex + marker.Length)..];
        var parts = raw.Split(["&text="], StringSplitOptions.RemoveEmptyEntries);

        List<string> results = [];
        foreach (var part in parts)
        {
            var text = TryDecode(part);

            var suffixSep = text.LastIndexOf(",-", StringComparison.Ordinal);
            text = suffixSep switch
            {
                >= 0 => text[..suffixSep],
                _ => text
            };

            var prefixSep = text.IndexOf("-,", StringComparison.Ordinal);
            text = prefixSep switch
            {
                >= 0 => text[(prefixSep + 2)..],
                _ => text
            };

            var segments = text.Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();

            switch (segments.Length)
            {
                case > 0:
                    results.Add(string.Join("〜", segments));
                    break;
            }
        }

        return results.Count switch
        {
            > 0 => string.Join(" | ", results),
            _ => null
        };
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "デコード不能なフラグメントは元の文字列をそのまま返すフォールバック")]
    private static string TryDecode(string part)
    {
        try
        {
            return Uri.UnescapeDataString(part);
        }
        catch
        {
            return part;
        }
    }

    /// <summary>
    ///     指定された URL が内部ルート（相対パス、または現在のベース URI と同一オリジンのパス）であるかを判定し、
    ///     内部ルートの場合は遷移先パス（クエリ・フラグメント含む）を出力する。
    /// </summary>
    [SuppressMessage("Design", "CA1054:Change the type of parameter from string to System.Uri",
        Justification = "URL 文字列からの相対パス解析処理のため")]
    public static bool TryGetInternalRoute(string? url, string? baseUri, [NotNullWhen(true)] out string? internalRoute)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            internalRoute = null;
            return false;
        }

        var trimmedUrl = url.Trim();

        // 1. スラッシュから始まる相対パス ("/ItemDetail/123" など)
        if (trimmedUrl.StartsWith('/', StringComparison.Ordinal) && !trimmedUrl.StartsWith("//", StringComparison.Ordinal))
        {
            internalRoute = trimmedUrl;
            return true;
        }

        // 2. 絶対 URI の場合、baseUri と同一オリジンかを判定
        if (Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var targetUri) &&
            !string.IsNullOrEmpty(baseUri) &&
            Uri.TryCreate(baseUri, UriKind.Absolute, out var baseParsed))
        {
            if (string.Equals(targetUri.Scheme, baseParsed.Scheme, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(targetUri.Authority, baseParsed.Authority, StringComparison.OrdinalIgnoreCase))
            {
                // 同一オリジンのため、相対パス部分（PathAndQuery + Fragment）を抽出
                internalRoute = $"{targetUri.PathAndQuery}{targetUri.Fragment}";
                return true;
            }
        }

        internalRoute = null;
        return false;
    }
}