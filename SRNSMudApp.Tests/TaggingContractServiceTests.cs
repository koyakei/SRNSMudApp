using SRNSMudApp.Models.Unions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using SRNSMudApp.Data;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests;

public class TaggingContractServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TaggingContractService _service;

    public TaggingContractServiceTests()
    {
        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbContext = new ApplicationDbContext(options);
        _service = new TaggingContractService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }

    // =====================================================================================
    // 1. TaggingRequestEntity のシナリオ
    // =====================================================================================

    [Fact]
    public async Task ProposeGratisContractAsync_ShouldCreateGratisContractInProposedStatus()
    {
        // Arrange
        var requesterId = "Requester";
        var tagOwnerId = "TagOwner";
        var message = "Please give me this tag!";

        // Act
        var result = await _service.ProposeGratisContractAsync(
            requesterId, tagOwnerId, 1, 2, message: message);
        Assert.True(result is Success<TaggingRequestEntity>);
        var contract = (result switch { Success<TaggingRequestEntity> s => s.Value, _ => throw new Exception("Expected Success") });

        // Assert
        Assert.NotNull(contract);
        Assert.Equal(requesterId, contract.RequesterUserId);
        Assert.Equal(requesterId, contract.OwnerId);
        Assert.Equal(tagOwnerId, contract.TagOwnerUserId);
        Assert.Equal(1, contract.TargetItemId);
        Assert.Equal(2, contract.RequestedTagId);
        Assert.Equal(TradeStatus.Proposed, contract.Status);
        Assert.True(contract.Payload is GratisPayload p && p.RequesterMessage == message);

        TaggingRequestEntity? saved = await _dbContext.TaggingRequestEntities
            .FirstOrDefaultAsync(c => c.Id == contract.Id);
        Assert.NotNull(saved);
    }

    [Fact]
    public async Task ProposeMutualContractAsync_ShouldCreateMutualContractInProposedStatus()
    {
        // Arrange
        var requesterId = "Requester";
        var tagOwnerId = "TagOwner";

        // Act
        var result = await _service.ProposeMutualContractAsync(
            requesterId, tagOwnerId, 1, 2, 3, 4, 5);
        Assert.True(result is Success<TaggingRequestEntity>);
        var contract = (result switch { Success<TaggingRequestEntity> s => s.Value, _ => throw new Exception("Expected Success") });

        // Assert
        Assert.NotNull(contract);
        Assert.Equal(requesterId, contract.RequesterUserId);
        Assert.Equal(tagOwnerId, contract.TagOwnerUserId);
        Assert.Equal(1, contract.TargetItemId);
        Assert.Equal(2, contract.RequestedTagId);
        Assert.True(contract.Payload is MutualPayload pm && pm.OfferedTargetItemId == 3);
        Assert.True(contract.Payload is MutualPayload pm2 && pm2.OfferedTagId == 4);
        Assert.Equal(5, contract.ConsumedRightAssetId);
        Assert.Equal(TradeStatus.Proposed, contract.Status);

        TaggingRequestEntity? saved = await _dbContext.TaggingRequestEntities
            .FirstOrDefaultAsync(c => c.Id == contract.Id);
        Assert.NotNull(saved);
    }

    [Fact]
    public async Task ScenarioA_GratisWithTip_ShouldConsumeProvidedAssetAndTagItem()
    {
        // Arrange
        // Motivation: ユーザーA（Requester）は、ユーザーB（TagOwner）のタグ「C# Master」を自分のアイテムに付けたい。
        // チップとして、手持ちの「C# Master」の RightAsset を自発的に添付して Gratis 契約を提案した。
        var userA = "UserA";
        var userB = "UserB";

        var targetItem = new Item { Content = "My C# Project", OwnerId = userA };
        var tag = new Tag { Name = "C# Master", OwnerId = userB, CachedWeight = 100 };
        var rightAsset = new RightAsset { Amount = 1, OwnerId = userA, TargetTagId = 1 }; // チップ

        _dbContext.Items!.Add(targetItem);
        _dbContext.Tags!.Add(tag);
        await _dbContext.SaveChangesAsync();

        rightAsset.TargetTagId = tag.Id;
        _dbContext.RightAssets!.Add(rightAsset);
        await _dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity { ContractType = "Gratis", 
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItem.Id,
            RequestedTagId = tag.Id,
            ConsumedRightAssetId = rightAsset.Id, // チップを添付
            Status = TradeStatus.Proposed,
            OwnerId = userA,
            Payload = new GratisPayload("Please tag my item. Here is a tip!")
        };
        _dbContext.TaggingRequestEntities!.Add(contract);
        await _dbContext.SaveChangesAsync();

        // Act
        // When: ユーザーBが承認を実行する
        var acceptResult = await _service.AcceptContractAsync(contract.Id, userB);
        
        // Assert
        // Then: ユーザーAが提供した RightAsset が消費される
        var assetIsBurned = await _dbContext.RightAssets.AnyAsync(a => a.Id == rightAsset.Id && a.IsBurned);
        Assert.True(assetIsBurned, "チップとして提供された RightAsset は消費されるべき");

        // Then: タグリレーションが作成される
        TagRelation? relation =
            await _dbContext.TagRelations!.FirstOrDefaultAsync(tr => tr.ItemId == targetItem.Id && tr.TagId == tag.Id);
        Assert.NotNull(relation);
        Assert.Equal(userA, relation.OwnerId);

        // Then: 元帳に IsOwnerAction=true として記録され、提供したアセットIDが結びつく
        TagWeightLedger? ledger =
            await _dbContext.TagWeightLedgers!.FirstOrDefaultAsync(l => l.SourceId == relation.Id);
        Assert.NotNull(ledger);
        Assert.Equal(rightAsset.Id, ledger.ConsumedRightAssetId);
        Assert.Equal(100, ledger.PreviousWeight);
        Assert.Equal(101, ledger.NewWeight);
        Assert.True(ledger.IsOwnerAction);
        Assert.Equal("C# Master", ledger.TagNameSnapshot);
        Assert.Equal("Gratis Tagging Contract Accepted", ledger.Reason);
        Assert.Equal(userB, ledger.OwnerId); // 承認したタグオーナーが実行者
    }

    [Fact]
    public async Task ScenarioB_GratisWithoutTip_ShouldMintAndBurnAssetByOwner()
    {
        // Arrange
        // Motivation: ユーザーAはアセットを持たないが、ユーザーBのタグを使いたくて Gratis 契約を送る。
        // ユーザーBはアイテムを気に入り、無償でタグを提供することにした。
        var userA = "UserA";
        var userB = "UserB";

        var targetItem = new Item { Content = "Nice Project", OwnerId = userA };
        var tag = new Tag { Name = "Approved", OwnerId = userB, CachedWeight = 100 };

        _dbContext.Items!.Add(targetItem);
        _dbContext.Tags!.Add(tag);
        await _dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity { ContractType = "Gratis", 
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItem.Id,
            RequestedTagId = tag.Id,
            ConsumedRightAssetId = 0, // アセットなし
            Status = TradeStatus.Proposed,
            OwnerId = userA
        };
        _dbContext.TaggingRequestEntities!.Add(contract);
        await _dbContext.SaveChangesAsync();

        // Act
        // When: ユーザーBが承認を実行する
        var acceptResult = await _service.AcceptContractAsync(contract.Id, userB);
        Assert.True(acceptResult is Success<string>, acceptResult switch { Failure f => f.ErrorMessage, _ => "Expected Success" });
        
        // Assert
        // Then: タグリレーションが作成される
        TagRelation? relation =
            await _dbContext.TagRelations!.FirstOrDefaultAsync(tr => tr.ItemId == targetItem.Id && tr.TagId == tag.Id);
        Assert.NotNull(relation);

        // Then: ユーザーB（タグオーナー）が自らタグ発行のための RightAsset を新規発行し、即消費する
        TagWeightLedger? ledger =
            await _dbContext.TagWeightLedgers!.FirstOrDefaultAsync(l => l.SourceId == relation.Id);
        Assert.NotNull(ledger);
        Assert.NotEqual(0, ledger.ConsumedRightAssetId);

        RightAsset? mintedAsset = await _dbContext.RightAssets!.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == ledger.ConsumedRightAssetId);
        Assert.NotNull(mintedAsset);
        Assert.True(mintedAsset.IsBurned);
        Assert.Equal(userB, mintedAsset.OwnerId); // オーナーが発行したアセット
    }

    // =====================================================================================
    // 2. TaggingRequestEntity のシナリオ
    // =====================================================================================

    [Fact]
    public async Task ScenarioC_MutualTagging_ShouldSwapTagsAndMintComplementaryAsset()
    {
        // Arrange
        // Motivation: ユーザーAとBがアイテムを評価し合い、タグを交換することに合意。
        // ユーザーAはBのタグを取得するための対価アセットを提供して Mutual 契約を提案。
        var userA = "UserA";
        var userB = "UserB";

        var targetItemA = new Item { Content = "TargetItemA", OwnerId = userA };
        var targetItemB = new Item { Content = "TargetItemB", OwnerId = userB };
        var tagA = new Tag { Name = "TagA", OwnerId = userA, CachedWeight = 50 };
        var tagB = new Tag { Name = "TagB", OwnerId = userB, CachedWeight = 100 };
        var rightAssetA = new RightAsset { Amount = 1, OwnerId = userA }; // User A が Bのタグのために提供するアセット

        _dbContext.Items!.AddRange(targetItemA, targetItemB);
        _dbContext.Tags!.AddRange(tagA, tagB);
        _dbContext.RightAssets!.Add(rightAssetA);
        await _dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity { ContractType = "Mutual", 
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            Payload = new MutualPayload(targetItemB.Id, tagA.Id),
            ConsumedRightAssetId = rightAssetA.Id, // User A が提供
            Status = TradeStatus.Proposed,
            OwnerId = userA
        };
        _dbContext.TaggingRequestEntities!.Add(contract);
        await _dbContext.SaveChangesAsync();

        // Act
        // When: ユーザーBが提案を受け入れ、「承認」を実行する
        var acceptResult = await _service.AcceptContractAsync(contract.Id, userB);
        
        // Assert
        // Then: ユーザーAが提供したアセットが消費される
        var assetIsBurned = await _dbContext.RightAssets.AnyAsync(a => a.Id == rightAssetA.Id && a.IsBurned);
        Assert.True(assetIsBurned);

        // Then: 両方のタグリレーションが作成される
        TagRelation? relation1 =
            await _dbContext.TagRelations!.FirstOrDefaultAsync(tr =>
                tr.ItemId == targetItemA.Id && tr.TagId == tagB.Id);
        TagRelation? relation2 =
            await _dbContext.TagRelations!.FirstOrDefaultAsync(tr =>
                tr.ItemId == targetItemB.Id && tr.TagId == tagA.Id);
        Assert.NotNull(relation1);
        Assert.NotNull(relation2);

        // Then: TagB (RequestedTag) の元帳更新 (ユーザーBの承認による)
        TagWeightLedger? ledger1 =
            await _dbContext.TagWeightLedgers!.FirstOrDefaultAsync(l => l.SourceId == relation1.Id);
        Assert.NotNull(ledger1);
        Assert.Equal(rightAssetA.Id, ledger1.ConsumedRightAssetId);
        Assert.True(ledger1.IsOwnerAction);

        // Then: TagA (OfferedTag) の元帳更新 (ユーザーAのタグなので、Aがアセットを発行・消費して補完される)
        TagWeightLedger? ledger2 =
            await _dbContext.TagWeightLedgers!.FirstOrDefaultAsync(l => l.SourceId == relation2.Id);
        Assert.NotNull(ledger2);
        Assert.False(ledger2.IsOwnerAction); // Bが実行者だが、タグオーナーはAなので false

        RightAsset? mintedAssetA = await _dbContext.RightAssets!.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == ledger2.ConsumedRightAssetId);
        Assert.NotNull(mintedAssetA);
        Assert.True(mintedAssetA.IsBurned);
        Assert.Equal(userA, mintedAssetA.OwnerId); // ユーザーA名義で発行された
    }

    [Fact]
    public async Task ScenarioC_MutualTagging_Error_NoAssetProvided()
    {
        // Arrange
        // Given: ユーザーAがアセットを提供せずに相互タグ付けを要求する
        var userA = "UserA";
        var userB = "UserB";
        var targetItemA = new Item { Content = "TargetItemA", OwnerId = userA };
        var targetItemB = new Item { Content = "TargetItemB", OwnerId = userB };
        var tagA = new Tag { Name = "TagA", OwnerId = userA, CachedWeight = 50 };
        var tagB = new Tag { Name = "TagB", OwnerId = userB, CachedWeight = 100 };

        _dbContext.Items!.AddRange(targetItemA, targetItemB);
        _dbContext.Tags!.AddRange(tagA, tagB);
        await _dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity { ContractType = "Mutual", 
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            Payload = new MutualPayload(targetItemB.Id, tagA.Id),
            ConsumedRightAssetId = 0, // アセットなし
            Status = TradeStatus.Proposed,
            OwnerId = userA
        };
        _dbContext.TaggingRequestEntities!.Add(contract);
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        // When: ユーザーBが承認しようとする
        // Then: 対価アセットが必要である旨の例外が発生する
        var res = await _service.AcceptContractAsync(contract.Id, userB);
                Assert.True(res is Failure);
        var ex = res switch { Failure f => f, _ => throw new Exception("Expected Failure") };
        Assert.Equal("相互タグ付けには対価のアセットが必要です。", ex.ErrorMessage);
    }

    // =====================================================================================
    // 3. TaggingRequestEntity のシナリオ
    // =====================================================================================

    [Fact]
    public async Task ScenarioD_PublicOffer_ExactAsset_ShouldTriggerAndConsume()
    {
        // Arrange
        // Motivation: ユーザーBはスパム防止のため、RightAssetを10個要求するオファーを作成。
        // ユーザーAは対象アセットを10個集めてトリガーを実行する。
        var userA = "UserA";
        var userB = "UserB";

        var targetItemA = new Item { Content = "TargetItemA", OwnerId = userA };
        var tagB = new Tag { Name = "TagB", OwnerId = userB, CachedWeight = 100 };

        _dbContext.Items!.Add(targetItemA);
        _dbContext.Tags!.Add(tagB);
        await _dbContext.SaveChangesAsync();

        var rightAsset = new RightAsset { Amount = 10, OwnerId = userA, TargetTagId = tagB.Id };
        _dbContext.RightAssets!.Add(rightAsset);

        var publicOffer = new PublicTradeOffer
        {
            OwnerId = userB,
            OfferedTagId = tagB.Id,
            RequiredAssetAmount = 10, // 10要求
            IsActive = true
        };
        _dbContext.PublicTradeOffers!.Add(publicOffer);
        await _dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity { ContractType = "Trigger", 
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            ConsumedRightAssetId = rightAsset.Id,
            Payload = new PublicOfferPayload(publicOffer.Id),
            Status = TradeStatus.Proposed,
            OwnerId = userA
        };
        _dbContext.TaggingRequestEntities!.Add(contract);
        await _dbContext.SaveChangesAsync();

        // Act
        // When: ユーザーAがトリガーを実行する
        var acceptResult = await _service.AcceptContractAsync(contract.Id, userA);
        
        // Assert
        // Then: アセットが消費され、リレーションと元帳（IsOwnerAction=false）が作成される。
        var assetIsBurned = await _dbContext.RightAssets.AnyAsync(a => a.Id == rightAsset.Id && a.IsBurned);
        Assert.True(assetIsBurned, "要求量ピッタリのアセットは消費されるべき");

        TagWeightLedger? ledger = await _dbContext.TagWeightLedgers!.FirstOrDefaultAsync();
        Assert.NotNull(ledger);
        Assert.False(ledger.IsOwnerAction); // 要求者自身がトリガーしたため
        Assert.Equal("Public Offer Triggered", ledger.Reason);
    }

    [Fact]
    public async Task ScenarioF_PublicOffer_FreeCampaign_ShouldMintAssetByOwner()
    {
        // Arrange
        // Motivation: ユーザーBはプロモーションのため、要求量0のオファーを公開。
        // ユーザーAはアセットを提供せずにトリガーする。
        var userA = "UserA";
        var userB = "UserB";

        var targetItemA = new Item { Content = "TargetItemA", OwnerId = userA };
        var tagB = new Tag { Name = "TagB", OwnerId = userB, CachedWeight = 100 };

        _dbContext.Items!.Add(targetItemA);
        _dbContext.Tags!.Add(tagB);
        await _dbContext.SaveChangesAsync();

        var publicOffer = new PublicTradeOffer
        {
            OwnerId = userB,
            OfferedTagId = tagB.Id,
            RequiredAssetAmount = 0, // 無料
            IsActive = true
        };
        _dbContext.PublicTradeOffers!.Add(publicOffer);
        await _dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity { ContractType = "Trigger", 
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            ConsumedRightAssetId = 0, // アセット提供なし
            Payload = new PublicOfferPayload(publicOffer.Id),
            Status = TradeStatus.Proposed,
            OwnerId = userA
        };
        _dbContext.TaggingRequestEntities!.Add(contract);
        await _dbContext.SaveChangesAsync();

        // Act
        // When: ユーザーAがトリガーを実行する
        var acceptResult = await _service.AcceptContractAsync(contract.Id, userA);
        
        // Assert
        // Then: オファー作成者(ユーザーB)名義の RightAsset が新規発行＆消費される
        TagWeightLedger? ledger = await _dbContext.TagWeightLedgers!.FirstOrDefaultAsync();
        Assert.NotNull(ledger);
        // Assert.NotNull(ledger.ConsumedRightAssetId);

        RightAsset? mintedAsset = await _dbContext.RightAssets!.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == ledger.ConsumedRightAssetId);
        Assert.NotNull(mintedAsset);
        Assert.True(mintedAsset.IsBurned);
        Assert.Equal(userB, mintedAsset.OwnerId); // オーナー名義で補填
    }

    [Fact]
    public async Task ScenarioE_PublicOffer_Error_InsufficientAsset()
    {
        // Arrange
        // Given: オファーが10要求しているが、提供されたアセットが5。
        var userA = "UserA";
        var userB = "UserB";

        var targetItemA = new Item { Content = "TargetItemA", OwnerId = userA };
        var tagB = new Tag { Name = "TagB", OwnerId = userB };

        _dbContext.Items!.Add(targetItemA);
        _dbContext.Tags!.Add(tagB);
        await _dbContext.SaveChangesAsync();

        var rightAsset = new RightAsset { Amount = 5, OwnerId = userA, TargetTagId = tagB.Id }; // 量が不足している
        _dbContext.RightAssets!.Add(rightAsset);

        var publicOffer = new PublicTradeOffer
        {
            OwnerId = userB, OfferedTagId = tagB.Id, RequiredAssetAmount = 10, IsActive = true
        };
        _dbContext.PublicTradeOffers!.Add(publicOffer);
        await _dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity { ContractType = "Trigger", 
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            ConsumedRightAssetId = rightAsset.Id,
            Payload = new PublicOfferPayload(publicOffer.Id),
            Status = TradeStatus.Proposed,
            OwnerId = userA
        };
        _dbContext.TaggingRequestEntities!.Add(contract);
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        // When: ユーザーAがトリガーを実行する
        // Then: 量が不足している旨の例外が発生する
        var res = await _service.AcceptContractAsync(contract.Id, userA);
                Assert.True(res is Failure);
        var ex = res switch { Failure f => f, _ => throw new Exception("Expected Failure") };
        Assert.Equal("提供された RightAsset の量が不足しています。", ex.ErrorMessage);
    }

    [Fact]
    public async Task ScenarioE_PublicOffer_Error_WrongTagAsset()
    {
        // Arrange
        // Given: オファーはTagB用だが、提供されたアセットは別のTagC用。
        var userA = "UserA";
        var userB = "UserB";

        var targetItemA = new Item { Content = "TargetItemA", OwnerId = userA };
        var tagB = new Tag { Name = "TagB", OwnerId = userB };
        var tagC = new Tag { Name = "TagC", OwnerId = userA };

        _dbContext.Items!.Add(targetItemA);
        _dbContext.Tags!.AddRange(tagB, tagC);
        await _dbContext.SaveChangesAsync();

        var rightAsset = new RightAsset { Amount = 10, OwnerId = userA, TargetTagId = tagC.Id }; // 間違ったタグのアセット
        _dbContext.RightAssets!.Add(rightAsset);

        var publicOffer = new PublicTradeOffer
        {
            OwnerId = userB, OfferedTagId = tagB.Id, RequiredAssetAmount = 10, IsActive = true
        };
        _dbContext.PublicTradeOffers!.Add(publicOffer);
        await _dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity { ContractType = "Trigger", 
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            ConsumedRightAssetId = rightAsset.Id,
            Payload = new PublicOfferPayload(publicOffer.Id),
            Status = TradeStatus.Proposed,
            OwnerId = userA
        };
        _dbContext.TaggingRequestEntities!.Add(contract);
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        // When: ユーザーAがトリガーを実行する
        // Then: 対象のタグの権利ではない旨の例外が発生する
        var res = await _service.AcceptContractAsync(contract.Id, userA);
                Assert.True(res is Failure);
        var ex = res switch { Failure f => f, _ => throw new Exception("Expected Failure") };
        Assert.Equal("提供された RightAsset は対象のタグの権利ではありません。", ex.ErrorMessage);
    }

    [Fact]
    public async Task ScenarioE_PublicOffer_Error_InactiveOffer()
    {
        // Arrange
        // Given: オファーの IsActive が false
        var userA = "UserA";
        var userB = "UserB";

        var targetItemA = new Item { Content = "TargetItemA", OwnerId = userA };
        var tagB = new Tag { Name = "TagB", OwnerId = userB };

        _dbContext.Items!.Add(targetItemA);
        _dbContext.Tags!.Add(tagB);
        await _dbContext.SaveChangesAsync();

        var rightAsset = new RightAsset { Amount = 10, OwnerId = userA, TargetTagId = tagB.Id };
        _dbContext.RightAssets!.Add(rightAsset);

        var publicOffer = new PublicTradeOffer
        {
            OwnerId = userB, OfferedTagId = tagB.Id, RequiredAssetAmount = 10, IsActive = false
        }; // 非アクティブ
        _dbContext.PublicTradeOffers!.Add(publicOffer);
        await _dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity { ContractType = "Trigger", 
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            ConsumedRightAssetId = rightAsset.Id,
            Payload = new PublicOfferPayload(publicOffer.Id),
            Status = TradeStatus.Proposed,
            OwnerId = userA
        };
        _dbContext.TaggingRequestEntities!.Add(contract);
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        // When: ユーザーAがトリガーを実行する
        // Then: 現在有効ではない旨の例外が発生する
        var res = await _service.AcceptContractAsync(contract.Id, userA);
                Assert.True(res is Failure);
        var ex = res switch { Failure f => f, _ => throw new Exception("Expected Failure") };
        Assert.Equal("この公開オファーは現在有効ではありません。", ex.ErrorMessage);
    }

    // =====================================================================================
    // その他の基本エッジケース
    // =====================================================================================

    [Fact]
    public async Task AcceptContractAsync_ShouldThrow_WhenUserIsNotOwner()
    {
        // Arrange
        var contract = new TaggingRequestEntity { ContractType = "Gratis", 
            TagOwnerUserId = "UserB",
            Status = TradeStatus.Proposed,
            OwnerId = "UserA",
            RequestedTag = new Tag { Name = "Tag", OwnerId = "UserB" },
            TargetItem = new Item { Content = "Item", OwnerId = "System" }
        };
        _dbContext.TaggingRequestEntities!.Add(contract);
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        var res = await _service.AcceptContractAsync(contract.Id, "WrongUser");
                Assert.True(res is Failure);
        var ex = res switch { Failure f => f, _ => throw new Exception("Expected Failure") };
        Assert.Equal("承認できない契約です。", ex.ErrorMessage);
    }

    [Fact]
    public async Task AcceptContractAsync_ShouldThrow_WhenStatusIsNotProposed()
    {
        // Arrange
        var contract = new TaggingRequestEntity { ContractType = "Gratis", 
            TagOwnerUserId = "UserB",
            Status = TradeStatus.Canceled,
            OwnerId = "UserA",
            RequestedTag = new Tag { Name = "Tag", OwnerId = "UserB" },
            TargetItem = new Item { Content = "Item", OwnerId = "System" }
        };
        _dbContext.TaggingRequestEntities!.Add(contract);
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        var res = await _service.AcceptContractAsync(contract.Id, "UserB");
                Assert.True(res is Failure);
        var ex = res switch { Failure f => f, _ => throw new Exception("Expected Failure") };
        Assert.Equal("実行・承認できない状態の契約です。", ex.ErrorMessage);
    }

    [Fact]
    public async Task CancelContractAsync_ByRequester_ShouldChangeStatusToCanceled()
    {
        // Arrange
        var requesterId = "UserA";
        var tagOwnerId = "UserB";
        var contract = new TaggingRequestEntity { ContractType = "Gratis", 
            RequesterUserId = requesterId,
            TagOwnerUserId = tagOwnerId,
            Status = TradeStatus.Proposed,
            OwnerId = requesterId,
            RequestedTag = new Tag { Name = "Tag", OwnerId = tagOwnerId },
            TargetItem = new Item { Content = "Item", OwnerId = "System" }
        };
        _dbContext.TaggingRequestEntities!.Add(contract);
        await _dbContext.SaveChangesAsync();

        // Act
        var cancelResult = await _service.CancelContractAsync(contract.Id, requesterId);
        
        // Assert
        TaggingRequestEntity? updatedContract = await _dbContext.TaggingRequestEntities.FindAsync(contract.Id);
        Assert.Equal(TradeStatus.Canceled, updatedContract!.Status);
    }

    [Fact]
    public async Task CancelContractAsync_ByTagOwner_ShouldChangeStatusToCanceled()
    {
        // Arrange
        var requesterId = "UserA";
        var tagOwnerId = "UserB";
        var contract = new TaggingRequestEntity { ContractType = "Gratis", 
            RequesterUserId = requesterId,
            TagOwnerUserId = tagOwnerId,
            Status = TradeStatus.Proposed,
            OwnerId = requesterId,
            RequestedTag = new Tag { Name = "Tag", OwnerId = tagOwnerId },
            TargetItem = new Item { Content = "Item", OwnerId = "System" }
        };
        _dbContext.TaggingRequestEntities!.Add(contract);
        await _dbContext.SaveChangesAsync();

        // Act
        var cancelResult = await _service.CancelContractAsync(contract.Id, tagOwnerId);
        
        // Assert
        TaggingRequestEntity? updatedContract = await _dbContext.TaggingRequestEntities.FindAsync(contract.Id);
        Assert.Equal(TradeStatus.Canceled, updatedContract!.Status);
    }

    [Fact]
    public async Task CancelContractAsync_ShouldThrow_WhenUserIsNeitherRequesterNorTagOwner()
    {
        // Arrange
        var contract = new TaggingRequestEntity { ContractType = "Gratis", 
            RequesterUserId = "UserA",
            TagOwnerUserId = "UserB",
            Status = TradeStatus.Proposed,
            OwnerId = "UserA",
            RequestedTag = new Tag { Name = "Tag", OwnerId = "UserB" },
            TargetItem = new Item { Content = "Item", OwnerId = "System" }
        };
        _dbContext.TaggingRequestEntities!.Add(contract);
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        var res = await _service.CancelContractAsync(contract.Id, "UserC");
                Assert.True(res is Failure);
        var ex = res switch { Failure f => f, _ => throw new Exception("Expected Failure") };
        Assert.Equal("この契約をキャンセル・拒否する権限がありません。", ex.ErrorMessage);
    }

    [Fact]
    public async Task CancelContractAsync_ShouldThrow_WhenStatusIsNotProposed()
    {
        // Arrange
        var contract = new TaggingRequestEntity { ContractType = "Gratis", 
            RequesterUserId = "UserA",
            TagOwnerUserId = "UserB",
            Status = TradeStatus.Executed,
            OwnerId = "UserA",
            RequestedTag = new Tag { Name = "Tag", OwnerId = "UserB" },
            TargetItem = new Item { Content = "Item", OwnerId = "System" }
        };
        _dbContext.TaggingRequestEntities!.Add(contract);
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        var res = await _service.CancelContractAsync(contract.Id, "UserA");
                Assert.True(res is Failure);
        var ex = res switch { Failure f => f, _ => throw new Exception("Expected Failure") };
        Assert.Equal("この状態の契約はキャンセルできません。", ex.ErrorMessage);
    }

    // =====================================================================================
    // 4. TaggingRequestEntity のシナリオ (オープンウィッシュリスト ＆ 逆オファー)
    // =====================================================================================

    [Fact]
    public async Task ScenarioG_Bounty_Goodwill_ShouldConsumeFulfillerAsset()
    {
        // Arrange
        // Motivation: UserA requests TagB on their item. No reward is offered.
        // UserC (not the owner, but has a TagB asset) fulfills it out of goodwill.
        var userA = "UserA";
        var userB = "UserB"; // Tag Owner
        var userC = "UserC"; // Fulfiller

        var targetItemA = new Item { Content = "My Item", OwnerId = userA };
        var tagB = new Tag { Name = "Expert", OwnerId = userB, CachedWeight = 100 };

        _dbContext.Items!.Add(targetItemA);
        _dbContext.Tags!.Add(tagB);
        await _dbContext.SaveChangesAsync();

        var fulfillerAsset = new RightAsset { Amount = 1, OwnerId = userC, TargetTagId = tagB.Id };
        _dbContext.RightAssets!.Add(fulfillerAsset);
        await _dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity { ContractType = "Bounty", 
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id, // No reward
            Status = TradeStatus.Proposed,
            OwnerId = userA
        };
        _dbContext.TaggingRequestEntities!.Add(contract);
        await _dbContext.SaveChangesAsync();

        // Act
        // UserC fulfills it by providing their asset
        var acceptResult = await _service.AcceptContractAsync(contract.Id, userC, fulfillerAsset.Id);
        
        // Assert
        // Fulfiller's asset is burned
        var assetIsBurned = await _dbContext.RightAssets.AnyAsync(a => a.Id == fulfillerAsset.Id && a.IsBurned);
        Assert.True(assetIsBurned);

        // Relation is created
        TagRelation? relation =
            await _dbContext.TagRelations!.FirstOrDefaultAsync(tr =>
                tr.ItemId == targetItemA.Id && tr.TagId == tagB.Id);
        Assert.NotNull(relation);
        Assert.Equal(userA, relation.OwnerId);

        // Ledger is updated
        TagWeightLedger? ledger =
            await _dbContext.TagWeightLedgers!.FirstOrDefaultAsync(l => l.SourceId == relation.Id);
        Assert.NotNull(ledger);
        Assert.Equal(userC, ledger.OwnerId);
        Assert.False(ledger.IsOwnerAction); // UserC is not the owner of TagB
        Assert.Equal("Goodwill Bounty Fulfilled", ledger.Reason);
    }

    [Fact]
    public async Task ScenarioH_Bounty_ReverseMutual_ShouldTransferRewardAndConsumeAsset()
    {
        // Arrange
        // Motivation: UserA offers TagC asset as a reward for someone to apply TagB to their item.
        // UserC fulfills it, burns their TagB asset, and receives UserA's TagC asset.
        var userA = "UserA";
        var userB = "UserB";
        var userC = "UserC";

        var targetItemA = new Item { Content = "My Item", OwnerId = userA };
        var tagB = new Tag { Name = "Expert", OwnerId = userB };
        var tagC = new Tag { Name = "RewardTag", OwnerId = userA };

        _dbContext.Items!.Add(targetItemA);
        _dbContext.Tags!.AddRange(tagB, tagC);
        await _dbContext.SaveChangesAsync();

        // User A's reward asset
        var rewardAsset = new RightAsset { Amount = 1, OwnerId = userA, TargetTagId = tagC.Id };

        // User C's fulfiller asset
        var fulfillerAsset = new RightAsset { Amount = 1, OwnerId = userC, TargetTagId = tagB.Id };

        _dbContext.RightAssets!.AddRange(rewardAsset, fulfillerAsset);
        await _dbContext.SaveChangesAsync();

        var contract = new TaggingRequestEntity { ContractType = "Bounty", 
            RequesterUserId = userA,
            TagOwnerUserId = userB,
            TargetItemId = targetItemA.Id,
            RequestedTagId = tagB.Id,
            Payload = new BountyPayload(rewardAsset.Id), // Reward!
            Status = TradeStatus.Proposed,
            OwnerId = userA
        };
        _dbContext.TaggingRequestEntities!.Add(contract);
        await _dbContext.SaveChangesAsync();

        // Act
        var acceptResult = await _service.AcceptContractAsync(contract.Id, userC, fulfillerAsset.Id);
        
        // Assert
        // Fulfiller's asset is burned
        var fulfillerAssetIsBurned =
            await _dbContext.RightAssets.AnyAsync(a => a.Id == fulfillerAsset.Id && a.IsBurned);
        Assert.True(fulfillerAssetIsBurned);

        // Reward asset ownership is transferred to Fulfiller (UserC)
        RightAsset? updatedRewardAsset = await _dbContext.RightAssets.FirstOrDefaultAsync(a => a.Id == rewardAsset.Id);
        Assert.NotNull(updatedRewardAsset);
        Assert.Equal(userC, updatedRewardAsset.OwnerId); // Ownership changed!

        // Relation is created
        TagRelation? relation =
            await _dbContext.TagRelations!.FirstOrDefaultAsync(tr =>
                tr.ItemId == targetItemA.Id && tr.TagId == tagB.Id);
        Assert.NotNull(relation);

        // Ledger is updated
        TagWeightLedger? ledger =
            await _dbContext.TagWeightLedgers!.FirstOrDefaultAsync(l => l.SourceId == relation.Id);
        Assert.NotNull(ledger);
        Assert.Equal("Reward Bounty Fulfilled", ledger.Reason);
    }
}