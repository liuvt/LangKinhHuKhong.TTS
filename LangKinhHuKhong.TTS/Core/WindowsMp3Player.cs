using System.Runtime.InteropServices;
using System.Text;

namespace LangKinhHuKhong.TTS.Core;

// Dùng multimedia API có sẵn trên Windows. Không phụ thuộc vào NuGet/Windows Media Player COM.
public sealed class WindowsMp3Player : IDisposable
{
    private readonly string _alias = "lkh" + Guid.NewGuid().ToString("N")[..12];
    private bool _opened;
    public string? CurrentFile { get; private set; }
    public bool IsPlaying { get; private set; }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
    private static extern int MciSendString(string command, StringBuilder? buffer, int bufferSize, IntPtr callback);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciGetErrorStringW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MciGetErrorString(int error, StringBuilder buffer, int length);

    private static string Execute(string command, bool expectText = false)
    {
        var builder = expectText ? new StringBuilder(128) : null;
        var rc = MciSendString(command, builder, builder?.Capacity ?? 0, IntPtr.Zero);
        if (rc != 0)
        {
            var detail = new StringBuilder(300);
            MciGetErrorString(rc, detail, detail.Capacity);
            throw new InvalidOperationException($"Không phát được MP3: {detail} (MCI {rc}).");
        }
        return builder?.ToString() ?? "";
    }

    public void Play(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("File MP3 không tồn tại.", path);
        Close();
        Execute($"open \"{path}\" type mpegvideo alias {_alias}");
        _opened = true;
        CurrentFile = path;
        Execute($"set {_alias} time format milliseconds");
        Execute($"play {_alias}");
        IsPlaying = true;
    }

    public void TogglePause()
    {
        if (!_opened) return;
        Execute(IsPlaying ? $"pause {_alias}" : $"resume {_alias}");
        IsPlaying = !IsPlaying;
    }

    public int PositionMs => Query("position");
    public int DurationMs => Query("length");

    private int Query(string key)
    {
        if (!_opened) return 0;
        return int.TryParse(Execute($"status {_alias} {key}", true), out var value) ? value : 0;
    }

    public void Seek(int millis)
    {
        if (!_opened) return;
        var ms = Math.Clamp(millis, 0, DurationMs);
        Execute($"seek {_alias} to {ms}");
        Execute($"play {_alias} from {ms}");
        IsPlaying = true;
    }

    public void Close()
    {
        if (!_opened) return;
        try { Execute($"close {_alias}"); }
        catch (InvalidOperationException) { }
        _opened = false;
        IsPlaying = false;
        CurrentFile = null;
    }

    public void Dispose() => Close();
}
