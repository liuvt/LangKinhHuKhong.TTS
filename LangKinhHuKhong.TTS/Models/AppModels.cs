namespace LangKinhHuKhong.TTS.Models;

public sealed record VoiceSpeaker(int Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record VoiceModel(
    string Name, string ModelPath, string ConfigPath, string Language,
    string Quality, int NumSpeakers, IReadOnlyList<VoiceSpeaker> Speakers, string Category)
{
    public override string ToString() => Name;
}

public sealed class AudioEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Type { get; set; } = "Render";
    public string Voice { get; set; } = "";
    public double Speed { get; set; } = 1;
    public string Language { get; set; } = "";
    public int Characters { get; set; }
    public int SourceCount { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string Filename => Path.GetFileName(FilePath);
    public long Bytes => File.Exists(FilePath) ? new FileInfo(FilePath).Length : 0L;
}

public sealed class DesktopSettings
{
    public string OutputFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LangKinhHuKhong", "MP3");
    public string VoicesFolder { get; set; } = Path.Combine(AppContext.BaseDirectory, "Voices");
    public string RuntimeFolder { get; set; } = Path.Combine(AppContext.BaseDirectory, "Runtime");
    public double Speed { get; set; } = 1;
    public string LastVoicePath { get; set; } = "";
    public string LastCategory { get; set; } = "Tất cả";
    public string LastLanguage { get; set; } = "vi-VN";
}
