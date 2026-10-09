
// IDE0010 / IDE0072: union 型・enum の網羅的 switch に対する「Populate switch」抑制
#pragma warning disable IDE0010, IDE0072

using System.Globalization;

using MudBlazor;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

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
///     TagCard の操作結果種別。
/// </summary>
public enum TagCardActionResultType
{
    Success,
    Warning,
    Error,
    NoOp
}

/// <summary>
///     TagCard の操作結果。UI (Snackbar や NotifyChanged) への通知制御を行う。
/// </summary>
public sealed record TagCardActionResult(
    TagCardActionResultType Type,
    string? Message = null,
    bool ShouldNotifyChanged = false,
    int? DuplicateTagId = null)
{
    public static TagCardActionResult Success(string? message = null, bool shouldNotifyChanged = true) =>
        new(TagCardActionResultType.Success, message, shouldNotifyChanged);

    public static TagCardActionResult Warning(string message, int? duplicateTagId = null) =>
        new(TagCardActionResultType.Warning, message, false, duplicateTagId);

    public static TagCardActionResult Error(string message, int? duplicateTagId = null) =>
        new(TagCardActionResultType.Error, message, false, duplicateTagId);

    public static TagCardActionResult NoOp() =>
        new(TagCardActionResultType.NoOp);
}

/// <summary>
///     TagCard コンポーネントに含まれる表示ロジックおよびデータ操作ロジックを集約する ViewModel。
///     UI への依存を持たないため、bUnit を使わずに xUnit で直接単体テストできる。
/// </summary>
public class TagCardViewModel
{
    public const int DisplayLimit = 4;
    private const int HasManyThreshold = 5;

    private static readonly string[] ChipBackgrounds = ["#EEEDFE"];
    private static readonly string[] ChipTextColors = ["#26215C"];

    private readonly Services.ITagCardDataProvider _tagCardData;

    public TagCardViewModel(Services.ITagCardDataProvider tagCardData)
    {
        _tagCardData = tagCardData;
    }

    /// <summary>
    ///     タグへの投票 (good / bad) を切り替える。
    /// </summary>
    public async Task<TagCardActionResult> ToggleTagVoteAsync(
        int tagId,
        string? currentUserId,
        int? currentUserGoodTagId,
        int? currentUserBadTagId,
        bool isUpvote)
    {
        if (string.IsNullOrEmpty(currentUserId))
        {
            return TagCardActionResult.Warning("ログインが必要です。");
        }

        if (!currentUserGoodTagId.HasValue || !currentUserBadTagId.HasValue)
        {
            return TagCardActionResult.Error("システムタグの取得に失敗しました。");
        }

        var targetSystemTagId = isUpvote ? currentUserGoodTagId.Value : currentUserBadTagId.Value;
        var oppositeSystemTagId = isUpvote ? currentUserBadTagId.Value : currentUserGoodTagId.Value;

        await _tagCardData.ToggleTagVoteAsync(tagId, currentUserId, targetSystemTagId, oppositeSystemTagId);
        return TagCardActionResult.Success();
    }

    /// <summary>
    ///     タグに関連タグを追加する。
    /// </summary>
    public async Task<TagCardActionResult> AddTagToTagAsync(int targetTagId, int selectedTagId, string currentUserId)
    {
        Services.TagCardOperationResult result =
            await _tagCardData.AddTagToTagAsync(targetTagId, selectedTagId, currentUserId);

        return result switch
        {
            Services.TagCardOperationResult.AlreadyExists =>
                TagCardActionResult.Warning("このタグは既に追加されています。"),
            Services.TagCardOperationResult.Success =>
                TagCardActionResult.Success("タグを追加しました。"),
            _ => TagCardActionResult.NoOp()
        };
    }

    /// <summary>
    ///     タグ間の関連付けを解除する。本人権限チェック付き。
    /// </summary>
    public async Task<TagCardActionResult> RemoveRelationAsync(TagRelationToTag relation, string currentUserId)
    {
        if (!IsRelationOwner(relation.OwnerId, currentUserId))
        {
            return TagCardActionResult.Error("関連付けた本人ではないため、解除する権限がありません。");
        }

        Services.TagCardOperationResult result =
            await _tagCardData.RemoveRelationAsync(relation.Id, currentUserId);

        return result switch
        {
            Services.TagCardOperationResult.Success =>
                TagCardActionResult.Success("タグの関連付けを解除しました。"),
            _ => TagCardActionResult.NoOp()
        };
    }

    /// <summary>
    ///     タグ間の関連付けの Weight を変更する。本人権限チェック付き。
    /// </summary>
    public async Task<TagCardActionResult> UpdateRelationWeightAsync(
        TagRelationToTag relation,
        int delta,
        string currentUserId)
    {
        if (!IsRelationOwner(relation.OwnerId, currentUserId))
        {
            return TagCardActionResult.Error("関連付けた本人ではないため、Weightを変更する権限がありません。");
        }

        Services.TagCardOperationResult result =
            await _tagCardData.UpdateRelationWeightAsync(relation.Id, delta, currentUserId);

        return result switch
        {
            Services.TagCardOperationResult.Success =>
                TagCardActionResult.Success(),
            _ => TagCardActionResult.NoOp()
        };
    }

    /// <summary>
    ///     タグ間の関連付けの Weight を絶対値で設定する。本人権限チェック付き。
    /// </summary>
    public async Task<TagCardActionResult> SetRelationWeightAsync(
        TagRelationToTag relation,
        int newWeight,
        string currentUserId)
    {
        if (!IsRelationOwner(relation.OwnerId, currentUserId))
        {
            return TagCardActionResult.Error("関連付けた本人ではないため、Weightを変更する権限がありません。");
        }

        if (!HasWeightChange(relation.Weight, newWeight))
        {
            return TagCardActionResult.NoOp();
        }

        Services.TagCardOperationResult result =
            await _tagCardData.SetRelationWeightAsync(relation.Id, newWeight, currentUserId);

        return result switch
        {
            Services.TagCardOperationResult.Success =>
                TagCardActionResult.Success(),
            _ => TagCardActionResult.NoOp()
        };
    }

    /// <summary>
    ///     タグ間の関連付け先タグを変更する。本人権限チェック付き。
    /// </summary>
    public async Task<TagCardActionResult> ChangeRelationTagAsync(
        TagRelationToTag relation,
        int tagId,
        int newTagId,
        string currentUserId)
    {
        if (IsSameTagChange(relation.TagId, newTagId))
        {
            return TagCardActionResult.NoOp();
        }

        if (!IsRelationOwner(relation.OwnerId, currentUserId))
        {
            return TagCardActionResult.Error("関連付けた本人ではないため、変更する権限がありません。");
        }

        Services.TagCardOperationResult result =
            await _tagCardData.ChangeRelationTagAsync(relation.Id, tagId, newTagId, currentUserId);

        return result switch
        {
            Services.TagCardOperationResult.AlreadyExists =>
                TagCardActionResult.Warning("変更先のタグは既に追加されています。"),
            Services.TagCardOperationResult.Success =>
                TagCardActionResult.Success("タグを変更しました。"),
            _ => TagCardActionResult.NoOp()
        };
    }

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