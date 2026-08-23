using System.Diagnostics.CodeAnalysis;

namespace SRNSMudApp.Models;

public record TagNameSearch(string TagName);
public record TagWithUserSearch(string TagName, string UserName);
public record IncompleteSearch(string TagName);
public record EmptySearch;

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public union TagSearchQuery(TagNameSearch, TagWithUserSearch, IncompleteSearch, EmptySearch)
{
    public static TagSearchQuery Parse(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new EmptySearch();
        }

        var text = query.Trim();
        if (text.EndsWith(" @", StringComparison.Ordinal))
        {
            var parts = text.Split(" @", 2, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? new IncompleteSearch(parts[0]) : new EmptySearch();
        }

        var splitParts = text.Split(" @", 2, StringSplitOptions.RemoveEmptyEntries);
        if (splitParts.Length == 0) return new EmptySearch();

        var tagName = splitParts[0];
        if (splitParts.Length > 1)
        {
            return new TagWithUserSearch(tagName, splitParts[1]);
        }

        return new TagNameSearch(tagName);
    }
}