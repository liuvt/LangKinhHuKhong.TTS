using System.Globalization;

namespace LangKinhHuKhong.TTS.Core;

public sealed record MergeTrack(string Mp3Path, string SrtPath, double Speed = 1.0);
public sealed record MergeProgress(int Completed, int Total, string Message);

/// <summary>
/// Default 1x: concatenate MP3 compressed frames directly, without decoding/re-encoding.
/// Speed changes: decode once to lossless FLAC segments, concatenate, encode MP3 once at
/// maximum available LAME VBR quality (MP3 cannot be lossless after time stretching).
/// SRT always comes from the original SRT files, not audio transcription.
/// </summary>
public static class SrtAudioMerger
{
    private static readonly double[] Allowed = { 0.5, 1.0, 1.25, 1.5, 2.0 };

    public static async Task MergeAsync(IReadOnlyList<MergeTrack> tracks, string outputMp3,
        string ffmpegPath = "ffmpeg", string ffprobePath = "ffprobe",
        IProgress<MergeProgress>? progress = null, CancellationToken ct = default)
    {
        if (tracks.Count == 0) throw new ArgumentException("Cần chọn ít nhất một MP3.");
        if (!outputMp3.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Đầu ra phải là .mp3.");
        string destMp3 = Path.GetFullPath(outputMp3);
        string destSrt = Path.ChangeExtension(destMp3, ".srt");
        if (File.Exists(destMp3) || File.Exists(destSrt))
            throw new IOException("File đầu ra đã tồn tại, hãy chọn tên khác.");
        if (tracks.Any(x => !Allowed.Any(a => Math.Abs(x.Speed - a) < 0.00001)))
            throw new ArgumentOutOfRangeException(nameof(tracks), "Tốc độ hỗ trợ: 0.5, 1, 1.25, 1.5, 2.");
        foreach (var track in tracks)
        {
            if (!File.Exists(track.Mp3Path)) throw new FileNotFoundException("Không tìm thấy MP3.", track.Mp3Path);
            if (!File.Exists(track.SrtPath)) throw new FileNotFoundException("Không tìm thấy SRT gốc.", track.SrtPath);
            if (string.Equals(Path.GetFullPath(track.Mp3Path), destMp3, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Không được ghi đè MP3 đầu vào.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destMp3)!);
        string work = Path.Combine(Path.GetTempPath(), "LKH-SRTMerge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        bool outputWasCopied = false;
        try
        {
            int n = tracks.Count;
            bool directCopy = tracks.All(t => Math.Abs(t.Speed - 1) < 1e-10);
            var info = new List<Mp3StreamCopy.Mp3Info>(n);
            for (int i = 0; i < n; i++)
            {
                ct.ThrowIfCancellationRequested();
                info.Add(await Mp3StreamCopy.InspectAsync(ffprobePath, tracks[i].Mp3Path, ct));
            }
            if (directCopy) Mp3StreamCopy.EnsureCompatible(info);

            // When speed change is requested, normalize ONLY if input formats differ.
            int targetRate = info.Max(x => x.SampleRate);
            int targetChannels = info.Max(x => x.Channels);
            bool formatMismatch = info.Any(x => x.SampleRate != targetRate || x.Channels != targetChannels);
            var subtitles = new List<SubtitleCue>();
            var pcmParts = new List<string>();
            double runningOffset = 0;
            for (int i = 0; i < n; i++)
            {
                ct.ThrowIfCancellationRequested();
                var track = tracks[i];
                progress?.Report(new MergeProgress(i, n + 1,
                    directCopy ? $"Kiểm tra SRT {i + 1}/{n} · giữ nguyên MP3 (copy)..." :
                    $"Xử lý tốc độ {i + 1}/{n} · chỉ mã hóa MP3 một lần sau cùng..."));
                var cues = await SubtitleSrt.LoadAsync(track.SrtPath, ct);
                if (cues.Count == 0) throw new InvalidDataException("SRT không có phụ đề: " + track.SrtPath);
                double durationBefore = info[i].DurationSeconds;
                double durationAfter = durationBefore;

                if (!directCopy)
                {
                    string part = Path.Combine(work, $"audio-{i + 1:D5}.flac");
                    var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                        "-i", track.Mp3Path, "-map", "0:a:0", "-vn" };
                    if (Math.Abs(track.Speed - 1) > 1e-10)
                        args.AddRange(["-af", "atempo=" + track.Speed.ToString("0.##", CultureInfo.InvariantCulture)]);
                    if (formatMismatch)
                        args.AddRange(["-ar", targetRate.ToString(CultureInfo.InvariantCulture),
                            "-ac", targetChannels.ToString(CultureInfo.InvariantCulture)]);
                    args.AddRange(["-c:a", "flac", part]);
                    await Mp3StreamCopy.RunAsync(ffmpegPath, work, ct, args.ToArray());
                    durationAfter = await Mp3StreamCopy.ProbeDurationAsync(ffprobePath, part, ct);
                    pcmParts.Add(part);
                }
                double ratio = durationAfter / durationBefore;
                foreach (var cue in cues)
                {
                    if (cue.Start.TotalSeconds > durationBefore + 1.0)
                        throw new InvalidDataException("Phụ đề SRT vượt quá thời lượng MP3: " + track.SrtPath);
                    double start = Math.Clamp(cue.Start.TotalSeconds, 0, durationBefore);
                    double end = Math.Clamp(cue.End.TotalSeconds, start, durationBefore);
                    if (end <= start) continue;
                    subtitles.Add(new SubtitleCue(
                        TimeSpan.FromSeconds(runningOffset + start * ratio),
                        TimeSpan.FromSeconds(runningOffset + end * ratio), cue.Text));
                }
                runningOffset += durationAfter;
            }
            string temporaryMp3 = Path.Combine(work, "merged.mp3");
            progress?.Report(new MergeProgress(n, n + 1,
                directCopy ? "Ghép MP3 không giải mã, không nén lại (-c copy)..." :
                "Xuất MP3 chất lượng cao (LAME VBR -q:a 0, mã hóa một lần)..."));

            if (directCopy)
            {
                await Mp3StreamCopy.ConcatenateAsync(ffmpegPath, ffprobePath,
                    tracks.Select(t => t.Mp3Path).ToArray(), temporaryMp3, work, ct, info);
            }
            else
            {
                string concatFile = Path.Combine(work, "flac-list.txt");
                await File.WriteAllLinesAsync(concatFile, pcmParts.Select(Mp3StreamCopy.MakeConcatEntry),
                    new System.Text.UTF8Encoding(false), ct);
                await Mp3StreamCopy.RunAsync(ffmpegPath, work, ct,
                    "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                    "-f", "concat", "-safe", "0", "-i", concatFile, "-map", "0:a:0", "-vn",
                    "-c:a", "libmp3lame", "-q:a", "0", temporaryMp3);
            }
            ct.ThrowIfCancellationRequested();
            string temporarySrt = Path.Combine(work, "merged.srt");
            await SubtitleSrt.SaveAsync(temporarySrt, subtitles, ct);
            ct.ThrowIfCancellationRequested();
            File.Copy(temporaryMp3, destMp3, overwrite: false);
            outputWasCopied = true;
            File.Copy(temporarySrt, destSrt, overwrite: false);
            outputWasCopied = false;
            progress?.Report(new MergeProgress(n + 1, n + 1, directCopy ?
                "Hoàn tất: ghép nguyên bản 1×, không nén lại, SRT đồng bộ." :
                "Hoàn tất: tốc độ mới, MP3 mã hóa một lần chất lượng cao + SRT."));
        }
        catch
        {
            if (outputWasCopied && File.Exists(destMp3)) File.Delete(destMp3);
            throw;
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
