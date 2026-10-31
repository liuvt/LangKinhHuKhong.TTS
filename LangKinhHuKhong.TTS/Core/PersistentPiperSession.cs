using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace LangKinhHuKhong.TTS.Core;

/// <summary>One native Piper process for an entire render job, one model load, one JSON line per WAV.</summary>
public sealed class PersistentPiperSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _stderr;
    private bool _disposed;

    private PersistentPiperSession(Process process)
    {
        _process = process;
        _stderr = process.StandardError.ReadToEndAsync();
    }

    public static async Task<PersistentPiperSession> StartAsync(string exe, string model,
        string config, bool cuda, CancellationToken ct)
    {
        if (!File.Exists(exe)) throw new FileNotFoundException("Không thấy piper.exe", exe);
        string? gpuArg = null;
        if (cuda)
        {
            var help = new ProcessStartInfo(exe)
            {
                WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            help.ArgumentList.Add("--help");
            using var helpProcess = Process.Start(help) ?? throw new InvalidOperationException("Không kiểm tra được phiên bản Piper.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            var stdout = helpProcess.StandardOutput.ReadToEndAsync();
            var stderr = helpProcess.StandardError.ReadToEndAsync();
            try { await helpProcess.WaitForExitAsync(timeout.Token); }
            catch { if (!helpProcess.HasExited) helpProcess.Kill(entireProcessTree: true); throw; }
            var helpText = await stdout + "\n" + await stderr;
            if (helpText.Contains("--use-cuda", StringComparison.OrdinalIgnoreCase)) gpuArg = "--use-cuda";
            else if (helpText.Contains("--cuda", StringComparison.OrdinalIgnoreCase)) gpuArg = "--cuda";
            else throw new NotSupportedException("piper.exe hiện tại không khai báo CUDA. Chọn CPU hoặc thay runtime Piper có CUDA.");
        }
        var info = new ProcessStartInfo(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false)
        };
        foreach (var arg in new[] { "--model", model, "--config", config, "--json-input" })
            info.ArgumentList.Add(arg);
        if (gpuArg is not null) info.ArgumentList.Add(gpuArg);
        var process = Process.Start(info) ?? throw new InvalidOperationException("Không khởi động được Piper JSON session.");
        return new PersistentPiperSession(process);
    }

    public async Task SynthesizeAsync(string text, string wav, int? speakerId, CancellationToken ct)
    {
        if (_disposed || _process.HasExited) throw new InvalidOperationException("Phiên Piper đã dừng.");
        if (File.Exists(wav)) File.Delete(wav);
        var input = speakerId is int id
            ? new { text, output_file = wav, speaker_id = (int?)id }
            : new { text, output_file = wav, speaker_id = (int?)null };
        var json = JsonSerializer.Serialize(input, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        await _process.StandardInput.WriteLineAsync(json.AsMemory(), ct);
        await _process.StandardInput.FlushAsync(ct);
        // Piper does not offer a documented per-line acknowledgement. Wait until WAV writer closes.
        // Windows FileShare.None opens only after the file is no longer held by Piper.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (_process.HasExited)
                    throw new InvalidOperationException("Piper đã thoát giữa quá trình tạo WAV. Hãy kiểm tra runtime và model.");
                if (File.Exists(wav))
                {
                    try
                    {
                        bool complete;
                        using (var ready = new FileStream(wav, FileMode.Open, FileAccess.Read, FileShare.None))
                            complete = ready.Length > 44;
                        if (complete) { _ = WavPacing.Inspect(wav); return; }
                    }
                    catch (IOException) { /* Piper đang ghi file */ }
                    catch (InvalidDataException) { /* File đang được ghi dở */ }
                }
                await Task.Delay(50, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new TimeoutException("Piper không tạo WAV trong 10 phút. Có thể phiên JSON không tương thích hoặc model bị treo."); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try { _process.StandardInput.Close(); } catch (IOException) { }
        if (!_process.HasExited)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await _process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { _process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            }
        }
        if (!_process.HasExited) await _process.WaitForExitAsync();
        _process.Dispose();
        // Ensure stderr background reader is disposed with the process (not logged by default).
        _ = _stderr;
    }
}
