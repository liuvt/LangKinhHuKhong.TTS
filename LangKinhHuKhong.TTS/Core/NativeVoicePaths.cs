using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LangKinhHuKhong.TTS.Models;

namespace LangKinhHuKhong.TTS.Core;

// Piper native thường lỗi với đường dẫn model tiếng Việt chứa dấu. Alias ASCII + hardlink.
public sealed class NativeVoicePaths
{
    private readonly object _gate = new();
    public string Root { get; }

    public NativeVoicePaths()
    {
        var temp = Path.Combine(Path.GetTempPath(), "LKH-TTS-Cache");
        var publicRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "LKH-TTS-Cache");
        if (string.IsNullOrWhiteSpace(publicRoot)) publicRoot = @"C:\Users\Public\Documents\LKH-TTS-Cache";
        Root = IsAscii(temp) ? temp : publicRoot;
        if (!IsAscii(Root))
            throw new InvalidOperationException("Không tìm được thư mục tạm không dấu cho Piper. Hãy cấu hình biến TEMP trỏ tới đường dẫn ASCII.");
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Path.Combine(Root, "jobs"));
        Directory.CreateDirectory(Path.Combine(Root, "voice-cache"));
    }

    public string CreateJobFolder()
    {
        var path = Path.Combine(Root, "jobs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public (string Model, string Config) Prepare(VoiceModel voice)
    {
        if (!File.Exists(voice.ModelPath) || !File.Exists(voice.ConfigPath))
            throw new FileNotFoundException("Thiếu cặp file giọng nói ONNX/JSON.");
        if (IsAscii(voice.ModelPath) && IsAscii(voice.ConfigPath)) return (voice.ModelPath, voice.ConfigPath);
        var info = new FileInfo(voice.ModelPath);
        var configInfo = new FileInfo(voice.ConfigPath);
        var fingerprint = $"{voice.ModelPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{voice.ConfigPath}|{configInfo.Length}|{configInfo.LastWriteTimeUtc.Ticks}";
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))[..20];
        var folder = Path.Combine(Root, "voice-cache", id);
        lock (_gate)
        {
            Directory.CreateDirectory(folder);
            LinkOrCopy(voice.ModelPath, Path.Combine(folder, "voice.onnx"));
            LinkOrCopy(voice.ConfigPath, Path.Combine(folder, "voice.onnx.json"));
        }
        return (Path.Combine(folder, "voice.onnx"), Path.Combine(folder, "voice.onnx.json"));
    }

    private static bool IsAscii(string value) => value.All(ch => ch <= 127);

    private static void LinkOrCopy(string source, string destination)
    {
        if (File.Exists(destination) && new FileInfo(destination).Length == new FileInfo(source).Length) return;
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (!CreateHardLink(temporary, source, IntPtr.Zero)) File.Copy(source, temporary);
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newFile, string existingFile, IntPtr securityAttributes);
}
