using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;

namespace SRNSMudApp.Tests.Components.Tag;

// 親名前空間の下にある namespace Tag より先に Data.Tag 型を解決させるため、
// エイリアスを名前空間の内側に置く
using Tag = SRNSMudApp.Data.Tag;

/// <summary>
///     ItemTagChipViewModel の単体テスト。
///     ハイライト状態・所有者別のチップ表示計算を bUnit なしで検証する。
/// </summary>
public class ItemTagChipViewModelTests
{
    private static readonly string[] Backgrounds = ["#EEEDFE", "#FDE8E8"];
    private static readonly string[] TextColors = ["#26215C", "#5C1010"];

    private static TagRelation CreateRelation(string ownerId = "user-1", int tagId = 10) =>
        new()
        {
            Id = 1,
            ItemId = 1,
            TagId = tagId,
            OwnerId = ownerId,
            Weight = 5,
            Tag = new Tag { Id = tagId, Name = "TestTag", OwnerId = ownerId }
        };

    private static IReadOnlyList<TagRelationToTag> NoAddedTags { get; } = [];

    [Fact]
    public void GetDisplay_ForOwnTag_UsesChipPaletteByIndex()
    {
        var relation = CreateRelation();

        var display = ItemTagChipViewModel.GetDisplay(
            relation, 1, "user-1", null, 1, Backgrounds, TextColors, true, NoAddedTags);

        Assert.Equal("#FDE8E8", display.BackgroundColor);
        Assert.Equal("#5C1010", display.TextColor);
    }

    [Fact]
    public void GetDisplay_ForIndexOutOfRange_FallsBackToFirstColor()
    {
        var relation = CreateRelation();

        var display = ItemTagChipViewModel.GetDisplay(
            relation, 1, "user-1", null, 99, Backgrounds, TextColors, true, NoAddedTags);

        Assert.Equal(Backgrounds[0], display.BackgroundColor);
        Assert.Equal(TextColors[0], display.TextColor);
    }

    [Fact]
    public void GetDisplay_ForOthersTag_UsesDefaultColors()
    {
        var relation = CreateRelation(ownerId: "owner-x");

        var display = ItemTagChipViewModel.GetDisplay(
            relation, 1, "user-1", null, 0, Backgrounds, TextColors, true, NoAddedTags);

        Assert.False(display.IsMyTag);
        Assert.Equal("#FFF9C4", display.BackgroundColor);
        Assert.Equal("#5C4B00", display.TextColor);
    }

    [Fact]
    public void GetDisplay_WithDeleteHighlight_ShowsStruckThroughStyle()
    {
        var relation = CreateRelation();
        var highlight = new TimelineEvent { EventType = "Delete", PreviousWeight = 5, NewWeight = 0, OwnerId = "u" };

        var display = ItemTagChipViewModel.GetDisplay(
            relation, 1, "user-1", highlight, 0, Backgrounds, TextColors, true, NoAddedTags);

        Assert.True(display.IsDeleted);
        Assert.Equal("#E0E0E0", display.BackgroundColor);
        Assert.Equal("5", display.DisplayWeight);
    }

    [Fact]
    public void GetDisplay_WithUpdateHighlight_ShowsTransitionAndBold()
    {
        var relation = CreateRelation();
        var highlight = new TimelineEvent { EventType = "Update", PreviousWeight = 3, NewWeight = 7, OwnerId = "u" };

        var display = ItemTagChipViewModel.GetDisplay(
            relation, 1, "user-1", highlight, 0, Backgrounds, TextColors, true, NoAddedTags);

        Assert.Equal("3 → 7", display.DisplayWeight);
        Assert.Equal("bold", display.FontWeight);
        Assert.True(display.WeightIncreased);
        Assert.Equal(MudBlazor.Color.Success, display.AddButtonColor);
    }

    [Fact]
    public void GetDisplay_WithoutHighlight_ShowsCurrentWeight()
    {
        var relation = CreateRelation();

        var display = ItemTagChipViewModel.GetDisplay(
            relation, 1, "user-1", null, 0, Backgrounds, TextColors, true, NoAddedTags);

        Assert.Equal("5", display.DisplayWeight);
        Assert.Equal("normal", display.FontWeight);
        Assert.Equal(MudBlazor.Color.Inherit, display.AddButtonColor);
    }

    [Fact]
    public void GetDisplay_BuildsChipIdFromItemAndTag()
    {
        var relation = CreateRelation(tagId: 42);

        var display = ItemTagChipViewModel.GetDisplay(
            relation, 77, "user-1", null, 0, Backgrounds, TextColors, true, NoAddedTags);

        Assert.Equal("tag-chip-77-42", display.ChipId);
    }

    [Fact]
    public void GetDisplay_WithoutNameAndOwner_ReturnsNullNames()
    {
        var relation = CreateRelation();

        var display = ItemTagChipViewModel.GetDisplay(
            relation, 1, "user-1", null, 0, Backgrounds, TextColors, showNameAndOwner: false, NoAddedTags);

        Assert.Null(display.TagName);
        Assert.Null(display.OwnerName);
    }

    [Fact]
    public void GetDisplay_FiltersAddedTags_ExcludesVotingTagsAndOtherTargets()
    {
        var relation = CreateRelation(tagId: 10);
        List<TagRelationToTag> all =
        [
            new() { Id = 1, TagId = 20, TargetTagId = 10, OwnerId = "u", Tag = new Tag { Id = 20, Name = "Child", OwnerId = "u" } },
            new() { Id = 2, TagId = 30, TargetTagId = 99, OwnerId = "u", Tag = new Tag { Id = 30, Name = "Other", OwnerId = "u" } },
            new()
            {
                Id = 3,
                TagId = 40,
                TargetTagId = 10,
                OwnerId = "u",
                Tag = new Tag { Id = 40, Name = "good", IsSystem = true, OwnerId = "u" }
            }
        ];

        var display = ItemTagChipViewModel.GetDisplay(
            relation, 1, "user-1", null, 0, Backgrounds, TextColors, true, all);

        TagRelationToTag added = Assert.Single(display.AddedTags);
        Assert.Equal(20, added.TagId);
    }

    [Fact]
    public void GetDisplay_ForSystemClassificationTag_DisplaysSystemOwnerName()
    {
        var systemTag = new Tag { Id = 50, Name = "SystemCategory", OwnerId = "system", IsSystem = true };
        var relation = new TagRelation
        {
            Id = 1,
            ItemId = 1,
            TagId = 50,
            OwnerId = "user-1",
            Weight = 3,
            Tag = systemTag
        };

        var display = ItemTagChipViewModel.GetDisplay(
            relation, 1, "user-1", null, 0, Backgrounds, TextColors, true, NoAddedTags);

        Assert.Equal("system", display.OwnerName);
        Assert.Equal("SystemCategory", display.TagName);
    }
}