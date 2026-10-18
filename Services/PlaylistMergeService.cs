using LangKinhHuKhong.TTS.Models;

namespace LangKinhHuKhong.TTS.Services;

/// <summary>Ghép các MP3 của cùng một tab theo thứ tự chọn, lưu thành mục lịch sử mới.</summary>
public sealed class PlaylistMergeService(AudioMergeService audio, SessionOutputService sessions)
{
    public async Task<RenderHistoryEntry> MergeAsync(string sessionId, string secret,
        IReadOnlyList<string> orderedJobIds, string title, CancellationToken cancellationToken)
    {
        title = SessionOutputService.NormalizeTitle(title);
        // Clone danh sách trước khi lock/await, chống UI thay đổi lúc đang ghép.
        var sourceIds = orderedJobIds.ToArray();
        using var lease = sessions.AcquireMergeInputs(sessionId, secret, sourceIds);
        var jobId = Guid.NewGuid().ToString("N");
        var filename = $"LangKinhHuKhong-{DateTime.Now:yyyyMMdd-HHmmss}-{jobId}.mp3";
        var output = sessions.OutputPath(sessionId, filename);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        try
        {
            sessions.EnsureOpen(sessionId, secret);
            await audio.MergeMp3Async(lease.Paths, output, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return sessions.CompleteMerge(sessionId, secret, output, jobId, title, sourceIds.Length);
        }
        catch
        {
            try { if (File.Exists(output)) File.Delete(output); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }
}
