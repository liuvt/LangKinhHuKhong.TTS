using System.Diagnostics;

namespace LangKinhHuKhong.TTS.Services;

public sealed class AudioMergeService(IWebHostEnvironment env)
{
    public async Task MergeAsync(IReadOnlyList<string> wavs, string mp3, CancellationToken ct)
    {
        if (wavs.Count == 0) throw new InvalidOperationException("Không có dữ liệu âm thanh để ghép.");
        var exe = Path.Combine(env.ContentRootPath, "Runtime", "ffmpeg", "ffmpeg.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("Thiếu Runtime/ffmpeg/ffmpeg.exe", exe);

        var list = Path.Combine(Path.GetDirectoryName(wavs[0])!, "concat.txt");
        await File.WriteAllLinesAsync(list, wavs.Select(x => $"file '{x.Replace("\\", "/").Replace("'", "'\\''")}'"), ct);
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0", "-i", list,
                     "-c:a", "libmp3lame", "-b:a", "192k", mp3 })
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Không khởi động được bộ ghép MP3.");
        try
        {
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(ct);
            var log = await stderr;
            if (process.ExitCode != 0) throw new InvalidOperationException($"Không ghép được MP3 ({process.ExitCode}): {log}");
        }
        catch
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* Tiến trình vừa kết thúc. */ }
                try { await process.WaitForExitAsync(); }
                catch (InvalidOperationException) { }
            }
            throw;
        }
    }

    /// <summary>
    /// Ghép MP3 theo thứ tự bằng filter concat: chuẩn hóa sample rate/channel từng đoạn trước
    /// khi nối, hỗ trợ MP3 khác giọng đọc / sampling rate. Chia từng lô 20 đoạn để
    /// tránh vượt giới hạn độ dài command-line trên Windows; không xóa MP3 nguồn.
    /// </summary>
    public async Task MergeMp3Async(IReadOnlyList<string> inputs, string output, CancellationToken ct)
    {
        if (inputs.Count < 2) throw new InvalidOperationException("Cần ít nhất 2 file MP3 để ghép.");
        var exe = Path.Combine(env.ContentRootPath, "Runtime", "ffmpeg", "ffmpeg.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("Thiếu Runtime/ffmpeg/ffmpeg.exe", exe);

        var tempDir = Path.Combine(env.ContentRootPath, "Temp", "merge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var queue = inputs.ToList();
            var stage = 0;
            // Một lô: 20 đường dẫn dưới giới hạn CreateProcess của Windows.
            while (queue.Count > 1)
            {
                var next = new List<string>();
                for (var start = 0; start < queue.Count; start += 20)
                {
                    ct.ThrowIfCancellationRequested();
                    var batch = queue.Skip(start).Take(20).ToArray();
                    if (batch.Length == 1)
                    {
                        next.Add(batch[0]);
                        continue;
                    }
                    var lastBatch = queue.Count <= 20 && start == 0;
                    var target = lastBatch ? output : Path.Combine(tempDir, $"stage-{stage}-{start}.mp3");
                    await MergeBatchAsync(exe, batch, target, ct);
                    next.Add(target);
                }
                queue = next;
                stage++;
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task MergeBatchAsync(string ffmpeg, IReadOnlyList<string> inputFiles,
        string target, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var option in new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-y" })
            psi.ArgumentList.Add(option);
        foreach (var input in inputFiles)
        {
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(input);
        }
        var filters = new List<string>();
        for (var i = 0; i < inputFiles.Count; i++)
            filters.Add($"[{i}:a:0]aresample=44100,aformat=sample_fmts=fltp:channel_layouts=stereo[s{i}]");
        filters.Add(string.Concat(Enumerable.Range(0, inputFiles.Count).Select(i => $"[s{i}]"))
            + $"concat=n={inputFiles.Count}:v=0:a=1[merged]");
        foreach (var arg in new[] {
            "-filter_complex", string.Join(";", filters), "-map", "[merged]",
            "-c:a", "libmp3lame", "-b:a", "192k", "-ar", "44100", "-ac", "2", target
        }) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Không chạy được FFmpeg ghép MP3.");
        try
        {
            var readError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(ct);
            var message = await readError;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"FFmpeg không ghép được các MP3 ({process.ExitCode}): {message}");
        }
        catch
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                try { await process.WaitForExitAsync(); }
                catch (InvalidOperationException) { }
            }
            throw;
        }
    }

}
