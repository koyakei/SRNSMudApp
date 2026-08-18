using System.Diagnostics.CodeAnalysis;

namespace SRNSMudApp.Models;

public record class RelativeUrl(string Path);
public record class AbsoluteUrl(Uri Uri);

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public union NotificationTarget(RelativeUrl, AbsoluteUrl)
{
    public readonly string ToHref() => this switch
    {
        RelativeUrl r => r.Path,
        AbsoluteUrl a => a.Uri.ToString(),
        _ => "#",
    };
}
