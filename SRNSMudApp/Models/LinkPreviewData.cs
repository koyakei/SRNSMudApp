#region

using System.Diagnostics.CodeAnalysis;

#endregion

namespace SRNSMudApp.Models;

public class LinkPreviewData
{
    [SuppressMessage("Design", "CA1056")] public string Url { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    [SuppressMessage("Design", "CA1056")] public string ImageUrl { get; set; } = string.Empty;

    public string SiteName { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
}