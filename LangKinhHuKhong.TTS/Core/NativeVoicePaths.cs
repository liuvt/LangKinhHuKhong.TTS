using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LangKinhHuKhong.TTS.Models;

namespace LangKinhHuKhong.TTS.Core;

// Piper native thường lỗi với đường dẫn model tiếng Việt chứa dấu. Alias ASCII + hardlink,
// đồng thời sinh file cấu hình Clarity JSON tối ưu độ nét âm vị và chống nuốt hư từ tiếng Việt.
public sealed class NativeVoicePaths
{
    private const string ClarityConfigVersion = "clarity-v2";
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
        var info = new FileInfo(voice.ModelPath);
        var configInfo = new FileInfo(voice.ConfigPath);
        var fingerprint = $"{ClarityConfigVersion}|{voice.ModelPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|" +
                          $"{voice.ConfigPath}|{configInfo.Length}|{configInfo.LastWriteTimeUtc.Ticks}|{voice.Language}";
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))[..20];
        var folder = Path.Combine(Root, "voice-cache", id);
        var modelTarget = IsAscii(voice.ModelPath) ? voice.ModelPath : Path.Combine(folder, "voice.onnx");
        var configTarget = Path.Combine(folder, "voice.onnx.json");

        lock (_gate)
        {
            Directory.CreateDirectory(folder);
            if (!IsAscii(voice.ModelPath))
                LinkOrCopy(voice.ModelPath, modelTarget);
            if (!File.Exists(configTarget) || new FileInfo(configTarget).Length == 0)
                WriteClarityConfig(voice.ConfigPath, configTarget, voice.Language);
        }
        return (modelTarget, configTarget);
    }

    /// <summary>
    /// Builds an enhanced runtime Piper config without mutating the user's original .onnx.json:
    /// 1. Restores full tone & vowel duration on Vietnamese/tonal function words ("mà, được, với, của, bị, từ, thì...")
    ///    by mapping eSpeak's unstressed marker 'ˌ' (ID 121) to primary syllable stress 'ˈ' (ID 120).
    /// 2. Tightens VITS stochastic noise (noise_scale / noise_w) and gives slight articulatory room (length_scale)
    ///    when the model ships with generic high-jitter defaults (0.667 / 0.80).
    /// </summary>
    public static string BuildClarityConfigJson(string rawJson, string? voiceLanguage = null)
    {
        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(rawJson);
        }
        catch
        {
            return rawJson;
        }
        if (rootNode is not JsonObject root) return rawJson;

        string? espeakVoice = root["espeak"]?["voice"]?.GetValue<string>();
        string? langCode = root["language"] is JsonObject langObj
            ? langObj["code"]?.GetValue<string>()
            : root["language"]?.GetValue<string>();
        string effectiveLang = (espeakVoice ?? langCode ?? voiceLanguage ?? "").Trim().ToLowerInvariant();

        bool isVietnamese = effectiveLang == "vi" || effectiveLang.StartsWith("vi-", StringComparison.Ordinal) ||
                            effectiveLang.StartsWith("vi_", StringComparison.Ordinal);
        bool isSyllableTimedTonal = isVietnamese ||
                                    effectiveLang is "zh" or "cmn" or "yue" or "th" or "lo" ||
                                    effectiveLang.StartsWith("zh-", StringComparison.Ordinal) ||
                                    effectiveLang.StartsWith("zh_", StringComparison.Ordinal) ||
                                    effectiveLang.StartsWith("cmn", StringComparison.Ordinal) ||
                                    effectiveLang.StartsWith("yue", StringComparison.Ordinal) ||
                                    effectiveLang.StartsWith("th-", StringComparison.Ordinal) ||
                                    effectiveLang.StartsWith("th_", StringComparison.Ordinal);

        // 1. Tonal syllable stress fix: prevent eSpeak-ng from reducing/swallowing Vietnamese function words.
        if (isSyllableTimedTonal &&
            root["phoneme_id_map"] is JsonObject idMap &&
            idMap["ˈ"] is JsonArray primaryIds &&
            primaryIds.Count > 0 &&
            idMap.ContainsKey("ˌ"))
        {
            var cloned = new JsonArray();
            foreach (var item in primaryIds)
                cloned.Add(item?.DeepClone());
            idMap["ˌ"] = cloned;
        }

        // 2. Stabilize VITS stochastic duration predictor & generator noise for crisp consonant/tone clarity.
        var inference = root["inference"] as JsonObject ?? new JsonObject();
        double noiseScale = ReadDouble(inference["noise_scale"], 0.667);
        double lengthScale = ReadDouble(inference["length_scale"], 1.0);
        double noiseW = ReadDouble(inference["noise_w"], 0.8);

        if (isVietnamese)
        {
            if (noiseScale >= 0.60) noiseScale = 0.42;
            if (noiseW >= 0.70) noiseW = 0.45;
            if (Math.Abs(lengthScale - 1.0) < 0.02) lengthScale = 1.06;
        }
        else
        {
            if (noiseScale >= 0.65) noiseScale = 0.50;
            if (noiseW >= 0.75) noiseW = 0.55;
        }

        inference["noise_scale"] = Math.Round(noiseScale, 3);
        inference["length_scale"] = Math.Round(lengthScale, 3);
        inference["noise_w"] = Math.Round(noiseW, 3);
        root["inference"] = inference;

        return root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    private static double ReadDouble(JsonNode? node, double fallback)
    {
        if (node is null) return fallback;
        try
        {
            var v = node.GetValue<double>();
            return double.IsFinite(v) && v > 0 ? v : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static void WriteClarityConfig(string sourceConfig, string destinationConfig, string? voiceLanguage)
    {
        var raw = File.ReadAllText(sourceConfig, Encoding.UTF8);
        var enhanced = BuildClarityConfigJson(raw, voiceLanguage);
        var temporary = destinationConfig + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, enhanced, new UTF8Encoding(false));
            File.Move(temporary, destinationConfig, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
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
