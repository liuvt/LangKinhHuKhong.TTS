using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LangKinhHuKhong.TTS.Models;

namespace LangKinhHuKhong.TTS.Core;

/// <summary>
/// Creates completed, durable MP3/SRT batches. On restart/cancellation or partial manuscript edits,
/// completed batches are reused via content-addressable batch hashing (ElevenLabs Studio style).
/// Keeps only one Piper model loaded. No per-sentence process launch and no 4GiB WAV.
/// </summary>
public static class ResumableRender
{
    public const int BatchSize = 80;
    private sealed record BatchState(string Key, int Index, int Count, long DurationTicks, uint SampleRate);
    public sealed record RenderResult(string Mp3, string? Srt, string CheckpointFolder);

    public static async Task<RenderResult> RunAsync(string content, VoiceModel voice, int? speakerId,
        double speed, bool createSrt, bool useCuda, Storage storage, NativeVoicePaths paths,
        AudioPipeline audio, IProgress<(int done, int total)>? progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("Chưa có văn bản cần đọc.");
        if (!AudioPipeline.SupportedSpeeds.Contains(speed)) throw new InvalidOperationException("Tốc độ không được hỗ trợ.");
        var normalized = TextProcessor.NormalizeForTts(content);
        var chunks = SpeechPacing.SegmentText(normalized);
        if (chunks.Count == 0) throw new InvalidOperationException("Nội dung không có câu hợp lệ để tạo giọng.");
        var modelInfo = new FileInfo(voice.ModelPath);
        var cfgInfo = new FileInfo(voice.ConfigPath);
        var voicePrefix = $"v1.2|{voice.ModelPath}|{modelInfo.Length}|{modelInfo.LastWriteTimeUtc.Ticks}|" +
            $"{cfgInfo.Length}|{cfgInfo.LastWriteTimeUtc.Ticks}|{speakerId}|{createSrt}|{useCuda}|";

        // Incremental hash: avoid allocating another giant copy of the source manuscript.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(voicePrefix + $"{speed}|"));
        for (int pos = 0; pos < normalized.Length;)
        {
            var count = Math.Min(16_384, normalized.Length - pos);
            if (count > 1 && pos + count < normalized.Length && char.IsHighSurrogate(normalized[pos + count - 1]))
                count--;
            hash.AppendData(Encoding.UTF8.GetBytes(normalized, pos, count));
            pos += count;
        }
        var key = Convert.ToHexString(hash.GetHashAndReset());
        // Native Piper on Windows sometimes cannot write to accented paths; use ASCII runtime cache.
        var folder = Path.Combine(paths.Root, "resumable-v11", key[..32]);
        Directory.CreateDirectory(folder);
        var total = chunks.Count;
        var batchCount = (total + BatchSize - 1) / BatchSize;
        var ready = new List<BatchState>(batchCount);
        PersistentPiperSession? session = null;
        try
        {
            for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
            {
                ct.ThrowIfCancellationRequested();
                var from = batchIndex * BatchSize;
                var length = Math.Min(BatchSize, total - from);
                var batchKey = ComputeBatchKey(voicePrefix, chunks, from, length);
                var prefix = Path.Combine(folder, $"batch-{batchIndex:D6}");
                var mp3 = prefix + ".mp3";
                var srt = prefix + ".srt";
                var meta = prefix + ".done.json";
                BatchState? state = null;
                if (File.Exists(meta) && File.Exists(mp3) && new FileInfo(mp3).Length > 500 &&
                    (!createSrt || File.Exists(srt)))
                {
                    try
                    {
                        var candidate = JsonSerializer.Deserialize<BatchState>(await File.ReadAllTextAsync(meta, ct));
                        if (candidate is not null && (candidate.Key == batchKey || candidate.Key == key) &&
                            candidate.Index == batchIndex && candidate.Count == length &&
                            candidate.DurationTicks > 0 && candidate.SampleRate > 0)
                            state = candidate;
                    }
                    catch (JsonException) { }
                }
                if (state is null)
                {
                    if (session is null)
                    {
                        var (model, config) = paths.Prepare(voice);
                        session = await PersistentPiperSession.StartAsync(audio.PiperExe, model, config, useCuda, ct);
                    }
                    foreach (var part in new[] { mp3, srt, meta })
                        if (File.Exists(part)) File.Delete(part);
                    var measures = new List<TtsSubtitleTimeline.MeasuredSegment>();
                    StreamingPcmEncoder? encoder = null;
                    try
                    {
                        for (int j = 0; j < length; j++)
                        {
                            ct.ThrowIfCancellationRequested();
                            var chunk = chunks[from + j];
                            var wav = Path.Combine(folder, "current.wav");
                            try
                            {
                                await session.SynthesizeAsync(chunk.Text, wav, speakerId, ct);
                                encoder ??= new StreamingPcmEncoder(audio.FfmpegExe, mp3, wav);
                                var frames = await encoder.AppendAsync(wav, chunk.PauseAfterMs,
                                    SpeechPacing.InitialPauseMs, ct);
                                if (createSrt) measures.Add(new(chunk.Text, frames, chunk.PauseAfterMs));
                            }
                            finally { if (File.Exists(wav)) File.Delete(wav); }
                            progress?.Report((from + j + 1, total + 1));
                        }
                        if (encoder is null) throw new InvalidOperationException("Không sinh được WAV nào.");
                        await encoder.CompleteAsync(ct);
                        var duration = await audio.GetActualMp3DurationAsync(mp3, ct);
                        if (createSrt)
                        {
                            var cues = TtsSubtitleTimeline.FromMeasured(measures, encoder.SampleRate);
                            var synced = SubtitleSrt.RescaleTimeline(cues, encoder.Duration, duration);
                            await SubtitleSrt.SaveAsync(srt, synced, ct);
                        }
                        state = new BatchState(batchKey, batchIndex, length, duration.Ticks, encoder.SampleRate);
                        var tmp = meta + ".tmp";
                        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(state), ct);
                        File.Move(tmp, meta, overwrite: true); // Commit last after MP3 + SRT both succeeded.
                    }
                    finally { if (encoder is not null) await encoder.DisposeAsync(); }
                }
                ready.Add(state);
                progress?.Report((Math.Min(total, from + length), total + 1));
            }
        }
        finally { if (session is not null) await session.DisposeAsync(); }

        var concat = Path.Combine(folder, "original-1x.mp3");
        var finished = Path.Combine(folder, "finished.mp3");
        var finalSrt = Path.Combine(folder, "finished.srt");
        foreach (var output in new[] { concat, finished, finalSrt })
            if (File.Exists(output)) File.Delete(output);
        var files = ready.Select(x => Path.Combine(folder, $"batch-{x.Index:D6}.mp3")).ToArray();
        if (files.Length == 1) File.Copy(files[0], concat);
        else
        {
            var probe = Path.Combine(Path.GetDirectoryName(audio.FfmpegExe)!, "ffprobe.exe");
            await Mp3StreamCopy.ConcatenateAsync(audio.FfmpegExe, probe, files, concat, folder, ct);
        }
        await audio.ChangeMp3SpeedAsync(concat, finished, speed, ct);
        if (createSrt)
        {
            var timeline = new List<SubtitleCue>();
            long offsetTicks = 0;
            foreach (var part in ready)
            {
                var srt = Path.Combine(folder, $"batch-{part.Index:D6}.srt");
                foreach (var cue in await SubtitleSrt.LoadAsync(srt, ct))
                    timeline.Add(new SubtitleCue(cue.Start + TimeSpan.FromTicks(offsetTicks),
                        cue.End + TimeSpan.FromTicks(offsetTicks), cue.Text));
                offsetTicks += part.DurationTicks;
            }
            var actual = await audio.GetActualMp3DurationAsync(finished, ct);
            var synced = SubtitleSrt.RescaleTimeline(timeline, TimeSpan.FromTicks(offsetTicks), actual);
            await SubtitleSrt.SaveAsync(finalSrt, synced, ct);
        }
        progress?.Report((total + 1, total + 1));
        return new RenderResult(finished, createSrt ? finalSrt : null, folder);
    }

    private static string ComputeBatchKey(string voicePrefix, IReadOnlyList<SpeechPacing.Segment> chunks, int from, int length)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(voicePrefix));
        for (int i = 0; i < length; i++)
        {
            var seg = chunks[from + i];
            hash.AppendData(Encoding.UTF8.GetBytes($"{seg.PauseAfterMs}:{seg.Text}\n"));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
