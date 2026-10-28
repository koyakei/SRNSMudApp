#region

using System.Globalization;

#endregion

namespace SRNSMudApp.Services;

/// <summary>
///     正規化レーベンシュタイン距離に基づく文字列類似度を計算する純粋な静的ユーティリティ。
///     全角↔半角正規化、大文字→小文字正規化を事前適用し、
///     stackalloc ベースの DP でアロケーションを最小限に抑える。
/// </summary>
public static class StringSimilarity
{
    /// <summary>候補検出の最低類似度（これ未満は候補にもならない）</summary>
    public const float MinCandidateThreshold = 0.50f;

    /// <summary>
    ///     2つの文字列の正規化類似度を 0.0f（完全不一致）〜 1.0f（完全一致）で返す。
    ///     空文字列同士は 1.0f、片方のみ空は 0.0f。
    /// </summary>
    public static float Calculate(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        // 正規化バッファを用意（全角→半角変換で長さが変わらない前提）
        Span<char> normalizedA = a.Length <= 256 ? stackalloc char[a.Length] : new char[a.Length];
        Span<char> normalizedB = b.Length <= 256 ? stackalloc char[b.Length] : new char[b.Length];

        int lenA = NormalizeInto(a, normalizedA);
        int lenB = NormalizeInto(b, normalizedB);

        ReadOnlySpan<char> spanA = normalizedA[..lenA];
        ReadOnlySpan<char> spanB = normalizedB[..lenB];

        if (spanA.IsEmpty && spanB.IsEmpty)
        {
            return 1.0f;
        }

        if (spanA.IsEmpty || spanB.IsEmpty)
        {
            return 0.0f;
        }

        int maxLen = Math.Max(spanA.Length, spanB.Length);
        int distance = LevenshteinDistance(spanA, spanB);
        return 1.0f - (float)distance / maxLen;
    }

    /// <summary>
    ///     レーベンシュタイン編集距離を返す（挿入・削除・置換の最小操作数）。
    ///     stackalloc で 1 行分のバッファだけ確保する省メモリ版。
    /// </summary>
    public static int LevenshteinDistance(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        if (a.IsEmpty)
        {
            return b.Length;
        }

        if (b.IsEmpty)
        {
            return a.Length;
        }

        // 短い方を b にスワップして行バッファサイズを最小化（ReadOnlySpan は ref struct のためタプルスワップではなくスカラー変数スワップを使用）
        if (a.Length < b.Length)
        {
            ReadOnlySpan<char> temp = a;
            a = b;
            b = temp;
        }

        int bLen = b.Length;
        Span<int> prev = bLen + 1 <= 512 ? stackalloc int[bLen + 1] : new int[bLen + 1];

        for (int j = 0; j <= bLen; j++)
        {
            prev[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            int prevDiag = prev[0];
            prev[0] = i;

            for (int j = 1; j <= bLen; j++)
            {
                int temp = prev[j];
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                prev[j] = Math.Min(
                    Math.Min(prev[j] + 1, prev[j - 1] + 1),
                    prevDiag + cost);
                prevDiag = temp;
            }
        }

        return prev[bLen];
    }

    /// <summary>
    ///     文字列を正規化（全角→半角英数、大文字→小文字）して出力バッファに書き込む。
    ///     戻り値は書き込んだ文字数。
    /// </summary>
    private static int NormalizeInto(ReadOnlySpan<char> source, Span<char> destination)
    {
        int written = 0;
        foreach (char c in source)
        {
            char normalized = NormalizeChar(c);
            destination[written++] = char.ToLower(normalized, CultureInfo.InvariantCulture);
        }

        return written;
    }

    /// <summary>
    ///     全角英数字・記号を半角に変換する。
    ///     Unicode の FF01-FF5E (全角 ASCII) → 0021-007E (半角 ASCII)。
    /// </summary>
    private static char NormalizeChar(char c) =>
        c is >= '\uFF01' and <= '\uFF5E'
            ? (char)(c - 0xFEE0)
            : c;
}