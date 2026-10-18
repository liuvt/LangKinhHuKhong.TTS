using LangKinhHuKhong.TTS.Models;

namespace LangKinhHuKhong.TTS.Services;

/// <summary>Không giữ trạng thái công việc toàn cục: mỗi tab có thể chạy song song trên máy LAN.</summary>
public sealed class RenderService(
    NativePathService nativePaths,
    TextSplitterService splitter,
    TtsEngineService engine,
    AudioMergeService audio,
    SessionOutputService sessions)
{
    public const int MaxCharacters = 100_000;

    public async Task<RenderHistoryEntry> RenderAsync(
        string text, VoiceModel voice, double speed, int? speakerId, string sessionId, string sessionSecret,
        IProgress<(int done, int total)>? progress, CancellationToken ct)
    {
        var inputCharacters = splitter.CharacterCount(text);
        if (inputCharacters > MaxCharacters)
            throw new InvalidOperationException("Nội dung vượt quá 100.000 ký tự.");
        text = splitter.NormalizeForTts(text);
        if (!TtsEngineService.AllowedSpeeds.Contains(speed))
            throw new InvalidOperationException("Tốc độ phát không nằm trong danh sách hỗ trợ.");
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Vui lòng nhập văn bản trước khi tạo MP3.");
        sessions.EnsureOpen(sessionId, sessionSecret);
        var chunks = splitter.Split(text);
        if (chunks.Count == 0) throw new InvalidOperationException("Không tìm thấy nội dung hợp lệ.");

        var jobId = Guid.NewGuid().ToString("N");
        var jobDirectory = nativePaths.NewJobDirectory();
        var wavs = new List<string>();
        try
        {
            for (var i = 0; i < chunks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                sessions.EnsureOpen(sessionId, sessionSecret);
                var wav = Path.Combine(jobDirectory, $"chunk_{i + 1:0000}.wav");
                await engine.RenderAsync(chunks[i], voice, wav, speed, speakerId, ct);
                wavs.Add(wav);
                progress?.Report((i + 1, chunks.Count));
            }
            ct.ThrowIfCancellationRequested();
            sessions.EnsureOpen(sessionId, sessionSecret);
            var filename = $"LangKinhHuKhong-{DateTime.Now:yyyyMMdd-HHmmss}-{jobId}.mp3";
            var output = sessions.OutputPath(sessionId, filename);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            try
            {
                await audio.MergeAsync(wavs, output, ct);
                ct.ThrowIfCancellationRequested();
                return sessions.Complete(sessionId, sessionSecret, output, jobId,
                    inputCharacters, voice.Name, speed);
            }
            catch
            {
                try { if (File.Exists(output)) File.Delete(output); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                throw;
            }
        }
        finally
        {
            try { Directory.Delete(jobDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
