using SRNSMudApp.Components.UI;

using Xunit;

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
}