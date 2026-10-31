using System.Text;

namespace LangKinhHuKhong.TTS.Core;

/// <summary>
/// Builds SRT in the SAME synthesis pass that produces one WAV per SpeechPacing.Segment.
/// Uses real PCM frame counts + exact silence frames inserted by WavPacing.
/// Never reads MP3 to recognize or recreate text.
/// </summary>
public static class TtsSubtitleTimeline
{
    public sealed record RenderedSegment(string Text, string WavFile, int PauseAfterMs);
    public sealed record MeasuredSegment(string Text, long FrameCount, int PauseAfterMs);
    private sealed record TimedSegment(string Text, TimeSpan Start, TimeSpan End, int PauseAfterMs);

    public static List<SubtitleCue> FromChunks(
        IReadOnlyList<SpeechPacing.Segment> segments,
        IReadOnlyList<string> wavFiles,
        int initialPauseMs = SpeechPacing.InitialPauseMs,
        int maxCaptionChars = 92,
        double maxCaptionSeconds = 8.0)
    {
        if (segments.Count != wavFiles.Count)
            throw new ArgumentException("Số segment TTS phải bằng số file WAV đã render.");
        var pieces = segments.Select((s, i) => new RenderedSegment(s.Text, wavFiles[i], s.PauseAfterMs)).ToList();
        return FromRendered(pieces, initialPauseMs, maxCaptionChars, maxCaptionSeconds);
    }

    public static List<SubtitleCue> FromRendered(
        IReadOnlyList<RenderedSegment> segments,
        int initialPauseMs,
        int maxCaptionChars = 92,
        double maxCaptionSeconds = 8.0)
    {
        if (segments.Count == 0) return new List<SubtitleCue>();
        if (initialPauseMs < 0 || segments.Any(s => s.PauseAfterMs < 0))
            throw new ArgumentOutOfRangeException(nameof(initialPauseMs));
        if (maxCaptionChars < 30 || maxCaptionSeconds < 1)
            throw new ArgumentException("Giới hạn subtitle không hợp lệ.");

        var formats = segments.Select(s => ReadPcm(s.WavFile)).ToArray();
        var fmt = formats[0];
        if (formats.Any(x => x.SampleRate != fmt.SampleRate || x.BlockAlign != fmt.BlockAlign))
            throw new InvalidDataException("WAV không cùng PCM format; cần đồng nhất trước khi ghép và tính SRT.");
        var measures = segments.Select((s, i) => new MeasuredSegment(s.Text, formats[i].FrameCount, s.PauseAfterMs)).ToArray();
        return FromMeasured(measures, fmt.SampleRate, initialPauseMs, maxCaptionChars, maxCaptionSeconds);
    }

    /// <summary>Builds captions from streaming PCM frame counts, not from MP3 recognition.</summary>
    public static List<SubtitleCue> FromMeasured(IReadOnlyList<MeasuredSegment> segments,
        uint sampleRate, int initialPauseMs = SpeechPacing.InitialPauseMs,
        int maxCaptionChars = 92, double maxCaptionSeconds = 8.0)
    {
        if (segments.Count == 0) return new List<SubtitleCue>();
        if (sampleRate == 0 || initialPauseMs < 0 || segments.Any(s => s.PauseAfterMs < 0 || s.FrameCount <= 0))
            throw new ArgumentOutOfRangeException(nameof(segments), "Thông số PCM/nhịp không hợp lệ.");
        if (maxCaptionChars < 30 || maxCaptionSeconds < 1)
            throw new ArgumentException("Giới hạn subtitle không hợp lệ.");

        long frameCursor = SilenceFrames(sampleRate, initialPauseMs);
        var timed = new List<TimedSegment>();
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            long start = frameCursor;
            frameCursor = checked(frameCursor + s.FrameCount);
            long end = frameCursor;
            if (!string.IsNullOrWhiteSpace(s.Text))
            {
                var textParts = SplitCaptionText(s.Text.Trim(), maxCaptionChars,
                    (end - start) / (double)sampleRate, maxCaptionSeconds);
                var textWeights = textParts.Select(x => Math.Max(1, x.Length)).ToArray();
                long denominator = textWeights.Sum(x => (long)x);
                long partial = 0;
                for (int part = 0; part < textParts.Count; part++)
                {
                    long partStart = start + (long)Math.Round((end - start) * (partial / (double)denominator));
                    partial += textWeights[part];
                    long partEnd = part == textParts.Count - 1 ? end :
                        start + (long)Math.Round((end - start) * (partial / (double)denominator));
                    if (partEnd <= partStart) continue;
                    timed.Add(new TimedSegment(textParts[part], FramesToTime(partStart, sampleRate),
                        FramesToTime(partEnd, sampleRate),
                        part == textParts.Count - 1 ? s.PauseAfterMs : 0));
                }
            }
            frameCursor = checked(frameCursor + SilenceFrames(sampleRate, s.PauseAfterMs));
        }

        // Small clauses are joined without guessing word boundaries in the audio.
        // A punctuation stop or a large pause forces a new subtitle.
        var captions = new List<SubtitleCue>();
        StringBuilder? group = null;
        TimeSpan groupStart = TimeSpan.Zero, groupEnd = TimeSpan.Zero;
        int lastPause = 0;
        foreach (var current in timed)
        {
            bool canJoin = group is not null &&
                           lastPause <= 250 &&
                           !EndsSentence(group.ToString()) &&
                           group.Length + 1 + current.Text.Length <= maxCaptionChars &&
                           (current.End - groupStart).TotalSeconds <= maxCaptionSeconds;
            if (!canJoin)
            {
                if (group is not null)
                    captions.Add(new SubtitleCue(groupStart, groupEnd, WrapLines(group.ToString())));
                group = new StringBuilder(current.Text);
                groupStart = current.Start;
            }
            else
            {
                group!.Append(' ').Append(current.Text);
            }
            groupEnd = current.End;
            lastPause = current.PauseAfterMs;
        }
        if (group is not null)
            captions.Add(new SubtitleCue(groupStart, groupEnd, WrapLines(group.ToString())));
        return captions;
    }

    private static List<string> SplitCaptionText(string text, int maxChars, double duration, double maxSeconds)
    {
        int desired = duration > maxSeconds ? (int)Math.Floor(text.Length * maxSeconds / duration) : maxChars;
        int size = Math.Clamp(desired, 30, maxChars);
        var pieces = new List<string>();
        string remainder = text.Trim();
        while (remainder.Length > size)
        {
            int cut = remainder.LastIndexOf(' ', Math.Min(size, remainder.Length - 1));
            if (cut < size / 2)
            {
                // CJK has no spaces. Preserve Unicode surrogate pairs when slicing.
                cut = size;
                if (cut < remainder.Length && char.IsHighSurrogate(remainder[cut - 1]) &&
                    char.IsLowSurrogate(remainder[cut])) cut--;
                if (cut <= 0) break;
                pieces.Add(remainder[..cut]);
                remainder = remainder[cut..].TrimStart();
            }
            else
            {
                pieces.Add(remainder[..cut].TrimEnd());
                remainder = remainder[(cut + 1)..].TrimStart();
            }
        }
        if (remainder.Length > 0) pieces.Add(remainder);
        return pieces;
    }

    private static string WrapLines(string text, int maxLine = 48)
    {
        if (text.Length <= maxLine) return text;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = new StringBuilder();
        var result = new List<string>();
        foreach (var word in words)
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > maxLine)
            {
                result.Add(line.ToString());
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0) result.Add(line.ToString());
        return string.Join("\n", result);
    }

    private static bool EndsSentence(string text) => text.TrimEnd().TrimEnd('"', '\'', '”', '’', ')', ']').EndsWith('.') ||
                                                     text.TrimEnd().TrimEnd('"', '\'', '”', '’', ')', ']').EndsWith('!') ||
                                                     text.TrimEnd().TrimEnd('"', '\'', '”', '’', ')', ']').EndsWith('?');
    private static long SilenceFrames(uint sampleRate, int ms) =>
        (long)Math.Round(sampleRate * (double)ms / 1000, MidpointRounding.AwayFromZero);
    private static TimeSpan FramesToTime(long frames, uint sampleRate) =>
        TimeSpan.FromTicks((long)Math.Round(frames * (double)TimeSpan.TicksPerSecond / sampleRate,
            MidpointRounding.AwayFromZero));

    private sealed record PcmFormat(uint SampleRate, ushort BlockAlign, long FrameCount);

    // Count PCM frames without decoding audio and without loading entire WAV into RAM.
    private static PcmFormat ReadPcm(string path)
    {
        using var fs = File.OpenRead(path);
        using var r = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);
        if (fs.Length < 44 || Encoding.ASCII.GetString(r.ReadBytes(4)) != "RIFF")
            throw new InvalidDataException($"Không phải WAV RIFF: {path}");
        _ = r.ReadUInt32();
        if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "WAVE")
            throw new InvalidDataException($"WAV sai header: {path}");
        ushort align = 0;
        uint rate = 0;
        long dataSize = -1;
        while (fs.Position + 8 <= fs.Length)
        {
            string tag = Encoding.ASCII.GetString(r.ReadBytes(4));
            uint size = r.ReadUInt32();
            long start = fs.Position;
            if (start + size > fs.Length) throw new InvalidDataException("WAV chunk lỗi size.");
            if (tag == "fmt ")
            {
                if (size < 16) throw new InvalidDataException("WAV fmt lỗi.");
                ushort codec = r.ReadUInt16();
                _ = r.ReadUInt16();
                rate = r.ReadUInt32();
                _ = r.ReadUInt32();
                align = r.ReadUInt16();
                ushort bits = r.ReadUInt16();
                if (codec != 1 || bits != 16 || rate == 0 || align == 0)
                    throw new InvalidDataException("Yêu cầu WAV signed PCM 16-bit từ Piper.");
            }
            else if (tag == "data") dataSize = size;
            fs.Position = start + size + (size & 1);
        }
        if (rate == 0 || align == 0 || dataSize < 0 || dataSize % align != 0)
            throw new InvalidDataException("WAV không có PCM data hợp lệ.");
        return new PcmFormat(rate, align, dataSize / align);
    }
}
