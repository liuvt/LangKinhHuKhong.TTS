using System.Text.RegularExpressions;

namespace LangKinhHuKhong.TTS.Services;

public sealed class TextSplitterService
{
    private static readonly Regex WordRegex = new(
        @"[\p{L}\p{M}\p{N}]+(?:['’\-][\p{L}\p{M}\p{N}]+)*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TrailingClosers = new(@"[”’""')\]]+$", RegexOptions.Compiled);

    public int WordCount(string? text)
        => string.IsNullOrWhiteSpace(text) ? 0 : WordRegex.Matches(text).Count;

    public int CharacterCount(string? text) => text?.Length ?? 0;

    /// <summary>Thêm dấu chấm trước xuống dòng nếu dòng chưa có dấu kết câu.
    /// Không gộp/đánh mất dòng trống và không thêm trùng dấu kết câu.</summary>
    public string NormalizeForTts(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var lines = input.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimEnd();
            if (string.IsNullOrWhiteSpace(trimmed)) { lines[i] = string.Empty; continue; }
            var closers = TrailingClosers.Match(trimmed);
            var punctuationPosition = closers.Success ? closers.Index - 1 : trimmed.Length - 1;
            if (punctuationPosition >= 0 && !".!?…;:".Contains(trimmed[punctuationPosition]))
                trimmed = trimmed.Insert(punctuationPosition + 1, ".");
            lines[i] = trimmed;
        }
        return string.Join("\n", lines);
    }

    public List<string> Split(string text, int maxChars = 1500)
    {
        var sentences = Regex.Split(text.Trim(), @"(?<=[.!?…])\s+|\r?\n+")
            .Where(x => !string.IsNullOrWhiteSpace(x));
        var chunks = new List<string>();
        var cur = "";
        foreach (var s in sentences)
        {
            if (s.Length > maxChars)
            {
                if (cur.Length > 0) { chunks.Add(cur.Trim()); cur = ""; }
                for (int i = 0; i < s.Length; i += maxChars)
                    chunks.Add(s.Substring(i, Math.Min(maxChars, s.Length - i)));
                continue;
            }
            if (cur.Length + s.Length + 1 > maxChars)
            {
                if (cur.Length > 0) chunks.Add(cur.Trim());
                cur = s;
            }
            else cur += (cur.Length == 0 ? "" : " ") + s;
        }
        if (cur.Length > 0) chunks.Add(cur.Trim());
        return chunks;
    }
}
