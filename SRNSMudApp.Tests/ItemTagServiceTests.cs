using Microsoft.EntityFrameworkCore;
using Moq;
using SRNSMudApp.Data;
using SRNSMudApp.Services;

namespace SRNSMudApp.Tests.Services;

public class ItemTagServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ItemTagService _service;

    public ItemTagServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
            
        _dbContext = new ApplicationDbContext(options);
        
        var mockDbFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockDbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApplicationDbContext(options));

        _service = new ItemTagService(mockDbFactory.Object);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AddReplyToRequestAsync_ShouldCreateItemAndReturnItWithRelations()
    {
        // Arrange
        var userId = "TestUser";
        var user = new ApplicationUser { Id = userId, UserName = "TestUser" };
        _dbContext.Users.Add(user);
        
        var request = new GratisTaggingContract
        {
            OwnerId = userId,
            TargetItemId = 1,
            RequestedTagId = 1,
            RequesterUserId = userId,
            TagOwnerUserId = userId
        };
        _dbContext.TaggingRequestEntities.Add(request);
        await _dbContext.SaveChangesAsync();

        var message = "This is a test reply";

        // Act
        var replyItem = await _service.AddReplyToRequestAsync(request.Id, userId, message);

        // Assert
        Assert.NotNull(replyItem);
        Assert.Equal(request.Id, replyItem.TaggingRequestEntityId);
        Assert.Equal(userId, replyItem.OwnerId);
        Assert.Equal(message, replyItem.Content);
        Assert.NotNull(replyItem.Owner);
        Assert.Equal(userId, replyItem.Owner.Id);
        
        // Ensure it's saved in the DB
        var savedItem = await _dbContext.Items.FirstOrDefaultAsync(i => i.Id == replyItem.Id);
        Assert.NotNull(savedItem);
        Assert.Equal(request.Id, savedItem.TaggingRequestEntityId);
        Assert.Equal(message, savedItem.Content);
    }
}
