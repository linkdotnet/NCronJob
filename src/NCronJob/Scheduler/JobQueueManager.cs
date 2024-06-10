using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;

namespace NCronJob;

internal sealed class JobQueueManager : IDisposable
{
    private readonly TimeProvider timeProvider;
    private readonly ConcurrentDictionary<string, JobQueue> jobQueues = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> semaphores = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> jobCancellationTokens = new();
    private bool disposed;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event Action<string>? QueueAdded;

    public JobQueueManager(TimeProvider timeProvider) => this.timeProvider = timeProvider;

    public JobQueue GetOrAddQueue(string jobType)
    {
        var isCreating = false;
        var jobQueue = jobQueues.GetOrAdd(jobType, jt =>
        {
            isCreating = true;
            var queue = new JobQueue(timeProvider);
            queue.CollectionChanged += JobQueue_CollectionChanged;
            return queue;
        });

        if (isCreating)
        {
            QueueAdded?.Invoke(jobType);
        }

        return jobQueue;
    }

    private void JobQueue_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => CollectionChanged?.Invoke(sender, e);

    public bool TryGetQueue(string jobType, [MaybeNullWhen(false)] out JobQueue jobQueue) => jobQueues.TryGetValue(jobType, out jobQueue);

    public IEnumerable<string> GetAllJobTypes() => jobQueues.Keys;

    public SemaphoreSlim GetOrAddSemaphore(string jobType, int concurrencyLimit) =>
        semaphores.GetOrAdd(jobType, _ => new SemaphoreSlim(concurrencyLimit));

    public CancellationTokenSource GetOrAddCancellationTokenSource(string jobType)
    {
        lock (jobCancellationTokens)
        {
            if (jobCancellationTokens.TryGetValue(jobType, out var cts))
            {
                if (cts.IsCancellationRequested)
                {
                    cts.Dispose();
                    jobCancellationTokens[jobType] = new CancellationTokenSource();
                }
            }
            else
            {
                jobCancellationTokens[jobType] = new CancellationTokenSource();
            }

            return jobCancellationTokens[jobType];
        }
    }

    public void SignalJobQueue(string jobType)
    {
        lock (jobCancellationTokens)
        {
            if (jobCancellationTokens.TryGetValue(jobType, out var cts))
            {
                cts.Cancel();
                Task.Delay(10).Wait();
                jobCancellationTokens[jobType] = new CancellationTokenSource();
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        foreach (var jobQueue in jobQueues.Values)
        {
            jobQueue.CollectionChanged -= JobQueue_CollectionChanged;
        }

        foreach (var semaphore in semaphores.Values)
        {
            semaphore.Dispose();
        }

        foreach (var cts in jobCancellationTokens.Values)
        {
            cts.Dispose();
        }
        jobQueues.Clear();
        semaphores.Clear();
        jobCancellationTokens.Clear();

        disposed = true;
    }
}