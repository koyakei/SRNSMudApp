using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SRNSMudApp.Data;
using Microsoft.Extensions.DependencyInjection;

namespace SRNSMudApp.E2ETests;

[TestFixture]
public class ManualTest
{
    [Test]
    public async Task InsertTagAndRelation_ThrowsWhat()
    {
        using var factory = new CustomWebApplicationFactory();
        factory.EnsureServer();
        using var scope = factory.Services.CreateScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var context = await dbContextFactory.CreateDbContextAsync();
        
        var ownerId = "testuser_manual";
        context.Users.Add(new ApplicationUser { Id = ownerId, UserName = ownerId, Email = ownerId + "@example.com" });
        await context.SaveChangesAsync();
        
        var newTag = new Tag { Name = "ManualTestTag", Content = "Test", OwnerId = ownerId, CachedWeight = 0 };
        context.Tags.Add(newTag);
        await context.SaveChangesAsync();
        
        var item = new Item { Content = "Manual Test Item", OwnerId = ownerId };
        context.Items.Add(item);
        await context.SaveChangesAsync();

        var newRelation = new TagRelation { ItemId = item.Id, TagId = newTag.Id, Weight = 1, OwnerId = ownerId };
        context.Set<TagRelation>().Add(newRelation);
        
        context.TimelineEvents.Add(new TimelineEvent
        {
            OwnerId = ownerId,
            Target = new SRNSMudApp.Models.Unions.ItemTarget(item.Id),
            FollowedTagId = newTag.Id,
            EventType = "Insert",
            NewWeight = 1
        });
        
        try
        {
            await context.SaveChangesAsync();
            Console.WriteLine("Success!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex}");
            throw;
        }
    }
}
