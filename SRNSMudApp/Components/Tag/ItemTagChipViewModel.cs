using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Components.Tag;

/// <summary>ItemTagChip の表示に必要な算出済み値のスナップショット。</summary>
public sealed record ItemTagChipDisplay
{
    public int TagId { get; init; }
    public bool IsDeleted { get; init; }
    public bool IsMyTag { get; init; }
    public string BackgroundColor { get; init; } = "";
    public string TextColor { get; init; } = "";
    public string FontWeight { get; init; } = "";
    public bool WeightIncreased { get; init; }
    public string? TagName { get; init; }
    public string? OwnerName { get; init; }
    public string ChipId { get; init; } = "";
    public string DisplayWeight { get; init; } = "";
    public MudBlazor.Color AddButtonColor { get; init; } = MudBlazor.Color.Inherit;
    public IReadOnlyList<TagRelationToTag> AddedTags { get; init; } = [];
}

/// <summary>
///     ItemTagChip の純粋な表示計算 (ハイライト状態による色・文字色・Weight 表記など) を集約する静的 ViewModel。
///     bUnit を介さない xUnit 単体テストを可能にする。
/// </summary>
public static class ItemTagChipViewModel
{
    /// <summary>パラメータ群からチップの表示状態を算出する。</summary>
    public static ItemTagChipDisplay GetDisplay(
        TagRelation relation,
        int itemId,
        string currentUserId,
        TimelineEvent? highlightEvent,
        int chipIndex,
        IReadOnlyList<string> chipBackgrounds,
        IReadOnlyList<string> chipTextColors,
        bool showNameAndOwner,
        IReadOnlyList<TagRelationToTag> allTagRelationsToTags)
    {
        var tagId = relation.Tag?.Id ?? -1;
        var isDeleted = highlightEvent?.EventType == "Delete";
        var isUpdated = highlightEvent?.EventType == "Update";
        var isMyTag = relation.OwnerId == currentUserId;
        var hasHighlight = highlightEvent != null;

        var ownerUserName = relation.Tag?.Owner?.UserName ??
            (relation.Tag?.GetKind() is SystemClassificationTag ? "system" : null);

        return new ItemTagChipDisplay
        {
            TagId = tagId,
            IsDeleted = isDeleted,
            IsMyTag = isMyTag,
            BackgroundColor = GetBackgroundColor(isDeleted, hasHighlight, isMyTag, chipIndex, chipBackgrounds),
            TextColor = GetTextColor(isDeleted, hasHighlight, isMyTag, chipIndex, chipTextColors),
            FontWeight = hasHighlight ? "bold" : "normal",
            WeightIncreased = highlightEvent != null && highlightEvent.NewWeight > highlightEvent.PreviousWeight,
            TagName = showNameAndOwner ? relation.Tag?.Name ?? "不明" : null,
            OwnerName = showNameAndOwner ? ItemCardViewModel.GetShortOwnerName(ownerUserName) : null,
            ChipId = $"tag-chip-{itemId}-{tagId}",
            DisplayWeight = GetDisplayWeight(isDeleted, isUpdated, highlightEvent, relation.Weight),
            AddButtonColor = (highlightEvent != null && highlightEvent.NewWeight > highlightEvent.PreviousWeight)
                ? MudBlazor.Color.Success
                : MudBlazor.Color.Inherit,
            AddedTags =
            [
                .. (allTagRelationsToTags ?? []).Where(ttr =>
                    ttr.TargetTagId == tagId && ttr.Tag?.GetKind() is not VotingReactionTag)
            ]
        };
    }

    private static string GetBackgroundColor(
        bool isDeleted,
        bool hasHighlight,
        bool isMyTag,
        int chipIndex,
        IReadOnlyList<string> backgrounds) =>
        isDeleted switch
        {
            true => "#E0E0E0",
            false => hasHighlight switch
            {
                true => "#FFEB3B",
                false => isMyTag switch
                {
                    true => chipIndex < backgrounds.Count ? backgrounds[chipIndex] : backgrounds[0],
                    false => "#FFF9C4"
                }
            }
        };

    private static string GetTextColor(
        bool isDeleted,
        bool hasHighlight,
        bool isMyTag,
        int chipIndex,
        IReadOnlyList<string> textColors) =>
        isDeleted switch
        {
            true => "#9E9E9E",
            false => hasHighlight switch
            {
                true => "#F57F17",
                false => isMyTag switch
                {
                    true => chipIndex < textColors.Count ? textColors[chipIndex] : textColors[0],
                    false => "#5C4B00"
                }
            }
        };

    private static string GetDisplayWeight(
        bool isDeleted,
        bool isUpdated,
        TimelineEvent? highlightEvent,
        int weight) =>
        isDeleted switch
        {
            true => $"{highlightEvent?.PreviousWeight}",
            false => (isUpdated || highlightEvent?.EventType == "Insert") switch
            {
                true => $"{highlightEvent?.PreviousWeight} → {highlightEvent?.NewWeight}",
                false => weight.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }
        };
}