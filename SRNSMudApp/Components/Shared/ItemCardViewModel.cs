#region

using System.Text.RegularExpressions;

using SRNSMudApp.Data;

#endregion

namespace SRNSMudApp.Components.Shared;

/// <summary>
/// ItemCard コンポーネントに含まれる純粋なビジネスロジックを切り出した ViewModel。
/// UI への依存を持たないため、bUnit を使わずに xUnit で直接単体テストできる。
/// </summary>
public static class ItemCardViewModel
{
    public class RequestInfo
    {
        public bool IsTaggingRequest { get; set; }
        public TaggingRequestType? RequestType { get; set; }
        public int? TargetItemId { get; set; }
        public string? TargetItemContent { get; set; }
        public int? TargetTagId { get; set; }
        public string? TargetTagName { get; set; }
    }

    public static RequestInfo GetRequestInfo(SRNSMudApp.Data.Item item)
    {
        if (item.AsRequestOf == null) return new RequestInfo { IsTaggingRequest = false };
        return new RequestInfo
        {
            IsTaggingRequest = true,
            RequestType = item.AsRequestOf.RequestType,
            TargetItemId = item.AsRequestOf.TargetItemId,
            TargetItemContent = item.AsRequestOf.TargetItem?.Content,
            TargetTagId = item.AsRequestOf.RequestedTagId,
            TargetTagName = item.AsRequestOf.RequestedTag?.Name
        };
    }

    // ReSharper disable once InconsistentNaming
    private static readonly Regex UrlRegex =
        new(@"https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6}\b([-a-zA-Z0-9()@:%_\+.~#?&//=]*)",
            RegexOptions.Compiled);

    /// <summary>
    /// フォーカス状態に応じた ItemCard のインラインスタイル文字列を返す。
    /// </summary>
    public static string GetItemCardStyle(bool isFocused)
    {
        var borderColor = isFocused ? "var(--mud-palette-primary)" : "var(--mud-palette-lines-default)";
        var borderWidth = isFocused ? "2px" : "1px";
        return $"background: var(--mud-palette-surface); border-color: {borderColor}; border-width: {borderWidth}; border-style: solid; transition: border 0.2s ease;";
    }

    /// <summary>
    /// アイテムの TagRelation コレクションから投票スコア（good - bad）を計算する。
    /// </summary>
    public static int GetItemScore(IEnumerable<TagRelation>? tagRelations)
    {
        if (tagRelations == null) return 0;
        var relations = tagRelations.ToList();
        var goodCount = relations.Count(tr => tr.Tag?.Name == "good" && tr.Tag?.IsSystem == true);
        var badCount = relations.Count(tr => tr.Tag?.Name == "bad" && tr.Tag?.IsSystem == true);
        return goodCount - badCount;
    }

    /// <summary>
    /// 現在のユーザーがアイテムにアップボート済みかどうかを返す。
    /// </summary>
    public static bool IsItemUpvoted(IEnumerable<TagRelation>? tagRelations, string? currentUserId, int? goodTagId)
    {
        if (!goodTagId.HasValue || string.IsNullOrEmpty(currentUserId)) return false;
        return tagRelations?.Any(tr => tr.TagId == goodTagId.Value && tr.OwnerId == currentUserId) == true;
    }

    /// <summary>
    /// 現在のユーザーがアイテムにダウンボート済みかどうかを返す。
    /// </summary>
    public static bool IsItemDownvoted(IEnumerable<TagRelation>? tagRelations, string? currentUserId, int? badTagId)
    {
        if (!badTagId.HasValue || string.IsNullOrEmpty(currentUserId)) return false;
        return tagRelations?.Any(tr => tr.TagId == badTagId.Value && tr.OwnerId == currentUserId) == true;
    }

    /// <summary>
    /// 指定したリレーションを現在のユーザーが変更できるかどうかを返す。
    /// </summary>
    public static bool CanModifyRelation(string? relationOwnerId, string? currentUserId)
        => !string.IsNullOrEmpty(currentUserId) && relationOwnerId == currentUserId;

    /// <summary>
    /// テキストから URL を抽出して返す。重複は除去される。
    /// </summary>
    public static List<string> ExtractUrls(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        List<string> results = [];
        var matches = UrlRegex.Matches(text);
        foreach (Match match in matches)
        {
            if (!results.Contains(match.Value))
                results.Add(match.Value);
        }
        return results;
    }

    /// <summary>
    /// オーナー名を最大7文字に短縮して返す。null/空の場合は「不明」を返す。
    /// </summary>
    public static string GetShortOwnerName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "不明";
        return name.Length > 7 ? name[..7] : name;
    }

    /// <summary>
    /// タグチップに表示するウェイト文字列を生成する（イベントの差分表示を含む）。
    /// </summary>
    public static string GetTagDisplayWeight(TagRelation relation, TimelineEvent? highlightEvent)
    {
        var isUpdated = highlightEvent?.EventType == "Update";
        var isInserted = highlightEvent?.EventType == "Insert";
        var isDeleted = highlightEvent?.EventType == "Delete";

        if (isUpdated || isInserted)
            return $"{highlightEvent?.PreviousWeight ?? 0} → {highlightEvent?.NewWeight}";
        if (isDeleted)
            return $"{highlightEvent?.PreviousWeight}";
        return relation.Weight.ToString();
    }

    /// <summary>
    /// タグチップの背景色を返す。
    /// </summary>
    public static string GetTagChipBackground(
        TagRelation relation,
        TimelineEvent? highlightEvent,
        string? currentUserId,
        string[] myChipBackgrounds)
    {
        var isDeleted = highlightEvent?.EventType == "Delete";
        if (isDeleted) return "#E0E0E0";
        if (highlightEvent != null) return "#FFEB3B";
        if (relation.OwnerId == currentUserId)
            return myChipBackgrounds.Length > 0 ? myChipBackgrounds[0] : "#EEEDFE";
        return "#FFF9C4";
    }

    /// <summary>
    /// タグチップのテキスト色を返す。
    /// </summary>
    public static string GetTagChipTextColor(
        TagRelation relation,
        TimelineEvent? highlightEvent,
        string? currentUserId,
        string[] myChipTextColors)
    {
        var isDeleted = highlightEvent?.EventType == "Delete";
        if (isDeleted) return "#9E9E9E";
        if (highlightEvent != null) return "#F57F17";
        if (relation.OwnerId == currentUserId)
            return myChipTextColors.Length > 0 ? myChipTextColors[0] : "#26215C";
        return "#5C4B00";
    }
}
