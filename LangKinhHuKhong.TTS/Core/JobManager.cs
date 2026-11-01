using System.Text;
using LangKinhHuKhong.TTS.Models;

namespace LangKinhHuKhong.TTS.Core;

public sealed record ReviewSnippetResult(string Mp3Path, int WordCount, bool Truncated, string ExtractedText);

public sealed class JobManager
{
    private readonly Storage _storage;
    private readonly NativeVoicePaths _paths;
    private readonly AudioPipeline _audio;

    public JobManager(Storage storage, NativeVoicePaths paths, AudioPipeline audio)
    { _storage = storage; _paths = paths; _audio = audio; }

    public async Task<AudioEntry> RenderAsync(string content, VoiceModel voice, double speed,
        int? speakerId, IProgress<(int done, int total)>? progress, CancellationToken ct,
        bool createSrt = true, bool useCuda = false)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("Chưa có văn bản cần đọc.");

        var dest = _storage.NewOutputPath();
        var srtDest = Path.ChangeExtension(dest, ".srt");
        ResumableRender.RenderResult? result = null;
        try
        {
            result = await ResumableRender.RunAsync(content, voice, speakerId, speed, createSrt,
                useCuda, _storage, _paths, _audio, progress, ct);
            ct.ThrowIfCancellationRequested();
            File.Copy(result.Mp3, dest, overwrite: false);
            if (createSrt && result.Srt is not null) File.Copy(result.Srt, srtDest, overwrite: false);
            var entry = new AudioEntry
            {
                FilePath = dest, Title = $"Bản đọc {DateTime.Now:dd-MM HH:mm}",
                Type = createSrt ? "Render + SRT" : "Render", Voice = voice.Name,
                Speed = speed, Language = voice.Language, Characters = content.Length
            };
            _storage.Add(entry);
            // Clear checkpoint only after final MP3/SRT successfully recorded in library.
            AudioPipeline.SafeDeleteDirectory(result.CheckpointFolder);
            return entry;
        }
        catch
        {
            if (File.Exists(dest)) File.Delete(dest);
            if (File.Exists(srtDest)) File.Delete(srtDest);
            // Keep completed checkpoint batches when render cancelled/crashed.
            throw;
        }
    }

    /// <summary>
    /// Renders a word-limited snippet of the current text into a temporary preview MP3 for quick studio review.
    /// Does not clutter the permanent MP3 library.
    /// </summary>
    public async Task<ReviewSnippetResult> RenderReviewSnippetAsync(
        string content, int maxWords, VoiceModel voice, double speed,
        int? speakerId, bool useCuda, IProgress<(int done, int total)>? progress, CancellationToken ct)
    {
        var (snippet, wordCount, truncated) = TextProcessor.ExtractReviewSnippet(content, maxWords);
        if (string.IsNullOrWhiteSpace(snippet) || wordCount == 0)
            throw new InvalidOperationException("Chưa có nội dung hợp lệ để nghe thử (review).");

        var previewDir = Path.Combine(_paths.Root, "preview");
        Directory.CreateDirectory(previewDir);
        var previewMp3 = Path.Combine(previewDir, $"review-{Guid.NewGuid():N}.mp3");

        ResumableRender.RenderResult? result = null;
        try
        {
            result = await ResumableRender.RunAsync(
                snippet, voice, speakerId, speed, createSrt: false,
                useCuda, _storage, _paths, _audio, progress, ct);
            ct.ThrowIfCancellationRequested();
            File.Copy(result.Mp3, previewMp3, overwrite: true);
            return new ReviewSnippetResult(previewMp3, wordCount, truncated, snippet);
        }
        finally
        {
            if (result is not null)
                AudioPipeline.SafeDeleteDirectory(result.CheckpointFolder);
        }
    }

    /// <summary>
    /// Ensures a sample MP3 exists in Voices/example for the given voice (ElevenLabs voice preview style).
    /// </summary>
    public async Task<string> EnsureVoiceExampleAsync(
        VoiceModel voice, int? speakerId, string? sampleText, double speed,
        bool useCuda, bool forceOverwrite, CancellationToken ct)
    {
        var targetMp3 = VoiceCatalog.GetExampleFilePath(_storage.Settings.VoicesFolder, voice, speakerId);
        if (!forceOverwrite && File.Exists(targetMp3) && new FileInfo(targetMp3).Length > 512)
            return targetMp3;

        var script = string.IsNullOrWhiteSpace(sampleText)
            ? _storage.Settings.SampleText
            : sampleText.Trim();

        var exampleDir = Path.GetDirectoryName(targetMp3)!;
        Directory.CreateDirectory(exampleDir);
        await File.WriteAllTextAsync(Path.Combine(exampleDir, "sample-text.txt"), script, new UTF8Encoding(false), ct);

        ResumableRender.RenderResult? result = null;
        try
        {
            result = await ResumableRender.RunAsync(
                script, voice, speakerId, speed, createSrt: false,
                useCuda, _storage, _paths, _audio, progress: null, ct);
            ct.ThrowIfCancellationRequested();
            var tmp = targetMp3 + ".tmp";
            File.Copy(result.Mp3, tmp, overwrite: true);
            File.Move(tmp, targetMp3, overwrite: true);
            return targetMp3;
        }
        finally
        {
            if (result is not null)
                AudioPipeline.SafeDeleteDirectory(result.CheckpointFolder);
        }
    }

    /// <summary>
    /// Batch-renders the common sample paragraph for all voices into the Voices/example directory.
    /// </summary>
    public async Task<int> RenderAllVoiceExamplesAsync(
        IReadOnlyList<VoiceModel> voices, string sampleText, double speed,
        bool useCuda, bool forceOverwrite, IProgress<(int done, int total, string voiceName)>? progress,
        CancellationToken ct)
    {
        if (voices.Count == 0)
            throw new InvalidOperationException("Chưa có model giọng nào trong thư mục Voices.");
        if (string.IsNullOrWhiteSpace(sampleText))
            throw new InvalidOperationException("Hãy nhập đoạn văn mẫu chung trước khi tạo mẫu giọng.");

        _storage.Settings.SampleText = sampleText.Trim();
        _storage.SaveSettings();

        int renderedCount = 0;
        for (int i = 0; i < voices.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var voice = voices[i];
            int? defaultSpeaker = voice.NumSpeakers > 1 && voice.Speakers.Count > 0 ? voice.Speakers[0].Id : null;
            progress?.Report((i, voices.Count, voice.Name));
            await EnsureVoiceExampleAsync(voice, defaultSpeaker, sampleText, speed, useCuda, forceOverwrite, ct);
            renderedCount++;
            progress?.Report((i + 1, voices.Count, voice.Name));
        }
        return renderedCount;
    }

    public async Task<AudioEntry> MergeWithSubtitlesAsync(IReadOnlyList<MergeTrack> tracks,
        string title, IProgress<(int done, int total)>? progress, CancellationToken ct)
    {
        if (tracks.Count < 2) throw new InvalidOperationException("Cần ít nhất hai MP3 để ghép.");
        var safeTitle = Storage.SafeName(title);
        var dest = _storage.NewOutputPath(safeTitle);
        var srtDest = Path.ChangeExtension(dest, ".srt");
        try
        {
            var ffmpeg = _audio.FfmpegExe;
            var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
            if (!File.Exists(ffmpeg)) throw new FileNotFoundException("Thiếu ffmpeg.exe trong Runtime.", ffmpeg);
            if (!File.Exists(ffprobe)) throw new FileNotFoundException(
                "Thiếu ffprobe.exe cạnh ffmpeg.exe. Bổ sung ffprobe để đo thời lượng và ghép SRT chính xác.", ffprobe);
            var mergeProgress = new Progress<MergeProgress>(p => progress?.Report((p.Completed, p.Total)));
            await SrtAudioMerger.MergeAsync(tracks, dest, ffmpeg, ffprobe, mergeProgress, ct);
            ct.ThrowIfCancellationRequested();
            var commonSpeed = tracks.Select(t => t.Speed).Distinct().Count() == 1 ? tracks[0].Speed : 0;
            var entry = new AudioEntry
            {
                FilePath = dest, Title = safeTitle, Type = "Ghép + SRT", SourceCount = tracks.Count,
                Voice = "Nhiều đoạn", Speed = commonSpeed
            };
            _storage.Add(entry);
            return entry;
        }
        catch
        {
            if (File.Exists(dest)) File.Delete(dest);
            if (File.Exists(srtDest)) File.Delete(srtDest);
            throw;
        }
    }

    public async Task<AudioEntry> MergeAsync(IReadOnlyList<AudioEntry> selected, string title,
        IProgress<(int done, int total)>? progress, CancellationToken ct)
    {
        if (selected.Count < 2) throw new InvalidOperationException("Cần chọn ít nhất 2 đoạn MP3.");
        if (selected.Select(x => x.Id).Distinct().Count() != selected.Count)
            throw new InvalidOperationException("Một MP3 không được xuất hiện nhiều lần trong lượt ghép.");
        var safeTitle = Storage.SafeName(title);
        var dest = _storage.NewOutputPath(safeTitle);
        try
        {
            await _audio.MergeMp3Async(selected.Select(x => x.FilePath).ToArray(), dest, progress, ct);
            ct.ThrowIfCancellationRequested();
            var entry = new AudioEntry { FilePath = dest, Title = safeTitle, Type = "Ghép",
                SourceCount = selected.Count, Voice = "Nhiều đoạn", Speed = 1 };
            try { _storage.Add(entry); }
            catch { File.Delete(dest); throw; }
            return entry;
        }
        catch { if (File.Exists(dest)) File.Delete(dest); throw; }
    }
}
