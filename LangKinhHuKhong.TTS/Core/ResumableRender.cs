using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LangKinhHuKhong.TTS.Models;

namespace LangKinhHuKhong.TTS.Core;

/// <summary>
/// Creates completed, durable MP3/SRT batches. On restart/cancellation or partial manuscript edits,
/// completed batches are reused via content-addressable batch hashing (ElevenLabs Studio style).
/// Thread-safe for multi-stream parallel audio renders AND adaptive intra-job multi-batch workers.
/// </summary>
public static class ResumableRender
{
    public const int BatchSize = 80;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> KeyGates = new(StringComparer.Ordinal);
    private sealed record BatchState(string Key, int Index, int Count, long DurationTicks, uint SampleRate);
    public sealed record ParallelBudget(
        Func<bool> TryAcquireHelper,
        Func<bool> TryYieldIfOversubscribed,
        Action ReleaseHelper,
        Func<int> GetTotalConcurrentWorkers);

    public sealed record RenderResult(string Mp3, string? Srt, string CheckpointFolder);

    public static async Task<RenderResult> RunAsync(string content, VoiceModel voice, int? speakerId,
        double speed, bool createSrt, bool useCuda, Storage storage, NativeVoicePaths paths,
        AudioPipeline audio, IProgress<(int done, int total)>? progress, CancellationToken ct,
        ParallelBudget? budget = null)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("Chưa có văn bản cần đọc.");
        if (!AudioPipeline.SupportedSpeeds.Contains(speed)) throw new InvalidOperationException("Tốc độ không được hỗ trợ.");
        var normalized = TextProcessor.NormalizeForTts(content);
        var chunks = SpeechPacing.SegmentText(normalized);
        if (chunks.Count == 0) throw new InvalidOperationException("Nội dung không có câu hợp lệ để tạo giọng.");
        var modelInfo = new FileInfo(voice.ModelPath);
        var cfgInfo = new FileInfo(voice.ConfigPath);
        var voicePrefix = $"v1.8-clarity|{voice.ModelPath}|{modelInfo.Length}|{modelInfo.LastWriteTimeUtc.Ticks}|" +
            $"{cfgInfo.Length}|{cfgInfo.LastWriteTimeUtc.Ticks}|{speakerId}|{createSrt}|{useCuda}|{speed:0.####}|";

        // Incremental hash: avoid allocating another giant copy of the source manuscript.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(voicePrefix));
        for (int pos = 0; pos < normalized.Length;)
        {
            var count = Math.Min(16_384, normalized.Length - pos);
            if (count > 1 && pos + count < normalized.Length && char.IsHighSurrogate(normalized[pos + count - 1]))
                count--;
            hash.AppendData(Encoding.UTF8.GetBytes(normalized, pos, count));
            pos += count;
        }
        var key = Convert.ToHexString(hash.GetHashAndReset());
        var gate = KeyGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Native Piper on Windows sometimes cannot write to accented paths; use ASCII runtime cache.
            var folder = Path.Combine(paths.Root, "resumable-v12", key[..32]);
            Directory.CreateDirectory(folder);
            var total = chunks.Count;
            var batchCount = (total + BatchSize - 1) / BatchSize;
            var states = new BatchState[batchCount];
            int completedChunks = 0;

            var pendingBatches = new ConcurrentQueue<int>();
            for (int b = 0; b < batchCount; b++)
            {
                var from = b * BatchSize;
                var length = Math.Min(BatchSize, total - from);
                var batchKey = ComputeBatchKey(voicePrefix, chunks, from, length);
                var prefix = Path.Combine(folder, $"batch-{b:D6}");
                var mp3 = prefix + ".mp3";
                var srt = prefix + ".srt";
                var meta = prefix + ".done.json";
                BatchState? cached = null;
                if (File.Exists(meta) && File.Exists(mp3) && new FileInfo(mp3).Length > 500 &&
                    (!createSrt || File.Exists(srt)))
                {
                    try
                    {
                        var candidate = JsonSerializer.Deserialize<BatchState>(await File.ReadAllTextAsync(meta, ct));
                        if (candidate is not null && (candidate.Key == batchKey || candidate.Key == key) &&
                            candidate.Index == b && candidate.Count == length &&
                            candidate.DurationTicks > 0 && candidate.SampleRate > 0)
                            cached = candidate;
                    }
                    catch (JsonException) { }
                }
                if (cached is not null)
                {
                    states[b] = cached;
                    completedChunks += length;
                    progress?.Report((Math.Min(total, completedChunks), total + 1));
                }
                else
                {
                    pendingBatches.Enqueue(b);
                }
            }

            if (!pendingBatches.IsEmpty)
            {
                var (model, config) = paths.Prepare(voice);
                var helperTasks = new List<Task>();
                var helperLock = new object();
                int nextWorkerId = 0;

                int ComputeIntraThreads()
                {
                    int concurrent = budget is not null ? Math.Max(1, budget.GetTotalConcurrentWorkers()) : 1;
                    return concurrent > 1
                        ? Math.Max(1, Environment.ProcessorCount / concurrent)
                        : Math.Max(1, Environment.ProcessorCount);
                }

                void TrySpawnScaleUpHelpers()
                {
                    if (budget is null || ct.IsCancellationRequested) return;
                    while (pendingBatches.Count > 1 && budget.TryAcquireHelper())
                    {
                        int id = Interlocked.Increment(ref nextWorkerId);
                        lock (helperLock)
                        {
                            // Never pass ct to Task.Run here so RunWorkerAsync always enters its try/finally to ReleaseHelper()!
                            helperTasks.Add(Task.Run(() => RunWorkerAsync(id, isHelper: true)));
                        }
                    }
                }

                async Task RunWorkerAsync(int workerId, bool isHelper)
                {
                    bool slotHeld = isHelper;
                    PersistentPiperSession? session = null;
                    var workerTag = $"w{workerId}-{Guid.NewGuid():N}";
                    try
                    {
                        while (pendingBatches.TryDequeue(out int batchIndex))
                        {
                            if (ct.IsCancellationRequested)
                            {
                                pendingBatches.Enqueue(batchIndex);
                                ct.ThrowIfCancellationRequested();
                            }
                            if (isHelper && budget is not null && budget.TryYieldIfOversubscribed())
                            {
                                // Atomically yielded 1 excess helper slot so newly queued audio jobs get dedicated CPU cores without stampeding!
                                slotHeld = false;
                                pendingBatches.Enqueue(batchIndex);
                                break;
                            }

                            var from = batchIndex * BatchSize;
                            var length = Math.Min(BatchSize, total - from);
                            var batchKey = ComputeBatchKey(voicePrefix, chunks, from, length);
                            var prefix = Path.Combine(folder, $"batch-{batchIndex:D6}");
                            var mp3 = prefix + ".mp3";
                            var srt = prefix + ".srt";
                            var meta = prefix + ".done.json";
                            var tmpMp3 = prefix + $".{workerTag}.tmp.mp3";
                            var tmpSrt = prefix + $".{workerTag}.tmp.srt";
                            var wav = Path.Combine(folder, $"current-{batchIndex:D6}-{workerTag}.wav");

                            if (session is null || !session.IsAlive)
                            {
                                if (session is not null) await session.DisposeAsync();
                                session = await PersistentPiperSession.StartAsync(
                                    audio.PiperExe, model, config, useCuda, ct, ComputeIntraThreads());
                            }

                            foreach (var part in new[] { mp3, srt, meta, tmpMp3, tmpSrt })
                                if (File.Exists(part)) File.Delete(part);

                            var measures = new List<TtsSubtitleTimeline.MeasuredSegment>(length);
                            StreamingPcmEncoder? encoder = null;
                            try
                            {
                                for (int j = 0; j < length; j++)
                                {
                                    ct.ThrowIfCancellationRequested();
                                    var chunk = chunks[from + j];
                                    try
                                    {
                                        try
                                        {
                                            await session.SynthesizeAsync(chunk.Text, wav, speakerId, ct, speed);
                                        }
                                        catch (Exception ex) when (ex is InvalidOperationException or IOException && !ct.IsCancellationRequested)
                                        {
                                            await session.DisposeAsync();
                                            session = await PersistentPiperSession.StartAsync(
                                                audio.PiperExe, model, config, useCuda, ct, ComputeIntraThreads());
                                            var fallbackText = SpeechPacing.SanitizeFallbackForPiper(chunk.Text);
                                            await session.SynthesizeAsync(fallbackText, wav, speakerId, ct, speed);
                                        }
                                        encoder ??= new StreamingPcmEncoder(audio.FfmpegExe, tmpMp3, wav, speed);
                                        var frames = await encoder.AppendAsync(wav, chunk.PauseAfterMs,
                                            SpeechPacing.InitialPauseMs, ct);
                                        if (createSrt) measures.Add(new(chunk.Text, frames, chunk.PauseAfterMs));
                                    }
                                    finally { if (File.Exists(wav)) File.Delete(wav); }

                                    var doneNow = Interlocked.Increment(ref completedChunks);
                                    progress?.Report((Math.Min(total, doneNow), total + 1));
                                }
                                if (encoder is null) throw new InvalidOperationException("Không sinh được WAV nào.");
                                await encoder.CompleteAsync(ct);
                                File.Move(tmpMp3, mp3, overwrite: true);

                                var duration = await audio.GetActualMp3DurationAsync(mp3, ct);
                                if (createSrt)
                                {
                                    var cues = TtsSubtitleTimeline.FromMeasured(measures, encoder.SampleRate);
                                    var synced = SubtitleSrt.RescaleTimeline(cues, encoder.Duration, duration);
                                    await SubtitleSrt.SaveAsync(tmpSrt, synced, ct);
                                    File.Move(tmpSrt, srt, overwrite: true);
                                }
                                var batchState = new BatchState(batchKey, batchIndex, length, duration.Ticks, StreamingPcmEncoder.OutputMp3SampleRate);
                                var tmpMeta = meta + $".{workerTag}.tmp";
                                await File.WriteAllTextAsync(tmpMeta, JsonSerializer.Serialize(batchState), ct);
                                File.Move(tmpMeta, meta, overwrite: true);
                                states[batchIndex] = batchState;
                            }
                            finally
                            {
                                if (encoder is not null) await encoder.DisposeAsync();
                                if (File.Exists(tmpMp3)) try { File.Delete(tmpMp3); } catch { }
                                if (File.Exists(tmpSrt)) try { File.Delete(tmpSrt); } catch { }
                            }

                            // Dynamically scale UP intra-job helpers if another parallel job just finished and freed CPU cores!
                            TrySpawnScaleUpHelpers();
                        }
                    }
                    finally
                    {
                        if (session is not null) await session.DisposeAsync();
                        if (slotHeld && budget is not null)
                            budget.ReleaseHelper();
                    }
                }

                // Acquire initial full-CPU helper slots for multi-batch manuscripts
                TrySpawnScaleUpHelpers();
                await RunWorkerAsync(0, isHelper: false);

                while (true)
                {
                    Task[] snapshot;
                    lock (helperLock) snapshot = helperTasks.Where(t => !t.IsCompleted).ToArray();
                    if (snapshot.Length == 0) break;
                    await Task.WhenAll(snapshot);
                }

                // Propagate any fault/cancellation from helpers
                Task[] allHelpers;
                lock (helperLock) allHelpers = helperTasks.ToArray();
                if (allHelpers.Length > 0) await Task.WhenAll(allHelpers);

                // Ensure any batch re-enqueued by yielding helpers is finished by primary flow
                if (!pendingBatches.IsEmpty)
                    await RunWorkerAsync(99, isHelper: false);
            }

            var ready = states.OrderBy(x => x.Index).ToList();
            var stageFolder = Path.Combine(paths.Root, "jobs", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stageFolder);
            var finished = Path.Combine(stageFolder, "finished.mp3");
            var finalSrt = Path.Combine(stageFolder, "finished.srt");

            var files = ready.Select(x => Path.Combine(folder, $"batch-{x.Index:D6}.mp3")).ToArray();
            if (files.Length == 1) File.Copy(files[0], finished, overwrite: true);
            else
            {
                var probe = Path.Combine(Path.GetDirectoryName(audio.FfmpegExe)!, "ffprobe.exe");
                // Pass pre-verified batch metadata to eliminate spawning hundreds of ffprobe processes on 1M+ char books!
                var preInspected = ready.Select(x => new Mp3StreamCopy.Mp3Info(
                    (int)x.SampleRate, 1, Math.Max(0.01, TimeSpan.FromTicks(x.DurationTicks).TotalSeconds))).ToArray();
                await Mp3StreamCopy.ConcatenateAsync(audio.FfmpegExe, probe, files, finished, stageFolder, ct, preInspected);
            }
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
            // Remove batch cache after staging into isolated per-invocation stageFolder
            AudioPipeline.SafeDeleteDirectory(folder);
            progress?.Report((total + 1, total + 1));
            return new RenderResult(finished, createSrt ? finalSrt : null, stageFolder);
        }
        finally
        {
            gate.Release();
        }
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
