using System.Text;
using System.Text.RegularExpressions;

namespace LangKinhHuKhong.TTS.Core;

public static class TextProcessor
{
    // No application-defined maximum character count. The editor and runtime are resource-bound.
    private static readonly Regex WordRegex = new(@"[\p{L}\p{M}\p{N}]+(?:['’\-][\p{L}\p{M}\p{N}]+)*", RegexOptions.Compiled);

    /// <summary>Zero-allocation O(N) word counter capable of scanning 10M+ characters in milliseconds.</summary>
    public static int CountWords(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int count = 0;
        bool inWord = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (char.IsLetterOrDigit(ch) || System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                if (!inWord)
                {
                    count++;
                    inWord = true;
                }
            }
            else if (ch is '\'' or '’' or '-' && inWord && i + 1 < text.Length && char.IsLetterOrDigit(text[i + 1]))
            {
                // Keep compound/hyphenated token as one word
            }
            else
            {
                inWord = false;
            }
        }
        return count;
    }

    /// <summary>
    /// Extracts up to <paramref name="maxWords"/> words from the input text for quick studio audio review,
    /// stopping lazily at maxWords + 1 so 1M-10M character manuscripts extract in &lt;1ms.
    /// </summary>
    public static (string Text, int WordCount, bool Truncated) ExtractReviewSnippet(string? input, int maxWords)
    {
        if (string.IsNullOrWhiteSpace(input)) return (string.Empty, 0, false);
        maxWords = Math.Clamp(maxWords, 5, 2000);
        var trimmed = input.Trim();
        var match = WordRegex.Match(trimmed);
        if (!match.Success) return (string.Empty, 0, false);

        int count = 0;
        Match lastKept = match;
        bool hasMore = false;
        while (match.Success)
        {
            count++;
            if (count <= maxWords)
            {
                lastKept = match;
            }
            else
            {
                hasMore = true;
                break;
            }
            match = match.NextMatch();
        }

        if (!hasMore)
            return (trimmed, count, false);

        var endIndex = lastKept.Index + lastKept.Length;
        while (endIndex < trimmed.Length && !char.IsWhiteSpace(trimmed[endIndex]) &&
               ".!?…;:,。！？；：，、”’\"')]".Contains(trimmed[endIndex]))
        {
            endIndex++;
        }
        var slice = trimmed[..endIndex].Trim();
        return (slice, maxWords, true);
    }

    public static List<string> Split(string text, int limit = 1500)
    {
        var sentences = Regex.Split(text.Trim(), @"(?<=[.!?…])\s+|\r?\n+")
            .Where(x => !string.IsNullOrWhiteSpace(x));
        var chunks = new List<string>();
        var current = new StringBuilder();
        foreach (var sentence in sentences)
        {
            if (sentence.Length > limit)
            {
                if (current.Length != 0) { chunks.Add(current.ToString()); current.Clear(); }
                // Ưu tiên cắt ở khoảng trắng để không chia đôi từ tiếng Việt.
                var remain = sentence.AsSpan();
                while (!remain.IsEmpty)
                {
                    if (remain.Length <= limit) { chunks.Add(remain.ToString().Trim()); break; }
                    var end = remain[..limit].LastIndexOf(' ');
                    if (end < limit / 2) end = limit;
                    chunks.Add(remain[..end].ToString().Trim());
                    remain = remain[end..].TrimStart();
                }
                continue;
            }
            if (current.Length + sentence.Length + 1 > limit)
            {
                if (current.Length != 0) chunks.Add(current.ToString());
                current.Clear();
            }
            if (current.Length != 0) current.Append(' ');
            current.Append(sentence);
        }
        if (current.Length != 0) chunks.Add(current.ToString());
        return chunks;
    }
}
