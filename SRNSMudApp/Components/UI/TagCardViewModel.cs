
using System.Globalization;

using MudBlazor;

using SRNSMudApp.Data;

namespace SRNSMudApp.Components.UI;

/// <summary>
///     チップ 1 個分の表示情報。
/// </summary>
public record TagCardChipDisplayInfo
{
    public bool IsDeleted { get; init; }
    public bool IsInserted { get; init; }
    public bool IsUpdated { get; init; }
    public bool WeightIncreased { get; init; }
    public string BackgroundColor { get; init; } = "#FFF9C4";
    public string TextColor { get; init; } = "#5C4B00";
    public string DisplayWeight { get; init; } = "";
    public Color AddButtonColor { get; init; } = Color.Inherit;
}

/// <summary>
///     タグチップ一覧の表示計算結果。
/// </summary>
public record TagCardDisplayList(
    IReadOnlyList<TagRelationToTag> TagsToDisplay,
    bool HasManyTags,
    int HiddenCount);

/// <summary>
///     TagCard コンポーネントに含まれる純粋な表示ロジックを切り出した ViewModel。
///     UI への依存を持たないため、bUnit を使わずに xUnit で直接単体テストできる。
/// </summary>
public static class TagCardViewModel
{
    public const int DisplayLimit = 4;
    private const int HasManyThreshold = 5;

    private static readonly string[] ChipBackgrounds = ["#EEEDFE"];
    private static readonly string[] ChipTextColors = ["#26215C"];

    /// <summary>good / bad システムタグへのリレーション数からスコアを計算する。</summary>
    public static int GetTagScore(Data.Tag tag)
    {
        var goodCount =
            tag.TargetTagRelations?.Count(tr => tr.Tag?.Name == "good" && tr.Tag?.IsSystem == true) ?? 0;
        var badCount =
            tag.TargetTagRelations?.Count(tr => tr.Tag?.Name == "bad" && tr.Tag?.IsSystem == true) ?? 0;
        return goodCount - badCount;
    }

    public static bool IsTagUpvoted(Data.Tag tag, int? currentUserGoodTagId, string currentUserId)
    {
        return currentUserGoodTagId.HasValue &&
               tag.TargetTagRelations?.Any(tr =>
                   tr.TagId == currentUserGoodTagId.Value && tr.OwnerId == currentUserId) == true;
    }

    public static bool IsTagDownvoted(Data.Tag tag, int? currentUserBadTagId, string currentUserId)
    {
        return currentUserBadTagId.HasValue &&
               tag.TargetTagRelations?.Any(tr =>
                   tr.TagId == currentUserBadTagId.Value && tr.OwnerId == currentUserId) == true;
    }

    /// <summary>
    ///     表示対象のタグリレーション一覧を構築する。
    ///     システムタグを除外し、削除イベントがあれば仮想的なリレーションとして追加する。
    /// </summary>
    public static TagCardDisplayList BuildDisplayList(Data.Tag tag, IReadOnlyList<TimelineEvent>? highlightEvents, bool areTagsExpanded)
    {
        List<TagRelationToTag> allTags = tag.TargetTagRelations?
            .Where(tr => tr.Tag is { IsSystem: false })
            .OrderByDescending(tr => tr.Weight)
            .ToList() ?? [];

        // 削除されたタグが TimelineEvent に含まれている場合は、仮想的な TagRelationToTag としてリストに追加して表示する
        if (highlightEvents is not null)
        {
            foreach (TimelineEvent ev in highlightEvents.Where(e => e.EventType == "Delete"))
            {
                if (ev.FollowedTag is not null && allTags.All(t => t.TagId != ev.FollowedTagId))
                {
                    allTags.Add(new TagRelationToTag
                    {
                        TagId = ev.FollowedTagId,
                        Tag = ev.FollowedTag,
                        Weight = ev.PreviousWeight,
                        OwnerId = ev.OwnerId // fake owner
                    });
                }
            }
        }

        var hasManyTags = allTags.Count >= HasManyThreshold;
        List<TagRelationToTag> tagsToDisplay =
            areTagsExpanded || !hasManyTags ? allTags : [.. allTags.Take(DisplayLimit)];
        var hiddenCount = hasManyTags ? allTags.Count - DisplayLimit : 0;

        return new TagCardDisplayList(tagsToDisplay, hasManyTags, hiddenCount);
    }

    /// <summary>チップ 1 個分の色・Weight 表示などの表示情報を計算する。</summary>
    public static TagCardChipDisplayInfo GetChipDisplayInfo(
        TagRelationToTag relation,
        TimelineEvent? highlightEvent,
        bool isMyTag,
        int index)
    {
        var isDeleted = highlightEvent?.EventType == "Delete";
        var isInserted = highlightEvent?.EventType == "Insert";
        var isUpdated = highlightEvent?.EventType == "Update";
        var weightIncreased = highlightEvent != null && highlightEvent.NewWeight > highlightEvent.PreviousWeight;

        var bgColor = isDeleted
            ? "#E0E0E0"
            : highlightEvent != null
                ? "#FFEB3B"
                : isMyTag
                    ? index < ChipBackgrounds.Length ? ChipBackgrounds[index] : ChipBackgrounds[0]
                    : "#FFF9C4";
        var textColor = isDeleted
            ? "#9E9E9E"
            : highlightEvent != null
                ? "#F57F17"
                : isMyTag
                    ? index < ChipTextColors.Length ? ChipTextColors[index] : ChipTextColors[0]
                    : "#5C4B00";

        var displayWeight = isUpdated || isInserted
            ? $"{highlightEvent?.PreviousWeight} → {highlightEvent?.NewWeight}"
            : isDeleted
                ? $"{highlightEvent?.PreviousWeight}"
                : relation.Weight.ToString(CultureInfo.InvariantCulture);

        return new TagCardChipDisplayInfo
        {
            IsDeleted = isDeleted,
            IsInserted = isInserted,
            IsUpdated = isUpdated,
            WeightIncreased = weightIncreased,
            BackgroundColor = bgColor,
            TextColor = textColor,
            DisplayWeight = displayWeight,
            AddButtonColor = weightIncreased ? Color.Success : Color.Inherit
        };
    }

    /// <summary>親タグを設定した際に循環参照が発生するかどうかを判定する。</summary>
    public static bool HasParentCycle(Data.Tag parentTag, Data.Tag childTag, IReadOnlyList<Data.Tag> allTags)
    {
        // 循環参照の簡易チェック
        int? currentParent = parentTag.ParentTagId;
        bool hasCycle = false;
        while (currentParent != null && !hasCycle)
        {
            hasCycle = currentParent == childTag.Id;
            Data.Tag? p = allTags.FirstOrDefault(t => t.Id == currentParent);
            currentParent = p?.ParentTagId;
        }

        return hasCycle;
    }

    /// <summary>現在ユーザーがそのリレーションの所有者 (操作権限あり) かどうかを判定する。</summary>
    public static bool IsRelationOwner(string relationOwnerId, string currentUserId) =>
        relationOwnerId == currentUserId;

    /// <summary>自分自身を親タグに設定しようとしているかどうかを判定する。</summary>
    public static bool IsSelfParent(Data.Tag parentTag, Data.Tag childTag) => parentTag.Id == childTag.Id;

    /// <summary>同一タグへの変更 (無意味な変更) かどうかを判定する。</summary>
    public static bool IsSameTagChange(int currentTagId, int newTagId) => currentTagId == newTagId;

    /// <summary>Weight に変化があるかどうかを判定する。</summary>
    public static bool HasWeightChange(int currentWeight, int newWeight) => currentWeight != newWeight;

    public static string GetShortOwnerName(string? name)
    {
        return string.IsNullOrEmpty(name) switch
        {
            true => "不明",
            false => name.Length > 7 ? name[..7] : name
        };
    }
}