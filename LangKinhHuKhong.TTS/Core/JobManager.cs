using LangKinhHuKhong.TTS.Models;

namespace LangKinhHuKhong.TTS.Core;

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
