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

    public const uint OutputMp3SampleRate = 44100u;
    private static readonly HashSet<double> AllowedSpeeds = [0.5, 1, 1.25, 1.5, 2];

    public StreamingPcmEncoder(string ffmpegExe, string destinationMp3, string initialWav, double outputSpeed = 1.0)
    {
        if (!File.Exists(ffmpegExe)) throw new FileNotFoundException("Thiếu ffmpeg.exe.", ffmpegExe);
        if (!AllowedSpeeds.Contains(outputSpeed))
            throw new ArgumentOutOfRangeException(nameof(outputSpeed), "Tốc độ không được hỗ trợ.");
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
        string clarityFilter = BuildStudioClarityFilter(outputSpeed);
        foreach (var arg in new[] {
            "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-f", "s16le", "-ar", rate, "-ac", channels, "-i", "pipe:0",
            "-vn", "-af", clarityFilter,
            "-c:a", "libmp3lame", "-b:a", "192k", "-ar", "44100", destinationMp3 })
            start.ArgumentList.Add(arg);
        _process = Process.Start(start) ?? throw new InvalidOperationException("Không khởi động được FFmpeg.");
        _stdin = _process.StandardInput.BaseStream;
        _errors = _process.StandardError.ReadToEndAsync();
    }

    /// <summary>
    /// Single-pass 32-bit float audio mastering chain:
    /// - Upsamples 16k/22.05k VITS PCM to 44.1 kHz MPEG-1 Layer III (halves MP3 MDCT pre-echo smearing on plosives).
    /// - Headroom pad (-1.1 dB) in fltp prevents inter-sample & filter clipping.
    /// - 65 Hz highpass cleans sub-bass rumble; gentle +1.8 dB @ 3.2 kHz presence shelf sharpens consonant clarity.
    /// - Applies requested playback speed directly on lossless float PCM so MP3 is encoded ONCE (zero double-encode loss).
    /// </summary>
    public static string BuildStudioClarityFilter(double speed = 1.0)
    {
        if (!AllowedSpeeds.Contains(speed))
            throw new ArgumentOutOfRangeException(nameof(speed), "Tốc độ không được hỗ trợ.");
        var chain = "aformat=sample_fmts=fltp,aresample=44100,volume=0.88,highpass=f=65,equalizer=f=3200:t=q:w=1.2:g=1.8";
        if (Math.Abs(speed - 1.0) > 1e-9)
            chain += "," + BuildAtempoFilter(speed);
        chain += ",alimiter=limit=0.95:level=false";
        return chain;
    }

    private static string BuildAtempoFilter(double speed)
    {
        var filters = new List<string>();
        while (speed > 2.0 + 1e-10) { filters.Add("atempo=2"); speed /= 2.0; }
        while (speed < 0.5 - 1e-10) { filters.Add("atempo=0.5"); speed /= 0.5; }
        filters.Add("atempo=" + speed.ToString("0.########", CultureInfo.InvariantCulture));
        return string.Join(',', filters);
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
