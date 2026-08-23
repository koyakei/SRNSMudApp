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

        var results = new List<string>();
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
}