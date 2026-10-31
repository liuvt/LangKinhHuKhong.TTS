using System.Text;

namespace LangKinhHuKhong.TTS.Core;

/// <summary>
/// Safe natural-sentence chunking for offline voices.
/// Commas and colons STAY IN a full sentence to avoid model losing Vietnamese syllable context.
/// WAV silence is inserted only after completed sentences and line breaks.
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
            if (!char.IsWhiteSpace(c)) consecutiveBreaks = 0;
            segment.Append(c);

            // 3.14, 1,5, 10:30 are never sentence boundaries.
            bool betweenDigits = i > 0 && i + 1 < input.Length &&
                                 char.IsDigit(input[i - 1]) && char.IsDigit(input[i + 1]);
            if (betweenDigits && c is '.' or ',' or ':') continue;

            int pause = c switch
            {
                '.' when i + 1 < input.Length && input[i + 1] == '.' => 0,
                '.' when i > 0 && input[i - 1] == '.' => EllipsisPauseMs,
                '.' or '。' or '۔' => PeriodPauseMs,
                '!' or '！' => ExclamationPauseMs,
                '?' or '？' or '؟' => QuestionPauseMs,
                '…' => EllipsisPauseMs,
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
}
