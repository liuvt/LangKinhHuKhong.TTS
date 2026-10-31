using System.Text;
using System.Text.Json;
using LangKinhHuKhong.TTS.Models;

namespace LangKinhHuKhong.TTS.Core;

public sealed class Storage
{
    private readonly object _gate = new();
    private readonly string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LangKinhHuKhong", "TTS-Premium-v1.1");
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    // Named mutex bảo vệ history.json khi mở nhiều cửa sổ/tiến trình trên cùng Windows session.
    private static readonly Mutex HistoryLock = new(false, @"Local\LKH_TTS_PREMIUM_HISTORY_V11");
    private readonly List<AudioEntry> _history = [];
    public string DataFolder => _root;
    public DesktopSettings Settings { get; private set; } = new();

    public Storage()
    {
        Directory.CreateDirectory(_root);
        Settings = Read<DesktopSettings>("settings.json") ?? new DesktopSettings();
        _history.AddRange(Read<List<AudioEntry>>("history.json") ?? []);
        Directory.CreateDirectory(Settings.OutputFolder);
    }

    private T? Read<T>(string name)
    {
        try
        {
            var path = Path.Combine(_root, name);
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8), _options) : default;
        }
        catch (Exception) { return default; } // File config hỏng: khởi động bằng mặc định, không crash ứng dụng.
    }

    private void WriteAtomic<T>(string name, T data)
    {
        var destination = Path.Combine(_root, name);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(data, _options), new UTF8Encoding(false));
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void SaveSettings()
    {
        lock (_gate) WriteAtomic("settings.json", Settings);
    }

    private T WithHistory<T>(Func<List<AudioEntry>, T> callback, bool save = false)
    {
        lock (_gate)
        {
            var locked = false;
            try
            {
                try { locked = HistoryLock.WaitOne(TimeSpan.FromSeconds(20)); }
                catch (AbandonedMutexException) { locked = true; }
                if (!locked) throw new TimeoutException("Thư viện MP3 đang bị khóa bởi cửa sổ khác.");
                var latest = Read<List<AudioEntry>>("history.json") ?? [];
                var result = callback(latest);
                if (save) WriteAtomic("history.json", latest);
                _history.Clear();
                _history.AddRange(latest);
                return result;
            }
            finally { if (locked) HistoryLock.ReleaseMutex(); }
        }
    }

    public List<AudioEntry> GetHistory() => WithHistory(history => history
        .Where(x => File.Exists(x.FilePath))
        .OrderByDescending(x => x.CreatedUtc).Select(Clone).ToList());

    public int Count => GetHistory().Count;

    private static AudioEntry Clone(AudioEntry x) => new()
    {
        Id = x.Id, Title = x.Title, FilePath = x.FilePath, Type = x.Type, Voice = x.Voice,
        Speed = x.Speed, Language = x.Language, Characters = x.Characters, SourceCount = x.SourceCount, CreatedUtc = x.CreatedUtc
    };

    public void Add(AudioEntry entry) => WithHistory(history =>
    {
        history.Add(Clone(entry));
        return 0;
    }, save: true);

    public void Rename(string id, string title) => WithHistory(history =>
    {
        var entry = history.FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException("Không còn MP3 trong thư viện.");
        entry.Title = SafeName(title);
        return 0;
    }, save: true);

    public void Delete(IEnumerable<string> ids) => WithHistory(history =>
    {
        foreach (var id in ids.Distinct())
        {
            var found = history.FirstOrDefault(x => x.Id == id);
            if (found is null) continue;
            if (File.Exists(found.FilePath)) File.Delete(found.FilePath);
            var srt = Path.ChangeExtension(found.FilePath, ".srt");
            if (File.Exists(srt)) File.Delete(srt);
            history.Remove(found);
        }
        return 0;
    }, save: true);

    public void PurgeMissing() => WithHistory(history =>
    {
        history.RemoveAll(x => !File.Exists(x.FilePath));
        return 0;
    }, save: true);

    public static string SafeName(string? input)
    {
        var name = (input ?? "").Trim();
        if (name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '-');
        name = string.Concat(name.Where(c => !char.IsControl(c))).Trim().Trim('.');
        if (string.IsNullOrWhiteSpace(name)) return "LangKinhHuKhong-Audio";
        name = name[..Math.Min(name.Length, 120)];
        var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        return reserved.Contains(name, StringComparer.OrdinalIgnoreCase) ? "LKH-" + name : name;
    }

    public string NewOutputPath(string? title = null)
    {
        Directory.CreateDirectory(Settings.OutputFolder);
        var prefix = string.IsNullOrWhiteSpace(title) ? "LKH" : SafeName(title)[..Math.Min(SafeName(title).Length, 65)];
        return Path.Combine(Settings.OutputFolder, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.mp3");
    }
}
