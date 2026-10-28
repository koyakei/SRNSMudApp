#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using MudBlazor;
using SRNSMudApp.Components.Admin;
using SRNSMudApp.Components.Item;
using SRNSMudApp.Components.Pages;
using SRNSMudApp.Components.Tag;
using SRNSMudApp.Components.UI;
using SRNSMudApp.Components.User;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Services;
using ClientGuard = SRNSMudApp.Client.Services.DoubleSubmissionGuard;
using TagEntity = SRNSMudApp.Data.Tag;

namespace M2Verification;

public static class Program
{
    private static int _passed = 0;
    private static int _failed = 0;

    public static async Task<int> Main()
    {
        Console.WriteLine("==================================================================");
        Console.WriteLine("    EMPIRICAL VERIFICATION SUITE — MILESTONE 2 (UI & VIEWMODELS)");
        Console.WriteLine("==================================================================");

        // Task 1: ItemEditViewModel.ParsePillsToHtml
        Test("ItemEditViewModel_ParsePillsToHtml_NullOrEmpty", () =>
        {
            Assert(ItemEditViewModel.ParsePillsToHtml(null) == "", "null should return empty string");
            Assert(ItemEditViewModel.ParsePillsToHtml("") == "", "empty should return empty string");
        });

        Test("ItemEditViewModel_ParsePillsToHtml_NormalText", () =>
        {
            var res = ItemEditViewModel.ParsePillsToHtml("Hello World\nSecond Line");
            Assert(res == "Hello&nbsp;World<br>Second&nbsp;Line", $"Expected Hello&nbsp;World<br>Second&nbsp;Line but got {res}");
        });

        Test("ItemEditViewModel_ParsePillsToHtml_InternalLinks", () =>
        {
            var res = ItemEditViewModel.ParsePillsToHtml("Link /TagDetail/10 and /User/UserDetail/u1 here");
            Assert(res.Contains("#タグ"), "Should contain #タグ");
            Assert(res.Contains("@ユーザー"), "Should contain @ユーザー");
            Assert(res.Contains("data-url=\"/TagDetail/10\""), "Should contain data-url for tag");
            Assert(res.Contains("data-url=\"/User/UserDetail/u1\""), "Should contain data-url for user");
        });

        // Task 2: TagCardViewModel.BuildDisplayList
        Test("TagCardViewModel_BuildDisplayList_VirtualDeleteAddRange", () =>
        {
            var tag = new TagEntity
            {
                Id = 1,
                Name = "ParentTag",
                OwnerId = "u1",
                TargetTagRelations = new List<TagRelationToTag>
                {
                    new() { TagId = 10, Tag = new TagEntity { Id = 10, Name = "Existing", IsSystem = false, OwnerId = "u1" }, Weight = 5, OwnerId = "u1" }
                }
            };

            var deletedTag = new TagEntity { Id = 20, Name = "Deleted", IsSystem = false, OwnerId = "u1" };
            var highlightEvents = new List<TimelineEvent>
            {
                new() { ParsedEventType = TimelineEventType.Delete, FollowedTagId = 20, FollowedTag = deletedTag, PreviousWeight = 3, OwnerId = "fake" },
                // Duplicate delete event for same tag id:
                new() { ParsedEventType = TimelineEventType.Delete, FollowedTagId = 20, FollowedTag = deletedTag, PreviousWeight = 3, OwnerId = "fake2" },
                // Non-delete event:
                new() { ParsedEventType = TimelineEventType.Insert, FollowedTagId = 30, FollowedTag = new TagEntity { Id = 30, Name = "Inserted", OwnerId = "u1" }, OwnerId = "u1" },
                // Delete event for already existing tag:
                new() { ParsedEventType = TimelineEventType.Delete, FollowedTagId = 10, FollowedTag = tag.TargetTagRelations.First().Tag, PreviousWeight = 5, OwnerId = "u1" }
            };

            var displayList = TagCardViewModel.BuildDisplayList(tag, highlightEvents, true);
            Assert(displayList.TagsToDisplay.Count == 2, $"Expected 2 tags but got {displayList.TagsToDisplay.Count}");
            Assert(displayList.TagsToDisplay.Any(t => t.TagId == 10), "Should contain existing tag 10");
            Assert(displayList.TagsToDisplay.Any(t => t.TagId == 20), "Should contain virtual delete tag 20");
            Assert(displayList.TagsToDisplay.Count(t => t.TagId == 20) == 1, "Should not duplicate tag 20");
        });

        // Task 3: TagDiagramPage.GetEffectiveItemIds (via reflection)
        Test("TagDiagramPage_GetEffectiveItemIds_DeduplicationAndOrder", () =>
        {
            var page = new TagDiagramPage();
            var propQueryItemId = typeof(TagDiagramPage).GetProperty("QueryItemId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var propQueryItemIds = typeof(TagDiagramPage).GetProperty("QueryItemIds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var methodGetEffective = typeof(TagDiagramPage).GetMethod("GetEffectiveItemIds", BindingFlags.NonPublic | BindingFlags.Instance);

            propQueryItemId!.SetValue(page, 10);
            propQueryItemIds!.SetValue(page, "20, 10, 30, abc, 30, ");

            var result = (List<int>)methodGetEffective!.Invoke(page, null)!;
            Assert(result.SequenceEqual(new[] { 10, 20, 30 }), $"Expected [10, 20, 30] but got [{string.Join(", ", result)}]");
        });

        // Task 4: TagDiagramPageViewModel.UnionWith
        Test("TagDiagramPageViewModel_UnionWith_PreservesAndMerges", async () =>
        {
            var mockData = new Mock<ITagDiagramDataProvider>();
            mockData.Setup(d => d.LoadAllTagsAsync())
                .ReturnsAsync(new List<TagEntity>());
            mockData.Setup(d => d.LoadAllEdgesAsync())
                .ReturnsAsync(new List<TagEdge>());
            mockData.Setup(d => d.GetContextTagIdsForItemsAsync(It.IsAny<IEnumerable<int>>()))
                .ReturnsAsync(new List<int> { 100, 200, 100 });
            mockData.Setup(d => d.GetContextItemsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<string?>(), It.IsAny<bool>()))
                .ReturnsAsync(new List<SRNSMudApp.Data.Item>());

            var vm = new TagDiagramPageViewModel(mockData.Object);
            vm.PinnedTagIds.Add(10);
            vm.PinnedTagIds.Add(20);
            vm.HiddenTagIds.Add(30);
            vm.HiddenTagIds.Add(40);
            vm.HiddenItemIds.Add(50);
            vm.HiddenItemIds.Add(60);

            await vm.ReloadDiagramForItemsAsync(new[] { 1, 2 }, true);

            Assert(vm.PinnedTagIds.Contains(10), "Should contain preservedExtraIds 10");
            Assert(vm.PinnedTagIds.Contains(20), "Should contain preservedExtraIds 20");
            Assert(vm.PinnedTagIds.Contains(100), "Should contain context tag 100");
            Assert(vm.PinnedTagIds.Contains(200), "Should contain context tag 200");
            Assert(vm.HiddenTagIds.Contains(30), "Should contain hidden tag 30");
            Assert(vm.HiddenTagIds.Contains(40), "Should contain hidden tag 40");
            Assert(vm.HiddenItemIds.Contains(50), "Should contain hidden item 50");
            Assert(vm.HiddenItemIds.Contains(60), "Should contain hidden item 60");
        });

        // Task 5: ItemListViewModel.InitializeAsync
        Test("ItemListViewModel_InitializeAsync_FilterAndSortMapping", async () =>
        {
            var mockListData = new Mock<IItemListDataProvider>();
            var mockExport = new Mock<IItemListExportService>();
            var tag1 = new TagEntity { Id = 10, Name = "CSharp", OwnerId = "u1" };
            var tag2 = new TagEntity { Id = 20, Name = "DotNet", OwnerId = "u1" };
            var tagsById = new Dictionary<int, TagEntity> { [10] = tag1, [20] = tag2 };
            var tagsByName = new Dictionary<string, TagEntity> { ["DotNet"] = tag2 };

            mockListData.Setup(d => d.GetTagsByIdsAsync(It.IsAny<IEnumerable<int>>()))
                .ReturnsAsync(tagsById);
            mockListData.Setup(d => d.GetTagsByNamesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(tagsByName);
            mockListData.Setup(d => d.LoadItemsAndTagsAsync(
                    It.IsAny<IReadOnlyList<ItemListFilter>>(),
                    It.IsAny<IReadOnlyList<ItemListSort>>(),
                    It.IsAny<string?>()))
                .ReturnsAsync(new ItemListPageData([], [tag1, tag2]));

            var vm = new ItemListViewModel(mockListData.Object, mockExport.Object);

            var uri = new Uri("https://localhost/items?f=10@user1&f=name:DotNet&sort=10:asc&sort=20:desc&sort=99:asc");
            await vm.InitializeAsync(uri);

            Assert(vm.TagSearch.SelectedFilters.Count == 2, $"Expected 2 filters but got {vm.TagSearch.SelectedFilters.Count}");
            Assert(vm.TagSearch.SelectedFilters.Any(f => f.TagId == 10 && f.UserName == "user1"), "Filter 10 with user1");
            Assert(vm.TagSearch.SelectedFilters.Any(f => f.TagName == "DotNet"), "Filter DotNet");
            Assert(vm.SortConditions.Count == 2, $"Expected 2 sort conditions (99 excluded) but got {vm.SortConditions.Count}");
            Assert(vm.SortConditions[0].Tag.Id == 10 && vm.SortConditions[0].Order == SortOrder.Asc, "Sort 10 asc");
            Assert(vm.SortConditions[1].Tag.Id == 20 && vm.SortConditions[1].Order == SortOrder.Desc, "Sort 20 desc");
        });

        // Task 6: ItemCardViewModel.GetTagDisplayWeight
        Test("ItemCardViewModel_GetTagDisplayWeight_PatternMatching", () =>
        {
            var relation = new TagRelation { Weight = 5, OwnerId = "u1" };

            // Null event
            Assert(ItemCardViewModel.GetTagDisplayWeight(relation, null) == "5", "Null event should return relation weight");

            // Update event
            var evUpdate = new TimelineEvent { EventType = "Update", PreviousWeight = 2, NewWeight = 8, OwnerId = "u1" };
            Assert(ItemCardViewModel.GetTagDisplayWeight(relation, evUpdate) == "2 → 8", "Update event diff format");

            // Insert event
            var evInsert = new TimelineEvent { EventType = "Insert", PreviousWeight = 0, NewWeight = 3, OwnerId = "u1" };
            Assert(ItemCardViewModel.GetTagDisplayWeight(relation, evInsert) == "0 → 3", "Insert event diff format");

            // Delete event
            var evDelete = new TimelineEvent { EventType = "Delete", PreviousWeight = 4, OwnerId = "u1" };
            Assert(ItemCardViewModel.GetTagDisplayWeight(relation, evDelete) == "4", "Delete event previous weight");
        });

        // Task 7: ItemListQueryState list pattern
        Test("ItemListQueryState_ParseSort_ListPatternAndSwitch", () =>
        {
            var uri = new Uri("https://localhost/items?sort=10:asc&sort=20:desc&sort=invalid&sort=30:invalidorder&sort=40:asc:extra");
            var state = ItemListQueryState.ParseFromUri(uri);

            Assert(state.SortEntries.Count == 2, $"Expected 2 valid sort entries but got {state.SortEntries.Count}");
            Assert(state.SortEntries[0].TagId == 10 && state.SortEntries[0].Order == SortOrder.Asc, "10:asc parsed");
            Assert(state.SortEntries[1].TagId == 20 && state.SortEntries[1].Order == SortOrder.Desc, "20:desc parsed");
        });

        // Task 8: TagTreeViewModel.BuildTreeData
        Test("TagTreeViewModel_BuildTreeDataInternal_PendingMoveProjection", () =>
        {
            var pendingMoves = new List<PendingTagMoveDto>
            {
                new(RequestId: 1, TagId: 101, TagName: "MoveTag1", NewParentTagId: 5, RequesterUserId: "user1", TagOwnerUserId: "owner1"),
                new(RequestId: 2, TagId: 102, TagName: "MoveTag2", NewParentTagId: 99, RequesterUserId: "user2", TagOwnerUserId: "owner2")
            };

            var tags = new List<TagEntity>
            {
                new() { Id = 5, Name = "Parent5", ParentTagId = null, OwnerId = "u1" }
            };

            var tree = TagTreeViewModel.BuildTreeData(5, tags, pendingMoves, "user1");
            Assert(tree.Count == 1, $"Expected 1 matching move node under parent 5 but got {tree.Count}");
            var dynamicNode = tree[0];
            var propId = dynamicNode.GetType().GetProperty("id")!.GetValue(dynamicNode);
            var propCanCancel = (bool)dynamicNode.GetType().GetProperty("canCancel")!.GetValue(dynamicNode)!;

            Assert((string)propId! == "move-req-1", $"Node id should be move-req-1, got {propId}");
            Assert(propCanCancel == true, "User1 should be able to cancel move-req-1");
        });

        // Task 9: TagResolutionViewModel.ExceptBy and Take
        Test("TagResolutionViewModel_InitializeAsync_ExceptByAndTake", async () =>
        {
            var mockData = new Mock<ITaggingImportDataProvider>();
            mockData.Setup(d => d.SearchTagsAsync("Test", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<TagEntity> { new() { Id = 1, Name = "Test", OwnerId = "u1" } });

            // parent candidates initially return 2 items (IDs 10 and 20)
            mockData.Setup(d => d.SearchParentCandidateTagsAsync("Test", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<TagEntity>
                {
                    new() { Id = 10, Name = "P10", OwnerId = "u1" },
                    new() { Id = 20, Name = "P20", OwnerId = "u1" }
                });

            // default parents return 10 (duplicate), 30, 40, 50, 60, 70, 80, 90 (many)
            mockData.Setup(d => d.SearchParentCandidateTagsAsync(null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<TagEntity>
                {
                    new() { Id = 10, Name = "P10", OwnerId = "u1" }, // duplicate, should be skipped
                    new() { Id = 30, Name = "P30", OwnerId = "u1" },
                    new() { Id = 40, Name = "P40", OwnerId = "u1" },
                    new() { Id = 50, Name = "P50", OwnerId = "u1" },
                    new() { Id = 60, Name = "P60", OwnerId = "u1" },
                    new() { Id = 70, Name = "P70", OwnerId = "u1" },
                    new() { Id = 80, Name = "P80", OwnerId = "u1" },
                    new() { Id = 90, Name = "P90", OwnerId = "u1" }
                });

            var vm = new TagResolutionViewModel(mockData.Object);
            await vm.InitializeAsync("Test", null);

            Assert(vm.ParentSuggestions.Count == 8, $"ParentSuggestions should cap at 8, got {vm.ParentSuggestions.Count}");
            Assert(vm.ParentSuggestions.Select(p => p.Id).Distinct().Count() == 8, "All 8 suggestions should be distinct");
            Assert(vm.ParentSuggestions[0].Id == 10 && vm.ParentSuggestions[1].Id == 20, "First two should be 10 and 20");
            Assert(vm.ParentSuggestions[2].Id == 30, "Third should be 30");
        });

        // Task 10: UrlPreviewCardViewModel.ProcessTextFragment
        Test("UrlPreviewCardViewModel_ExtractTextFragment_CleanUp", () =>
        {
            var url1 = "https://example.com#:~:text=core%20text";
            Assert(UrlPreviewCardViewModel.ExtractTextFragment(url1) == "core text", "Single fragment");

            var url2 = "https://example.com#:~:text=-,prefix,text,-suffix";
            Assert(UrlPreviewCardViewModel.ExtractTextFragment(url2) == "prefix〜text", "Prefix/suffix stripped");

            var url3 = "https://example.com#:~:text=first&text=second";
            Assert(UrlPreviewCardViewModel.ExtractTextFragment(url3) == "first | second", "Multiple text parts joined with pipe");

            var url4 = "https://example.com#:~:text=%20%20,";
            Assert(UrlPreviewCardViewModel.ExtractTextFragment(url4) == null, "Empty segments return null");
        });

        // Task 11: DoubleSubmissionGuard.CleanupExpired
        Test("DoubleSubmissionGuard_CleanupExpired_WorksCorrectly", () =>
        {
            var guard = new ClientGuard(TimeSpan.FromMilliseconds(50));

            // First submit succeeds
            Assert(guard.TryBeginSubmit("op1"), "First submit should succeed");
            guard.Complete("op1");

            // Immediate submit with same key fails
            Assert(!guard.TryBeginSubmit("op1"), "Immediate retry should be blocked");

            // Sleep past expiration window
            Thread.Sleep(70);

            // After expiration, submit with same key succeeds
            Assert(guard.TryBeginSubmit("op1"), "Retry after window expiration should succeed");
            guard.Complete("op1");
        });

        // Task 12: Primary Constructors in ViewModels
        Test("ViewModels_PrimaryConstructors_InstantiateAndExecute", async () =>
        {
            // TagAutocompleteViewModel
            var mockTagSearch = new Mock<ITagSearchQueryService>();
            mockTagSearch.Setup(s => s.SearchTagsWithFallbackAsync("q", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<TagEntity> { new() { Id = 1, Name = "q", OwnerId = "u1" } });
            var autocompleteVm = new TagAutocompleteViewModel(mockTagSearch.Object);
            var acResults = await autocompleteVm.SearchTagsAsync("q", null, CancellationToken.None);
            Assert(acResults.Count() == 1, "TagAutocompleteViewModel works");

            // QuotedItemListViewModel
            var mockQuote = new Mock<IItemQuoteService>();
            mockQuote.Setup(q => q.GetSourceItemAsync(10, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SRNSMudApp.Data.Item { Id = 5, Content = "Source", OwnerId = "u1" });
            mockQuote.Setup(q => q.GetQuotedByItemsAsync(10, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<SRNSMudApp.Data.Item> { new() { Id = 15, Content = "Quote", OwnerId = "u1" } });
            var quoteVm = new QuotedItemListViewModel(mockQuote.Object);
            quoteVm.TargetItem = new SRNSMudApp.Data.Item { Id = 10, OwnerId = "u1" };
            await quoteVm.LoadAsync();
            Assert(quoteVm.HasSourceItem && quoteVm.HasQuotedItems, "QuotedItemListViewModel loaded items");

            // UserSearchViewModel
            var mockUser = new Mock<IUserDataProvider>();
            mockUser.Setup(u => u.SearchUsersByNormalizedNameAsync("admin", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ApplicationUser> { new() { Id = "u1", UserName = "admin" } });
            var userVm = new UserSearchViewModel(mockUser.Object);
            var users = await userVm.SearchUsersAsync("admin");
            Assert(users.Count() == 1, "UserSearchViewModel works");

            // ReportManagerViewModel
            var mockReport = new Mock<IContentReportService>();
            mockReport.Setup(r => r.GetReportsAsync(ReportStatus.Pending, null))
                .ReturnsAsync(new List<ContentReport> { new() { Id = 1, OwnerId = "u1" } });
            var reportVm = new ReportManagerViewModel(mockReport.Object);
            var repRes = await reportVm.LoadReportsAsync();
            Assert(repRes.Success && reportVm.Reports.Count == 1, "ReportManagerViewModel works");
        });

        Console.WriteLine("==================================================================");
        Console.WriteLine($"RESULTS: {_passed} PASSED, {_failed} FAILED");
        Console.WriteLine("==================================================================");

        return _failed == 0 ? 0 : 1;
    }

    private static void Test(string name, Action act)
    {
        try
        {
            act();
            Console.WriteLine($"[PASS] {name}");
            _passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {name}: {ex.Message}");
            _failed++;
        }
    }

    private static void Test(string name, Func<Task> act)
    {
        try
        {
            act().GetAwaiter().GetResult();
            Console.WriteLine($"[PASS] {name}");
            _passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {name}: {ex.Message}");
            _failed++;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception($"Assertion failed: {message}");
        }
    }
}
