using System.Text;
using System.Text.RegularExpressions;

namespace LangKinhHuKhong.TTS.Core;

public static class TextProcessor
{
    // No application-defined maximum character count. The editor and runtime are resource-bound.
    private static readonly Regex ClosingQuotes = new(@"[”’""')\]]+$", RegexOptions.Compiled);
    private static readonly Regex WordRegex = new(@"[\p{L}\p{M}\p{N}]+(?:['’\-][\p{L}\p{M}\p{N}]+)*", RegexOptions.Compiled);
    private static readonly Regex InlineNotes = new(@"\[\[[^\]]*\]\]|\[(?:note|ghichu|comment)\s*:[^\]]*\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TrailingPauseTag = new(@"\[(?:pause|nghi|break|silence|im)\s*:\s*\d+(?:[.,]\d+)?\s*(?:ms|s)?\s*\]\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static int CountWords(string? text) => string.IsNullOrEmpty(text) ? 0 : WordRegex.Matches(text).Count;

    /// <summary>
    /// Extracts up to <paramref name="maxWords"/> words from the input text for quick studio audio review,
    /// preserving punctuation and natural spacing.
    /// </summary>
    public static (string Text, int WordCount, bool Truncated) ExtractReviewSnippet(string? input, int maxWords)
    {
        if (string.IsNullOrWhiteSpace(input)) return (string.Empty, 0, false);
        maxWords = Math.Clamp(maxWords, 5, 2000);
        var trimmed = input.Trim();
        var matches = WordRegex.Matches(trimmed);
        if (matches.Count == 0) return (string.Empty, 0, false);
        if (matches.Count <= maxWords)
            return (NormalizeForTts(trimmed), matches.Count, false);

        var lastMatch = matches[maxWords - 1];
        var endIndex = lastMatch.Index + lastMatch.Length;
        while (endIndex < trimmed.Length && !char.IsWhiteSpace(trimmed[endIndex]) &&
               ".!?…;:,。！？；：，、”’\"')]".Contains(trimmed[endIndex]))
        {
            endIndex++;
        }
        var slice = trimmed[..endIndex].Trim();
        return (NormalizeForTts(slice), maxWords, true);
    }

    // Không xóa đoạn, không thay đổi nội dung gốc ngoài dấu câu cuối dòng và lọc ghi chú kịch bản.
    public static string NormalizeForTts(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var lines = input.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = InlineNotes.Replace(lines[i], "").TrimEnd();
            if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                lines[i] = "";
                continue;
            }
            if (string.IsNullOrWhiteSpace(line)) { lines[i] = ""; continue; }
            if (TrailingPauseTag.IsMatch(line))
            {
                lines[i] = line;
                continue;
            }
            var closers = ClosingQuotes.Match(line);
            var index = closers.Success ? closers.Index - 1 : line.Length - 1;
            if (index >= 0 && !".!?…;:,。！？；：，、؟۔،".Contains(line[index]))
                line = line.Insert(index + 1, ".");
            lines[i] = line;
        }
        return string.Join(Environment.NewLine, lines);
    }

    public static string FromImportedFile(string content, bool markdown)
    {
        if (!markdown) return NormalizeForTts(content);
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var output = new List<string>();
        var inFence = false;
        foreach (var original in lines)
        {
            var trimmed = original.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence) continue;
            var line = Regex.Replace(original, @"^\s{0,3}#{1,6}\s+", "");
            line = Regex.Replace(line, @"^\s{0,3}>\s?", "");
            line = Regex.Replace(line, @"^\s*([-*+]\s+|\d+[.)]\s+)", "");
            line = Regex.Replace(line, @"!\[([^\]]*)\]\([^)]*\)", "$1");
            line = Regex.Replace(line, @"\[([^\]]+)\]\([^)]*\)", "$1");
            line = Regex.Replace(line, @"(?<!\*)\*\*([^*]+)\*\*(?!\*)", "$1");
            line = Regex.Replace(line, @"(?<!_)__([^_]+)__(?!_)", "$1");
            line = Regex.Replace(line, @"(?<!\*)\*([^*\r\n]+)\*(?!\*)", "$1");
            line = Regex.Replace(line, @"(?<!_)_([^_\r\n]+)_(?!_)", "$1");
            line = Regex.Replace(line, @"`([^`]+)`", "$1");
            // Markdown thematic rule: không đọc thành chuỗi dấu gạch.
            if (Regex.IsMatch(line, @"^\s*(?:-{3,}|\*{3,}|_{3,})\s*$")) line = "";
            output.Add(line);
        }
        return NormalizeForTts(string.Join("\n", output));
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
