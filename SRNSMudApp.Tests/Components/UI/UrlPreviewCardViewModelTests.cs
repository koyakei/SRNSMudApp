using SRNSMudApp.Components.UI;

namespace SRNSMudApp.Tests.Components.UI;

/// <summary>
///     UrlPreviewCardViewModel の単体テスト。
///     URL の # :~:text= フラグメント解析を bUnit なしで検証する。
/// </summary>
public class UrlPreviewCardViewModelTests
{
    [Fact]
    public void ExtractTextFragment_WithoutFragment_ReturnsNull()
    {
        Assert.Null(UrlPreviewCardViewModel.ExtractTextFragment("https://example.com/page"));
    }

    [Fact]
    public void ExtractTextFragment_WithSingleText_ReturnsDecodedText()
    {
        var url = "https://example.com/page#:~:text=Hello%20World";

        Assert.Equal("Hello World", UrlPreviewCardViewModel.ExtractTextFragment(url));
    }

    [Fact]
    public void ExtractTextFragment_WithPrefixAndSuffixSeparators_StripsThem()
    {
        // "-,prefix,text,-suffix" 形式: 接頭・接尾の文脈指定を除去して本文のみ残す
        var url = "https://example.com/page#:~:text=-,core%20text,-";

        Assert.Equal("core text", UrlPreviewCardViewModel.ExtractTextFragment(url));
    }

    [Fact]
    public void ExtractTextFragment_WithMultipleTextParts_JoinsWithPipe()
    {
        var url = "https://example.com/page#:~:text=first&text=second";

        Assert.Equal("first | second", UrlPreviewCardViewModel.ExtractTextFragment(url));
    }

    [Fact]
    public void ExtractTextFragment_WithCommaSegments_JoinsWithWaveDash()
    {
        var url = "https://example.com/page#:~:text=start,%20end";

        Assert.Equal("start〜end", UrlPreviewCardViewModel.ExtractTextFragment(url));
    }

    [Fact]
    public void ExtractTextFragment_WithInvalidEncoding_FallsBackToRawValue()
    {
        var url = "https://example.com/page#:~:text=%ZZinvalid";

        Assert.Equal("%ZZinvalid", UrlPreviewCardViewModel.ExtractTextFragment(url));
    }

    [Fact]
    public void ExtractTextFragment_WithBlankSegments_ReturnsNull()
    {
        var url = "https://example.com/page#:~:text=%20%20,";

        Assert.Null(UrlPreviewCardViewModel.ExtractTextFragment(url));
    }

    [Theory]
    [InlineData("/ItemDetail/123", "https://localhost:5001/", "/ItemDetail/123")]
    [InlineData("  /ItemDetail/123  ", "https://localhost:5001/", "/ItemDetail/123")]
    [InlineData("/ItemDetail/123?tab=tags#section", "https://localhost:5001/", "/ItemDetail/123?tab=tags#section")]
    [InlineData("https://localhost:5001/ItemDetail/123", "https://localhost:5001/", "/ItemDetail/123")]
    [InlineData("https://localhost:5001/ItemDetail/123?tab=tags", "https://localhost:5001/", "/ItemDetail/123?tab=tags")]
    public void TryGetInternalRoute_WhenInternalUrl_ReturnsTrueAndRoute(string url, string baseUri, string expectedRoute)
    {
        var result = UrlPreviewCardViewModel.TryGetInternalRoute(url, baseUri, out var actualRoute);

        Assert.True(result);
        Assert.Equal(expectedRoute, actualRoute);
    }

    [Theory]
    [InlineData(null, "https://localhost:5001/")]
    [InlineData("", "https://localhost:5001/")]
    [InlineData("   ", "https://localhost:5001/")]
    [InlineData("//example.com/ItemDetail/123", "https://localhost:5001/")]
    [InlineData("https://external.com/ItemDetail/123", "https://localhost:5001/")]
    [InlineData("https://localhost:5002/ItemDetail/123", "https://localhost:5001/")]
    [InlineData("http://localhost:5001/ItemDetail/123", "https://localhost:5001/")]
    public void TryGetInternalRoute_WhenExternalOrInvalidUrl_ReturnsFalse(string? url, string? baseUri)
    {
        var result = UrlPreviewCardViewModel.TryGetInternalRoute(url, baseUri, out var actualRoute);

        Assert.False(result);
        Assert.Null(actualRoute);
    }
}