using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LangKinhHuKhong.TTS.Models;

namespace LangKinhHuKhong.TTS.Services;

/// <summary>
/// Mỗi tab sinh ID + khóa bí mật khác nhau. Output chỉ đọc/dọn được khi có cả hai.
/// Đóng tab bằng sendBeacon; dọn quá hạn để dự phòng khi trình duyệt crash/mất mạng.
/// </summary>
public sealed class SessionOutputService(IWebHostEnvironment env, ILogger<SessionOutputService> logger) : BackgroundService
{
    private sealed class State(string secret)
    {
        public readonly object Gate = new();
        public readonly string Secret = secret;
        public readonly List<RenderHistoryEntry> History = [];
        // Chặn xóa MP3 trong khi ffmpeg đang sử dụng nó làm nguồn ghép.
        public readonly Dictionary<string, int> MergeReaders = new(StringComparer.Ordinal);
        public DateTimeOffset LastTouched = DateTimeOffset.UtcNow;
        public bool Closed;
        public DateTimeOffset? LeaveDeadline;
    }

    private readonly ConcurrentDictionary<string, State> _sessions = new(StringComparer.Ordinal);
    private static readonly TimeSpan MaxIdle = TimeSpan.FromMinutes(45);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LeaveGrace = TimeSpan.FromSeconds(12);

    public static bool ValidId(string? id) => id is not null && Regex.IsMatch(id, "^[a-fA-F0-9]{32}$", RegexOptions.CultureInvariant);
    public static bool ValidFilename(string? file) => file is not null &&
        Regex.IsMatch(file, "^LangKinhHuKhong-[0-9]{8}-[0-9]{6}-[a-f0-9]{32}\\.mp3$", RegexOptions.CultureInvariant);

    public void Register(string id, string secret)
    {
        if (!ValidId(id) || !ValidId(secret)) throw new ArgumentException("ID hoặc khóa phiên không hợp lệ.");
        if (!_sessions.TryAdd(id, new State(secret))) throw new InvalidOperationException("Trùng ID phiên làm việc.");
    }

    private static bool SameSecret(string actual, string? provided)
    {
        if (!ValidId(provided)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(provided!));
    }

    public bool Touch(string id, string? secret)
    {
        if (!ValidId(id) || !_sessions.TryGetValue(id, out var state)) return false;
        lock (state.Gate)
        {
            if (state.Closed || !SameSecret(state.Secret, secret)) return false;
            state.LastTouched = DateTimeOffset.UtcNow;
            return true;
        }
    }


    /// <summary>Tiếp tục cùng một tab khi người dùng F5, giữ nguyên toàn bộ MP3.</summary>
    public bool Resume(string id, string? secret)
    {
        if (!ValidId(id) || !_sessions.TryGetValue(id, out var state)) return false;
        lock (state.Gate)
        {
            if (state.Closed || !SameSecret(state.Secret, secret) ||
                (state.LeaveDeadline is DateTimeOffset deadline && deadline < DateTimeOffset.UtcNow)) return false;
            state.LeaveDeadline = null;
            state.LastTouched = DateTimeOffset.UtcNow;
            return true;
        }
    }

    /// <summary>pagehide: chờ ngắn để phân biệt F5 với tab thực sự đã đóng.</summary>
    public bool Leave(string id, string? secret)
    {
        if (!ValidId(id) || !_sessions.TryGetValue(id, out var state)) return false;
        lock (state.Gate)
        {
            if (state.Closed || !SameSecret(state.Secret, secret)) return false;
            state.LeaveDeadline = DateTimeOffset.UtcNow + LeaveGrace;
            return true;
        }
    }

    public void EnsureOpen(string id, string secret)
    {
        if (!Touch(id, secret)) throw new OperationCanceledException("Phiên đã đóng hoặc không hợp lệ.");
    }

    public string OutputPath(string id, string filename)
    {
        if (!ValidId(id) || !ValidFilename(filename)) throw new ArgumentException("ID hoặc tên tệp không hợp lệ.");
        return Path.Combine(env.ContentRootPath, "Output", id, filename);
    }

    public RenderHistoryEntry Complete(string id, string secret, string outputFile, string jobId,
        int characters, string voice, double speed)
    {
        if (!ValidId(jobId)) throw new ArgumentException("Mã công việc không hợp lệ.");
        if (!_sessions.TryGetValue(id, out var state)) throw new OperationCanceledException("Phiên đã kết thúc.");
        lock (state.Gate)
        {
            if (state.Closed || !SameSecret(state.Secret, secret))
            {
                TryDeleteFile(outputFile);
                throw new OperationCanceledException("Tab đã đóng khi đang tạo MP3.");
            }
            var filename = Path.GetFileName(outputFile);
            if (!ValidFilename(filename)) throw new InvalidOperationException("Tên MP3 không hợp lệ.");
            var entry = new RenderHistoryEntry(jobId, filename, DateTimeOffset.Now,
                new FileInfo(outputFile).Length, characters, voice, speed);
            state.History.Insert(0, entry);
            state.LastTouched = DateTimeOffset.UtcNow;
            return entry;
        }
    }

    public IReadOnlyList<RenderHistoryEntry> GetHistory(string id, string secret)
    {
        if (!ValidId(id) || !_sessions.TryGetValue(id, out var state)) return [];
        lock (state.Gate)
        {
            if (state.Closed || !SameSecret(state.Secret, secret)) return [];
            return state.History.ToArray();
        }
    }

    /// <summary>Lấy snapshot nguồn theo thứ tự và giữ khóa đọc logic tới khi ghép xong.</summary>
    public MergeInputLease AcquireMergeInputs(string id, string secret, IReadOnlyList<string> orderedJobIds)
    {
        if (!ValidId(id) || !_sessions.TryGetValue(id, out var state))
            throw new InvalidOperationException("Phiên làm việc đã hết hạn.");
        if (orderedJobIds.Count < 2)
            throw new InvalidOperationException("Hãy chọn ít nhất 2 file MP3 để ghép.");
        if (orderedJobIds.Distinct(StringComparer.Ordinal).Count() != orderedJobIds.Count)
            throw new InvalidOperationException("Không thể chọn trùng một file MP3.");
        lock (state.Gate)
        {
            if (state.Closed || !SameSecret(state.Secret, secret))
                throw new InvalidOperationException("Phiên làm việc không hợp lệ.");
            var sources = new List<string>(orderedJobIds.Count);
            foreach (var jobId in orderedJobIds)
            {
                if (!ValidId(jobId)) throw new InvalidOperationException("Mã MP3 không hợp lệ.");
                var entry = state.History.FirstOrDefault(h => h.JobId == jobId)
                    ?? throw new InvalidOperationException("Có file MP3 đã bị xóa khỏi phiên.");
                var path = OutputPath(id, entry.Filename);
                if (!File.Exists(path)) throw new FileNotFoundException("Có file MP3 nguồn không còn trên máy chủ.", entry.Filename);
                sources.Add(path);
            }
            foreach (var jobId in orderedJobIds)
                state.MergeReaders[jobId] = state.MergeReaders.GetValueOrDefault(jobId) + 1;
            state.LastTouched = DateTimeOffset.UtcNow;
            return new MergeInputLease(sources, () =>
            {
                lock (state.Gate)
                {
                    foreach (var jobId in orderedJobIds)
                    {
                        if (!state.MergeReaders.TryGetValue(jobId, out var readers)) continue;
                        if (readers <= 1) state.MergeReaders.Remove(jobId);
                        else state.MergeReaders[jobId] = readers - 1;
                    }
                }
            });
        }
    }

    public sealed class MergeInputLease(IReadOnlyList<string> paths, Action release) : IDisposable
    {
        public IReadOnlyList<string> Paths { get; } = paths;
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) release();
        }
    }

    /// <summary>Xóa tệp vật lý cùng bản ghi, chỉ trong phiên chủ sở hữu.</summary>
    public void RemoveFile(string id, string secret, string jobId)
    {
        if (!ValidId(id) || !ValidId(jobId) || !_sessions.TryGetValue(id, out var state))
            throw new InvalidOperationException("Không tìm thấy phiên hoặc mã MP3.");
        lock (state.Gate)
        {
            if (state.Closed || !SameSecret(state.Secret, secret))
                throw new InvalidOperationException("Phiên làm việc không hợp lệ.");
            var entry = state.History.FirstOrDefault(h => h.JobId == jobId)
                ?? throw new InvalidOperationException("File MP3 đã được xóa.");
            if (state.MergeReaders.GetValueOrDefault(jobId) > 0)
                throw new InvalidOperationException("File đang dùng để ghép. Vui lòng chờ ghép xong rồi xóa.");
            var path = OutputPath(id, entry.Filename);
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException ex) { throw new InvalidOperationException("Không xóa được MP3. Hãy dừng phát audio rồi thử lại.", ex); }
            catch (UnauthorizedAccessException ex) { throw new InvalidOperationException("Máy chủ không có quyền xóa file MP3.", ex); }
            state.History.Remove(entry);
            state.LastTouched = DateTimeOffset.UtcNow;
        }
    }

    public RenderHistoryEntry CompleteMerge(string id, string secret, string outputFile, string jobId,
        string customTitle, int partCount)
    {
        if (partCount < 2) throw new InvalidOperationException("Cần ít nhất 2 đoạn âm thanh.");
        return AddCompletedMerge(id, secret, outputFile, jobId, NormalizeTitle(customTitle), partCount);
    }

    private RenderHistoryEntry AddCompletedMerge(string id, string secret, string outputFile,
        string jobId, string title, int partCount)
    {
        if (!ValidId(id) || !ValidId(jobId) || !_sessions.TryGetValue(id, out var state))
            throw new OperationCanceledException("Phiên đã đóng.");
        lock (state.Gate)
        {
            if (state.Closed || !SameSecret(state.Secret, secret))
            {
                TryDeleteFile(outputFile);
                throw new OperationCanceledException("Phiên đã đóng trong khi ghép MP3.");
            }
            var filename = Path.GetFileName(outputFile);
            if (!ValidFilename(filename)) throw new InvalidOperationException("Tên tệp MP3 không hợp lệ.");
            var entry = new RenderHistoryEntry(jobId, filename, DateTimeOffset.Now,
                new FileInfo(outputFile).Length, 0, "MP3 đã ghép", 1.0, title, partCount);
            state.History.Insert(0, entry);
            state.LastTouched = DateTimeOffset.UtcNow;
            return entry;
        }
    }

    public static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("Vui lòng đặt tên cho file MP3 ghép.");
        var clean = new string(title.Normalize(NormalizationForm.FormC)
            .Where(c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-' or '.' or '(' or ')').Take(100).ToArray())
            .Trim(' ', '.');
        if (clean.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) clean = clean[..^4].Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(clean)) throw new InvalidOperationException("Tên MP3 phải có chữ hoặc số.");
        // Tên thiết bị Windows là tên dành riêng, ngay cả khi có phần mở rộng.
        var stem = clean.Split('.')[0].Trim();
        if (Regex.IsMatch(stem, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase))
            throw new InvalidOperationException("Tên MP3 trùng tên thiết bị hệ thống Windows.");
        return clean;
    }

    public string? DownloadName(string id, string filename, string? secret)
    {
        if (!ValidId(id) || !ValidFilename(filename) || !_sessions.TryGetValue(id, out var state)) return null;
        lock (state.Gate)
        {
            if (state.Closed || !SameSecret(state.Secret, secret)) return null;
            return state.History.FirstOrDefault(h => h.Filename == filename)?.DisplayName;
        }
    }

    public bool CanDownload(string id, string filename, string? secret)
    {
        if (!ValidId(id) || !ValidFilename(filename) || !_sessions.TryGetValue(id, out var state)) return false;
        lock (state.Gate)
        {
            if (state.Closed || !SameSecret(state.Secret, secret)) return false;
            return state.History.Any(h => h.Filename == filename);
        }
    }

    public bool Close(string id, string? secret)
    {
        if (!ValidId(id) || !_sessions.TryGetValue(id, out var state)) return false;
        lock (state.Gate)
        {
            if (!SameSecret(state.Secret, secret)) return false;
            state.Closed = true;
            state.History.Clear();
        }
        TryDeleteSessionDirectory(id);
        _sessions.TryRemove(id, out _);
        return true;
    }

    private void TryDeleteSessionDirectory(string id)
    {
        try
        {
            var dir = Path.Combine(env.ContentRootPath, "Output", id);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException ex) { logger.LogDebug(ex, "MP3 vẫn còn đang được đọc, sẽ thử dọn lại: {Session}", id); }
        catch (UnauthorizedAccessException ex) { logger.LogWarning(ex, "Không đủ quyền dọn Output phiên {Session}", id); }
    }

    private static void TryDeleteFile(string filename)
    {
        try { if (File.Exists(filename)) File.Delete(filename); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var pair in _sessions)
                {
                    bool expired;
                    lock (pair.Value.Gate)
                    {
                        expired = pair.Value.Closed ||
                            (pair.Value.LeaveDeadline is DateTimeOffset deadline && now >= deadline) ||
                            now - pair.Value.LastTouched > MaxIdle;
                        if (expired) { pair.Value.Closed = true; pair.Value.History.Clear(); }
                    }
                    if (!expired) continue;
                    TryDeleteSessionDirectory(pair.Key);
                    _sessions.TryRemove(pair.Key, out _);
                }

                // File mồ côi sau restart hoặc khi Windows đang giữ khóa lúc close.
                var outputRoot = Path.Combine(env.ContentRootPath, "Output");
                if (!Directory.Exists(outputRoot)) continue;
                foreach (var sessionDir in Directory.EnumerateDirectories(outputRoot))
                {
                    var id = Path.GetFileName(sessionDir);
                    if (!ValidId(id) || _sessions.ContainsKey(id)) continue;
                    try
                    {
                        if (now - Directory.GetLastWriteTimeUtc(sessionDir) > MaxIdle)
                            Directory.Delete(sessionDir, recursive: true);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
