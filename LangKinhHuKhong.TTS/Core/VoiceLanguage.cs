using System.Text.RegularExpressions;
using LangKinhHuKhong.TTS.Models;

namespace LangKinhHuKhong.TTS.Core;

/// <summary>
/// Piper uses the phonemizer/language declared in each ONNX configuration.
/// Selecting a language chooses a compatible installed model; it does NOT
/// translate input text or make a single-language ONNX multilingual.
/// </summary>
public static class VoiceLanguage
{
    public const string All = "*";
    public const string Unknown = "unknown";

    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vi"] = "Tiếng Việt", ["en"] = "Tiếng Anh", ["zh"] = "Tiếng Trung",
        ["ja"] = "Tiếng Nhật", ["ko"] = "Tiếng Hàn", ["th"] = "Tiếng Thái",
        ["fr"] = "Tiếng Pháp", ["de"] = "Tiếng Đức", ["es"] = "Tiếng Tây Ban Nha",
        ["ru"] = "Tiếng Nga", ["id"] = "Tiếng Indonesia", ["pt"] = "Tiếng Bồ Đào Nha",
        ["it"] = "Tiếng Ý", ["nl"] = "Tiếng Hà Lan", ["pl"] = "Tiếng Ba Lan",
        ["ar"] = "Tiếng Ả Rập", ["hi"] = "Tiếng Hindi", ["tr"] = "Tiếng Thổ Nhĩ Kỳ",
        ["uk"] = "Tiếng Ukraina", ["cs"] = "Tiếng Séc", ["sv"] = "Tiếng Thụy Điển"
    };

    // ISO language codes, including common three-letter Piper/eSpeak languages.
    private static readonly Regex LocaleRegex = new(
        @"^(?<language>[a-z]{2,3})(?:[-_](?<region>[a-zA-Z]{2}|[0-9]{3}))?(?:[-_][a-zA-Z0-9]+)*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FilenameLocaleRegex = new(
        @"(?<![a-zA-Z])(?<language>[a-z]{2,3})[-_](?<region>[a-z]{2}|[0-9]{3})(?![a-zA-Z])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Unknown;
        var value = raw.Trim().Replace('_', '-');
        if (value == All) return All;
        // eSpeak can append a variant (e.g. en-us+f3) to an otherwise valid code.
        value = value.Split('+', 2)[0];
        var match = LocaleRegex.Match(value);
        if (!match.Success) return Unknown;
        var baseCode = match.Groups["language"].Value.ToLowerInvariant();
        if (!Names.ContainsKey(baseCode)) return Unknown;
        var region = match.Groups["region"].Value;
        return region.Length == 0 ? baseCode : baseCode + "-" + region.ToUpperInvariant();
    }

    public static string InferFromFilename(string modelPath)
    {
        var match = FilenameLocaleRegex.Match(Path.GetFileName(modelPath));
        if (!match.Success) return Unknown;
        return Normalize(match.Value);
    }

    public static string DisplayName(string? raw)
    {
        var code = Normalize(raw);
        if (code == All) return "Tất cả ngôn ngữ";
        if (code == Unknown) return "Chưa xác định (kiểm tra JSON)";
        var baseCode = code.Split('-')[0];
        return Names.TryGetValue(baseCode, out var name) ? $"{name} ({code})" : code;
    }

    public static bool Matches(string? actual, string? selected)
    {
        var required = Normalize(selected);
        return required == All || Normalize(actual) == required;
    }

    public static IReadOnlyList<string> CommonLanguages { get; } =
    ["vi-VN", "en-US", "en-GB", "zh-CN", "ja-JP", "ko-KR", "th-TH",
     "fr-FR", "de-DE", "es-ES", "ru-RU", "id-ID", "pt-BR"];

    public static IReadOnlyList<LanguageChoice> Choices(IEnumerable<VoiceModel> installed)
    {
        var counts = installed.GroupBy(v => Normalize(v.Language))
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var options = new List<LanguageChoice> { new(All, "Tất cả ngôn ngữ", counts.Values.Sum()) };
        foreach (var code in CommonLanguages.Concat(counts.Keys.Where(c => c != Unknown))
                     .Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var count = counts.GetValueOrDefault(code);
            options.Add(new LanguageChoice(code, $"{DisplayName(code)}  ·  {count} giọng", count));
        }
        if (counts.TryGetValue(Unknown, out var unknownCount))
            options.Add(new LanguageChoice(Unknown, $"{DisplayName(Unknown)}  ·  {unknownCount} giọng", unknownCount));
        return options;
    }

    public sealed record LanguageChoice(string Code, string Label, int Count)
    {
        public override string ToString() => Label;
    }
}
