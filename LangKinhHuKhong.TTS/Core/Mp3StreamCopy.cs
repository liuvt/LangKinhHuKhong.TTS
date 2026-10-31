using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace LangKinhHuKhong.TTS.Core;

/// <summary>
/// Lossless-in-the-bitstream-sense MP3 concatenation: -c:a copy, no decode,
/// resample, channel remix, volume change or second MP3 encode.
/// Does NOT make lossy MP3 source audio mathematically lossless.
/// </summary>
public static class Mp3StreamCopy
{
    public sealed record Mp3Info(int SampleRate, int Channels, double DurationSeconds);

    public static async Task<Mp3Info> InspectAsync(string ffprobe, string path, CancellationToken ct)
    {
        string stdout = await RunAsync(ffprobe, Path.GetDirectoryName(Path.GetFullPath(path))!, ct,
            "-v", "error", "-select_streams", "a:0",
            "-show_entries", "stream=codec_name,sample_rate,channels",
            "-of", "default=noprint_wrappers=1", path);
        var pairs = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().Split('=', 2))
            .Where(x => x.Length == 2)
            .ToDictionary(x => x[0].Trim(), x => x[1].Trim(), StringComparer.OrdinalIgnoreCase);
        if (!pairs.TryGetValue("codec_name", out string? codec) || codec != "mp3" ||
            !pairs.TryGetValue("sample_rate", out string? rateText) ||
            !int.TryParse(rateText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rate) ||
            !pairs.TryGetValue("channels", out string? channelsText) ||
            !int.TryParse(channelsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int channels) ||
            rate <= 0 || channels <= 0)
            throw new InvalidDataException("File đầu vào phải có audio MP3 hợp lệ: " + path);
        double duration = await ProbeDurationAsync(ffprobe, path, ct);
        return new Mp3Info(rate, channels, duration);
    }

    public static void EnsureCompatible(IReadOnlyList<Mp3Info> infos)
    {
        if (infos.Count == 0) throw new ArgumentException("Không có MP3 cần ghép.");
        var first = infos[0];
        if (infos.Any(x => x.SampleRate != first.SampleRate || x.Channels != first.Channels))
            throw new InvalidOperationException(
                "Ghép nguyên bản không nén yêu cầu MP3 cùng sample rate và số kênh. " +
                "Các file hiện không cùng thông số; không tự mã hóa lại để tránh nhòe giọng. " +
                "Hãy tạo các đoạn cùng model/cấu hình, hoặc chuẩn hóa nguồn có chủ đích trước khi ghép.");
    }

    public static string MakeConcatEntry(string path)
    {
        // FFmpeg concat demuxer uses single quotes; portable '/' avoids backslash escaping on Windows.
        string normalized = Path.GetFullPath(path).Replace('\\', '/');
        return "file '" + normalized.Replace("'", "'\\''") + "'";
    }

    public static async Task ConcatenateAsync(string ffmpeg, string ffprobe, IReadOnlyList<string> sources,
        string outputMp3, string workDir, CancellationToken ct,
        IReadOnlyList<Mp3Info>? alreadyInspected = null)
    {
        if (sources.Count == 0) throw new ArgumentException("Không có MP3 cần ghép.");
        var checkedInfo = alreadyInspected ?? await Task.WhenAll(sources.Select(p => InspectAsync(ffprobe, p, ct)));
        EnsureCompatible(checkedInfo);
        foreach (var file in sources)
            if (!File.Exists(file)) throw new FileNotFoundException("Không tìm thấy MP3 đầu vào.", file);
        if (sources.Any(s => string.Equals(Path.GetFullPath(s), Path.GetFullPath(outputMp3),
                StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Không được ghi đè file MP3 nguồn.");
        Directory.CreateDirectory(workDir);
        string concatFile = Path.Combine(workDir, "mp3-copy-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            await File.WriteAllLinesAsync(concatFile, sources.Select(MakeConcatEntry),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
            await RunAsync(ffmpeg, workDir, ct, "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                "-f", "concat", "-safe", "0", "-i", concatFile,
                "-map", "0:a:0", "-vn", "-c:a", "copy", outputMp3);
            if (!File.Exists(outputMp3) || new FileInfo(outputMp3).Length == 0)
                throw new IOException("FFmpeg không tạo được MP3 ghép (stream copy).");
        }
        finally { if (File.Exists(concatFile)) File.Delete(concatFile); }
    }

    public static async Task<double> ProbeDurationAsync(string ffprobe, string path, CancellationToken ct)
    {
        string output = await RunAsync(ffprobe, Path.GetDirectoryName(Path.GetFullPath(path))!, ct,
            "-v", "error", "-show_entries", "format=duration", "-of",
            "default=noprint_wrappers=1:nokey=1", path);
        if (!double.TryParse(output.Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double duration) || !double.IsFinite(duration) || duration <= 0)
            throw new InvalidDataException("Không xác định được thời lượng MP3: " + path);
        return duration;
    }

    public static async Task<string> RunAsync(string executable, string cwd, CancellationToken ct,
        params string[] args)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("Không khởi động được: " + executable);
        try
        {
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct);
            Task<string> stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            string output = await stdout;
            string errors = await stderr;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"{Path.GetFileName(executable)} lỗi ({process.ExitCode}): {errors}");
            return output;
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            throw;
        }
    }
}
