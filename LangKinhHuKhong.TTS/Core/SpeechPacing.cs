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
    public const int CommaPauseMs = 95;    // smart clause break pause (+120ms Piper tail = ~215ms natural comma breath)
    public const int ClausePauseMs = 135;  // semicolon/colon clause break (+120ms Piper tail = ~255ms)
    public const int PeriodPauseMs = 310;
    public const int ExclamationPauseMs = 350;
    public const int QuestionPauseMs = 330;
    public const int EllipsisPauseMs = 480;
    public const int LinePauseMs = 350;
    public const int ParagraphPauseMs = 550;
    public const int SoftPauseMs = 100;
    public const int MaxPhraseLength = 180;

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
                int ellipStart = i;
                while (i + 1 < input.Length && input[i + 1] is '.' or '…')
                    i++;

                int look = i + 1;
                while (look < input.Length && (input[look] == ' ' || input[look] == '\t'))
                    look++;

                if (look < input.Length && char.IsLower(input[look]) && segment.Length < MaxPhraseLength - 30)
                {
                    CountWordsAndCjk(segment.ToString().AsSpan(), out int curWords, out int curCjk);
                    bool isDramaticShortEllipsis = curWords is >= 1 and <= 2 && curCjk <= 3 &&
                                                   !StartsWithWeakConnector(input.AsSpan(look));
                    if (!isDramaticShortEllipsis)
                    {
                        // Replace mid-sentence '...' or '…' before a lowercase word with ',' so Piper keeps full clause context.
                        segment[^1] = ',';
                        continue;
                    }
                }

                for (int k = ellipStart + 1; k <= i; k++)
                    segment.Append(input[k]);
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

            // Smart clause splitting (sherpa-onnx / piper1-gpl style):
            // Keep short commas ("Không, nàng...", "Chuẩn bị xong, ông bước ra cửa.") inside one utterance,
            // but split multi-clause sentences once a clause reaches 6+ words (~28+ chars) and the remainder
            // also has 4+ words (~16+ chars). Prevents 300-550 phoneme-ID VITS attention drift & slurring.
            if (c is ',' or '，' or '、' or ';' or '；' or ':' or '：')
            {
                bool isMajorClause = c is ';' or '；' or ':' or '：';
                if (ShouldSplitAtClauseDelimiter(input, i, segment, isMajorClause))
                {
                    Flush(isMajorClause ? ClausePauseMs : CommaPauseMs);
                    continue;
                }
            }

            // Soft split for unusually long unpunctuated clauses.
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

    private static bool ShouldSplitAtClauseDelimiter(string input, int delimiterIndex, StringBuilder currentSegment, bool isMajorClause)
    {
        var current = currentSegment.ToString().AsSpan().Trim();
        int minChars = isMajorClause ? 20 : 28;
        int minWords = isMajorClause ? 4 : 6;
        int minCjk = isMajorClause ? 12 : 18;

        CountWordsAndCjk(current, out int words, out int cjk);
        bool currentReady = (words >= minWords && current.Length >= minChars) || cjk >= minCjk;
        if (!currentReady) return false;

        int remStart = delimiterIndex + 1;
        while (remStart < input.Length && (input[remStart] == ' ' || input[remStart] == '\t'))
            remStart++;
        if (remStart >= input.Length) return false;

        // Avoid splitting short clauses right before weak connectors ("thì", "là") unless already long.
        if (!isMajorClause && current.Length < 55 && StartsWithWeakConnector(input.AsSpan(remStart)))
            return false;

        int remEnd = remStart;
        int maxLookahead = Math.Min(input.Length, remStart + 128); // O(1) bounded scan for multi-million char manuscripts
        while (remEnd < maxLookahead)
        {
            char ch = input[remEnd];
            if (ch is '\n' or '[' or '!' or '?' or '！' or '？' or '؟' or '…' or '。' or '۔')
                break;
            if (ch == '.')
            {
                bool decimalDot = remEnd > 0 && remEnd + 1 < input.Length &&
                                  char.IsDigit(input[remEnd - 1]) && char.IsDigit(input[remEnd + 1]);
                if (!decimalDot) break;
            }
            remEnd++;
        }

        var remaining = input.AsSpan(remStart, remEnd - remStart).Trim();
        CountWordsAndCjk(remaining, out int remWords, out int remCjk);
        return (remWords >= 4 && remaining.Length >= 16) || remCjk >= 8;
    }

    private static bool StartsWithWeakConnector(ReadOnlySpan<char> span)
    {
        string[] connectors = ["thì ", "là "];
        foreach (var conn in connectors)
        {
            if (span.StartsWith(conn, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void CountWordsAndCjk(ReadOnlySpan<char> span, out int words, out int cjk)
    {
        words = 0;
        cjk = 0;
        bool inWord = false;
        foreach (var ch in span)
        {
            if (IsCjk(ch))
            {
                cjk++;
                inWord = false;
            }
            else if (char.IsLetterOrDigit(ch))
            {
                if (!inWord)
                {
                    words++;
                    inWord = true;
                }
            }
            else if (ch is not '\'' and not '’' and not '-')
            {
                inWord = false;
            }
        }
    }

    private static bool IsCjk(char c) => c is >= '\u3400' and <= '\u9FFF' or
        >= '\u3040' and <= '\u30FF' or >= '\uAC00' and <= '\uD7AF';

    private static bool IsTrailing(char c) => c is '.' or '!' or '?' or '…' or '。' or
        '！' or '？' or '؟' or '”' or '’' or '"' or '\'' or ')' or ']' or '）' or '」' or '』';

    private static readonly Regex NonCompoundDashOrQuote = new(
        @"(?<![\p{L}\p{N}])[-–—'‘’]+|[-–—'‘’]+(?![\p{L}\p{N}])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TrailingEllipsis = new(
        @"(?:\.{2,}|…+)\s*([""'”’)\]]*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AnyEllipsis = new(
        @"(?:\.{2,}|…+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MultiWhitespace = new(
        @"\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

    public static bool HasPronounceableText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var ch in text)
        {
            if (ch == '\uFFFD') continue;
            if (char.IsLetterOrDigit(ch)) return true;
        }
        return false;
    }

    private static readonly Regex ClockTimeRegex = new(
        @"(?<![\d:])(?<h>[01]?\d|2[0-3]):(?<m>[0-5]\d)(?![\d:])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static bool ContainsVietnameseDiacritics(string value)
    {
        foreach (var ch in value)
        {
            if (ch is (>= '\u0102' and <= '\u0103') or (>= '\u0110' and <= '\u0111') or
                (>= '\u0128' and <= '\u0129') or (>= '\u0168' and <= '\u0169') or
                (>= '\u01A0' and <= '\u01B0') or (>= '\u1EA0' and <= '\u1EF9') or
                'ă' or 'â' or 'đ' or 'ê' or 'ô' or 'ơ' or 'ư' or
                'Ă' or 'Â' or 'Đ' or 'Ê' or 'Ô' or 'Ơ' or 'Ư' or
                'á' or 'à' or 'ả' or 'ã' or 'ạ' or 'é' or 'è' or 'ẻ' or 'ẽ' or 'ẹ' or
                'í' or 'ì' or 'ỉ' or 'ĩ' or 'ị' or 'ó' or 'ò' or 'ỏ' or 'õ' or 'ọ' or
                'ú' or 'ù' or 'ủ' or 'ũ' or 'ụ' or 'ý' or 'ỳ' or 'ỷ' or 'ỹ' or 'ỵ')
                return true;
        }
        return false;
    }

    public static string PrepareTextForPiper(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // 1. Convert full-width ASCII variants and strip BOM / zero-width / decorative quotes & brackets
        //    that cause eSpeak-ng clause parser bugs (e.g. '"-Lão già' triggers 0xC0000005 segfault in espeak-ng.dll).
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch is (>= '\uFF10' and <= '\uFF19') or (>= '\uFF21' and <= '\uFF3A') or (>= '\uFF41' and <= '\uFF5A'))
                sb.Append((char)(ch - 0xFEE0));
            else if (ch is '\uFEFF' or '\u200B' or '\u200C' or '\u200D' or '\u2060' or '\uFFFD')
                continue;
            else if ("\"“”„«»「」『』【】《》〈〉()[]{}_*=#~^<>|\\/" .Contains(ch))
                sb.Append(' ');
            else
                sb.Append(ch);
        }

        // 2. Strip hyphens/dashes/single-quotes that are NOT between two alphanumeric characters.
        //    Prevents native eSpeak-ng null-dereference crash on patterns like '"-Word', ',-Word', ':-Word'.
        var cleaned = NonCompoundDashOrQuote.Replace(sb.ToString(), " ");

        // 3. Expand HH:MM clock times in Vietnamese text ("10:30" -> "10 giờ 30") so eSpeak-ng (vi)
        //    does not read "10:30" unnaturally as "mười ba mươi". Subtitle .SRT keeps original "10:30".
        if (ContainsVietnameseDiacritics(cleaned) && cleaned.Contains(':', StringComparison.Ordinal))
        {
            cleaned = ClockTimeRegex.Replace(cleaned, m =>
            {
                var h = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
                var min = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
                return min == 0 ? $"{h} giờ" : $"{h} giờ {min}";
            });
        }

        // 4. eSpeak-ng in piper_phonemize strips '...' and '…' without emitting a period/comma phoneme token,
        //    which causes missing sentence-end cadence or fused words. Convert trailing ellipsis to '.' for Piper.
        cleaned = TrailingEllipsis.Replace(cleaned.Trim(), ".$1");
        cleaned = AnyEllipsis.Replace(cleaned, ",");
        cleaned = MultiWhitespace.Replace(cleaned, " ").Trim();

        if (cleaned.Any(IsCjk))
        {
            foreach (var (pattern, replacement) in ChinesePolyphoneFixes)
                cleaned = cleaned.Replace(pattern, replacement, StringComparison.Ordinal);
        }
        return cleaned;
    }

    /// <summary>
    /// VITS/Piper models trained on full sentences suffer severe BOS/EOS Conv1D edge collapse on isolated 1-2 word
    /// utterances (e.g. "Giết!" -> "Chiết", "Giữ." -> "Thứ", "Đánh!" -> "Đấy", "Cút!" -> "Cố").
    /// Prepending a neutral vowel + colon ("À: " / "Ừ: ") warms up the HiFi-GAN & duration predictor inside a single
    /// tensor with a clean digital silence valley right before the target word, which WavPacing then slices off cleanly.
    /// </summary>
    public static (string TextToSynthesize, bool CarrierApplied) BuildShortUtteranceCarrier(string preparedText, string? languageHint = null)
    {
        if (string.IsNullOrWhiteSpace(preparedText)) return (preparedText, false);
        CountWordsAndCjk(preparedText.AsSpan(), out int words, out int cjk);

        if (cjk is >= 1 and <= 2 && words == 0)
        {
            char firstCjk = preparedText.FirstOrDefault(IsCjk);
            string cjkCarrier = firstCjk == '啊' ? "嗯，" : "啊，";
            return (cjkCarrier + preparedText, true);
        }

        if (words is >= 1 and <= 2 && cjk == 0 && preparedText.Length <= 22)
        {
            bool isVi = ContainsVietnameseDiacritics(preparedText) ||
                        (!string.IsNullOrWhiteSpace(languageHint) &&
                         languageHint.Trim().StartsWith("vi", StringComparison.OrdinalIgnoreCase));
            char firstLetter = preparedText.FirstOrDefault(char.IsLetter);
            if (isVi)
            {
                // Voiceless-stop coda ('-t') physically closes the vocal tract to near-zero amplitude before ':',
                // creating a guaranteed silence valley on ALL VITS models (including fast male voices like lacphi)
                // with zero voiced formant bleed into initial 'gi-', 'đ-', 'ch-', or open vowels.
                bool startsWithU = firstLetter != '\0' &&
                    "uUùÙúÚủỦũŨụỤưƯừỪứỨửỬữỮựỰ".Contains(firstLetter, StringComparison.Ordinal);
                return ((startsWithU ? "Ất: " : "Út: ") + preparedText, true);
            }
            else
            {
                bool startsWithA = firstLetter is 'a' or 'A';
                return ((startsWithA ? "Up: " : "At: ") + preparedText, true);
            }
        }

        return (preparedText, false);
    }

    public readonly record struct EllipsisSustainSpec(double StretchFactor, int MinTargetCoreMs)
    {
        public bool IsActive => StretchFactor > 1.05;
    }

    /// <summary>
    /// Computes both the stretch multiplier and minimum target vocalic-core duration (ms)
    /// when a segment ends with trailing ellipsis ("Giết...", "Không…", "Sư phụ ơi...", "Giết......").
    /// Compensates for playbackSpeed &gt; 1.0 (e.g. 1.5x) so sustained vowels still sound clearly elongated
    /// even after FFmpeg atempo compression, and ensures naturally short checked syllables ('-t', '-p', '-c'
    /// like "Giết...") reach a full dramatic ~560-760ms vocal hold instead of only adding ~60ms.
    /// </summary>
    public static EllipsisSustainSpec GetTrailingEllipsisSustainSpec(string? rawText, double playbackSpeed = 1.0)
    {
        if (string.IsNullOrWhiteSpace(rawText) || !HasPronounceableText(rawText))
            return new(1.0, 0);

        ReadOnlySpan<char> span = rawText.AsSpan().TrimEnd();
        while (!span.IsEmpty && span[^1] is '"' or '\'' or '”' or '’' or '»' or '」' or '』' or ')' or ']' or '}' or '）'
               or '!' or '?' or '！' or '？' or ' ')
        {
            span = span[..^1];
        }

        int effectiveDots = 0;
        int idx = span.Length - 1;
        while (idx >= 0 && (span[idx] == '.' || span[idx] == '…'))
        {
            effectiveDots += span[idx] == '…' ? 3 : 1;
            idx--;
        }

        if (effectiveDots < 2)
            return new(1.0, 0);

        double speedComp = double.IsFinite(playbackSpeed) && playbackSpeed > 1.0
            ? Math.Sqrt(Math.Min(playbackSpeed, 2.0))
            : 1.0;

        CountWordsAndCjk(span[..(idx + 1)], out int words, out int cjk);
        bool isShortDramatic = (words is >= 1 and <= 3 && cjk == 0) || (cjk is >= 1 and <= 4 && words == 0);
        if (isShortDramatic)
        {
            double baseStretch = effectiveDots >= 5 ? 4.6 : 3.6;
            int baseCoreMs = effectiveDots >= 5 ? 760 : 560;
            return new(baseStretch * speedComp, (int)Math.Round(baseCoreMs * speedComp));
        }

        bool isMediumClause = (words is >= 4 and <= 6 && cjk == 0) || (cjk is >= 5 and <= 10 && words == 0);
        if (isMediumClause)
        {
            double baseStretch = effectiveDots >= 5 ? 2.8 : 2.2;
            int baseCoreMs = effectiveDots >= 5 ? 420 : 320;
            return new(baseStretch * speedComp, (int)Math.Round(baseCoreMs * speedComp));
        }

        double longStretch = effectiveDots >= 5 ? 2.25 : 1.65;
        int longCoreMs = effectiveDots >= 5 ? 320 : 220;
        return new(longStretch * speedComp, (int)Math.Round(longCoreMs * speedComp));
    }

    public static double GetTrailingEllipsisStretchFactor(string? rawText, double playbackSpeed = 1.0)
        => GetTrailingEllipsisSustainSpec(rawText, playbackSpeed).StretchFactor;

    public static string SanitizeFallbackForPiper(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ".";
        var sb = new StringBuilder(text.Length);
        foreach (var ch in PrepareTextForPiper(text))
        {
            if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) || ch is ',' or '.' or '?' or '!' or '，' or '。' or '？' or '！')
                sb.Append(ch);
            else
                sb.Append(' ');
        }
        var res = MultiWhitespace.Replace(sb.ToString(), " ").Trim();
        return HasPronounceableText(res) ? res : ".";
    }
}
