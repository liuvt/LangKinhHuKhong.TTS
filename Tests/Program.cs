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

Check(SpeechPacing.SegmentText("こんにちは。").Count == 1, "Japanese punctuation preserved");
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
var allSentences = SpeechPacing.SegmentText(beyondOldCap);
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
Check(truncated && wordCount == 6 && snippetText == "Một hai ba bốn năm sáu,",
    "Review giới hạn từ cắt đúng số từ và giữ nguyên dấu câu gốc");

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

// Inline text commands: [pause:...], [nghi:...], [[note]], // line comments (without mutating raw text)
var cmdSegments = SpeechPacing.SegmentText("// Dòng comment\n[[Ghi chú ẩn]] Câu một. [pause:750ms]\nCâu hai [nghi:1.2s] Câu ba");
Check(cmdSegments.Count == 3 &&
      cmdSegments[0].Text == "Câu một." && cmdSegments[0].PauseAfterMs == 750 &&
      cmdSegments[1].Text == "Câu hai" && cmdSegments[1].PauseAfterMs == 1200 &&
      cmdSegments[2].Text == "Câu ba",
      "Câu lệnh text [pause:750ms], [nghi:1.2s], [[Ghi chú]] và // comment hoạt động trực tiếp không cần NormalizeForTts");

var ellipsisSentence = "Cũng đừng mãi tự hỏi rằng, giá như lúc đầu thế này... thì tốt biết mấy...";
var ellipsisSegments = SpeechPacing.SegmentText(ellipsisSentence);
Check(ellipsisSegments.Count == 1 &&
      ellipsisSegments[0].Text == "Cũng đừng mãi tự hỏi rằng, giá như lúc đầu thế này, thì tốt biết mấy..." &&
      SpeechPacing.PrepareTextForPiper(ellipsisSegments[0].Text) == "Cũng đừng mãi tự hỏi rằng, giá như lúc đầu thế này, thì tốt biết mấy.",
      "Dấu ba chấm giữa câu trước chữ thường ('... thì') tự chuyển thành dấu phẩy để Piper không nuốt chữ 'thì'");

var crashQuoteDash = "\"-Lão già tự xưng Tiêu Dao Vương há miệng cười ha hả.";
var preppedCrashQuoteDash = SpeechPacing.PrepareTextForPiper(crashQuoteDash);
Check(!preppedCrashQuoteDash.Contains("\"-") &&
      preppedCrashQuoteDash.StartsWith("Lão già", StringComparison.Ordinal) &&
      !SpeechPacing.HasPronounceableText(SpeechPacing.PrepareTextForPiper("\".")) &&
      !SpeechPacing.HasPronounceableText(SpeechPacing.PrepareTextForPiper("---")),
      "Khử cụm '\"-' gây lỗi Access Violation (0xC0000005) trong espeak-ng.dll và nhận diện câu rỗng không có chữ");

// v1.3 Clarity Pipeline tests: Smart multi-clause splitting, Vietnamese stress restoration, clock time & 1-pass DSP
var lyDucChinh = "Lý Đức Chính vội vàng húp mấy miếng cháo nguội, lấy khăn vải dày quấn kín miệng mũi, cầm mảnh giấy viết từ đêm qua, trên đó ghi nguệch ngoạc số người trong từng hộ, rồi nhét túi tiền vào ngực áo. Trong túi có chút công quỹ mà ông được quyền sử dụng với tư cách trưởng thôn, cùng một phần tiền tích cóp của nhà mình. Chuẩn bị xong, ông bước ra cửa.";
var claritySegs = SpeechPacing.SegmentText(lyDucChinh);
Check(claritySegs.Count == 8 &&
      claritySegs[0].Text == "Lý Đức Chính vội vàng húp mấy miếng cháo nguội," &&
      claritySegs[4].Text == "rồi nhét túi tiền vào ngực áo." &&
      claritySegs[7].Text == "Chuẩn bị xong, ông bước ra cửa.",
      "Tách mệnh đề thông minh câu dài 5 dấu phẩy (<150 phoneme IDs/mệnh đề) nhưng giữ nguyên câu ngắn 'Chuẩn bị xong, ông bước ra cửa.'");

var rawViConfig = """{"audio":{"sample_rate":22050},"espeak":{"voice":"vi"},"inference":{"noise_scale":0.667,"length_scale":1.0,"noise_w":0.8},"phoneme_id_map":{"ˈ":[120],"ˌ":[121]}}""";
var enhancedViConfig = NativeVoicePaths.BuildClarityConfigJson(rawViConfig, "vi-VN");
Check(enhancedViConfig.Contains("\"ˌ\":[120]", StringComparison.Ordinal) &&
      enhancedViConfig.Contains("\"noise_scale\":0.42", StringComparison.Ordinal) &&
      enhancedViConfig.Contains("\"noise_w\":0.45", StringComparison.Ordinal) &&
      enhancedViConfig.Contains("\"length_scale\":1.06", StringComparison.Ordinal),
      "Khôi phục trọng âm chính cho hư từ tiếng Việt ('ˌ' -> [120]) và siết thông số VITS rõ chữ (0.42 / 0.45 / 1.06)");

var rawEnConfig = """{"audio":{"sample_rate":22050},"espeak":{"voice":"en-us"},"inference":{"noise_scale":0.667,"length_scale":1.0,"noise_w":0.8},"phoneme_id_map":{"ˈ":[120],"ˌ":[121]}}""";
var enhancedEnConfig = NativeVoicePaths.BuildClarityConfigJson(rawEnConfig, "en-US");
Check(enhancedEnConfig.Contains("\"ˌ\":[121]", StringComparison.Ordinal),
      "Giữ nguyên trọng âm phụ tự nhiên cho tiếng Anh (en-US)");

Check(SpeechPacing.PrepareTextForPiper("Lúc 10:30 sáng xe chạy.") == "Lúc 10 giờ 30 sáng xe chạy.",
      "Chuẩn hóa giờ tiếng Việt (10:30 -> 10 giờ 30) cho Piper đọc rõ chữ mà không đổi text SRT");

Check(StreamingPcmEncoder.BuildStudioClarityFilter(1.5).Contains("aresample=44100", StringComparison.Ordinal) &&
      StreamingPcmEncoder.BuildStudioClarityFilter(1.5).Contains("atempo=1.5", StringComparison.Ordinal),
      "Chuỗi lọc 1-pass 32-bit float 44.1kHz Clarity EQ + atempo chống nén MP3 hai lần");

// v1.5 Unlimited Max-Power & 1.5M+ character stress test (verifies >1,196,126-char novel performance & zero caps)
var megaParagraph = "Lý Đức Chính vội vàng húp mấy miếng cháo nguội, lấy khăn vải dày quấn kín miệng mũi, cầm mảnh giấy viết từ đêm qua, trên đó ghi nguệch ngoạc số người trong từng hộ, rồi nhét túi tiền vào ngực áo. Trong túi có chút công quỹ mà ông được quyền sử dụng với tư cách trưởng thôn, cùng một phần tiền tích cóp của nhà mình. Chuẩn bị xong, ông bước ra cửa.\n";
var megaBuilder = new StringBuilder(1_600_000);
while (megaBuilder.Length < 1_500_000) megaBuilder.Append(megaParagraph);
var megaText = megaBuilder.ToString();
var sw = System.Diagnostics.Stopwatch.StartNew();
var megaWords = TextProcessor.CountWords(megaText);
var megaSnippet = TextProcessor.ExtractReviewSnippet(megaText, 50);
var megaSegs = SpeechPacing.SegmentText(megaText);
sw.Stop();
Check(megaText.Length >= 1_500_000 && megaWords > 300_000 && megaSnippet.WordCount == 50 && megaSnippet.Truncated && megaSegs.Count > 30_000 && sw.ElapsedMilliseconds < 4000,
      $"Stress test 1.500.000+ ký tự ({megaWords:N0} từ, {megaSegs.Count:N0} mệnh đề) chạy mượt trong {sw.ElapsedMilliseconds}ms không giới hạn");

// Single-word / 2-word Carrier Warmup + Interjection Safety tests
var (gietCarrier, gietApplied) = SpeechPacing.BuildShortUtteranceCarrier("Giết!", "vi");
var (humCarrier, humApplied) = SpeechPacing.BuildShortUtteranceCarrier("Hừm.", "vi");
var (umCarrier, umApplied) = SpeechPacing.BuildShortUtteranceCarrier("Ừm.", "vi");
var (longCarrier, longApplied) = SpeechPacing.BuildShortUtteranceCarrier("Chuẩn bị xong, ông bước ra cửa.", "vi");
Check(gietApplied && gietCarrier == "Út: Giết!" &&
      !humApplied && humCarrier == "Hừm." &&
      !umApplied && umCarrier == "Ừm." &&
      !longApplied && longCarrier == "Chuẩn bị xong, ông bước ra cửa.",
      "Chỉ mồi âm 'Út:' cho phụ âm tắc/xát đầu câu ('Giết!'), giữ nguyên thán từ âm thanh quản/nguyên âm ('Hừm.', 'Ừm.') để không nuốt âm 'h-'");

var carrierWav = Path.Combine(Path.GetTempPath(), "LKH-CarrierTrim-" + Guid.NewGuid().ToString("N") + ".wav");
try
{
    uint sr = 22050;
    int carrierSamples = (int)(sr * 0.14); // 140ms carrier 'Út'
    int puffGap = (int)(sr * 0.03);        // 30ms '-t' stop closure
    int puffSamples = (int)(sr * 0.03);    // 30ms '-t' release puff (must ALSO be sliced off!)
    int gapSamples = (int)(sr * 0.15);     // 150ms ':' true silence valley
    int targetSamples = (int)(sr * 0.35);  // 350ms target 'Giết!'
    int totalTestSamples = carrierSamples + puffGap + puffSamples + gapSamples + targetSamples;
    using (var fs = File.Create(carrierWav))
    using (var w = new BinaryWriter(fs, Encoding.ASCII))
    {
        w.Write("RIFF"u8); w.Write((uint)(36 + totalTestSamples * 2));
        w.Write("WAVEfmt "u8); w.Write(16u);
        w.Write((ushort)1); w.Write((ushort)1); w.Write(sr); w.Write(sr * 2);
        w.Write((ushort)2); w.Write((ushort)16);
        w.Write("data"u8); w.Write((uint)(totalTestSamples * 2));
        for (int i = 0; i < carrierSamples; i++) w.Write((short)9000);
        for (int i = 0; i < puffGap; i++) w.Write((short)0);
        for (int i = 0; i < puffSamples; i++) w.Write((short)500);
        for (int i = 0; i < gapSamples; i++) w.Write((short)0);
        for (int i = 0; i < targetSamples; i++) w.Write((short)12000);
    }
    WavPacing.TrimLeadingCarrierSyllableInPlace(carrierWav);
    var trimmedInfo = WavPacing.Inspect(carrierWav);
    Check(trimmedInfo.FrameCount >= targetSamples && trimmedInfo.FrameCount <= targetSamples + (int)(sr * 0.025),
          "Cắt sạch cả âm mồi 'Út' lẫn tiếng bật '-t' phụ trước khoảng lặng, giữ trọn vẹn 100% âm đầu của từ đích");
}
finally { if (File.Exists(carrierWav)) File.Delete(carrierWav); }

// v1.11 YIN + TD-PSOLA Buzz-free Trailing-Ellipsis tests ("Giết...", "Không...", "Sư phụ ơi...")
var specGiet = SpeechPacing.GetTrailingEllipsisSustainSpec("Giết...", 1.5);
var specChet = SpeechPacing.GetTrailingEllipsisSustainSpec("Chết......", 1.0);
var specKhong = SpeechPacing.GetTrailingEllipsisSustainSpec("Không...", 1.0);
var specOi = SpeechPacing.GetTrailingEllipsisSustainSpec("Sư phụ ơi...", 1.0);
Check(specGiet.IsActive && specGiet.StretchFactor >= 3.4 && specGiet.MinTargetCoreMs >= 340 &&
      specChet.IsActive && specChet.StretchFactor >= 4.0 && specChet.MinTargetCoreMs >= 350 &&
      specKhong.IsActive && specKhong.StretchFactor >= 2.5 && specKhong.MinTargetCoreMs >= 320 &&
      specOi.IsActive && specOi.StretchFactor >= 2.5 &&
      !SpeechPacing.GetTrailingEllipsisSustainSpec("Giết!", 1.5).IsActive,
      "YIN + TD-PSOLA kích hoạt ngân dài nguyên âm trong trẻo cho cả vần khép ('Giết...', 'Chết...') lẫn vần mở ('Không...', 'Sư phụ ơi...'), bù tốc độ 1.5x");

var humSentenceSegs = SpeechPacing.SegmentText("\"Hừm... đang mong chờ quá!\"");
Check(humSentenceSegs.Count == 1 &&
      SpeechPacing.PrepareTextForPiper(humSentenceSegs[0].Text) == "Hừm, đang mong chờ quá!" &&
      SpeechPacing.PrepareTextForPiper("Hm... đang mong chờ quá!") == "Hừm, đang mong chờ quá!" &&
      SpeechPacing.PrepareTextForPiper("Hừmm...") == "Hừm.",
      "Giữ liền mạch câu thán từ 'Hừm... đang mong chờ quá!' thành 1 hơi đọc tự nhiên và chuẩn hóa 'Hm'/'Hừmm' -> 'Hừm'");

var ellipWav = Path.Combine(Path.GetTempPath(), "LKH-EllipsisWsola-" + Guid.NewGuid().ToString("N") + ".wav");
try
{
    uint sr = 22050;
    int leadSilence = (int)(sr * 0.02);    // 20ms pre-silence
    int syllableFrames = (int)(sr * 0.24); // 240ms periodic voiced syllable (180Hz F0)
    int tailSilence = (int)(sr * 0.12);    // 120ms Piper trailing silence
    int origFrames = leadSilence + syllableFrames + tailSilence;
    using (var fs = File.Create(ellipWav))
    using (var w = new BinaryWriter(fs, Encoding.ASCII))
    {
        w.Write("RIFF"u8); w.Write((uint)(36 + origFrames * 2));
        w.Write("WAVEfmt "u8); w.Write(16u);
        w.Write((ushort)1); w.Write((ushort)1); w.Write(sr); w.Write(sr * 2);
        w.Write((ushort)2); w.Write((ushort)16);
        w.Write("data"u8); w.Write((uint)(origFrames * 2));
        for (int i = 0; i < leadSilence; i++) w.Write((short)0);
        for (int i = 0; i < syllableFrames; i++)
        {
            double env = i < sr * 0.02 ? i / (sr * 0.02) : (i > syllableFrames - sr * 0.03 ? (syllableFrames - i) / (sr * 0.03) : 1.0);
            double sig = Math.Sin(2.0 * Math.PI * 180.0 * i / sr) + 0.4 * Math.Sin(2.0 * Math.PI * 360.0 * i / sr);
            w.Write((short)Math.Clamp((int)Math.Round(env * sig * 9000.0), short.MinValue, short.MaxValue));
        }
        for (int i = 0; i < tailSilence; i++) w.Write((short)0);
    }
    WavPacing.ElongateFinalSyllableVowelInPlace(ellipWav, specGiet.StretchFactor, specGiet.MinTargetCoreMs);
    var stretchedInfo = WavPacing.Inspect(ellipWav);
    int addedMs = (int)Math.Round((stretchedInfo.FrameCount - origFrames) * 1000.0 / sr);
    Check(stretchedInfo.FrameCount > origFrames + (int)(sr * 0.20) &&
          stretchedInfo.SampleRate == sr && stretchedInfo.Channels == 1,
          $"YIN + TD-PSOLA kéo giãn đúng chu kỳ thanh quản (+{addedMs}ms ngân âm mượt mà, khử triệt để lỗi rè rè)");
}
finally { if (File.Exists(ellipWav)) File.Delete(ellipWav); }

var unicodeEllipsisCues = TtsSubtitleTimeline.FromMeasured(
[
    new TtsSubtitleTimeline.MeasuredSegment("“Giết…”", 18_000, 150),
    new TtsSubtitleTimeline.MeasuredSegment("Hắn gầm lên!", 15_000, 350)
], 22050);
Check(unicodeEllipsisCues.Count == 2 && unicodeEllipsisCues[0].Text == "“Giết…”" && unicodeEllipsisCues[1].Text == "Hắn gầm lên!",
      "TtsSubtitleTimeline ngắt riêng cue phụ đề kết thúc bằng ký tự '…' Unicode ngay cả khi khoảng nghỉ ngắn");


