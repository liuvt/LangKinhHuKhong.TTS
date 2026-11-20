using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace LangKinhHuKhong.TTS.Core;

/// <summary>One native Piper process for an entire render job, one model load, one JSON line per WAV.</summary>
public sealed class PersistentPiperSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _stderr;
    private readonly uint _sampleRate;
    private bool _disposed;

    public bool IsCudaActive { get; }
    public bool IsAlive => !_disposed && !_process.HasExited;

    private PersistentPiperSession(Process process, bool isCudaActive, uint sampleRate)
    {
        _process = process;
        _stderr = process.StandardError.ReadToEndAsync();
        IsCudaActive = isCudaActive;
        _sampleRate = sampleRate > 0 ? sampleRate : 22050u;
    }

    public static bool HasCudaProvider(string exe)
    {
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return false;
        var dir = Path.GetDirectoryName(exe);
        return dir is not null && File.Exists(Path.Combine(dir, "onnxruntime_providers_cuda.dll"));
    }

    public sealed record SynthesisParams(uint SampleRate, double NoiseScale, double LengthScale, double NoiseW);

    public static SynthesisParams ReadSynthesisParams(string configPath)
    {
        uint sampleRate = 22050u;
        double noiseScale = 0.42;
        double lengthScale = 1.06;
        double noiseW = 0.45;
        try
        {
            if (!File.Exists(configPath)) return new(sampleRate, noiseScale, lengthScale, noiseW);
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = doc.RootElement;
            if (root.TryGetProperty("audio", out var audio) &&
                audio.ValueKind == JsonValueKind.Object &&
                audio.TryGetProperty("sample_rate", out var sr) &&
                sr.TryGetUInt32(out var rate) && rate >= 8000 && rate <= 96000)
            {
                sampleRate = rate;
            }
            if (root.TryGetProperty("inference", out var inf) && inf.ValueKind == JsonValueKind.Object)
            {
                if (inf.TryGetProperty("noise_scale", out var ns) && ns.TryGetDouble(out var nsVal) && nsVal > 0 && nsVal <= 2.0)
                    noiseScale = nsVal;
                if (inf.TryGetProperty("length_scale", out var ls) && ls.TryGetDouble(out var lsVal) && lsVal >= 0.25 && lsVal <= 4.0)
                    lengthScale = lsVal;
                if (inf.TryGetProperty("noise_w", out var nw) && nw.TryGetDouble(out var nwVal) && nwVal > 0 && nwVal <= 2.0)
                    noiseW = nwVal;
            }
        }
        catch { }
        return new(sampleRate, noiseScale, lengthScale, noiseW);
    }

    public static async Task<PersistentPiperSession> StartAsync(string exe, string model,
        string config, bool cuda, CancellationToken ct)
    {
        if (!File.Exists(exe)) throw new FileNotFoundException("Không thấy piper.exe", exe);
        var syn = ReadSynthesisParams(config);
        string? gpuArg = null;
        if (cuda && HasCudaProvider(exe))
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
            // If piper.exe does not advertise CUDA flags, fall back cleanly to CPU instead of crashing the render job.
        }
        var info = new ProcessStartInfo(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8
        };
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var arg in new[]
        {
            "--model", model,
            "--config", config,
            "--noise_scale", syn.NoiseScale.ToString("0.###", inv),
            "--length_scale", syn.LengthScale.ToString("0.###", inv),
            "--noise_w", syn.NoiseW.ToString("0.###", inv),
            "--sentence_silence", "0.12",
            "--json-input",
            "--quiet"
        })
        {
            info.ArgumentList.Add(arg);
        }
        if (gpuArg is not null) info.ArgumentList.Add(gpuArg);
        var process = Process.Start(info) ?? throw new InvalidOperationException("Không khởi động được Piper JSON session.");
        return new PersistentPiperSession(process, gpuArg is not null, syn.SampleRate);
    }

    public static void WriteSilentWav(string wavPath, uint sampleRate, int durationMs = 20)
    {
        int frames = Math.Max(1, (int)(sampleRate * durationMs / 1000));
        int dataBytes = frames * 2; // 16-bit mono
        Span<byte> header = stackalloc byte[44];
        "RIFF"u8.CopyTo(header[0..4]);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..8], 36 + dataBytes);
        "WAVEfmt "u8.CopyTo(header[8..16]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..20], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..22], 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(header[22..24], 1); // 1 channel
        BinaryPrimitives.WriteInt32LittleEndian(header[24..28], (int)sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..32], (int)sampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..34], 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[34..36], 16);
        "data"u8.CopyTo(header[36..40]);
        BinaryPrimitives.WriteInt32LittleEndian(header[40..44], dataBytes);

        using var fs = new FileStream(wavPath, FileMode.Create, FileAccess.Write, FileShare.None);
        fs.Write(header);
        fs.Write(new byte[dataBytes]);
    }

    public async Task SynthesizeAsync(string text, string wav, int? speakerId, CancellationToken ct)
    {
        if (_disposed || _process.HasExited) throw new InvalidOperationException("Phiên Piper đã dừng.");
        if (File.Exists(wav)) File.Delete(wav);
        var piperText = SpeechPacing.PrepareTextForPiper(text);
        if (!SpeechPacing.HasPronounceableText(piperText))
        {
            WriteSilentWav(wav, _sampleRate, durationMs: 20);
            _ = WavPacing.Inspect(wav);
            return;
        }

        var input = speakerId is int id
            ? new { text = piperText, output_file = wav, speaker_id = (int?)id }
            : new { text = piperText, output_file = wav, speaker_id = (int?)null };
        var json = JsonSerializer.Serialize(input, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        await _process.StandardInput.WriteLineAsync(json.AsMemory(), ct);
        await _process.StandardInput.FlushAsync(ct);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            // piper.exe writes the output WAV path line to stdout immediately after textToWavFile finishes.
            var ackLine = await _process.StandardOutput.ReadLineAsync(deadline.Token);
            if (ackLine is null && _process.HasExited)
            {
                throw new InvalidOperationException("Piper đã thoát giữa quá trình tạo WAV. Hãy kiểm tra runtime và model.");
            }

            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (_process.HasExited && !File.Exists(wav))
                    throw new InvalidOperationException("Piper đã thoát giữa quá trình tạo WAV. Hãy kiểm tra runtime và model.");
                if (File.Exists(wav))
                {
                    try
                    {
                        long len;
                        using (var ready = new FileStream(wav, FileMode.Open, FileAccess.Read, FileShare.None))
                            len = ready.Length;
                        if (len == 44)
                        {
                            // Piper synthesized 0 phonemes (e.g. symbols ignored by eSpeak); replace with 20ms silence.
                            WriteSilentWav(wav, _sampleRate, durationMs: 20);
                            len = new FileInfo(wav).Length;
                        }
                        if (len > 44) { _ = WavPacing.Inspect(wav); return; }
                    }
                    catch (IOException) { /* Piper đang đóng file */ }
                    catch (InvalidDataException) { /* File đang được ghi dở */ }
                }
                await Task.Delay(15, deadline.Token);
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
