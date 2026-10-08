using CheckersApi.Core.Models;

namespace CheckersApi.Core.Engine;

public class EngineWorker : IDisposable
{
    public int Id { get; }
    public IEngineAdapter Adapter { get; }
    public SemaphoreSlim AsyncLock { get; } = new(1, 1);

    public EngineWorker(int id, IEngineAdapter adapter)
    {
        Id = id;
        Adapter = adapter;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await AsyncLock.WaitAsync(cancellationToken);
        try
        {
            await Adapter.InitializeAsync(cancellationToken);
        }
        finally
        {
            AsyncLock.Release();
        }
    }

    public void Dispose()
    {
        Adapter.Dispose();
        AsyncLock.Dispose();
    }
}

public interface IEngineWorkerPool : IDisposable
{
    int WorkerCount { get; }
    bool IsHealthy { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<TResult> ExecuteAsync<TResult>(Func<EngineWorker, CancellationToken, Task<TResult>> action, CancellationToken cancellationToken = default);
}

public class EngineWorkerPool : IEngineWorkerPool
{
    private readonly EngineWorker[] _workers;
    private int _roundRobinCounter;
    private bool _disposed;

    public int WorkerCount => _workers.Length;
    public bool IsHealthy => _workers.Length > 0 && _workers.All(w => w.Adapter.IsAlive);

    public EngineWorkerPool(int workerCount, Func<int, IEngineAdapter> adapterFactory)
    {
        if (workerCount <= 0) workerCount = 2;
        _workers = new EngineWorker[workerCount];

        for (int i = 0; i < workerCount; i++)
        {
            _workers[i] = new EngineWorker(i + 1, adapterFactory(i + 1));
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var tasks = _workers.Select(w => w.InitializeAsync(cancellationToken));
        await Task.WhenAll(tasks);
    }

    public async Task<TResult> ExecuteAsync<TResult>(Func<EngineWorker, CancellationToken, Task<TResult>> action, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(EngineWorkerPool));

        int counter = Interlocked.Increment(ref _roundRobinCounter) & 0x7FFFFFFF;
        int index = counter % _workers.Length;
        var worker = _workers[index];

        await worker.AsyncLock.WaitAsync(cancellationToken);
        try
        {
            return await action(worker, cancellationToken);
        }
        finally
        {
            worker.AsyncLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var worker in _workers)
        {
            worker.Dispose();
        }
    }
}
