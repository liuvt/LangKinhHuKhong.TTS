using System.Globalization;
using System.Text;
using System.Text.Json;
using LangKinhHuKhong.TTS.Models;

namespace LangKinhHuKhong.TTS.Core;

public static class VoiceCatalog
{
    private static readonly string[] FemaleNames =
    ["ngochuyen", "mytam", "maiphuong", "phuongtrang", "minhthu", "thanhphuong", "banmai", "ngoclan", "minhanh", "thuha", "yen nhi", "yen_nhi", "vais1000"];
    private static readonly string[] MaleNames =
    ["ngocngan", "manhdung", "taian", "minhquang", "thientam", "duyoryx", "chieu", "lacphi", "minhkhang", "tranthanh", "vietthao", "quanghuy", "25hours"];

    public static List<VoiceModel> Scan(string root)
    {
        if (!Directory.Exists(root)) return [];
        var results = new List<VoiceModel>();
        foreach (var model in Directory.EnumerateFiles(root, "*.onnx", SearchOption.AllDirectories))
        {
            var config = File.Exists(model + ".json") ? model + ".json" :
                Path.Combine(Path.GetDirectoryName(model)!, "config.json");
            if (!File.Exists(config)) continue;
            var relative = Path.GetRelativePath(root, model);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var folder = Path.GetFileName(Path.GetDirectoryName(model)) ?? "Giọng nói";
            // Generic folder names are categories, not voice names. Avoid several
            // English voices all appearing simply as "en" in the dropdown.
            var commonFolder = Normalize(folder) is
                "nu" or "nam" or "khac" or "vi" or "en" or "zh" or "ja" or "ko" or "th" or
                "fr" or "de" or "es" or "ru" or "id" or "pt" or "voices" or "voice";
            var name = parts.Length == 1 || commonFolder
                ? Path.GetFileNameWithoutExtension(model) : folder;
            var category = Category(parts, name);
            var language = VoiceLanguage.Unknown;
            var quality = "custom";
            var numSpeakers = 1;
            var speakers = new List<VoiceSpeaker>();
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(config));
                var r = doc.RootElement;
                if (r.TryGetProperty("language", out var lang))
                {
                    if (lang.ValueKind == JsonValueKind.Object && lang.TryGetProperty("code", out var code))
                        language = VoiceLanguage.Normalize(code.GetString());
                    else if (lang.ValueKind == JsonValueKind.String) language = VoiceLanguage.Normalize(lang.GetString());
                }
                if (language == VoiceLanguage.Unknown && r.TryGetProperty("espeak", out var espeak) &&
                    espeak.ValueKind == JsonValueKind.Object && espeak.TryGetProperty("voice", out var espeakVoice) &&
                    espeakVoice.ValueKind == JsonValueKind.String)
                    language = VoiceLanguage.Normalize(espeakVoice.GetString());
                if (language == VoiceLanguage.Unknown)
                    language = VoiceLanguage.InferFromFilename(model);
                if (r.TryGetProperty("audio", out var a) && a.ValueKind == JsonValueKind.Object &&
                    a.TryGetProperty("quality", out var q) && q.ValueKind == JsonValueKind.String)
                    quality = q.GetString() ?? quality;
                if (r.TryGetProperty("num_speakers", out var ns) && ns.TryGetInt32(out var count))
                    numSpeakers = Math.Max(1, count);
                if (r.TryGetProperty("speaker_id_map", out var map) && map.ValueKind == JsonValueKind.Object)
                    foreach (var s in map.EnumerateObject())
                        if (s.Value.TryGetInt32(out var id)) speakers.Add(new VoiceSpeaker(id, s.Name));
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
            { /* Vẫn liệt kê model có JSON metadata không chuẩn. */ }
            if (speakers.Count == 0 && numSpeakers > 1)
                speakers.AddRange(Enumerable.Range(0, numSpeakers).Select(i => new VoiceSpeaker(i, $"Speaker {i}")));
            results.Add(new VoiceModel(name, model, config, language, quality, numSpeakers,
                speakers.OrderBy(s => s.Id).ToList(), category));
        }
        return results.OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static string Category(string[] parts, string name)
    {
        if (parts.Length >= 2)
        {
            var section = Normalize(parts[0]);
            if (section == "nu") return "Nữ";
            if (section == "nam") return "Nam";
            if (section == "khac") return "Khác";
        }
        var probe = Normalize(string.Join(' ', parts) + name).Replace(" ", "").Replace("_", "");
        if (FemaleNames.Any(n => probe.Contains(Normalize(n).Replace(" ", "").Replace("_", "")))) return "Nữ";
        if (MaleNames.Any(n => probe.Contains(Normalize(n).Replace(" ", "").Replace("_", "")))) return "Nam";
        return "Khác";
    }

    private static string Normalize(string s)
    {
        var normalized = s.ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        return new string(normalized.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }
}
