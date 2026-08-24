#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using Bunit;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using MudBlazor.Services;

using SRNSMudApp.Components.Tag;
using SRNSMudApp.Data;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Services;
using SRNSMudApp.Tests.TestSupport;

using Xunit;

#endregion

namespace SRNSMudApp.Tests.Components.Tag;

[Collection(MsSqlCollection.Name)]
public class TaggingRequestApprovalTests : IAsyncLifetime
{
    private const string ItemOwnerId = "item-owner";
    private const string TagOwnerId = "tag-owner";

    private readonly MsSqlContainerFixture _fixture;
    private MsSqlTestDatabase _testDb = null!;
    private BunitContext _ctx = null!;
    private int _onRequestChangedCount;
    private string _currentUserId = TagOwnerId;

    public TaggingRequestApprovalTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _testDb = await MsSqlTestDatabase.CreateAsync(_fixture.ConnectionString, nameof(TaggingRequestApprovalTests));

        _ctx = new BunitContext();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ = _ctx.Services.AddMudServices().AddSrnsComponentServices();
        _ctx.Services.AddAuthorizationCore();

        var authState = CreateAuthState(TagOwnerId);
        var authMock = new Mock<AuthenticationStateProvider>();
        _ = authMock.Setup(p => p.GetAuthenticationStateAsync()).ReturnsAsync(authState);
        _ctx.Services.AddScoped(_ => authMock.Object);

        _ = _ctx.Services.AddMsSqlDbFactory(_testDb.ConnectionString);

        _ctx.Services.AddScoped<TaggingContractService>();
        _ctx.Services.AddScoped<ITaggingService, TaggingService>();
    }

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
        await _testDb.DisposeAsync();
    }

    [Fact]
    public async Task ApprovingRemoveRequest_ShouldRemoveRelationAndSetExecuted()
    {
        TaggingRequestEntity contract = await SeedRemoveRequestAsync();

        IRenderedComponent<TaggingRequestList> cut = RenderList(contract);

        cut.Find("[data-testid='tagging-request-approve']").Click();

        cut.WaitForAssertion(() =>
        {
            using ApplicationDbContext db = CreateDbContext();
            Assert.Equal(TradeStatus.Executed, db.TaggingRequestEntities.Find(contract.Id)!.Status);
            Assert.False(db.TagRelations.Any(tr =>
                tr.ItemId == contract.TargetItemId && tr.TagId == contract.RequestedTagId));
        });
        Assert.Equal(1, _onRequestChangedCount);
    }

    [Fact]
    public async Task ApproveButton_ShouldBeHidden_WhenCurrentUserIsUnrelated()
    {
        TaggingRequestEntity contract = await SeedRemoveRequestAsync();
        _currentUserId = "unrelated-user";

        IRenderedComponent<TaggingRequestList> cut = RenderList(contract);

        Assert.Empty(cut.FindAll("[data-testid='tagging-request-approve']"));
        Assert.Empty(cut.FindAll("[data-testid='tagging-request-reject']"));
    }

    [Fact]
    public async Task ApprovingAddRequest_ShouldCreateRelationAndSetExecuted()
    {
        TaggingRequestEntity contract = await SeedAddRequestAsync();

        IRenderedComponent<TaggingRequestList> cut = RenderList(contract);

        cut.Find("[data-testid='tagging-request-approve']").Click();

        cut.WaitForAssertion(() =>
        {
            using ApplicationDbContext db = CreateDbContext();
            Assert.Equal(TradeStatus.Executed, db.TaggingRequestEntities.Find(contract.Id)!.Status);
            Assert.NotNull(db.TagRelations.FirstOrDefault(tr =>
                tr.ItemId == contract.TargetItemId && tr.TagId == contract.RequestedTagId));
        });
        Assert.Equal(1, _onRequestChangedCount);
    }

    private IRenderedComponent<TaggingRequestList> RenderList(params TaggingRequestEntity[] requests)
    {
        return _ctx.Render<TaggingRequestList>(parameters => parameters
            .Add(p => p.Requests, requests.ToList())
            .Add(p => p.OnRequestChanged, () => _onRequestChangedCount++)
            .AddCascadingValue(Task.FromResult(CreateAuthState(_currentUserId))));
    }

    private async Task<TaggingRequestEntity> SeedRemoveRequestAsync()
    {
        await using ApplicationDbContext db = await CreateDbContextAsync();
        db.Users.AddRange(
            new ApplicationUser { Id = ItemOwnerId, UserName = ItemOwnerId },
            new ApplicationUser { Id = TagOwnerId, UserName = TagOwnerId });
        SRNSMudApp.Data.Item item = new() { Content = "TargetItem", OwnerId = ItemOwnerId };
        SRNSMudApp.Data.Tag tag = new() { Name = "RemovableTag", OwnerId = TagOwnerId, CachedWeight = 10 };
        db.Items.Add(item);
        db.Tags.Add(tag);
        _ = await db.SaveChangesAsync();

        db.TagRelations.Add(new TagRelation { ItemId = item.Id, TagId = tag.Id, OwnerId = TagOwnerId, Weight = 3 });
        TaggingRequestEntity contract = new()
        {
            ContractType = "Gratis",
            OwnerId = ItemOwnerId,
            RequesterUserId = ItemOwnerId,
            TagOwnerUserId = TagOwnerId,
            TargetItemId = item.Id,
            RequestedTagId = tag.Id,
            Status = TradeStatus.Proposed,
            Payload = new GratisPayload("Please remove this tag"),
            RequestType = TaggingRequestType.Remove
        };
        db.TaggingRequestEntities.Add(contract);
        _ = await db.SaveChangesAsync();
        return contract;
    }

    private async Task<TaggingRequestEntity> SeedAddRequestAsync()
    {
        await using ApplicationDbContext db = await CreateDbContextAsync();
        db.Users.AddRange(
            new ApplicationUser { Id = ItemOwnerId, UserName = ItemOwnerId },
            new ApplicationUser { Id = TagOwnerId, UserName = TagOwnerId });
        SRNSMudApp.Data.Item item = new() { Content = "TargetItem", OwnerId = ItemOwnerId };
        SRNSMudApp.Data.Tag tag = new() { Name = "AddableTag", OwnerId = TagOwnerId, CachedWeight = 10 };
        db.Items.Add(item);
        db.Tags.Add(tag);
        _ = await db.SaveChangesAsync();

        TaggingRequestEntity contract = new()
        {
            ContractType = "Gratis",
            OwnerId = ItemOwnerId,
            RequesterUserId = ItemOwnerId,
            TagOwnerUserId = TagOwnerId,
            TargetItemId = item.Id,
            RequestedTagId = tag.Id,
            Status = TradeStatus.Proposed,
            Payload = new GratisPayload("Please add this tag"),
            RequestType = TaggingRequestType.Add
        };
        db.TaggingRequestEntities.Add(contract);
        _ = await db.SaveChangesAsync();
        return contract;
    }

    private ApplicationDbContext CreateDbContext() => _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();

    private Task<ApplicationDbContext> CreateDbContextAsync() => _ctx.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync();

    private static AuthenticationState CreateAuthState(string userId)
    {
        Claim[] claims = [new(ClaimTypes.NameIdentifier, userId), new(ClaimTypes.Name, userId)];
        ClaimsIdentity identity = new(claims, "TestAuthType");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }
}