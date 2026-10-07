global using ReactionTagIds = SRNSMudApp.Models.ReactionTagIds;
global using SystemTagIds = SRNSMudApp.Models.SystemTagIds;

namespace SRNSMudApp.Components.UI;

using System.Collections.Generic;
using System.Threading.Tasks;

using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;

/// <summary>
///     ResourceList コンポーネントの状態管理、タグ取得・システムタグ確保ロジックを担う ViewModel。
/// </summary>
public class ResourceListViewModel
{
    private readonly IHomeDataProvider _homeData;
    private readonly ISystemTagEnsurer _systemTagEnsurer;

    public ResourceListViewModel(IHomeDataProvider homeData, ISystemTagEnsurer systemTagEnsurer)
    {
        _homeData = homeData;
        _systemTagEnsurer = systemTagEnsurer;
    }

    public string CurrentUserId { get; private set; } = string.Empty;
    public IReadOnlyList<Tag> AllTags { get; private set; } = [];
    public IReadOnlyList<TagRelationToTag> AllTagRelationsToTags { get; private set; } = [];

    public int? CurrentUserGoodTagId { get; private set; }
    public int? CurrentUserBadTagId { get; private set; }
    public int? CurrentUserShinjiTagId { get; private set; }
    public int? CurrentUserZenTagId { get; private set; }
    public int? CurrentUserBiTagId { get; private set; }

    /// <summary>
    ///     ユーザーIDを設定し、タグおよびシステムタグを読み込む。
    /// </summary>
    public async Task InitializeAsync(string currentUserId)
    {
        CurrentUserId = currentUserId ?? string.Empty;
        await FetchTagsAsync();
    }

    /// <summary>
    ///     タグとリレーションを取得し、ユーザーのシステムタグ・リアクションタグを解決する。
    /// </summary>
    public async Task FetchTagsAsync()
    {
        var (tags, relations) = await _homeData.GetTagsAndRelationsAsync();
        AllTags = tags;
        AllTagRelationsToTags = relations;

        var systemTags = FindSystemTags(AllTags, CurrentUserId);
        CurrentUserGoodTagId = systemTags.GoodTagId;
        CurrentUserBadTagId = systemTags.BadTagId;

        var reactionTags = FindReactionTags(AllTags, CurrentUserId);
        CurrentUserShinjiTagId = reactionTags.ShinjiTagId;
        CurrentUserZenTagId = reactionTags.ZenTagId;
        CurrentUserBiTagId = reactionTags.BiTagId;
    }

    /// <summary>
    ///     必要なシステムタグ・リアクションタグが存在することを確認・作成する。
    /// </summary>
    public async Task EnsureSystemTagsExistAsync()
    {
        var (voteIds, reactionIds, refetch) = await _systemTagEnsurer.EnsureAllAsync(
            CurrentUserId,
            new SystemTagIds(CurrentUserGoodTagId, CurrentUserBadTagId),
            new ReactionTagIds(CurrentUserShinjiTagId, CurrentUserZenTagId, CurrentUserBiTagId));

        CurrentUserGoodTagId = voteIds.GoodTagId;
        CurrentUserBadTagId = voteIds.BadTagId;
        CurrentUserShinjiTagId = reactionIds.ShinjiTagId;
        CurrentUserZenTagId = reactionIds.ZenTagId;
        CurrentUserBiTagId = reactionIds.BiTagId;

        if (refetch)
        {
            await FetchTagsAsync();
        }
    }

    /// <summary>
    ///     現在ユーザー所有の投票用システムタグ (good / bad) の ID を返す。
    /// </summary>
    public static SystemTagIds FindSystemTags(IEnumerable<Tag>? tags, string? currentUserId)
    {
        if (tags == null || string.IsNullOrEmpty(currentUserId))
        {
            return default;
        }

        List<Tag> tagList = [.. tags];
        Tag? goodTag = tagList.Find(
            t => t.OwnerId == currentUserId && t.Name == "good" && t.IsSystem);
        Tag? badTag = tagList.Find(
            t => t.OwnerId == currentUserId && t.Name == "bad" && t.IsSystem);

        return new SystemTagIds(goodTag?.Id, badTag?.Id);
    }

    /// <summary>
    ///     現在ユーザー所有のリアクション用システムタグ (真実 / 善 / 美) の ID を返す。
    /// </summary>
    public static ReactionTagIds FindReactionTags(IEnumerable<Tag>? tags, string? currentUserId)
    {
        if (tags == null || string.IsNullOrEmpty(currentUserId))
        {
            return default;
        }

        List<Tag> tagList = [.. tags];
        Tag? shinjiTag = tagList.Find(
            t => t.OwnerId == currentUserId && t.Name == ReactionTagNames.Shinji && t.IsSystem);
        Tag? zenTag = tagList.Find(
            t => t.OwnerId == currentUserId && t.Name == ReactionTagNames.Zen && t.IsSystem);
        Tag? biTag = tagList.Find(
            t => t.OwnerId == currentUserId && t.Name == ReactionTagNames.Bi && t.IsSystem);

        return new ReactionTagIds(shinjiTag?.Id, zenTag?.Id, biTag?.Id);
    }

    /// <summary>
    ///     フォーカス対象に応じたスクロール先セレクタを返す。
    ///     タグを優先し、未指定の場合はアイテムへフォールバックする。どちらも無ければ null。
    /// </summary>
    public static string? GetFocusSelector(int? focusTagId, int? focusItemId)
    {
        return (focusTagId, focusItemId) switch
        {
            (int tagId, _) => $"#tag-card-{tagId}",
            (_, int itemId) => $"#item-card-{itemId}",
            _ => null
        };
    }
}