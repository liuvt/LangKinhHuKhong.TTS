namespace LangKinhHuKhong.TTS.Core;

/// <summary>
/// Optional refinement BEFORE synthesis, for readable subtitles. Splits a long
/// text segment only at whitespace, carrying the original punctuation/pause to
/// the LAST subsegment. Captions still come from real per-chunk PCM timings.
/// </summary>
public static class SubtitleChunking
{
    private static bool ContainsCjk(string value) => value.Any(c =>
        c is >= '\u3400' and <= '\u9FFF' or >= '\u3040' and <= '\u30FF' or >= '\uAC00' and <= '\uD7AF');

    public static List<SpeechPacing.Segment> RefineForSrt(
        IReadOnlyList<SpeechPacing.Segment> original, int maxCharacters = 95)
    {
        if (maxCharacters < 40) throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        var result = new List<SpeechPacing.Segment>();
        foreach (var s in original)
        {
            string remaining = s.Text.Trim();
            if (remaining.Length == 0) continue;
            while (remaining.Length > maxCharacters)
            {
                int cut = remaining.LastIndexOf(' ', maxCharacters, maxCharacters + 1);
                if (cut < maxCharacters / 2)
                {
                    int next = remaining.IndexOf(' ', maxCharacters);
                    if (next < 0)
                    {
                        // CJK text frequently has no spaces. Split on a Unicode scalar
                        // boundary, keeping every character for both TTS and SRT.
                        if (!ContainsCjk(remaining)) break;
                        int boundary = maxCharacters;
                        if (boundary < remaining.Length && boundary > 0 &&
                            char.IsHighSurrogate(remaining[boundary - 1]) && char.IsLowSurrogate(remaining[boundary]))
                            boundary--;
                        result.Add(new SpeechPacing.Segment(remaining[..boundary], SpeechPacing.SoftPauseMs));
                        remaining = remaining[boundary..];
                        continue;
                    }
                    cut = next;
                }
                var head = remaining[..cut].Trim();
                if (head.Length > 0)
                    result.Add(new SpeechPacing.Segment(head, SpeechPacing.SoftPauseMs));
                remaining = remaining[(cut + 1)..].TrimStart();
            }
            if (remaining.Length > 0)
                result.Add(new SpeechPacing.Segment(remaining, s.PauseAfterMs));
            else if (result.Count > 0)
                result[^1] = result[^1] with { PauseAfterMs = s.PauseAfterMs };
        }
        return result;
    }
}
