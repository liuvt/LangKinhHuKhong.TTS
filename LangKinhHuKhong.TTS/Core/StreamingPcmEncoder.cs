using System.Diagnostics;
using System.Globalization;

namespace LangKinhHuKhong.TTS.Core;

/// <summary>
/// Streams each Piper WAV as raw PCM into ONE FFmpeg/LAME process and deletes the chunk afterwards.
/// No temporary combined RIFF WAV, so stories are not limited by RIFF's ~4 GiB data cap.
/// Raw PCM streaming does not resample or re-encode individual chunks.
/// </summary>
public sealed class StreamingPcmEncoder : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _errors;
    private readonly Stream _stdin;
    private readonly WavPacing.PcmInfo _format;
    private bool _completed;
    private bool _initialWritten;
    public uint SampleRate => _format.SampleRate;
    public long TotalFrames { get; private set; }
    public TimeSpan Duration => TimeSpan.FromTicks((long)Math.Round(
        TotalFrames * (double)TimeSpan.TicksPerSecond / SampleRate,
        MidpointRounding.AwayFromZero));

    public StreamingPcmEncoder(string ffmpegExe, string destinationMp3, string initialWav)
    {
        if (!File.Exists(ffmpegExe)) throw new FileNotFoundException("Thiếu ffmpeg.exe.", ffmpegExe);
        _format = WavPacing.Inspect(initialWav);
        var start = new ProcessStartInfo(ffmpegExe)
        {
            WorkingDirectory = Path.GetDirectoryName(ffmpegExe)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true
        };
        string rate = _format.SampleRate.ToString(CultureInfo.InvariantCulture);
        string channels = _format.Channels.ToString(CultureInfo.InvariantCulture);
        foreach (var arg in new[] {
            "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-f", "s16le", "-ar", rate, "-ac", channels, "-i", "pipe:0",
            "-vn", "-c:a", "libmp3lame", "-b:a", "192k", destinationMp3 })
            start.ArgumentList.Add(arg);
        _process = Process.Start(start) ?? throw new InvalidOperationException("Không khởi động được FFmpeg.");
        _stdin = _process.StandardInput.BaseStream;
        _errors = _process.StandardError.ReadToEndAsync();
    }

    public async Task<long> AppendAsync(string wav, int pauseAfterMs, int initialPauseMs, CancellationToken ct)
    {
        if (_completed) throw new InvalidOperationException("MP3 đã kết thúc.");
        var info = WavPacing.Inspect(wav);
        if (!_initialWritten)
        {
            await WavPacing.AppendSilenceAsync(_stdin, _format, initialPauseMs, ct);
            TotalFrames = checked(TotalFrames + WavPacing.SilenceFrameCount(SampleRate, initialPauseMs));
            _initialWritten = true;
        }
        await WavPacing.AppendPcmAsync(wav, _stdin, _format, ct);
        await WavPacing.AppendSilenceAsync(_stdin, _format, pauseAfterMs, ct);
        TotalFrames = checked(TotalFrames + info.FrameCount +
            WavPacing.SilenceFrameCount(SampleRate, pauseAfterMs));
        return info.FrameCount;
    }

    public async Task CompleteAsync(CancellationToken ct)
    {
        if (_completed) return;
        await _stdin.FlushAsync(ct);
        _stdin.Close();
        await _process.WaitForExitAsync(ct);
        string errors = await _errors;
        if (_process.ExitCode != 0)
            throw new InvalidOperationException("FFmpeg tạo MP3 thất bại: " +
                (errors.Length > 2000 ? errors[..2000] : errors));
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        try { _stdin.Close(); } catch (IOException) { }
        if (!_process.HasExited)
        {
            try { _process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            try { await _process.WaitForExitAsync(); } catch (InvalidOperationException) { }
        }
        _process.Dispose();
    }
}
