using LangKinhHuKhong.TTS.Models;

namespace LangKinhHuKhong.TTS.Core;

public enum RenderJobState
{
    Queued,
    Running,
    Completed,
    Canceled,
    Failed
}

public sealed class RenderJobItem
{
    internal readonly CancellationTokenSource Cts = new();
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public int SequenceNumber { get; init; }
    public string Title { get; set; } = "";
    public string Content { get; init; } = "";
    public VoiceModel Voice { get; init; } = null!;
    public int? SpeakerId { get; init; }
    public double Speed { get; init; } = 1.0;
    public bool CreateSrt { get; init; } = true;
    public bool UseCuda { get; init; }
    public int Characters => Content.Length;
    public RenderJobState State { get; set; } = RenderJobState.Queued;
    public int DoneSteps { get; set; }
    public int TotalSteps { get; set; }
    public int Percent { get; set; }
    public DateTime CreatedLocal { get; init; } = DateTime.Now;
    public DateTime? StartedLocal { get; set; }
    public DateTime? FinishedLocal { get; set; }
    public AudioEntry? OutputEntry { get; set; }
    public string? ErrorMessage { get; set; }

    public TimeSpan Elapsed => (FinishedLocal ?? DateTime.Now) - (StartedLocal ?? CreatedLocal);
}

public sealed record RenderJobSnapshot(
    string Id,
    int SequenceNumber,
    string Title,
    string VoiceName,
    double Speed,
    bool CreateSrt,
    int Characters,
    RenderJobState State,
    int DoneSteps,
    int TotalSteps,
    int Percent,
    TimeSpan Elapsed,
    string? OutputMp3Path,
    string? ErrorMessage);

/// <summary>
/// Manages concurrent parallel audio rendering with an UNLIMITED max-power mode (MaxConcurrency = 0)
/// as well as any user-specified worker count (1..512+), with live progress and adaptive intra-job worker scaling.
/// </summary>
public sealed class RenderQueueManager : IDisposable
{
    public const int UnlimitedConcurrency = 0;
    private readonly object _gate = new();
    private readonly JobManager _jobManager;
    private readonly List<RenderJobItem> _items = [];
    private int _maxConcurrency; // 0 = Unlimited queue + 100% full-CPU intra-job batch saturation
    private int _runningCount;
    private int _activeHelpers;
    private int _sequenceCounter;
    private long _lastProgressNotifyTicks;
    private bool _disposed;

    public event Action? StateChanged;
    public event Action<RenderJobSnapshot, AudioEntry>? JobCompleted;
    public event Action<RenderJobSnapshot, string>? JobFailed;

    public RenderQueueManager(JobManager jobManager, int initialMaxConcurrency = UnlimitedConcurrency)
    {
        _jobManager = jobManager;
        _maxConcurrency = initialMaxConcurrency <= 0 ? UnlimitedConcurrency : initialMaxConcurrency;
    }

    public int MaxConcurrency
    {
        get { lock (_gate) return _maxConcurrency; }
    }

    public bool IsUnlimited
    {
        get { lock (_gate) return _maxConcurrency <= 0; }
    }

    public static int FullHardwareWorkerCount => Math.Max(Environment.ProcessorCount, 4);

    /// <summary>Total active Piper synthesis sessions currently running across all primary jobs + intra-job helpers.</summary>
    public int ActiveWorkerProcesses
    {
        get { lock (_gate) return _runningCount + _activeHelpers; }
    }

    public string ModeDisplayLabel
    {
        get
        {
            lock (_gate)
                return _maxConcurrency <= 0
                    ? $"Tối đa công suất (Không giới hạn · Full {FullHardwareWorkerCount} nhân)"
                    : $"{_maxConcurrency} luồng song song";
        }
    }

    public void SetMaxConcurrency(int maxConcurrency)
    {
        int normalized = maxConcurrency <= 0 ? UnlimitedConcurrency : maxConcurrency;
        lock (_gate)
        {
            if (_maxConcurrency == normalized) return;
            _maxConcurrency = normalized;
            ScheduleUnlocked();
        }
        NotifyStateChanged(force: true);
    }

    public (int Running, int Queued, int Completed, int Total, int AverageActivePercent) GetSummary()
    {
        lock (_gate)
        {
            int running = 0, queued = 0, completed = 0, pctSum = 0, activeDenom = 0;
            foreach (var item in _items)
            {
                switch (item.State)
                {
                    case RenderJobState.Running:
                        running++;
                        pctSum += item.Percent;
                        activeDenom++;
                        break;
                    case RenderJobState.Queued:
                        queued++;
                        activeDenom++;
                        break;
                    case RenderJobState.Completed:
                        completed++;
                        break;
                }
            }
            int avg = activeDenom > 0 ? pctSum / activeDenom : 0;
            return (running, queued, completed, _items.Count, avg);
        }
    }

    public int ActiveCount
    {
        get
        {
            lock (_gate)
                return _items.Count(x => x.State is RenderJobState.Running or RenderJobState.Queued);
        }
    }

    public IReadOnlyList<RenderJobSnapshot> Snapshot()
    {
        lock (_gate)
            return _items.Select(ToSnapshot).ToArray();
    }

    public RenderJobSnapshot Enqueue(
        string content,
        VoiceModel voice,
        double speed,
        int? speakerId,
        bool createSrt,
        bool useCuda,
        string? customTitle = null)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Nội dung kịch bản trống.");

        RenderJobItem job;
        RenderJobSnapshot snap;
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RenderQueueManager));
            int seq = ++_sequenceCounter;
            var resolvedTitle = JobManager.ResolveDisplayTitle(customTitle, content);
            job = new RenderJobItem
            {
                SequenceNumber = seq,
                Title = resolvedTitle,
                Content = content,
                Voice = voice,
                SpeakerId = speakerId,
                Speed = speed,
                CreateSrt = createSrt,
                UseCuda = useCuda
            };
            _items.Insert(0, job); // Newest at top of dashboard
            ScheduleUnlocked();
            snap = ToSnapshot(job);
        }
        NotifyStateChanged(force: true);
        return snap;
    }

    public bool CancelJob(string jobId)
    {
        bool changed = false;
        lock (_gate)
        {
            var found = _items.FirstOrDefault(x => x.Id == jobId);
            if (found is null) return false;
            if (found.State == RenderJobState.Queued)
            {
                found.State = RenderJobState.Canceled;
                found.FinishedLocal = DateTime.Now;
                found.Cts.Cancel();
                changed = true;
            }
            else if (found.State == RenderJobState.Running)
            {
                found.Cts.Cancel();
                changed = true;
            }
        }
        if (changed) NotifyStateChanged(force: true);
        return changed;
    }

    public void CancelAll()
    {
        lock (_gate)
        {
            foreach (var item in _items)
            {
                if (item.State == RenderJobState.Queued)
                {
                    item.State = RenderJobState.Canceled;
                    item.FinishedLocal = DateTime.Now;
                    item.Cts.Cancel();
                }
                else if (item.State == RenderJobState.Running)
                {
                    item.Cts.Cancel();
                }
            }
        }
        NotifyStateChanged(force: true);
    }

    public void ClearFinished()
    {
        lock (_gate)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (_items[i].State is RenderJobState.Completed or RenderJobState.Canceled or RenderJobState.Failed)
                {
                    _items[i].Cts.Dispose();
                    _items.RemoveAt(i);
                }
            }
        }
        NotifyStateChanged(force: true);
    }

    private static int ComputeUnlimitedSafeProcessCeiling()
    {
        try
        {
            long totalRam = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (totalRam > 0)
            {
                // ~320 MB headroom per concurrent ONNX+FFmpeg pair prevents Windows pagefile OOM on 100+ file drops
                // while still allowing 16-64+ simultaneous streams on 8-32 GB machines.
                int byRam = (int)(totalRam / (320L * 1024 * 1024));
                return Math.Max(FullHardwareWorkerCount * 3, byRam);
            }
        }
        catch { }
        return Math.Max(FullHardwareWorkerCount * 4, 24);
    }

    private void ScheduleUnlocked()
    {
        if (_disposed) return;
        int limit = _maxConcurrency <= 0 ? ComputeUnlimitedSafeProcessCeiling() : _maxConcurrency;
        // Start oldest queued jobs first (FIFO execution order); in Unlimited mode (0), launches up to full RAM-safe ceiling!
        for (int i = _items.Count - 1; i >= 0 && _runningCount < limit; i--)
        {
            var candidate = _items[i];
            if (candidate.State != RenderJobState.Queued) continue;
            candidate.State = RenderJobState.Running;
            candidate.StartedLocal = DateTime.Now;
            _runningCount++;
            _ = Task.Run(() => ExecuteJobAsync(candidate));
        }
    }

    private bool TryAcquireHelperSlot()
    {
        bool acquired = false;
        lock (_gate)
        {
            if (!_disposed && !_items.Any(x => x.State == RenderJobState.Queued))
            {
                int targetTotalWorkers = _maxConcurrency <= 0 ? FullHardwareWorkerCount : _maxConcurrency;
                if (_runningCount + _activeHelpers < targetTotalWorkers)
                {
                    _activeHelpers++;
                    acquired = true;
                }
            }
        }
        if (acquired) NotifyStateChanged(force: false);
        return acquired;
    }

    private bool TryYieldIfOversubscribed()
    {
        bool yielded = false;
        lock (_gate)
        {
            int targetTotalWorkers = _maxConcurrency <= 0 ? FullHardwareWorkerCount : _maxConcurrency;
            bool hasQueued = _items.Any(x => x.State == RenderJobState.Queued);
            if (_disposed || hasQueued || (_runningCount + _activeHelpers > targetTotalWorkers))
            {
                _activeHelpers = Math.Max(0, _activeHelpers - 1);
                yielded = true;
            }
        }
        if (yielded) NotifyStateChanged(force: false);
        return yielded;
    }

    private void ReleaseHelperSlot()
    {
        lock (_gate)
        {
            _activeHelpers = Math.Max(0, _activeHelpers - 1);
        }
        NotifyStateChanged(force: false);
    }

    private int GetTotalConcurrentWorkers()
    {
        lock (_gate)
            return Math.Max(1, _runningCount + _activeHelpers);
    }

    private async Task ExecuteJobAsync(RenderJobItem job)
    {
        var progress = new Progress<(int done, int total)>(p =>
        {
            lock (_gate)
            {
                job.DoneSteps = p.done;
                job.TotalSteps = p.total;
                job.Percent = Math.Clamp((int)(p.done * 100d / Math.Max(1, p.total)), 0, 99);
            }
            NotifyStateChanged(force: false);
        });

        AudioEntry? completedEntry = null;
        string? failedReason = null;
        RenderJobSnapshot finalSnap;

        try
        {
            var budget = new ResumableRender.ParallelBudget(
                TryAcquireHelperSlot,
                TryYieldIfOversubscribed,
                ReleaseHelperSlot,
                GetTotalConcurrentWorkers);
            var entry = await _jobManager.RenderAsync(
                job.Content,
                job.Voice,
                job.Speed,
                job.SpeakerId,
                progress,
                job.Cts.Token,
                job.CreateSrt,
                job.UseCuda,
                job.Title,
                budget);

            lock (_gate)
            {
                job.OutputEntry = entry;
                job.Title = entry.Title;
                job.Percent = 100;
                job.DoneSteps = job.TotalSteps;
                job.State = RenderJobState.Completed;
                job.FinishedLocal = DateTime.Now;
            }
            completedEntry = entry;
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                job.State = RenderJobState.Canceled;
                job.FinishedLocal = DateTime.Now;
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                job.State = RenderJobState.Failed;
                job.ErrorMessage = ex.Message;
                job.FinishedLocal = DateTime.Now;
            }
            failedReason = ex.Message;
        }
        finally
        {
            lock (_gate)
            {
                _runningCount = Math.Max(0, _runningCount - 1);
                ScheduleUnlocked();
                finalSnap = ToSnapshot(job);
            }
            NotifyStateChanged(force: true);
        }

        if (completedEntry is not null)
            JobCompleted?.Invoke(finalSnap, completedEntry);
        else if (failedReason is not null)
            JobFailed?.Invoke(finalSnap, failedReason);
    }

    private static RenderJobSnapshot ToSnapshot(RenderJobItem item) => new(
        item.Id,
        item.SequenceNumber,
        item.Title,
        item.Voice.Name,
        item.Speed,
        item.CreateSrt,
        item.Characters,
        item.State,
        item.DoneSteps,
        item.TotalSteps,
        item.Percent,
        item.Elapsed,
        item.OutputEntry?.FilePath,
        item.ErrorMessage);

    private void NotifyStateChanged(bool force)
    {
        if (_disposed) return;
        long now = Environment.TickCount64;
        if (!force)
        {
            long prev = Interlocked.Read(ref _lastProgressNotifyTicks);
            if (now - prev < 120) return;
        }
        Interlocked.Exchange(ref _lastProgressNotifyTicks, now);
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_gate)
        {
            foreach (var item in _items)
            {
                try { item.Cts.Cancel(); } catch { }
            }
        }
    }
}
