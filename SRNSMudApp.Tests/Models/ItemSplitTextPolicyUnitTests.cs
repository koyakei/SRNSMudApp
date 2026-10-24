#region

using SRNSMudApp.Models;
using SRNSMudApp.Models.Unions;
using SRNSMudApp.Resources;

#endregion

namespace SRNSMudApp.Tests.Models;

/// <summary>
///     <see cref="ItemSplitTextPolicy" /> の純粋ドメイン単体テスト。
///     データベースやインフラを一切介さず、文字列検索・改行コード正規化・リンク交差判定・文字数制限ロジックを高速に検証する。
/// </summary>
public sealed class ItemSplitTextPolicyUnitTests
{
    [Fact]
    public void NormalizeLineEndings_NormalizesLfAndCrlf()
    {
        // Arrange
        const string text = "Line1\r\nLine2\nLine3";

        // Act
        var (lf, crlf) = ItemSplitTextPolicy.NormalizeLineEndings(text);

        // Assert
        Assert.Equal("Line1\nLine2\nLine3", lf);
        Assert.Equal("Line1\r\nLine2\r\nLine3", crlf);
    }

    [Fact]
    public void FindSelectedText_WhenEmpty_ReturnsMinusOne()
    {
        Assert.Equal((-1, ""), ItemSplitTextPolicy.FindSelectedText("", ""));
        Assert.Equal((-1, "test"), ItemSplitTextPolicy.FindSelectedText("", "test"));
        Assert.Equal((-1, ""), ItemSplitTextPolicy.FindSelectedText("content", ""));
    }

    [Fact]
    public void FindSelectedText_ExactMatch_ReturnsCorrectIndex()
    {
        // Arrange
        const string content = "Hello world from Blazor";
        const string selected = "world";

        // Act
        var (index, textToReplace) = ItemSplitTextPolicy.FindSelectedText(content, selected);

        // Assert
        Assert.Equal(6, index);
        Assert.Equal("world", textToReplace);
    }

    [Fact]
    public void FindSelectedText_WhenContentIsCrlfAndSelectionIsLf_MatchesSuccessfully()
    {
        // Arrange
        const string content = "First line\r\nSecond line\r\nThird line";
        const string selected = "Second line\nThird line";

        // Act
        var (index, textToReplace) = ItemSplitTextPolicy.FindSelectedText(content, selected);

        // Assert
        Assert.True(index >= 0);
        Assert.Equal("Second line\r\nThird line", textToReplace);
    }

    [Fact]
    public void FindSelectedText_WhenContentIsLfAndSelectionIsCrlf_MatchesSuccessfully()
    {
        // Arrange
        const string content = "First line\nSecond line\nThird line";
        const string selected = "Second line\r\nThird line";

        // Act
        var (index, textToReplace) = ItemSplitTextPolicy.FindSelectedText(content, selected);

        // Assert
        Assert.True(index >= 0);
        Assert.Equal("Second line\nThird line", textToReplace);
    }

    [Fact]
    public void FindSelectedText_WhenMultipleOccurrences_FindsFirstOccurrence()
    {
        // Arrange
        const string content = "apple banana apple cherry";
        const string selected = "apple";

        // Act
        var (index, textToReplace) = ItemSplitTextPolicy.FindSelectedText(content, selected);

        // Assert
        Assert.Equal(0, index);
        Assert.Equal("apple", textToReplace);
    }

    [Fact]
    public void FindSelectedText_WithJapaneseAndEmoji_MatchesSuccessfully()
    {
        // Arrange
        const string content = "こんにちは🐱世界🎉テスト";
        const string selected = "世界🎉";

        // Act
        var (index, textToReplace) = ItemSplitTextPolicy.FindSelectedText(content, selected);

        // Assert
        Assert.True(index >= 0);
        Assert.Equal("世界🎉", textToReplace);
    }

    [Fact]
    public void FindSelectedText_WhenNotPresent_ReturnsMinusOne()
    {
        // Arrange
        const string content = "Hello world";
        const string selected = "not found";

        // Act
        var (index, textToReplace) = ItemSplitTextPolicy.FindSelectedText(content, selected);

        // Assert
        Assert.Equal(-1, index);
        Assert.Equal("not found", textToReplace);
    }

    [Fact]
    public void IntersectsExistingLink_WhenNoIntersection_ReturnsFalse()
    {
        // Arrange
        const string content = "Check out https://example.com for more info";
        int targetIndex = 0;
        int targetLength = 9; // "Check out"

        // Act & Assert
        Assert.False(ItemSplitTextPolicy.IntersectsExistingLink(content, targetIndex, targetLength));
    }

    [Fact]
    public void IntersectsExistingLink_WhenSelectionIntersectsUrlStart_ReturnsTrue()
    {
        // Arrange
        const string content = "Check https://example.com end";
        int urlStart = content.IndexOf("https://", StringComparison.Ordinal);
        int targetIndex = urlStart - 2;
        int targetLength = 5; // crosses into the URL

        // Act & Assert
        Assert.True(ItemSplitTextPolicy.IntersectsExistingLink(content, targetIndex, targetLength));
    }

    [Fact]
    public void IntersectsExistingLink_WhenSelectionIntersectsUrlEnd_ReturnsTrue()
    {
        // Arrange
        const string content = "Check https://example.com/path end";
        int urlEnd = content.IndexOf(" end", StringComparison.Ordinal);
        int targetIndex = urlEnd - 4; // inside path
        int targetLength = 6; // crosses outside path into " e"

        // Act & Assert
        Assert.True(ItemSplitTextPolicy.IntersectsExistingLink(content, targetIndex, targetLength));
    }

    [Fact]
    public void IntersectsExistingLink_WhenSelectionEnclosesEntireUrl_ReturnsFalse()
    {
        // Arrange
        const string content = "Prefix https://example.com Suffix";
        int targetIndex = 0;
        int targetLength = content.Length;

        // Act & Assert (リンクを完全に内包して置換する場合は分断ではない)
        Assert.False(ItemSplitTextPolicy.IntersectsExistingLink(content, targetIndex, targetLength));
    }

    [Fact]
    public void IntersectsExistingLink_WhenSelectionIntersectsInternalLink_ReturnsTrue()
    {
        // Arrange
        const string content = "Check /ItemDetail/42 and more";
        int linkStart = content.IndexOf("/ItemDetail", StringComparison.Ordinal);
        int targetIndex = linkStart - 2;
        int targetLength = 6; // crosses into "/Item"

        // Act & Assert
        Assert.True(ItemSplitTextPolicy.IntersectsExistingLink(content, targetIndex, targetLength));
    }

    [Fact]
    public void ValidateAndLocate_WhenTextNotInContent_ReturnsFailure()
    {
        // Act
        Result<ItemSplitTextMatch> result = ItemSplitTextPolicy.ValidateAndLocate("Hello world", "missing");

        // Assert
        Assert.True(result is Failure f && f.ErrorMessage == ErrorMessages.SplitSelectedTextNotInContent);
    }

    [Fact]
    public void ValidateAndLocate_WhenIntersectsLink_ReturnsFailure()
    {
        // Arrange
        const string content = "See https://example.com now";
        const string selected = "ee https://ex";

        // Act
        Result<ItemSplitTextMatch> result = ItemSplitTextPolicy.ValidateAndLocate(content, selected);

        // Assert
        Assert.True(result is Failure f && f.ErrorMessage == ErrorMessages.SplitIntersectsExistingLink);
    }

    [Fact]
    public void ValidateAndLocate_WhenResultExceedsMaxLength_ReturnsFailure()
    {
        // Arrange: 990文字の本文から 1文字だけ選択して置換すると、990 - 1 + 22 = 1011 > 1000文字
        string content = new('a', 990);
        const string selected = "a";

        // Act
        Result<ItemSplitTextMatch> result = ItemSplitTextPolicy.ValidateAndLocate(content, selected);

        // Assert
        Assert.True(result is Failure f && f.ErrorMessage == ErrorMessages.SplitResultExceedsMaxLength);
    }

    [Fact]
    public void ValidateAndLocate_WhenValid_ReturnsSuccessWithMatch()
    {
        // Arrange
        const string content = "This is a great paragraph for splitting.";
        const string selected = "great paragraph";

        // Act
        Result<ItemSplitTextMatch> result = ItemSplitTextPolicy.ValidateAndLocate(content, selected);

        // Assert
        Assert.True(result is Success<ItemSplitTextMatch> s
                    && s.Value.Index == 10
                    && s.Value.TextToReplace == "great paragraph");
    }

    [Fact]
    public void ValidateAndLocateForApprove_WhenContentChanged_ReturnsContentChangedFailure()
    {
        // Arrange
        const string content = "Edited content";
        const string selected = "Old paragraph";

        // Act
        Result<ItemSplitTextMatch> result = ItemSplitTextPolicy.ValidateAndLocateForApprove(content, selected);

        // Assert
        Assert.True(result is Failure f && f.ErrorMessage == ErrorMessages.SplitOriginalItemContentChanged);
    }
}