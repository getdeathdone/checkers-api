using CheckersApi.Core.Engine;

namespace CheckersApi.Web.Services;

public class WorkerPoolWarmupService : IHostedService
{
    private readonly IEngineWorkerPool _workerPool;
    private readonly ILogger<WorkerPoolWarmupService> _logger;

    public WorkerPoolWarmupService(IEngineWorkerPool workerPool, ILogger<WorkerPoolWarmupService> logger)
    {
        _workerPool = workerPool;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Warming up {Count} Chinook engine workers...", _workerPool.WorkerCount);
        try
        {
            await _workerPool.InitializeAsync(cancellationToken);
            _logger.LogInformation("All Chinook engine workers initialized successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error warming up Chinook engine workers.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
