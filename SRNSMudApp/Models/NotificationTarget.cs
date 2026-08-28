using System.Diagnostics.CodeAnalysis;

namespace SRNSMudApp.Models;

public record RelativeUrl(string Path);
public record AbsoluteUrl(Uri Uri);

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Union type handled by C# compiler")]
public readonly union NotificationTarget(RelativeUrl, AbsoluteUrl)
{
    public readonly string ToHref() => this switch
    {
        RelativeUrl r => r.Path,
        AbsoluteUrl a => a.Uri.ToString(),
        _ => "#",
    };
}