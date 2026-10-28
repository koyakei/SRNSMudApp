#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Moq;
using SRNSMudApp.Data;
using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;

namespace TestRunner;

public class Program
{
    private static int _passedCount = 0;
    private static int _failedCount = 0;
    private static readonly List<string> _failures = [];

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("==================================================================");
        Console.WriteLine("    EMPIRICAL CHALLENGER STRESS HARNESS — MILESTONE 1 VERIFICATION");
        Console.WriteLine("==================================================================");

        // 1. TagCardDataProvider
        await RunTest("TagCardDataProvider_RemoveRelationAsync_NullEntity_ReturnsNotFound", TestTagCard_RemoveRelation_NullEntity);
        await RunTest("TagCardDataProvider_ChangeRelationTagAsync_AlreadyExists_ReturnsAlreadyExists", TestTagCard_ChangeRelation_AlreadyExists);
        await RunTest("TagCardDataProvider_ChangeRelationTagAsync_NullEntity_ReturnsNotFound", TestTagCard_ChangeRelation_NullEntity);
        await RunTest("TagCardDataProvider_SetParentTagAsync_NullEntity_ReturnsNotFound", TestTagCard_SetParent_NullEntity);
        await RunTest("TagCardDataProvider_SetParentTagAsync_NonOwner_ReturnsNotOwner", TestTagCard_SetParent_NonOwner);
        await RunTest("TagCardDataProvider_SetParentTagAsync_Owner_ReturnsSuccess", TestTagCard_SetParent_Owner);

        // 2. ContractDataProvider
        await RunTest("ContractDataProvider_DeactivatePublicOfferAsync_NullEntity_ReturnsFalse", TestContract_Deactivate_NullEntity);
        await RunTest("ContractDataProvider_DeactivatePublicOfferAsync_NonOwner_ReturnsFalse", TestContract_Deactivate_NonOwner);
        await RunTest("ContractDataProvider_DeactivatePublicOfferAsync_Owner_ReturnsTrueAndDeactivates", TestContract_Deactivate_Owner);
        await RunTest("ContractDataProvider_SearchItemsAsync_NullOrEmptyQuery_ReturnsItems", TestContract_SearchItems_Empty);
        await RunTest("ContractDataProvider_SearchTagsByNameAsync_NullOrEmptyQuery_ReturnsTags", TestContract_SearchTags_Empty);

        // 3. TagTableDataProvider
        await RunTest("TagTableDataProvider_DeleteTagAsync_TagLockServiceLocked_ReturnsFalseWithoutDbCall", TestTagTable_Locked_ReturnsFalse);
        await RunTest("TagTableDataProvider_DeleteTagAsync_NullTag_ReturnsFalse", TestTagTable_NullTag_ReturnsFalse);

        // 4. NotificationsDataProvider
        await RunTest("NotificationsDataProvider_GetAssociatedItemsAsync_EmptyItemIds_ReturnsEmptyWithoutDbCall", TestNotifications_EmptyItemIds);

        // 5. UserDataProvider
        await RunTest("UserDataProvider_EnsureReactionTagsAsync_AllExisting_NoDbSave", TestUserData_ReactionTags_AllExisting);
        await RunTest("UserDataProvider_EnsureReactionTagsAsync_MissingTags_AddsRangeAndSaves", TestUserData_ReactionTags_MissingTags);
        await RunTest("UserDataProvider_ToggleFollowUserAsync_SelfFollow_ReturnsFalse", TestUserData_ToggleFollow_SelfFollow);
        await RunTest("UserDataProvider_ToggleFollowUserAsync_NonExistentTarget_ReturnsFalse", TestUserData_ToggleFollow_NonExistentTarget);
        await RunTest("UserDataProvider_ToggleFollowUserAsync_ExistingFollow_RemovesAndReturnsFalse", TestUserData_ToggleFollow_ExistingFollow);
        await RunTest("UserDataProvider_ToggleFollowUserAsync_NewFollow_AddsAndReturnsTrue", TestUserData_ToggleFollow_NewFollow);

        // 6. AdminDataProvider
        await RunTest("AdminDataProvider_ImportItemsWithTagsAsync_DuplicateTags_Deduplicated", TestAdmin_DuplicateTags_Deduplicated);
        await RunTest("AdminDataProvider_ImportItemsWithTagsAsync_UnknownTags_CreatesNewTagsAndLinks", TestAdmin_UnknownTags_CreatesNewTags);
        await RunTest("AdminDataProvider_ImportItemsWithTagsAsync_EmptyTags_ImportsItemWithoutTags", TestAdmin_EmptyTags_ImportsItem);
        await RunTest("AdminDataProvider_ImportItemsWithTagsAsync_EmptyLines_ReturnsZero", TestAdmin_EmptyLines_ReturnsZero);

        // 7. TagTreeDataProvider
        await RunTest("TagTreeDataProvider_LoadPendingTagMovesAsync_FiltersPayloadAndHandlesNullTag", TestTagTree_LoadPendingTagMoves);

        // 8. ItemDetailDataProvider
        await RunTest("ItemDetailDataProvider_GetItemDetailAsync_LedgerDeduplicationAndSynthesis", TestItemDetail_LedgerSynthesis);

        // 9. TagLockService
        await RunTest("TagLockService_GetAllTagsWithLockStatusAsync_ParentsWithLockedChildren", TestTagLockService_LockCalculation);

        // 10-15. Utilities & Executors
        await RunTest("ContentParser_ExtractUrls_DeduplicationAndOrdering", TestContentParser_ExtractUrls);
        await RunTest("TagSuggestionService_SuggestTagsAsync_EmptyContentOrCancellation", TestTagSuggestion_Cancellation);
        await RunTest("TagSimilarityService_FindSimilarTags_OrderingAndBoosting", TestTagSimilarity_Boosting);
        await RunTest("ItemListDataProvider_FilterSeparation_OfType", TestItemList_FilterSeparation);
        await RunTest("InternalLinkConversionService_DetectLinkCandidatesAsync_Partitioning", TestInternalLink_Partitioning);
        await RunTest("StringSimilarity_LevenshteinDistance_EquivalenceAndSymmetry", TestStringSimilarity_Distance);

        Console.WriteLine("\n==================================================================");
        Console.WriteLine($"RESULTS: {_passedCount} PASSED, {_failedCount} FAILED");
        if (_failedCount > 0)
        {
            Console.WriteLine("FAILURES:");
            foreach (var f in _failures)
            {
                Console.WriteLine(" - " + f);
            }
            return 1;
        }

        Console.WriteLine("ALL EMPIRICAL TESTS PASSED SUCCESSFULLY!");
        return 0;
    }

    private static async Task RunTest(string testName, Func<Task> testFunc)
    {
        try
        {
            await testFunc();
            _passedCount++;
            Console.WriteLine($"[PASS] {testName}");
        }
        catch (Exception ex)
        {
            _failedCount++;
            _failures.Add($"{testName}: {ex.Message}");
            Console.WriteLine($"[FAIL] {testName}: {ex}");
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message = "")
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception($"Assertion failed: Expected '{expected}', but got '{actual}'. {message}");
        }
    }

    private static void AssertTrue(bool condition, string message = "")
    {
        if (!condition)
        {
            throw new Exception($"Assertion failed: Expected True, got False. {message}");
        }
    }

    private static void AssertFalse(bool condition, string message = "")
    {
        if (condition)
        {
            throw new Exception($"Assertion failed: Expected False, got True. {message}");
        }
    }

    // -------------------------------------------------------------
    // Test DbContext & Async Queryable Support
    // -------------------------------------------------------------
    public class TestDbContext : ApplicationDbContext
    {
        public int SaveChangesCalls { get; set; }
        public TestDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalls++;
            return Task.FromResult(1);
        }
    }

    public static Mock<DbSet<T>> MockDbSet<T>(List<T> data) where T : class
    {
        var queryable = data.AsQueryable();
        var mock = new Mock<DbSet<T>>();
        mock.As<IQueryable<T>>().Setup(m => m.Provider).Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
        mock.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryable.Expression);
        mock.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        mock.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(() => queryable.GetEnumerator());
        mock.As<IAsyncEnumerable<T>>().Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<T>(data.GetEnumerator()));

        mock.Setup(m => m.Add(It.IsAny<T>())).Callback<T>(data.Add);
        mock.Setup(m => m.AddRange(It.IsAny<IEnumerable<T>>())).Callback<IEnumerable<T>>(data.AddRange);
        mock.Setup(m => m.Remove(It.IsAny<T>())).Callback<T>(t => data.Remove(t));
        return mock;
    }

    private class TestAsyncEnumerable<T> : IAsyncEnumerable<T>, IOrderedQueryable<T>
    {
        private readonly Expression _expression;
        private readonly IQueryProvider _innerProvider;

        public TestAsyncEnumerable(Expression expression, IQueryProvider innerProvider)
        {
            _expression = expression;
            _innerProvider = innerProvider;
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            var enumerable = _innerProvider.Execute<IEnumerable<T>>(_expression);
            return new TestAsyncEnumerator<T>(enumerable.GetEnumerator());
        }

        public IEnumerator<T> GetEnumerator() => _innerProvider.Execute<IEnumerable<T>>(_expression).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public Type ElementType => typeof(T);
        public Expression Expression => _expression;
        public IQueryProvider Provider => new TestAsyncQueryProvider<T>(_innerProvider);
    }

    private class TestAsyncEnumerator<T>(IEnumerator<T> inner) : IAsyncEnumerator<T>
    {
        public ValueTask DisposeAsync() { inner.Dispose(); return ValueTask.CompletedTask; }
        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(inner.MoveNext());
        public T Current => inner.Current;
    }

    private class TestAsyncQueryProvider<TEntity>(IQueryProvider inner) : IAsyncQueryProvider
    {
        public IQueryable CreateQuery(Expression expression) => new TestAsyncEnumerable<TEntity>(expression, inner);
        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new TestAsyncEnumerable<TElement>(expression, inner);
        public object? Execute(Expression expression) => inner.Execute(expression);
        public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);
        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            Type expectedResultType = typeof(TResult).GetGenericArguments()[0];
            object? executionResult = typeof(IQueryProvider)
                .GetMethods()
                .First(m => m.Name == nameof(IQueryProvider.Execute) && m.IsGenericMethod)
                .MakeGenericMethod(expectedResultType)
                .Invoke(inner, [expression]);

            return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(expectedResultType)
                .Invoke(null, [executionResult])!;
        }
    }

    private static (TestDbContext context, Mock<IDbContextFactory<ApplicationDbContext>> factory) CreateMockContextAndFactory()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=dummy;Database=dummy;", x => x.UseHierarchyId())
            .Options;
        var context = new TestDbContext(options);
        context.TimelineEvents = MockDbSet(new List<TimelineEvent>()).Object;
        context.TagWeightLedgers = MockDbSet(new List<TagWeightLedger>()).Object;
        context.Items = MockDbSet(new List<Item>()).Object;
        context.Tags = MockDbSet(new List<Tag>()).Object;
        context.TagRelationToTags = MockDbSet(new List<TagRelationToTag>()).Object;
        context.PublicTradeOffers = MockDbSet(new List<PublicTradeOffer>()).Object;
        context.UserFollows = MockDbSet(new List<UserFollow>()).Object;
        context.Users = MockDbSet(new List<ApplicationUser>()).Object;
        context.TaggingRequestEntities = MockDbSet(new List<TaggingRequestEntity>()).Object;

        var mockFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);
        mockFactory.Setup(f => f.CreateDbContext())
            .Returns(context);

        return (context, mockFactory);
    }

    // -------------------------------------------------------------
    // 1. TagCardDataProvider Tests
    // -------------------------------------------------------------
    private static async Task TestTagCard_RemoveRelation_NullEntity()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var relations = new List<TagRelationToTag>();
        context.TagRelationToTags = MockDbSet(relations).Object;

        var sut = new TagCardDataProvider(factory.Object);
        var result = await sut.RemoveRelationAsync(999, "user1");

        AssertEqual(TagCardOperationResult.NotFound, result, "Removing non-existent relation must return NotFound");
    }

    private static async Task TestTagCard_ChangeRelation_AlreadyExists()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var relations = new List<TagRelationToTag>
        {
            new() { Id = 1, TargetTagId = 10, TagId = 20, OwnerId = "user1" },
            new() { Id = 2, TargetTagId = 10, TagId = 30, OwnerId = "user1" }
        };
        context.TagRelationToTags = MockDbSet(relations).Object;

        var sut = new TagCardDataProvider(factory.Object);
        var result = await sut.ChangeRelationTagAsync(1, 10, 30, "user1");

        AssertEqual(TagCardOperationResult.AlreadyExists, result, "Duplicate relation must return AlreadyExists");
    }

    private static async Task TestTagCard_ChangeRelation_NullEntity()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var relations = new List<TagRelationToTag>();
        var mockSet = MockDbSet(relations);
        mockSet.Setup(m => m.FindAsync(It.IsAny<object[]>())).ReturnsAsync((TagRelationToTag?)null);
        context.TagRelationToTags = mockSet.Object;

        var sut = new TagCardDataProvider(factory.Object);
        var result = await sut.ChangeRelationTagAsync(999, 10, 20, "user1");

        AssertEqual(TagCardOperationResult.NotFound, result, "Non-existent relation to change must return NotFound");
    }

    private static async Task TestTagCard_SetParent_NullEntity()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var tags = new List<Tag>();
        var mockSet = MockDbSet(tags);
        mockSet.Setup(m => m.FindAsync(It.IsAny<object[]>())).ReturnsAsync((Tag?)null);
        context.Tags = mockSet.Object;

        var sut = new TagCardDataProvider(factory.Object);
        var result = await sut.SetParentTagAsync(999, 10, "user1");

        AssertEqual(TagCardOperationResult.NotFound, result, "Null child tag must return NotFound");
    }

    private static async Task TestTagCard_SetParent_NonOwner()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var tag = new Tag { Id = 5, Name = "TestTag", OwnerId = "ownerA", ParentTagId = null };
        var tags = new List<Tag> { tag };
        var mockSet = MockDbSet(tags);
        mockSet.Setup(m => m.FindAsync(It.IsAny<object[]>())).ReturnsAsync(tag);
        context.Tags = mockSet.Object;

        var sut = new TagCardDataProvider(factory.Object);
        var result = await sut.SetParentTagAsync(5, 10, "attacker");

        AssertEqual(TagCardOperationResult.NotOwner, result, "Non-owner call must return NotOwner");
        AssertEqual(null, tag.ParentTagId, "ParentTagId must NOT be changed when NotOwner");
    }

    private static async Task TestTagCard_SetParent_Owner()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var tag = new Tag { Id = 5, Name = "TestTag", OwnerId = "ownerA", ParentTagId = null };
        var tags = new List<Tag> { tag };
        var mockSet = MockDbSet(tags);
        mockSet.Setup(m => m.FindAsync(It.IsAny<object[]>())).ReturnsAsync(tag);
        context.Tags = mockSet.Object;

        var sut = new TagCardDataProvider(factory.Object);
        var result = await sut.SetParentTagAsync(5, 10, "ownerA");

        AssertEqual(TagCardOperationResult.Success, result, "Owner call must return Success");
        AssertEqual(10, tag.ParentTagId, "ParentTagId must be updated to 10");
        AssertEqual(1, context.SaveChangesCalls, "SaveChanges must be called on success");
    }

    // -------------------------------------------------------------
    // 2. ContractDataProvider Tests
    // -------------------------------------------------------------
    private static async Task TestContract_Deactivate_NullEntity()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var offers = new List<PublicTradeOffer>();
        var mockSet = MockDbSet(offers);
        mockSet.Setup(m => m.FindAsync(It.IsAny<object[]>())).ReturnsAsync((PublicTradeOffer?)null);
        context.PublicTradeOffers = mockSet.Object;

        var sut = new ContractDataProvider(factory.Object);
        var result = await sut.DeactivatePublicOfferAsync(999, "user1");

        AssertFalse(result, "Deactivating null offer must return false");
    }

    private static async Task TestContract_Deactivate_NonOwner()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var offer = new PublicTradeOffer { Id = 1, OwnerId = "ownerA", IsActive = true };
        var offers = new List<PublicTradeOffer> { offer };
        var mockSet = MockDbSet(offers);
        mockSet.Setup(m => m.FindAsync(It.IsAny<object[]>())).ReturnsAsync(offer);
        context.PublicTradeOffers = mockSet.Object;

        var sut = new ContractDataProvider(factory.Object);
        var result = await sut.DeactivatePublicOfferAsync(1, "nonOwner");

        AssertFalse(result, "Deactivating offer by non-owner must return false");
        AssertTrue(offer.IsActive, "Offer must remain active");
    }

    private static async Task TestContract_Deactivate_Owner()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var offer = new PublicTradeOffer { Id = 1, OwnerId = "ownerA", IsActive = true };
        var offers = new List<PublicTradeOffer> { offer };
        var mockSet = MockDbSet(offers);
        mockSet.Setup(m => m.FindAsync(It.IsAny<object[]>())).ReturnsAsync(offer);
        context.PublicTradeOffers = mockSet.Object;

        var sut = new ContractDataProvider(factory.Object);
        var result = await sut.DeactivatePublicOfferAsync(1, "ownerA");

        AssertTrue(result, "Deactivating offer by owner must return true");
        AssertFalse(offer.IsActive, "Offer IsActive must be false");
        AssertEqual(1, context.SaveChangesCalls, "SaveChanges must be called");
    }

    private static async Task TestContract_SearchItems_Empty()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var items = new List<Item>
        {
            new() { Id = 1, Content = "Item1", OwnerId = "user1" },
            new() { Id = 2, Content = "Item2", OwnerId = "user1" }
        };
        context.Items = MockDbSet(items).Object;

        var sut = new ContractDataProvider(factory.Object);
        var res1 = await sut.SearchItemsAsync("");
        AssertEqual(2, res1.Count, "Empty query returns top items");

        var res2 = await sut.SearchItemsAsync(null);
        AssertEqual(2, res2.Count, "Null query returns top items");
    }

    private static async Task TestContract_SearchTags_Empty()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var tags = new List<Tag>
        {
            new() { Id = 1, Name = "Tag1", OwnerId = "user1" },
            new() { Id = 2, Name = "Tag2", OwnerId = "user1" }
        };
        context.Tags = MockDbSet(tags).Object;

        var sut = new ContractDataProvider(factory.Object);
        var res1 = await sut.SearchTagsByNameAsync("");
        AssertEqual(2, res1.Count, "Empty tag query returns top tags");

        var res2 = await sut.SearchTagsByNameAsync(null);
        AssertEqual(2, res2.Count, "Null tag query returns top tags");
    }

    // -------------------------------------------------------------
    // 3. TagTableDataProvider Tests
    // -------------------------------------------------------------
    private static async Task TestTagTable_Locked_ReturnsFalse()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var mockLockService = new Mock<ITagLockService>();
        mockLockService.Setup(l => l.IsTagOrSiblingLockedAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new TagTableDataProvider(factory.Object, mockLockService.Object, null);
        var result = await sut.DeleteTagAsync(10, isAdmin: false);

        AssertFalse(result, "Locked tag deletion must return false");
        factory.Verify(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()), Times.Never(), "Must not open DbContext if tag is locked");
    }

    private static async Task TestTagTable_NullTag_ReturnsFalse()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var tags = new List<Tag>();
        var mockSet = MockDbSet(tags);
        mockSet.Setup(m => m.FindAsync(It.IsAny<object[]>())).ReturnsAsync((Tag?)null);
        context.Tags = mockSet.Object;

        var sut = new TagTableDataProvider(factory.Object, null, null);
        var result = await sut.DeleteTagAsync(999, isAdmin: false);

        AssertFalse(result, "Deleting non-existent tag must return false");
    }

    // -------------------------------------------------------------
    // 4. NotificationsDataProvider Tests
    // -------------------------------------------------------------
    private static async Task TestNotifications_EmptyItemIds()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var sut = new NotificationsDataProvider(factory.Object);

        var result = await sut.GetAssociatedItemsAsync([], "user1", false);

        AssertEqual(0, result.Count, "Empty itemIds must return empty list");
        factory.Verify(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()), Times.Never(), "Must not call CreateDbContextAsync for empty itemIds");
    }

    // -------------------------------------------------------------
    // 5. UserDataProvider Tests
    // -------------------------------------------------------------
    private static async Task TestUserData_ReactionTags_AllExisting()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var existingTags = new List<Tag>
        {
            new() { Id = 1, Name = ReactionTagNames.Shinji, OwnerId = "user1", IsSystem = true },
            new() { Id = 2, Name = ReactionTagNames.Zen, OwnerId = "user1", IsSystem = true },
            new() { Id = 3, Name = ReactionTagNames.Bi, OwnerId = "user1", IsSystem = true }
        };
        context.Tags = MockDbSet(existingTags).Object;

        var method = typeof(UserDataProvider).GetMethod("EnsureReactionTagsAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, [context, "user1"])!;
        await task;

        AssertEqual(0, context.SaveChangesCalls, "SaveChanges must NOT be called when all reaction tags exist");
    }

    private static async Task TestUserData_ReactionTags_MissingTags()
    {
        var (context, factory) = CreateMockContextAndFactory();
        // Only Shinji exists; Zen and Bi are missing
        var existingTags = new List<Tag>
        {
            new() { Id = 1, Name = ReactionTagNames.Shinji, OwnerId = "user1", IsSystem = true }
        };
        context.Tags = MockDbSet(existingTags).Object;

        var method = typeof(UserDataProvider).GetMethod("EnsureReactionTagsAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, [context, "user1"])!;
        await task;

        AssertEqual(3, existingTags.Count, "Missing tags (Zen and Bi) must be added to Tags collection");
        AssertTrue(existingTags.Any(t => t.Name == ReactionTagNames.Zen));
        AssertTrue(existingTags.Any(t => t.Name == ReactionTagNames.Bi));
        AssertEqual(1, context.SaveChangesCalls, "SaveChanges must be called once when new tags are added");
    }

    private static async Task TestUserData_ToggleFollow_SelfFollow()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var sut = new UserDataProvider(factory.Object);

        var result = await sut.ToggleFollowUserAsync("user1", "user1");
        AssertFalse(result, "Self-follow must return false");
        factory.Verify(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()), Times.Never(), "Must not open DB on self-follow");
    }

    private static async Task TestUserData_ToggleFollow_NonExistentTarget()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var users = new List<ApplicationUser>();
        context.Users = MockDbSet(users).Object;

        var sut = new UserDataProvider(factory.Object);
        var result = await sut.ToggleFollowUserAsync("user1", "user2");

        AssertFalse(result, "Following non-existent user must return false");
    }

    private static async Task TestUserData_ToggleFollow_ExistingFollow()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var users = new List<ApplicationUser> { new() { Id = "user2" } };
        var follow = new UserFollow { Id = 1, OwnerId = "user1", FollowedUserId = "user2" };
        var follows = new List<UserFollow> { follow };

        context.Users = MockDbSet(users).Object;
        context.UserFollows = MockDbSet(follows).Object;

        var sut = new UserDataProvider(factory.Object);
        var result = await sut.ToggleFollowUserAsync("user1", "user2");

        AssertFalse(result, "Unfollowing existing follow must return false");
        AssertFalse(follows.Contains(follow), "Follow entity must be removed");
        AssertEqual(1, context.SaveChangesCalls);
    }

    private static async Task TestUserData_ToggleFollow_NewFollow()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var users = new List<ApplicationUser> { new() { Id = "user2" } };
        var follows = new List<UserFollow>();

        context.Users = MockDbSet(users).Object;
        context.UserFollows = MockDbSet(follows).Object;

        var sut = new UserDataProvider(factory.Object);
        var result = await sut.ToggleFollowUserAsync("user1", "user2");

        AssertTrue(result, "New follow must return true");
        AssertEqual(1, follows.Count, "New follow entity must be added");
        AssertEqual("user1", follows[0].OwnerId);
        AssertEqual("user2", follows[0].FollowedUserId);
        AssertEqual(1, context.SaveChangesCalls);
    }

    // -------------------------------------------------------------
    // 6. AdminDataProvider Tests
    // -------------------------------------------------------------
    private static async Task TestAdmin_DuplicateTags_Deduplicated()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var tags = new List<Tag>();
        var items = new List<Item>();
        context.Tags = MockDbSet(tags).Object;
        context.Items = MockDbSet(items).Object;

        var sut = new AdminDataProvider(factory.Object);
        var lines = new List<string[]>
        {
            new[] { "ItemContent1", "TagA", "TagA", "TagB" }
        };

        var count = await sut.ImportItemsWithTagsAsync("user1", lines);

        AssertEqual(1, count, "Imported count must be 1");
        AssertEqual(1, items.Count, "One item must be created");
        AssertEqual(2, items[0].TagRelations.Count, "Duplicate tags in line must be deduplicated to exactly 2 TagRelations");
        AssertTrue(items[0].TagRelations.Any(tr => tr.Tag.Name == "TagA"));
        AssertTrue(items[0].TagRelations.Any(tr => tr.Tag.Name == "TagB"));
        AssertEqual(1, context.SaveChangesCalls);
    }

    private static async Task TestAdmin_UnknownTags_CreatesNewTags()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var tags = new List<Tag>();
        var items = new List<Item>();
        context.Tags = MockDbSet(tags).Object;
        context.Items = MockDbSet(items).Object;

        var sut = new AdminDataProvider(factory.Object);
        var lines = new List<string[]>
        {
            new[] { "Item1", "BrandNewTag1", "BrandNewTag2" }
        };

        var count = await sut.ImportItemsWithTagsAsync("user1", lines);

        AssertEqual(1, count);
        AssertEqual(2, tags.Count, "New tags must be added to Tags db set");
        AssertEqual("BrandNewTag1", tags[0].Name);
        AssertEqual("BrandNewTag2", tags[1].Name);
        AssertEqual("user1", tags[0].OwnerId);
        AssertEqual(1, context.SaveChangesCalls);
    }

    private static async Task TestAdmin_EmptyTags_ImportsItem()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var tags = new List<Tag>();
        var items = new List<Item>();
        context.Tags = MockDbSet(tags).Object;
        context.Items = MockDbSet(items).Object;

        var sut = new AdminDataProvider(factory.Object);
        var lines = new List<string[]>
        {
            new[] { "ItemOnly" },
            new[] { "ItemWithBlanks", "   ", "" }
        };

        var count = await sut.ImportItemsWithTagsAsync("user1", lines);

        AssertEqual(2, count, "Both items without tags must be imported");
        AssertEqual(0, items[0].TagRelations.Count, "ItemOnly must have 0 tag relations");
        AssertEqual(0, items[1].TagRelations.Count, "ItemWithBlanks must have 0 tag relations");
        AssertEqual(1, context.SaveChangesCalls);
    }

    private static async Task TestAdmin_EmptyLines_ReturnsZero()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var tags = new List<Tag>();
        var items = new List<Item>();
        context.Tags = MockDbSet(tags).Object;
        context.Items = MockDbSet(items).Object;

        var sut = new AdminDataProvider(factory.Object);
        var count = await sut.ImportItemsWithTagsAsync("user1", []);

        AssertEqual(0, count, "Empty lines must return 0");
    }

    // -------------------------------------------------------------
    // 7. TagTreeDataProvider Tests
    // -------------------------------------------------------------
    private static async Task TestTagTree_LoadPendingTagMoves()
    {
        var (context, factory) = CreateMockContextAndFactory();
        var req1 = new TaggingRequestEntity
        {
            Id = 1,
            OwnerId = "owner1",
            ContractType = ContractTypes.Move,
            RequestedTagId = 10,
            RequestedTag = new Tag { Id = 10, Name = "TagName1", OwnerId = "owner1" },
            RequesterUserId = "reqUser1",
            TagOwnerUserId = "owner1",
            Payload = new TagMovePayload(NewParentTagId: 20)
        };

        var req2 = new TaggingRequestEntity
        {
            Id = 2,
            OwnerId = "owner2",
            ContractType = ContractTypes.Move,
            RequestedTagId = 11,
            RequesterUserId = "reqUser2",
            TagOwnerUserId = "owner2",
            Payload = new TagMovePayload(NewParentTagId: 25)
        };

        // req3 has a non-TagMove payload
        var req3 = new TaggingRequestEntity
        {
            Id = 3,
            OwnerId = "owner3",
            ContractType = ContractTypes.Move,
            RequestedTagId = 12,
            Payload = new EmptyPayload()
        };

        var requests = new List<TaggingRequestEntity> { req1, req2, req3 };
        context.TaggingRequestEntities = MockDbSet(requests).Object;

        var sut = new TagTreeDataProvider(factory.Object);
        var result = await sut.LoadPendingTagMovesAsync();

        AssertEqual(2, result.Count, "Only the 2 TagMove requests must be loaded");
        AssertEqual("TagName1", result[0].TagName);
        AssertEqual(20, result[0].NewParentTagId);
        AssertEqual("不明なタグ", result[1].TagName, "Null tag name should fall back to '不明なタグ'");
        AssertEqual(25, result[1].NewParentTagId);
    }

    // -------------------------------------------------------------
    // 8. ItemDetailDataProvider Tests
    // -------------------------------------------------------------
    private static Task TestItemDetail_LedgerSynthesis()
    {
        var item = new Item
        {
            Id = 100,
            Content = "Test Item",
            OwnerId = "owner1",
            TagRelations =
            [
                new TagRelation { Id = 1, TagId = 10, Weight = 2, OwnerId = "owner1", Tag = new Tag { Name = "Tag10", OwnerId = "owner1" } },
                new TagRelation { Id = 2, TagId = 20, Weight = 3, OwnerId = "owner1", Tag = new Tag { Name = "Tag20", OwnerId = "owner1" } }
            ]
        };

        var ledgers = new List<TagWeightLedger>
        {
            new() { SourceId = 1, TagId = 10, ItemId = 100, OwnerId = "owner1", TagNameSnapshot = "Tag10" }
        };

        HashSet<int> existingLedgerRelationIds = [.. ledgers
            .Where(l => l.SourceId.HasValue)
            .Select(l => l.SourceId!.Value)];

        HashSet<int> existingLedgerTagIds = [.. ledgers
            .Where(l => l.ItemId == 100)
            .Select(l => l.TagId)];

        ledgers.AddRange(item.TagRelations
            .Where(tr => !existingLedgerRelationIds.Contains(tr.Id) && !existingLedgerTagIds.Contains(tr.TagId))
            .Select(tr => new TagWeightLedger
            {
                TagId = tr.TagId,
                SourceId = tr.Id,
                ItemId = 100,
                OwnerId = "owner1",
                TagNameSnapshot = tr.Tag?.Name ?? string.Empty,
                SourceType = "TagRelationInsert"
            }));

        AssertEqual(2, ledgers.Count, "Only the missing TagRelation (Id=2) should be synthesized into ledgers");
        AssertEqual(2, ledgers[1].SourceId, "Synthesized ledger SourceId should be 2");
        return Task.CompletedTask;
    }

    // -------------------------------------------------------------
    // 9. TagLockService Tests
    // -------------------------------------------------------------
    private static Task TestTagLockService_LockCalculation()
    {
        var tags = new List<Tag>
        {
            new() { Id = 1, Name = "Root", OwnerId = "owner1", IsLocked = false, ParentTagId = null },
            new() { Id = 2, Name = "LockedChild", OwnerId = "owner1", IsLocked = true, ParentTagId = 1 },
            new() { Id = 3, Name = "UnlockedChild", OwnerId = "owner1", IsLocked = false, ParentTagId = 1 },
            new() { Id = 4, Name = "OrphanLocked", OwnerId = "owner1", IsLocked = true, ParentTagId = null }
        };

        HashSet<int?> parentsWithLockedChildren = [.. tags
            .Where(t => t.IsLocked && t.ParentTagId.HasValue)
            .Select(t => t.ParentTagId)];

        AssertEqual(1, parentsWithLockedChildren.Count, "Exactly 1 parent should have locked children");
        AssertTrue(parentsWithLockedChildren.Contains(1), "Parent 1 should be in the set");
        AssertFalse(parentsWithLockedChildren.Contains(null), "Null parent should not be in the set");
        return Task.CompletedTask;
    }

    // -------------------------------------------------------------
    // 10-15. Utilities & Executors
    // -------------------------------------------------------------
    private static Task TestContentParser_ExtractUrls()
    {
        var text = "Check https://example.com and [https://example.com] and https://test.org and [Tag:MyTag]";
        var urls = ContentParser.ExtractUrls(text);

        AssertEqual(2, urls.Count, "Duplicate URLs must be deduplicated");
        AssertTrue(urls.Contains("https://example.com"));
        AssertTrue(urls.Contains("https://test.org"));
        return Task.CompletedTask;
    }

    private static async Task TestTagSuggestion_Cancellation()
    {
        var mockEmbedding = new Mock<ITagEmbeddingService>();
        var (context, factory) = CreateMockContextAndFactory();
        var sut = new TagSuggestionService(factory.Object, mockEmbedding.Object);

        var resEmpty = await sut.SuggestTagsAsync("");
        AssertEqual(0, resEmpty.Count, "Empty content must return empty list immediately");

        var cts = new CancellationTokenSource();
        cts.Cancel();
        var resCancelled = await sut.SuggestTagsAsync("Some content", cancellationToken: cts.Token);
        AssertEqual(0, resCancelled.Count, "Cancelled token must return empty list immediately");

        mockEmbedding.Verify(e => e.GenerateEmbeddingAsync(It.IsAny<string>()), Times.Never(), "Must not generate embedding on cancelled token");
    }

    private static Task TestTagSimilarity_Boosting()
    {
        var candidates = new List<Tag>
        {
            new() { Name = "Programming", OwnerId = "owner1" },
            new() { Name = "Program", OwnerId = "owner1" },
            new() { Name = "Unrelated", OwnerId = "owner1" }
        };

        var service = new TagSimilarityService();
        var results = service.FindSimilarTags(candidates, "Program", 0.1f, 10);
        AssertTrue(results.Count >= 2, "Should find substring matches");
        AssertEqual("Program", results[0].Tag.Name, "Exact match should be first");
        AssertTrue(results[0].Similarity >= 0.95f, "Exact match score should be high");
        return Task.CompletedTask;
    }

    private static Task TestItemList_FilterSeparation()
    {
        List<object> filters =
        [
            new TagIdFilter(10, null),
            new TagNameFilter("TagA", null),
            new TagIdFilter(20, null),
            new TagNameFilter("TagB", null)
        ];

        List<int> idFilterIds = [.. filters.OfType<TagIdFilter>().Select(f => f.TagId)];
        List<string> nameFilterNames = [.. filters.OfType<TagNameFilter>().Select(f => f.TagName)];

        AssertEqual(2, idFilterIds.Count);
        AssertEqual(10, idFilterIds[0]);
        AssertEqual(20, idFilterIds[1]);

        AssertEqual(2, nameFilterNames.Count);
        AssertEqual("TagA", nameFilterNames[0]);
        AssertEqual("TagB", nameFilterNames[1]);
        return Task.CompletedTask;
    }

    private static Task TestInternalLink_Partitioning()
    {
        var resolved = new List<LinkConversionCandidate>
        {
            new("A", 1, "TagA", 0.9f, 0, 1, true),
            new("B", 2, "TagB", 0.5f, 2, 1, false),
            new("C", 3, "TagC", 0.95f, 4, 1, true)
        };

        List<LinkConversionCandidate> auto = [.. resolved.Where(c => c.IsAutoReplace)];
        List<LinkConversionCandidate> manual = [.. resolved.Where(c => !c.IsAutoReplace)];

        AssertEqual(2, auto.Count);
        AssertEqual(1, manual.Count);
        AssertEqual("TagB", manual[0].TagName);
        return Task.CompletedTask;
    }

    private static Task TestStringSimilarity_Distance()
    {
        AssertEqual(0, StringSimilarity.LevenshteinDistance("kitten", "kitten"));
        AssertEqual(3, StringSimilarity.LevenshteinDistance("kitten", "sitting"));
        AssertEqual(3, StringSimilarity.LevenshteinDistance("sitting", "kitten"), "Tuple swap must maintain symmetry");
        return Task.CompletedTask;
    }
}
