using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LangKinhHuKhong.TTS.Core;

/// <summary>
/// Safe natural-sentence chunking for offline voices.
/// Commas and colons STAY IN a full sentence to avoid model losing Vietnamese syllable context.
/// WAV silence is inserted only after completed sentences, line breaks, or explicit [pause:...] / [nghi:...] text commands.
/// Long sentences get a limited soft split (never every comma).
/// </summary>
public static class SpeechPacing
{
    public const int InitialPauseMs = 160;
    public const int CommaPauseMs = 160;   // legacy constant, commas are now read inside sentences
    public const int ClausePauseMs = 220;
    public const int PeriodPauseMs = 310;
    public const int ExclamationPauseMs = 350;
    public const int QuestionPauseMs = 330;
    public const int EllipsisPauseMs = 420;
    public const int LinePauseMs = 350;
    public const int ParagraphPauseMs = 550;
    public const int SoftPauseMs = 100;
    public const int MaxPhraseLength = 240;

    private static readonly Regex InlinePauseCommand = new(
        @"^\[(?:pause|nghi|break|silence|im)\s*:\s*(?<val>\d+(?:[.,]\d+)?)\s*(?<unit>ms|s)?\s*\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex InlineNoteCommand = new(
        @"^(?:\[\[[^\]]*\]\]|\[(?:note|ghichu|comment)\s*:[^\]]*\])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public sealed record Segment(string Text, int PauseAfterMs);

    public static List<Segment> SegmentText(string? source)
    {
        var result = new List<Segment>();
        if (string.IsNullOrWhiteSpace(source)) return result;
        var input = source.Replace("\r\n", "\n").Replace('\r', '\n');
        var segment = new StringBuilder();
        var consecutiveBreaks = 0;

        void Flush(int pause)
        {
            var text = segment.ToString().Trim();
            segment.Clear();
            if (text.Length > 0) result.Add(new Segment(text, pause));
            else if (pause > 0 && result.Count > 0 && result[^1].PauseAfterMs < pause)
                result[^1] = result[^1] with { PauseAfterMs = pause };
        }

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '\n')
            {
                consecutiveBreaks++;
                Flush(consecutiveBreaks > 1 ? ParagraphPauseMs : LinePauseMs);
                continue;
            }

            // Handle inline studio text commands: [pause:500ms], [nghi:1.5s], [break:1000ms], [[ghi chú]]
            if (c == '[')
            {
                var remainingSpan = input[i..];
                var noteMatch = InlineNoteCommand.Match(remainingSpan);
                if (noteMatch.Success)
                {
                    i += noteMatch.Length - 1;
                    continue;
                }
                var pauseMatch = InlinePauseCommand.Match(remainingSpan);
                if (pauseMatch.Success)
                {
                    var rawVal = pauseMatch.Groups["val"].Value.Replace(',', '.');
                    var unit = pauseMatch.Groups["unit"].Value.ToLowerInvariant();
                    if (double.TryParse(rawVal, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    {
                        var ms = unit == "s" ? (int)Math.Round(number * 1000) : (int)Math.Round(number);
                        ms = Math.Clamp(ms, 50, 10_000);
                        Flush(ms);
                        i += pauseMatch.Length - 1;
                        continue;
                    }
                }
            }

            if (!char.IsWhiteSpace(c)) consecutiveBreaks = 0;
            segment.Append(c);

            // 3.14, 1,5, 10:30 are never sentence boundaries.
            bool betweenDigits = i > 0 && i + 1 < input.Length &&
                                 char.IsDigit(input[i - 1]) && char.IsDigit(input[i + 1]);
            if (betweenDigits && c is '.' or ',' or ':') continue;

            // Handle ellipsis (... or …):
            // If ellipsis is mid-sentence before a lowercase word (e.g. "thế này... thì tốt biết mấy"),
            // eSpeak-ng assigns secondary stress (tˌi2) to Vietnamese connectors ("thì", "là", "mà", "nhưng").
            // Splitting into a new Piper utterance at BOS causes VITS to swallow the unstressed first word.
            // Keep mid-sentence ellipsis inside the same sentence as a comma pause!
            if (c == '…' || (c == '.' && i + 1 < input.Length && input[i + 1] == '.'))
            {
                int extraDots = 0;
                while (i + 1 < input.Length && input[i + 1] is '.' or '…')
                {
                    i++;
                    extraDots++;
                }

                int look = i + 1;
                while (look < input.Length && (input[look] == ' ' || input[look] == '\t'))
                    look++;

                if (look < input.Length && char.IsLower(input[look]) && segment.Length < MaxPhraseLength - 30)
                {
                    // Replace mid-sentence '...' or '…' before a lowercase word with ',' so Piper keeps full clause context.
                    segment[^1] = ',';
                    continue;
                }

                for (int d = 0; d < extraDots; d++) segment.Append('.');
                while (i + 1 < input.Length && IsTrailing(input[i + 1]))
                    segment.Append(input[++i]);
                Flush(EllipsisPauseMs);
                continue;
            }

            int pause = c switch
            {
                '.' or '。' or '۔' => PeriodPauseMs,
                '!' or '！' => ExclamationPauseMs,
                '?' or '？' or '؟' => QuestionPauseMs,
                _ => 0
            };
            if (pause > 0)
            {
                // Preserve consecutive punctuation and quotation marks in the SAME synthesis call.
                while (i + 1 < input.Length && IsTrailing(input[i + 1]))
                    segment.Append(input[++i]);
                Flush(pause);
                continue;
            }

            // The only soft split is for unusually long sentences.
            // Prefer a break at an existing comma near the max length, not every comma.
            if (segment.Length >= MaxPhraseLength)
            {
                bool canSplit = char.IsWhiteSpace(c) || (c is ',' or '，' or '،' or '、');
                if (canSplit) { Flush(SoftPauseMs); continue; }
                // CJK usually lacks whitespace.
                if (segment.Length >= MaxPhraseLength + 20 && IsCjk(c)) Flush(SoftPauseMs);
            }
        }
        Flush(0);
        if (result.Count > 0) result[^1] = result[^1] with { PauseAfterMs = 0 };
        return result;
    }

    private static bool IsCjk(char c) => c is >= '\u3400' and <= '\u9FFF' or
        >= '\u3040' and <= '\u30FF' or >= '\uAC00' and <= '\uD7AF';

    private static bool IsTrailing(char c) => c is '.' or '!' or '?' or '…' or '。' or
        '！' or '？' or '؟' or '”' or '’' or '"' or '\'' or ')' or ']' or '）' or '」' or '』';

    private static readonly (string Pattern, string Replacement)[] ChinesePolyphoneFixes =
    [
        ("重来", "崇来"), ("重新", "崇新"), ("重复", "崇复"), ("重蹈", "崇蹈"), ("重逢", "崇逢"),
        ("重叠", "崇叠"), ("重温", "崇温"), ("重演", "崇演"), ("重现", "崇现"), ("重申", "崇申"),
        ("一遍地", "一遍的"), ("一次地", "一次的"), ("一步地", "一步的"), ("一点地", "一点的"),
        ("不断地", "不断的"), ("不停地", "不停的"), ("慢慢地", "慢慢的"), ("静静地", "静静的"),
        ("悄悄地", "悄悄的"), ("默默地", "默默的"), ("渐渐地", "渐渐的"), ("深深地", "深深的"),
        ("紧紧地", "紧紧的"), ("好好地", "好好的"), ("轻轻地", "轻轻的"), ("缓缓地", "缓缓的"),
        ("长大", "掌大"), ("成长", "成掌"), ("家长", "家掌"), ("校长", "校掌"),
        ("银行", "银航"), ("行业", "航业"), ("同行", "同航"),
        ("睡觉", "睡叫"), ("午觉", "午叫"),
        ("音乐", "音月"), ("乐器", "月器"), ("乐队", "月队"),
        ("归还", "归环"), ("偿还", "偿环"), ("还给", "环给"),
        ("了解", "暸解"), ("受不了", "受不暸"), ("大不了", "大不暸"), ("了不起", "暸不起")
    ];

    public static string PrepareTextForPiper(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        // eSpeak-ng in piper_phonemize strips '...' and '…' without emitting a period/comma phoneme token,
        // which causes missing sentence-end cadence or fused words. Convert trailing ellipsis to '.' for Piper.
        var cleaned = Regex.Replace(text.Trim(), @"(?:\.{2,}|…+)\s*([""'”’)\]]*)$", ".$1");
        cleaned = Regex.Replace(cleaned, @"(?:\.{2,}|…+)", ",");
        if (cleaned.Any(IsCjk))
        {
            foreach (var (pattern, replacement) in ChinesePolyphoneFixes)
                cleaned = cleaned.Replace(pattern, replacement, StringComparison.Ordinal);
        }
        return cleaned;
    }
}
