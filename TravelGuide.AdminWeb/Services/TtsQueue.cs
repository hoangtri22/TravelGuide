using System.Collections.Concurrent;
using System.Threading.Channels;

public sealed class TtsQueueOptions
{
    public int Capacity { get; set; } = 200;
    public int MaxRetryCount { get; set; } = 3;
    public int ProcessDelayMs { get; set; } = 250;
}

public sealed record TtsQueueEnqueueRequest(string Text, string? Voice);

public sealed class TtsQueueJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Text { get; init; } = string.Empty;
    public string Voice { get; init; } = "vi-VN";
    public DateTimeOffset EnqueuedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public int RetryCount { get; set; }
    public string Status { get; set; } = "queued";
    public string? LastError { get; set; }
}

public sealed class TtsQueueMetrics
{
    public long Enqueued { get; set; }
    public long Processed { get; set; }
    public long Failed { get; set; }
    public int QueueDepth { get; set; }
}

public interface ITtsQueue
{
    ValueTask<bool> EnqueueAsync(TtsQueueJob job, CancellationToken cancellationToken);
    ValueTask<TtsQueueJob> DequeueAsync(CancellationToken cancellationToken);
    TtsQueueMetrics GetMetricsSnapshot();
    bool TryGetJob(Guid id, out TtsQueueJob? job);
}

public sealed class TtsQueue : ITtsQueue
{
    private readonly Channel<TtsQueueJob> _channel;
    private readonly ConcurrentDictionary<Guid, TtsQueueJob> _jobs = new();
    private long _enqueued;
    private long _processed;
    private long _failed;
    private int _queueDepth;

    public TtsQueue(Microsoft.Extensions.Options.IOptions<TtsQueueOptions> options)
    {
        var capacity = Math.Max(10, options.Value.Capacity);
        var channelOptions = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        };
        _channel = Channel.CreateBounded<TtsQueueJob>(channelOptions);
    }

    public ValueTask<bool> EnqueueAsync(TtsQueueJob job, CancellationToken cancellationToken)
    {
        _jobs[job.Id] = job;
        var accepted = _channel.Writer.TryWrite(job);
        if (accepted)
        {
            Interlocked.Increment(ref _enqueued);
            Interlocked.Increment(ref _queueDepth);
            return ValueTask.FromResult(true);
        }

        job.Status = "dropped";
        job.LastError = "Queue is full";
        Interlocked.Increment(ref _failed);
        return ValueTask.FromResult(false);
    }

    public async ValueTask<TtsQueueJob> DequeueAsync(CancellationToken cancellationToken)
    {
        var job = await _channel.Reader.ReadAsync(cancellationToken);
        Interlocked.Decrement(ref _queueDepth);
        return job;
    }

    public TtsQueueMetrics GetMetricsSnapshot()
        => new()
        {
            Enqueued = Interlocked.Read(ref _enqueued),
            Processed = Interlocked.Read(ref _processed),
            Failed = Interlocked.Read(ref _failed),
            QueueDepth = Volatile.Read(ref _queueDepth)
        };

    public bool TryGetJob(Guid id, out TtsQueueJob? job)
        => _jobs.TryGetValue(id, out job);

    public void MarkStarted(TtsQueueJob job)
    {
        job.Status = "processing";
        job.StartedAtUtc = DateTimeOffset.UtcNow;
    }

    public void MarkDone(TtsQueueJob job)
    {
        job.Status = "done";
        job.CompletedAtUtc = DateTimeOffset.UtcNow;
        Interlocked.Increment(ref _processed);
    }

    public void MarkFailed(TtsQueueJob job, string error)
    {
        job.Status = "failed";
        job.LastError = error;
        job.CompletedAtUtc = DateTimeOffset.UtcNow;
        Interlocked.Increment(ref _failed);
    }
}

public sealed class TtsQueueWorker : BackgroundService
{
    private readonly TtsQueue _queue;
    private readonly TtsQueueOptions _options;
    private readonly ILogger<TtsQueueWorker> _logger;

    public TtsQueueWorker(
        ITtsQueue queue,
        Microsoft.Extensions.Options.IOptions<TtsQueueOptions> options,
        ILogger<TtsQueueWorker> logger)
    {
        _queue = (TtsQueue)queue;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var job = await _queue.DequeueAsync(stoppingToken);
            _queue.MarkStarted(job);

            var done = false;
            for (var attempt = 1; attempt <= Math.Max(1, _options.MaxRetryCount); attempt++)
            {
                try
                {
                    job.RetryCount = attempt - 1;
                    await SimulateTtsCallAsync(job, stoppingToken);
                    _queue.MarkDone(job);
                    done = true;
                    break;
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    job.LastError = ex.Message;
                    _logger.LogWarning(ex, "TTS queue attempt {Attempt} failed for {JobId}", attempt, job.Id);
                }
            }

            if (!done && !stoppingToken.IsCancellationRequested)
            {
                _queue.MarkFailed(job, job.LastError ?? "Unknown failure");
            }
        }
    }

    private async Task SimulateTtsCallAsync(TtsQueueJob job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.Text))
        {
            throw new InvalidOperationException("Text is empty");
        }

        // Local profile: giả lập I/O call để đo queue depth/latency trước khi nối API TTS thật.
        await Task.Delay(Math.Max(10, _options.ProcessDelayMs), cancellationToken);
    }
}
