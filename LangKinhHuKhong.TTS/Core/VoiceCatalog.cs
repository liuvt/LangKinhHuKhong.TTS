using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LangKinhHuKhong.TTS.Models;

namespace LangKinhHuKhong.TTS.Core;

public static class VoiceCatalog
{
    private static readonly string[] FemaleNames =
    ["ngochuyen", "mytam", "maiphuong", "phuongtrang", "minhthu", "thanhphuong", "banmai", "ngoclan", "minhanh", "thuha", "yen nhi", "yen_nhi", "vais1000", "female", "lessac", "amy", "alba", "cori", "jenny", "aria"];
    private static readonly string[] MaleNames =
    ["ngocngan", "manhdung", "taian", "minhquang", "thientam", "duyoryx", "chieu", "lacphi", "minhkhang", "tranthanh", "vietthao", "quanghuy", "25hours", "male", "ryan", "danny", "joe", "kusal", "alan"];

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
            if (parts.Length > 1 && string.Equals(parts[0], "example", StringComparison.OrdinalIgnoreCase))
                continue;

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
                if (r.TryGetProperty("display_name", out var dn) && dn.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(dn.GetString()))
                {
                    name = dn.GetString()!.Trim();
                }
                if (r.TryGetProperty("category", out var cat) && cat.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(cat.GetString()))
                {
                    var normCat = Normalize(cat.GetString()!);
                    if (normCat is "nu" or "female") category = "Nữ";
                    else if (normCat is "nam" or "male") category = "Nam";
                    else if (normCat is "khac" or "other") category = "Khác";
                }
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

    public static string GetExampleFilePath(string voicesFolder, VoiceModel voice, int? speakerId = null)
    {
        var exampleDir = Path.Combine(voicesFolder, "example");
        Directory.CreateDirectory(exampleDir);
        var stem = SanitizeFileStem(Path.GetFileNameWithoutExtension(voice.ModelPath));
        var suffix = voice.NumSpeakers > 1 && speakerId.HasValue ? $"_spk{speakerId.Value}" : "";
        return Path.Combine(exampleDir, $"{stem}{suffix}.mp3");
    }

    public static bool HasExampleAudio(string voicesFolder, VoiceModel voice, int? speakerId = null)
    {
        var path = GetExampleFilePath(voicesFolder, voice, speakerId);
        return File.Exists(path) && new FileInfo(path).Length > 512;
    }

    public static string SanitizeFileStem(string? raw)
    {
        var text = (raw ?? "").Trim();
        if (text.EndsWith(".onnx.json", StringComparison.OrdinalIgnoreCase)) text = text[..^10];
        else if (text.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)) text = text[..^5];
        foreach (var ch in Path.GetInvalidFileNameChars()) text = text.Replace(ch, '_');
        text = string.Concat(text.Where(c => !char.IsControl(c))).Trim().Trim('.');
        return string.IsNullOrWhiteSpace(text) ? "voice_model" : text;
    }

    /// <summary>
    /// Renames the ONNX + JSON pair on disk and updates metadata inside .onnx.json
    /// so the voice name, category, and language match the studio configuration.
    /// </summary>
    public static string RenameAndUpdateVoice(
        VoiceModel voice,
        string newFileStem,
        string? newDisplayName,
        string? newCategory,
        string? newLanguage,
        string? voicesFolder = null)
    {
        if (!File.Exists(voice.ModelPath))
            throw new FileNotFoundException("Không tìm thấy file model ONNX.", voice.ModelPath);
        if (!File.Exists(voice.ConfigPath))
            throw new FileNotFoundException("Không tìm thấy file cấu hình JSON của model.", voice.ConfigPath);

        var dir = Path.GetDirectoryName(voice.ModelPath)!;
        var cleanStem = SanitizeFileStem(newFileStem);
        var targetModel = Path.Combine(dir, cleanStem + ".onnx");
        var targetConfig = targetModel + ".json";

        bool sameModelPath = string.Equals(
            Path.GetFullPath(voice.ModelPath),
            Path.GetFullPath(targetModel),
            StringComparison.OrdinalIgnoreCase);

        if (!sameModelPath && (File.Exists(targetModel) || File.Exists(targetConfig)))
            throw new IOException($"Tên file '{cleanStem}.onnx' đã tồn tại trong thư mục.");

        // Update JSON metadata first or write to targetConfig
        JsonObject rootObj;
        try
        {
            var rawJson = File.ReadAllText(voice.ConfigPath, Encoding.UTF8);
            rootObj = JsonNode.Parse(rawJson) as JsonObject ?? new JsonObject();
        }
        catch
        {
            rootObj = new JsonObject();
        }

        var display = string.IsNullOrWhiteSpace(newDisplayName) ? cleanStem : newDisplayName.Trim();
        rootObj["display_name"] = display;

        if (!string.IsNullOrWhiteSpace(newCategory) && newCategory is "Nữ" or "Nam" or "Khác")
            rootObj["category"] = newCategory;

        if (!string.IsNullOrWhiteSpace(newLanguage) && newLanguage != VoiceLanguage.All && newLanguage != VoiceLanguage.Unknown)
        {
            var normalizedLang = VoiceLanguage.Normalize(newLanguage);
            if (normalizedLang != VoiceLanguage.Unknown)
            {
                if (rootObj["language"] is JsonObject langObj)
                    langObj["code"] = normalizedLang.Replace('-', '_');
                else
                    rootObj["language"] = new JsonObject { ["code"] = normalizedLang.Replace('-', '_') };
            }
        }

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var updatedJson = rootObj.ToJsonString(jsonOptions);

        if (!sameModelPath)
        {
            File.Move(voice.ModelPath, targetModel);
            File.WriteAllText(targetConfig, updatedJson, new UTF8Encoding(false));
            if (string.Equals(Path.GetFullPath(voice.ConfigPath), Path.GetFullPath(voice.ModelPath + ".json"), StringComparison.OrdinalIgnoreCase) &&
                File.Exists(voice.ConfigPath))
            {
                File.Delete(voice.ConfigPath);
            }

            // Rename cached sample files in Voices\example if present
            if (!string.IsNullOrWhiteSpace(voicesFolder))
            {
                var exampleDir = Path.Combine(voicesFolder, "example");
                if (Directory.Exists(exampleDir))
                {
                    var oldStem = SanitizeFileStem(Path.GetFileNameWithoutExtension(voice.ModelPath));
                    foreach (var sample in Directory.EnumerateFiles(exampleDir, oldStem + "*.mp3"))
                    {
                        var fileName = Path.GetFileName(sample);
                        var newSampleName = cleanStem + fileName[oldStem.Length..];
                        var destSample = Path.Combine(exampleDir, newSampleName);
                        if (!File.Exists(destSample))
                            File.Move(sample, destSample);
                    }
                }
            }
        }
        else
        {
            File.WriteAllText(targetConfig, updatedJson, new UTF8Encoding(false));
        }

        return targetModel;
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
        var stemNorm = Normalize(name);
        if (stemNorm.StartsWith("nu_", StringComparison.Ordinal) || stemNorm.StartsWith("nu-", StringComparison.Ordinal) ||
            stemNorm.StartsWith("female_", StringComparison.Ordinal) || stemNorm.StartsWith("female-", StringComparison.Ordinal))
            return "Nữ";
        if (stemNorm.StartsWith("nam_", StringComparison.Ordinal) || stemNorm.StartsWith("nam-", StringComparison.Ordinal) ||
            stemNorm.StartsWith("male_", StringComparison.Ordinal) || stemNorm.StartsWith("male-", StringComparison.Ordinal))
            return "Nam";

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
