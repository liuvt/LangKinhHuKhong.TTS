using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LangKinhHuKhong.TTS.Core;

public sealed record SubtitleCue(TimeSpan Start, TimeSpan End, string Text);

/// <summary>Pure SRT parser/writer. No speech recognition and no audio transcription.</summary>
public static class SubtitleSrt
{
    private static readonly Regex Timing = new(
        @"^\s*(?<start>\d{2,}:\d{2}:\d{2}[,.]\d{3})\s*-->\s*(?<end>\d{2,}:\d{2}:\d{2}[,.]\d{3})(?:\s+.*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static List<SubtitleCue> Parse(string contents)
    {
        if (contents is null) throw new ArgumentNullException(nameof(contents));
        string normalized = contents.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n');
        var output = new List<SubtitleCue>();
        var blocks = Regex.Split(normalized.Trim(), @"\n\s*\n");
        foreach (var block in blocks)
        {
            var lines = block.Split('\n');
            int timingLine = (lines.Length > 0 && int.TryParse(lines[0].Trim(), out _)) ? 1 : 0;
            if (timingLine >= lines.Length) continue;
            var m = Timing.Match(lines[timingLine]);
            if (!m.Success) throw new InvalidDataException($"SRT không có timecode hợp lệ: {lines[timingLine]}");
            var start = ParseTime(m.Groups["start"].Value);
            var end = ParseTime(m.Groups["end"].Value);
            var caption = string.Join("\n", lines.Skip(timingLine + 1)).Trim();
            if (end <= start || string.IsNullOrWhiteSpace(caption))
                throw new InvalidDataException("SRT có subtitle rỗng hoặc thời gian kết thúc không lớn hơn bắt đầu.");
            output.Add(new SubtitleCue(start, end, caption));
        }
        return output;
    }

    /// <summary>Apply actual post-FFmpeg PCM duration to captions from native-speed Piper WAVs.</summary>
    public static List<SubtitleCue> RescaleTimeline(IReadOnlyList<SubtitleCue> cues,
        TimeSpan originalDuration, TimeSpan renderedDuration)
    {
        if (originalDuration <= TimeSpan.Zero || renderedDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(originalDuration), "Thời lượng WAV phải dương.");
        double factor = renderedDuration.Ticks / (double)originalDuration.Ticks;
        var output = new List<SubtitleCue>(cues.Count);
        foreach (var cue in cues)
        {
            long startTicks = Math.Clamp((long)Math.Round(cue.Start.Ticks * factor), 0L, renderedDuration.Ticks);
            long endTicks = Math.Clamp((long)Math.Round(cue.End.Ticks * factor), startTicks, renderedDuration.Ticks);
            if (endTicks <= startTicks)
                endTicks = Math.Min(renderedDuration.Ticks, startTicks + TimeSpan.TicksPerMillisecond);
            if (endTicks > startTicks)
                output.Add(new SubtitleCue(new TimeSpan(startTicks), new TimeSpan(endTicks), cue.Text));
        }
        return output;
    }

    public static string Serialize(IReadOnlyList<SubtitleCue> cues)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < cues.Count; i++)
        {
            var cue = cues[i];
            if (cue.Start < TimeSpan.Zero || cue.End <= cue.Start)
                throw new InvalidDataException($"Subtitle #{i + 1} có thời gian không hợp lệ.");
            sb.Append(i + 1).Append("\r\n")
              .Append(FormatTime(cue.Start)).Append(" --> ")
              .Append(FormatTime(cue.End)).Append("\r\n")
              .Append(cue.Text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n"))
              .Append("\r\n\r\n");
        }
        return sb.ToString();
    }

    public static async Task SaveAsync(string path, IReadOnlyList<SubtitleCue> cues, CancellationToken ct = default)
    {
        await File.WriteAllTextAsync(path, Serialize(cues), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct);
    }

    public static async Task<List<SubtitleCue>> LoadAsync(string path, CancellationToken ct = default)
    {
        var txt = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
        return Parse(txt);
    }

    public static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        long ms = (long)Math.Round(time.TotalMilliseconds, MidpointRounding.AwayFromZero);
        long hours = ms / 3_600_000L;
        long minutes = ms / 60_000L % 60;
        long seconds = ms / 1000L % 60;
        long millis = ms % 1000L;
        return $"{hours:00}:{minutes:00}:{seconds:00},{millis:000}";
    }

    private static TimeSpan ParseTime(string value)
    {
        value = value.Replace('.', ',');
        string[] mainAndMs = value.Split(',');
        string[] parts = mainAndMs[0].Split(':');
        if (parts.Length != 3 || mainAndMs.Length != 2 ||
            !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long h) ||
            !int.TryParse(parts[1], out int min) || !int.TryParse(parts[2], out int sec) ||
            !int.TryParse(mainAndMs[1], out int ms) ||
            min is < 0 or > 59 || sec is < 0 or > 59 || ms is < 0 or > 999)
            throw new InvalidDataException("Timecode SRT không đúng định dạng HH:MM:SS,mmm.");
        return TimeSpan.FromMilliseconds(checked(h * 3_600_000L + min * 60_000L + sec * 1000L + ms));
    }
}
