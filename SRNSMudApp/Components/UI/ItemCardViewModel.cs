#region

using System.Globalization;
using System.Text.RegularExpressions;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Components.UI;

public class RequestInfo
{
    public bool IsTaggingRequest { get; set; }
    public TaggingRequestType? RequestType { get; set; }
    public int? ProposedWeight { get; set; }
    public int? TargetItemId { get; set; }
    public string? TargetItemContent { get; set; }
    public int? TargetTagId { get; set; }
    public string? TargetTagName { get; set; }
    public TradeStatus? Status { get; set; }
}

/// <summary>
///     ItemCard コンポーネントに含まれる純粋なビジネスロジックを切り出した ViewModel。
///     UI への依存を持たないため、bUnit を使わずに xUnit で直接単体テストできる。
/// </summary>
public static class ItemCardViewModel
{
    // ReSharper disable once InconsistentNaming
    private static readonly Regex UrlRegex =
        new(@"https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6}\b([-a-zA-Z0-9()@:%_\+.~#?&//=]*)",
            RegexOptions.Compiled);

    public static RequestInfo GetRequestInfo(Data.Item item)
    {
        return item.AsRequestOf == null
            ? new RequestInfo { IsTaggingRequest = false }
            : new RequestInfo
            {
                IsTaggingRequest = true,
                RequestType = item.AsRequestOf.RequestType,
                TargetItemId = item.AsRequestOf.TargetItemId,
                TargetItemContent = item.AsRequestOf.TargetItem?.Content,
                TargetTagId = item.AsRequestOf.RequestedTagId,
                TargetTagName = item.AsRequestOf.RequestedTag?.Name,
                Status = item.AsRequestOf.Status,
                ProposedWeight = item.AsRequestOf.ProposedWeight
            };
    }

    /// <summary>
    ///     フォーカス状態に応じた ItemCard のインラインスタイル文字列を返す。
    /// </summary>
    public static string GetItemCardStyle(bool isFocused)
    {
        var borderColor = isFocused ? "var(--mud-palette-primary)" : "var(--mud-palette-lines-default)";
        var borderWidth = isFocused ? "2px" : "1px";
        return
            $"background: var(--mud-palette-surface); border-color: {borderColor}; border-width: {borderWidth}; border-style: solid; transition: border 0.2s ease;";
    }

    /// <summary>
    ///     アイテムの TagRelation コレクションから投票スコア（good の Weight の合計）を計算する。
    /// </summary>
    public static int GetItemScore(IEnumerable<TagRelation>? tagRelations)
    {
        return tagRelations?
            .Where(tr => tr.Tag?.Name == "good" && tr.Tag?.IsSystem == true)
            .Sum(tr => tr.Weight) ?? 0;
    }

    /// <summary>
    ///     現在のユーザーがアイテムにアップボート済みかどうかを返す。
    /// </summary>
    public static bool IsItemUpvoted(IEnumerable<TagRelation>? tagRelations, string? currentUserId, int? goodTagId)
    {
        return goodTagId.HasValue && !string.IsNullOrEmpty(currentUserId) &&
               tagRelations?.Any(tr => tr.TagId == goodTagId.Value && tr.OwnerId == currentUserId && tr.Weight > 0) ==
               true;
    }

    /// <summary>
    ///     現在のユーザーがアイテムにダウンボート済みかどうかを返す。
    /// </summary>
    public static bool IsItemDownvoted(IEnumerable<TagRelation>? tagRelations, string? currentUserId, int? goodTagId)
    {
        return goodTagId.HasValue && !string.IsNullOrEmpty(currentUserId) &&
               tagRelations?.Any(tr => tr.TagId == goodTagId.Value && tr.OwnerId == currentUserId && tr.Weight < 0) ==
               true;
    }

    /// <summary>
    ///     指定したリレーションを現在のユーザーが変更できるかどうかを返す。
    /// </summary>
    public static bool CanModifyRelation(string? relationOwnerId, string? currentUserId)
        => !string.IsNullOrEmpty(currentUserId) && relationOwnerId == currentUserId;

    /// <summary>
    ///     テキストから URL を抽出して返す。重複は除去される。
    /// </summary>
    public static IReadOnlyList<string> ExtractUrls(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        List<string> results = [];
        MatchCollection matches = UrlRegex.Matches(text);
        foreach (Match match in matches)
        {
            if (!results.Contains(match.Value))
            {
                results.Add(match.Value);
            }
        }

        return results;
    }

    /// <summary>
    ///     オーナー名を最大7文字に短縮して返す。null/空の場合は「不明」を返す。
    /// </summary>
    public static string GetShortOwnerName(string? name) =>
        string.IsNullOrEmpty(name) ? "不明" : name.Length > 7 ? name[..7] : name;

    /// <summary>
    ///     タグチップに表示するウェイト文字列を生成する（イベントの差分表示を含む）。
    /// </summary>
    public static string GetTagDisplayWeight(TagRelation relation, TimelineEvent? highlightEvent)
    {
        var isUpdated = highlightEvent?.EventType == "Update";
        var isInserted = highlightEvent?.EventType == "Insert";
        var isDeleted = highlightEvent?.EventType == "Delete";

        if (isUpdated || isInserted)
        {
            return $"{highlightEvent?.PreviousWeight ?? 0} → {highlightEvent?.NewWeight}";
        }

        return isDeleted
            ? highlightEvent?.PreviousWeight?.ToString(CultureInfo.InvariantCulture) ?? ""
            : relation.Weight.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     タグチップの背景色を返す。
    /// </summary>
    public static string GetTagChipBackground(
        TagRelation relation,
        TimelineEvent? highlightEvent,
        string? currentUserId,
        string[] myChipBackgrounds)
    {
        var isDeleted = highlightEvent?.EventType == "Delete";
        if (isDeleted)
        {
            return "#E0E0E0";
        }

        return highlightEvent != null
            ? "#FFEB3B"
            : relation.OwnerId == currentUserId
                ? myChipBackgrounds.Length > 0 ? myChipBackgrounds[0] : "#EEEDFE"
                : "#FFF9C4";
    }

    /// <summary>
    ///     タグチップのテキスト色を返す。
    /// </summary>
    public static string GetTagChipTextColor(
        TagRelation relation,
        TimelineEvent? highlightEvent,
        string? currentUserId,
        string[] myChipTextColors)
    {
        var isDeleted = highlightEvent?.EventType == "Delete";
        if (isDeleted)
        {
            return "#9E9E9E";
        }

        return highlightEvent != null
            ? "#F57F17"
            : relation.OwnerId == currentUserId
                ? myChipTextColors.Length > 0 ? myChipTextColors[0] : "#26215C"
                : "#5C4B00";
    }
}