using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

using SRNSMudApp.Components.UI;
using SRNSMudApp.Data;

namespace SRNSMudApp.E2ETests;

[TestFixture]
public class InspectItem12006Test
{
    private CustomWebApplicationFactory _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = SharedTestServerFixture.Factory;
    }

    [Test]
    public async Task RunInspection()
    {
        var dbFactory = _factory.AppServices.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await db.Items.Include(i => i.TagRelations).FirstOrDefaultAsync(i => i.Id == 12006);
        if (item == null)
        {
            Console.WriteLine("Item 12006 not found!");
            return;
        }

        Console.WriteLine($"Item 12006 Content: {item.Content}");
        Console.WriteLine($"Item 12006 QuotedItemId: {item.QuotedItemId}");
        Console.WriteLine($"Item 12006 Tags count: {item.TagRelations?.Count ?? 0}");

        var matches = ItemCardViewModel.InternalLinkRegex().Matches(item.Content ?? "");
        Console.WriteLine($"Regex matches count: {matches.Count}");
        foreach (Match match in matches)
        {
            Console.WriteLine($"Match: {match.Value}");
        }

        var urlPattern = new Regex(@"https?://[^/\s]+(/ItemDetail/\d+)", RegexOptions.IgnoreCase);
        var urlMatches = urlPattern.Matches(item.Content ?? "");
        Console.WriteLine($"URL pattern matches count: {urlMatches.Count}");
        foreach (Match match in urlMatches)
        {
            Console.WriteLine($"URL Match: {match.Groups[1].Value}");
        }
    }
}