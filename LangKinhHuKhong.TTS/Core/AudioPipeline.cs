using System.Diagnostics;

namespace LangKinhHuKhong.TTS.Core;

public sealed class AudioPipeline
{
    private readonly NativeVoicePaths _paths;
    private readonly Storage _storage;
    public AudioPipeline(NativeVoicePaths paths, Storage storage)
    { _paths = paths; _storage = storage; }

    public string PiperExe => Path.Combine(_storage.Settings.RuntimeFolder, "piper", "piper.exe");
    public string FfmpegExe => Path.Combine(_storage.Settings.RuntimeFolder, "ffmpeg", "ffmpeg.exe");

    // Always synthesize using the native ONNX/Piper timing. Playback speed is a separate FFmpeg stage.
    public async Task RenderWavAsync(string text, Models.VoiceModel voice, string wav,
        int? speakerId, CancellationToken ct)
    {
        if (!File.Exists(PiperExe)) throw new FileNotFoundException("Thiếu Runtime/piper/piper.exe (và các DLL đi kèm).", PiperExe);
        ValidateSpeaker(voice, speakerId);
        var (model, config) = _paths.Prepare(voice);
        var info = new ProcessStartInfo(PiperExe)
        {
            WorkingDirectory = Path.GetDirectoryName(PiperExe)!,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardError = true,
            StandardInputEncoding = new System.Text.UTF8Encoding(false)
        };
        // Deliberately omit --length_scale: model default 1.0 at EVERY selected output speed.
        AddArgs(info, "--model", model, "--config", config, "--output_file", wav);
        if (speakerId.HasValue) AddArgs(info, "--speaker", speakerId.Value.ToString());
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Không chạy được Piper.");
        try
        {
            var error = process.StandardError.ReadToEndAsync();
            Exception? writeIssue = null;
            try
            {
                await process.StandardInput.WriteAsync(text.AsMemory(), ct);
                await process.StandardInput.WriteLineAsync();
                await process.StandardInput.FlushAsync(ct);
            }
            catch (IOException ex) { writeIssue = ex; }
            finally { try { process.StandardInput.Close(); } catch (IOException) { } }
            await process.WaitForExitAsync(ct);
            var log = await error;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Piper lỗi {process.ExitCode} ({voice.Name}): {Limit(log)}");
            if (writeIssue is not null) throw new InvalidOperationException("Không gửi được văn bản cho Piper.", writeIssue);
            if (!File.Exists(wav) || new FileInfo(wav).Length <= 44)
                throw new InvalidOperationException($"Giọng {voice.Name} không tạo được WAV. Kiểm tra model ONNX/JSON.");
        }
        catch { await KillAsync(process); throw; }
    }

    /// <summary>
    /// Always takes a finished ORIGINAL 1× MP3. FFmpeg applies speed only afterward.
    /// This never influences Piper/ONNX synthesis and preserves playback pitch via atempo.
    /// </summary>
    public async Task ChangeMp3SpeedAsync(string originalMp3, string outputMp3,
        double outputSpeed, CancellationToken ct)
    {
        if (!SupportedSpeeds.Contains(outputSpeed)) throw new ArgumentOutOfRangeException(nameof(outputSpeed));
        if (outputSpeed == 1)
        {
            File.Copy(originalMp3, outputMp3, overwrite: false);
            return;
        }
        EnsureFfmpeg();
        var info = NewFfmpeg();
        AddArgs(info, "-i", originalMp3, "-vn", "-af", AudioTempoFilter(outputSpeed),
            "-c:a", "libmp3lame", "-b:a", "192k", outputMp3);
        await RunAsync(info, ct);
        if (!File.Exists(outputMp3) || new FileInfo(outputMp3).Length == 0)
            throw new InvalidOperationException("FFmpeg không tạo được MP3 sau khi chỉnh tốc độ.");
    }

    public async Task<TimeSpan> GetActualMp3DurationAsync(string mp3, CancellationToken ct)
    {
        // ffprobe is distributed alongside ffmpeg.exe (also used for MP3/SRT merging).
        var probeExe = Path.Combine(Path.GetDirectoryName(FfmpegExe)!, "ffprobe.exe");
        if (!File.Exists(probeExe))
            throw new FileNotFoundException("Thiếu Runtime/ffmpeg/ffprobe.exe để hiệu chỉnh SRT từ MP3 cuối.", probeExe);
        var psi = new ProcessStartInfo(probeExe)
        {
            WorkingDirectory = Path.GetDirectoryName(probeExe)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        AddArgs(psi, "-v", "error", "-show_entries", "format=duration",
            "-of", "default=noprint_wrappers=1:nokey=1", mp3);
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Không chạy được ffprobe.");
        try
        {
            var outTask = proc.StandardOutput.ReadToEndAsync(ct);
            var errTask = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            var output = await outTask;
            var errors = await errTask;
            if (proc.ExitCode != 0 ||
                !double.TryParse(output.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var seconds) ||
                !double.IsFinite(seconds) || seconds <= 0)
                throw new InvalidDataException("ffprobe không xác định được độ dài MP3: " + Limit(errors));
            return TimeSpan.FromSeconds(seconds);
        }
        catch { await KillAsync(proc); throw; }
    }

    // Supports FFmpeg versions where one atempo filter accepts only 0.5..2.0.
    public static string AudioTempoFilter(double speed)
    {
        if (!SupportedSpeeds.Contains(speed)) throw new ArgumentOutOfRangeException(nameof(speed));
        var filters = new List<string>();
        while (speed > 2.0 + 1e-10) { filters.Add("atempo=2"); speed /= 2.0; }
        while (speed < 0.5 - 1e-10) { filters.Add("atempo=0.5"); speed /= 0.5; }
        filters.Add("atempo=" + speed.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture));
        return string.Join(',', filters);
    }

    public async Task EncodeWavsAsync(IReadOnlyList<string> wavs, string outputMp3,
        string workDir, CancellationToken ct)
    {
        EnsureFfmpeg();
        if (wavs.Count == 0) throw new InvalidOperationException("Không có đoạn WAV cần xuất.");
        var listPath = Path.Combine(workDir, "concat.txt");
        await File.WriteAllLinesAsync(listPath,
            wavs.Select(f => "file '" + f.Replace("\\", "/").Replace("'", "'\\''") + "'"), ct);
        var info = NewFfmpeg();
        AddArgs(info, "-f", "concat", "-safe", "0", "-i", listPath,
            "-c:a", "libmp3lame", "-b:a", "192k", outputMp3);
        await RunAsync(info, ct);
        if (!File.Exists(outputMp3) || new FileInfo(outputMp3).Length == 0)
            throw new InvalidOperationException("FFmpeg không tạo được MP3.");
    }

    /// <summary>Legacy merge path: also performs frame/stream copy at 1x; no re-encode.</summary>
    public async Task MergeMp3Async(IReadOnlyList<string> sourceFiles, string outputMp3,
        IProgress<(int done, int total)>? progress, CancellationToken ct)
    {
        EnsureFfmpeg();
        if (sourceFiles.Count < 2) throw new InvalidOperationException("Hãy chọn ít nhất 2 MP3.");
        string ffprobe = Path.Combine(Path.GetDirectoryName(FfmpegExe)!, "ffprobe.exe");
        if (!File.Exists(ffprobe)) throw new FileNotFoundException("Cần ffprobe.exe để kiểm tra định dạng MP3.", ffprobe);
        var dir = _paths.CreateJobFolder();
        try
        {
            progress?.Report((0, 1));
            await Mp3StreamCopy.ConcatenateAsync(FfmpegExe, ffprobe,
                sourceFiles, outputMp3, dir, ct);
            progress?.Report((1, 1));
        }
        finally { SafeDeleteDirectory(dir); }
    }

    private ProcessStartInfo NewFfmpeg()
    {
        var psi = new ProcessStartInfo(FfmpegExe)
        {
            WorkingDirectory = Path.GetDirectoryName(FfmpegExe)!,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
        };
        AddArgs(psi, "-hide_banner", "-loglevel", "error", "-nostdin", "-y");
        return psi;
    }

    private async Task RunAsync(ProcessStartInfo psi, CancellationToken ct)
    {
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Không khởi động được FFmpeg.");
        try
        {
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(ct);
            var log = await errors;
            if (process.ExitCode != 0) throw new InvalidOperationException($"FFmpeg lỗi {process.ExitCode}: {Limit(log)}");
        }
        catch { await KillAsync(process); throw; }
    }

    private static async Task KillAsync(Process process)
    {
        if (process.HasExited) return;
        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { return; }
        try { await process.WaitForExitAsync(); }
        catch (InvalidOperationException) { }
    }

    private static void ValidateSpeaker(Models.VoiceModel voice, int? id)
    {
        if (!id.HasValue) return;
        if (id < 0 || id >= voice.NumSpeakers) throw new InvalidOperationException("Speaker ID không hợp lệ.");
    }

    private static string Limit(string message) => message.Length > 1500 ? message[..1500] : message;
    private static void AddArgs(ProcessStartInfo info, params string[] args)
    { foreach (var arg in args) info.ArgumentList.Add(arg); }
    private void EnsureFfmpeg()
    {
        if (!File.Exists(FfmpegExe)) throw new FileNotFoundException("Thiếu Runtime/ffmpeg/ffmpeg.exe.", FfmpegExe);
    }
    public static void SafeDeleteDirectory(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static readonly HashSet<double> SupportedSpeeds =
        [0.5, 1, 1.25, 1.5, 2];
}
