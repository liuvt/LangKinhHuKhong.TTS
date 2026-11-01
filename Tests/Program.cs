using LangKinhHuKhong.TTS.Core;
using System.Text;

static void Check(bool okay, string what)
{
    if (!okay) throw new Exception("FAILED: " + what);
    Console.WriteLine("PASS: " + what);
}

var temp = Path.Combine(Path.GetTempPath(), "LKH-SRT-Test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    string a = Path.Combine(temp, "a.wav");
    string b = Path.Combine(temp, "b.wav");
    MakeWave(a, 1000);
    MakeWave(b, 500);
    var chunks = new[]
    {
        new TtsSubtitleTimeline.RenderedSegment("Một hôm.", a, 300),
        new TtsSubtitleTimeline.RenderedSegment("Nàng bắt đầu hành động!", b, 0),
    };
    var cues = TtsSubtitleTimeline.FromRendered(chunks, 160);
    Check(cues.Count == 2, "Cắt đúng 2 subtitle ở dấu kết thúc câu");
    Check(cues[0].Start == TimeSpan.FromMilliseconds(160), "Bắt đầu 160ms sau khoảng lặng PCM");
    Check(cues[0].End == TimeSpan.FromMilliseconds(1160), "WAV A 1000ms");
    Check(cues[1].Start == TimeSpan.FromMilliseconds(1460), "SRT B cộng 300ms pause thật");
    Check(cues[1].End == TimeSpan.FromMilliseconds(1960), "WAV B 500ms");
    var split = SubtitleChunking.RefineForSrt(new[] {
        new SpeechPacing.Segment("Một câu kể chuyện hơi dài có nhiều từ cần tách thành hai phần nhưng không được làm mất bất kỳ từ nào ở đây.", 350)
    }, maxCharacters: 60);
    Check(split.Count >= 2, "Tách đoạn dài trước TTS");
    Check(string.Join(" ", split.Select(x => x.Text)) ==
        "Một câu kể chuyện hơi dài có nhiều từ cần tách thành hai phần nhưng không được làm mất bất kỳ từ nào ở đây.",
        "Không thêm/bỏ nội dung khi chia segment");
    Check(split[^1].PauseAfterMs == 350, "Giữ nguyên thời gian ngắt nhịp cuối câu");
    var serialized = SubtitleSrt.Serialize(cues);
    var reparsed = SubtitleSrt.Parse(serialized);
    Check(reparsed.Count == 2 && reparsed[1].Text == cues[1].Text, "Serialize/parse roundtrip SRT");
    Check(serialized.Contains("00:00:01,460 --> 00:00:01,960"), "SRT timecode chuẩn");
    var vi = new[] { new SubtitleCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Ngọc Huyền kể truyện." ) };
    string viPath = Path.Combine(temp, "vi.srt");
    await SubtitleSrt.SaveAsync(viPath, vi);
    var viLoaded = await SubtitleSrt.LoadAsync(viPath);
    Check(viLoaded[0].Text == vi[0].Text, "Unicode tiếng Việt UTF-8");
}
finally { Directory.Delete(temp, true); }
Console.WriteLine("OK: Subtitle addon smoke tests.");

static void MakeWave(string path, int sampleCount)
{
    using var fs = File.Create(path);
    using var w = new BinaryWriter(fs, Encoding.ASCII);
    w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write((uint)(36 + sampleCount * 2));
    w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16u);
    w.Write((ushort)1); w.Write((ushort)1); w.Write(1000u); w.Write(2000u);
    w.Write((ushort)2); w.Write((ushort)16);
    w.Write(Encoding.ASCII.GetBytes("data")); w.Write((uint)(sampleCount * 2));
    for (int i = 0; i < sampleCount; i++) w.Write((short)123);
}

// v4.3: language metadata must never silently default to Vietnamese.
Check(VoiceLanguage.Normalize("en_US") == "en-US", "Piper en_US maps to English US");
Check(VoiceLanguage.Normalize("vi_VN") == "vi-VN", "Piper vi_VN maps to Vietnamese");
Check(VoiceLanguage.Normalize("en-us+f3") == "en-US", "eSpeak accent variant preserved as English US");
Check(VoiceLanguage.Normalize("xx_YY") == VoiceLanguage.Unknown, "Unknown language cannot be guessed");
Check(!VoiceLanguage.Matches("vi-VN", "en-US") && VoiceLanguage.Matches("en-US", "en-US"),
    "Language filters never force Vietnamese model to read English");
var voiceTmp = Path.Combine(Path.GetTempPath(), "LKH-Language-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(voiceTmp);
try
{
    File.WriteAllText(Path.Combine(voiceTmp, "vi_VN-test.onnx"), "fake");
    File.WriteAllText(Path.Combine(voiceTmp, "vi_VN-test.onnx.json"),
        "{\"language\":{\"code\":\"vi_VN\"}}");
    File.WriteAllText(Path.Combine(voiceTmp, "english.onnx"), "fake");
    File.WriteAllText(Path.Combine(voiceTmp, "english.onnx.json"),
        "{\"language\":{\"code\":\"en_GB\"}}");
    File.WriteAllText(Path.Combine(voiceTmp, "unknown.onnx"), "fake");
    File.WriteAllText(Path.Combine(voiceTmp, "unknown.onnx.json"), "{}");
    var found = VoiceCatalog.Scan(voiceTmp);
    Check(found.Count == 3, "Scan model ONNX with JSON");
    Check(found.Single(v => v.Name == "english").Language == "en-GB", "Language from model JSON");
    Check(found.Single(v => v.Name == "unknown").Language == VoiceLanguage.Unknown,
        "Missing metadata labeled unknown, not Vietnamese");
    Check(VoiceLanguage.Choices(found).Any(v => v.Code == "en-GB" && v.Count == 1),
        "Dropdown includes installed locale not in common presets");
    Check(VoiceLanguage.Choices(found).All(v => v.Code == VoiceLanguage.All || v.Count > 0),
        "Dropdown hides languages that have 0 installed voices");
}
finally { Directory.Delete(voiceTmp, recursive: true); }
Check(SpeechPacing.SegmentText("你好。欢迎！").Count == 2, "Chinese punctuation segments");
Check(SpeechPacing.SegmentText("你好，世界！").Count == 1, "Chinese comma stays inside full sentence for clear pronunciation");
Check(SpeechPacing.SegmentText("Không, nàng chưa bao giờ tin điều đó.").Count == 1,
    "Vietnamese comma stays in complete sentence to avoid chopped phonemes");
Check(SpeechPacing.SegmentText("Giá 1,5 triệu. Lúc 10:30, xe đến!").Count == 2,
    "Decimals and times are not split");
Check(SpeechPacing.SegmentText(new string('A', 250) + " nghỉ. ").Count == 2,
    "Very long unpunctuated clauses are split softly at the first whitespace");
var sourceCues = new[] {new SubtitleCue(TimeSpan.FromMilliseconds(160), TimeSpan.FromSeconds(2), "Bản thử tiếng Việt.")};
var faster = SubtitleSrt.RescaleTimeline(sourceCues, TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(1.25));
Check(faster[0].Start == TimeSpan.FromMilliseconds(80) && faster[0].End == TimeSpan.FromSeconds(1),
    "SRT 2x tracks real WAV duration ratio");
var slower = SubtitleSrt.RescaleTimeline(sourceCues, TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(5));
Check(slower[0].Start == TimeSpan.FromMilliseconds(320) && slower[0].End == TimeSpan.FromSeconds(4),
    "SRT 0.5x tracks real WAV duration ratio");
var longLine = "Quan trường là cuộc đấu trí không có chỗ cho sự nóng nảy, vì thế những người biết kiên nhẫn thường hiểu rõ đường lui và cách tiếp cận.";
var longWav = Path.Combine(Path.GetTempPath(), "LKH-TTS-long-" + Guid.NewGuid().ToString("N") + ".wav");
try
{
    MakeWave(longWav, 15000); // 15s at 1000 Hz, one whole sentence rendered once.
    var longCues = TtsSubtitleTimeline.FromRendered([
        new TtsSubtitleTimeline.RenderedSegment(longLine, longWav, 0)
    ], 160);
    Check(longCues.Count > 1, "Long sentence has readable SRT cues without re-cutting Piper input");
    Check(string.Join(" ", longCues.Select(x => x.Text.Replace("\n", " "))) == longLine,
        "SRT keeps all words in the original long sentence");
}
finally { File.Delete(longWav); }

Check(TextProcessor.NormalizeForTts("こんにちは。") == "こんにちは。", "Japanese punctuation preserved");
var japaneseChunks = SubtitleChunking.RefineForSrt([new SpeechPacing.Segment(new string('中', 120), 350)]);
Check(japaneseChunks.Count == 2 && string.Concat(japaneseChunks.Select(x => x.Text)).Length == 120,
    "Long CJK text splits without losing characters");

// v4.6: no re-encode MP3 merge compatibility checks.
Mp3StreamCopy.EnsureCompatible([
    new Mp3StreamCopy.Mp3Info(22050, 1, 12),
    new Mp3StreamCopy.Mp3Info(22050, 1, 10)
]);
Check(true, "Hai MP3 giống tần số và kênh có thể nối nguyên bitstream");
bool mismatchRejected = false;
try
{
    Mp3StreamCopy.EnsureCompatible([
        new Mp3StreamCopy.Mp3Info(22050, 1, 12),
        new Mp3StreamCopy.Mp3Info(44100, 2, 10)
    ]);
}
catch (InvalidOperationException) { mismatchRejected = true; }
Check(mismatchRejected, "Không âm thầm mã hóa lại MP3 không tương thích");

// v1.1: no app text size cap; long texts segment without truncation.
var beyondOldCap = string.Concat(Enumerable.Repeat("Một câu thử đọc rất rõ ràng. ", 4000));
Check(beyondOldCap.Length > 100_000, "Fixture vượt giới hạn 100.000 ký tự cũ");
var allSentences = SpeechPacing.SegmentText(TextProcessor.NormalizeForTts(beyondOldCap));
Check(allSentences.Count > 0 &&
      string.Concat(allSentences.Select(x => x.Text)).Length > 100_000 - 100,
    "TTS giữ nội dung dài hơn ngưỡng cũ, không cắt mất phần cuối");
var measureCues = TtsSubtitleTimeline.FromMeasured([
    new TtsSubtitleTimeline.MeasuredSegment("Câu đầu.", 1000, 300),
    new TtsSubtitleTimeline.MeasuredSegment("Câu tiếp!", 500, 0)
], 1000, 160);
Check(measureCues.Count == 2 &&
      measureCues[0].Start == TimeSpan.FromMilliseconds(160) &&
      measureCues[1].Start == TimeSpan.FromMilliseconds(1460),
      "Timeline từ số sample PCM, không cần giữ toàn bộ file WAV");
var longHours = TtsSubtitleTimeline.FromMeasured([
    new TtsSubtitleTimeline.MeasuredSegment("Đọc truyện rất dài.", 22050L * 60 * 60 * 55, 0)
], 22050, 160);
Check(longHours.Count > 0 && longHours[^1].End.TotalHours > 54,
    "SRT xử lý hơn 55 giờ mà không cần WAV RIFF 4 GB");

// v1.2: ElevenLabs Studio upgrades - Word-limited review snippet & Voice file rename/example sync
var reviewSource = "Một hai ba bốn năm sáu, bảy tám chín mười. Mười một mười hai mười ba.";
var (snippetText, wordCount, truncated) = TextProcessor.ExtractReviewSnippet(reviewSource, 6);
Check(truncated && wordCount == 6 && snippetText.StartsWith("Một hai ba bốn năm sáu", StringComparison.Ordinal),
    "Review giới hạn từ cắt đúng số từ và chuẩn hóa dấu câu");

var renameTmp = Path.Combine(Path.GetTempPath(), "LKH-VoiceRename-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(renameTmp);
try
{
    var oldOnnx = Path.Combine(renameTmp, "raw_model_x.onnx");
    File.WriteAllText(oldOnnx, "fake-onnx");
    File.WriteAllText(oldOnnx + ".json", "{\"audio\":{\"quality\":\"high\"}}");
    var exampleDir = Path.Combine(renameTmp, "example");
    Directory.CreateDirectory(exampleDir);
    File.WriteAllBytes(Path.Combine(exampleDir, "raw_model_x.mp3"), new byte[1024]);

    var scanned = VoiceCatalog.Scan(renameTmp);
    Check(scanned.Count == 1, "Quét model trước khi đổi tên");
    var newModelPath = VoiceCatalog.RenameAndUpdateVoice(
        scanned[0], "vi-VN_nu_ngochuyen-high", "Ngọc Huyền Studio", "Nữ", "vi-VN", renameTmp);
    Check(File.Exists(newModelPath) && File.Exists(newModelPath + ".json"), "Đổi tên cặp file .onnx + .onnx.json");
    var rescanned = VoiceCatalog.Scan(renameTmp);
    Check(rescanned.Count == 1 &&
          rescanned[0].Name == "Ngọc Huyền Studio" &&
          rescanned[0].Category == "Nữ" &&
          rescanned[0].Language == "vi-VN" &&
          VoiceCatalog.HasExampleAudio(renameTmp, rescanned[0]),
          "Lưu metadata display_name/category/language và đồng bộ file mẫu trong Voices/example");
}
finally { Directory.Delete(renameTmp, recursive: true); }

// Inline text commands: [pause:...], [nghi:...], [[note]], // line comments
var cmdNormalized = TextProcessor.NormalizeForTts("// Dòng comment\n[[Ghi chú ẩn]] Câu một. [pause:750ms]\nCâu hai [nghi:1.2s] Câu ba");
var cmdSegments = SpeechPacing.SegmentText(cmdNormalized);
Check(cmdSegments.Count == 3 &&
      cmdSegments[0].Text == "Câu một." && cmdSegments[0].PauseAfterMs == 750 &&
      cmdSegments[1].Text == "Câu hai" && cmdSegments[1].PauseAfterMs == 1200 &&
      cmdSegments[2].Text == "Câu ba.",
      "Câu lệnh text [pause:750ms], [nghi:1.2s], [[Ghi chú]] và // comment hoạt động chuẩn xác");
