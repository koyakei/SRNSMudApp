using System;
using System.Text.RegularExpressions;

using NUnit.Framework;

namespace SRNSMudApp.E2ETests;

[TestFixture]
public class RegexTest
{
    [Test]
    public void TestRegexMatch()
    {
        var regex = new Regex(@"\/(?:ItemDetail|TagDetail)\/\d+", RegexOptions.IgnoreCase);
        var match = regex.Match("http://localhost:5009/ItemDetail/12007");
        Console.WriteLine($"Match: '{match.Value}'");
        Assert.That(match.Value.StartsWith("/ItemDetail/", StringComparison.OrdinalIgnoreCase), Is.True);
    }
}