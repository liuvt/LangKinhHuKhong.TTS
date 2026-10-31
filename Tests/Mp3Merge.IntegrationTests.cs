using LangKinhHuKhong.TTS.Core;

// CLI: dotnet run --project Tests/Mp3Merge.IntegrationTests.csproj -- "C:\path\ffmpeg.exe"
var ffmpeg = args.Length > 0 ? args[0] : "ffmpeg";
var ffprobe = args.Length > 0 ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "ffprobe.exe") : "ffprobe";
var dir = Path.Combine(Path.GetTempPath(), "LKH-Merge-Integration-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
var ct = CancellationToken.None;
try
{
    var first = Path.Combine(dir, "chương một.mp3");
    var second = Path.Combine(dir, "chương hai.mp3");
    await Mp3StreamCopy.RunAsync(ffmpeg, dir, ct, "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi", "-i", "sine=frequency=440:duration=1.25:sample_rate=22050",
        "-ac", "1", "-c:a", "libmp3lame", "-b:a", "96k", first);
    await Mp3StreamCopy.RunAsync(ffmpeg, dir, ct, "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi", "-i", "sine=frequency=660:duration=1.5:sample_rate=22050",
        "-ac", "1", "-c:a", "libmp3lame", "-b:a", "96k", second);
    string firstSrt = Path.ChangeExtension(first, ".srt");
    string secondSrt = Path.ChangeExtension(second, ".srt");
    await SubtitleSrt.SaveAsync(firstSrt, [new SubtitleCue(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(900), "Câu đầu tiên.")]);
    await SubtitleSrt.SaveAsync(secondSrt, [new SubtitleCue(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1), "Câu thứ hai.")]);
    string output = Path.Combine(dir, "ghép nguyên bản.mp3");
    await SrtAudioMerger.MergeAsync([new(first, firstSrt), new(second, secondSrt)], output, ffmpeg, ffprobe);
    var srcPackets1 = await PacketHashes(first);
    var srcPackets2 = await PacketHashes(second);
    var mergedPackets = await PacketHashes(output);
    Assert(mergedPackets.SequenceEqual(srcPackets1.Concat(srcPackets2)),
        "PASS: tất cả packet MP3 gốc được copy nguyên vẹn theo đúng thứ tự");
    var format = await Mp3StreamCopy.InspectAsync(ffprobe, output, ct);
    Assert(format.SampleRate == 22050 && format.Channels == 1,
        "PASS: giữ nguyên 22050 Hz và mono (không đổi sang 48000 Hz)");
    var subtitles = await SubtitleSrt.LoadAsync(Path.ChangeExtension(output, ".srt"), ct);
    var durationFirst = await Mp3StreamCopy.ProbeDurationAsync(ffprobe, first, ct);
    Assert(subtitles.Count == 2 && subtitles[0].Text == "Câu đầu tiên." && subtitles[1].Text == "Câu thứ hai." &&
           Math.Abs(subtitles[1].Start.TotalSeconds - (durationFirst + 0.1)) < 0.015,
           "PASS: SRT gốc giữ nguyên nội dung, cộng đúng offset");
    string outputTempo = Path.Combine(dir, "ghép đổi tốc độ.mp3");
    await SrtAudioMerger.MergeAsync([new(first, firstSrt, 1.25), new(second, secondSrt, 1)],
        outputTempo, ffmpeg, ffprobe);
    var sped = await Mp3StreamCopy.InspectAsync(ffprobe, outputTempo, ct);
    Assert(sped.SampleRate == 22050 && sped.Channels == 1,
        "PASS: chế độ đổi tốc độ không tự ép 48 kHz");
    Assert((await SubtitleSrt.LoadAsync(Path.ChangeExtension(outputTempo, ".srt"), ct)).Count == 2,
        "PASS: chỉnh tốc độ vẫn tạo hai phụ đề đồng bộ");
    Console.WriteLine("OK: MP3 No-Reencode + SRT integration tests.");

    async Task<List<string>> PacketHashes(string path)
    {
        string data = await Mp3StreamCopy.RunAsync(ffprobe, dir, ct,
            "-v", "error", "-select_streams", "a:0", "-show_packets",
            "-show_data_hash", "sha256", "-show_entries", "packet=data_hash",
            "-of", "default=noprint_wrappers=1:nokey=1", path);
        return data.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase)).ToList();
    }
    static void Assert(bool ok, string message)
    {
        if (!ok) throw new Exception("FAILED: " + message);
        Console.WriteLine(message);
    }
}
finally
{
    try { Directory.Delete(dir, recursive: true); }
    catch (IOException) { }
}
