using CheckersApi.Core.Cache;
using CheckersApi.Core.Engine;
using CheckersApi.Core.Models;
using CheckersApi.Web.Configuration;
using CheckersApi.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CheckersApi.Tests;

public class CacheTests
{
    [Fact]
    public void LruCache_CapacityEviction_EvictsOldest()
    {
        var cache = new MemoryLruCache<string, string>(capacity: 2);

        cache.Set("k1", "v1");
        cache.Set("k2", "v2");

        Assert.True(cache.TryGet("k1", out var v1));
        Assert.Equal("v1", v1);

        // Accessing k1 makes k2 the LRU. Adding k3 should evict k2.
        cache.Set("k3", "v3");

        Assert.True(cache.TryGet("k1", out _));
        Assert.True(cache.TryGet("k3", out _));
        Assert.False(cache.TryGet("k2", out _)); // k2 was evicted
    }
}

public class CheckersServiceTests
{
    [Fact]
    public async Task SuggestMove_TablebasePosition_ReturnsFastHit()
    {
        // 4 pieces on board (< 8 pieces)
        string tbPdn = "W:W18,22:B10,14";
        var cache = new MemoryLruCache<string, MoveSuggestResponse>();
        var workerPool = new EngineWorkerPool(2, _ => new BuiltInCheckersEngine());
        await workerPool.InitializeAsync();

        var engineOptions = Options.Create(new EngineOptions { Type = "chinook" });
        var limitsOptions = Options.Create(new LimitsOptions { DefaultSoftTimeMs = 300, DefaultHardTimeMs = 1200 });

        var service = new CheckersService(
            workerPool,
            cache,
            engineOptions,
            limitsOptions,
            NullLogger<CheckersService>.Instance);

        var request = new MoveSuggestRequest
        {
            GameId = "checkers-8x8",
            State = new GameStateInput { Notation = "PDN", Position = tbPdn },
            Level = "strong"
        };

        var response = await service.SuggestMoveAsync(request, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("chinook", response.Engine);
        Assert.False(string.IsNullOrEmpty(response.BestMove));
        Assert.True(response.Info.TablebaseHit);
        Assert.True(response.Info.TimeMs <= 100);
    }

    [Fact]
    public void ValidateMove_ValidAndInvalid_ReturnsCorrectStatus()
    {
        var cache = new MemoryLruCache<string, MoveSuggestResponse>();
        var workerPool = new EngineWorkerPool(1, _ => new BuiltInCheckersEngine());
        var service = new CheckersService(
            workerPool,
            cache,
            Options.Create(new EngineOptions()),
            Options.Create(new LimitsOptions()),
            NullLogger<CheckersService>.Instance);

        var validRes = service.ValidateMove(new MoveValidateRequest
        {
            Position = "B:W18,19,22,25,27,28,30,32:B1,5,6,7,10,12,14,16",
            Move = "14x23" // Mandatory jump
        });
        Assert.True(validRes.Legal);

        var validInitial = service.ValidateMove(new MoveValidateRequest
        {
            Position = "B:W21,22,23,24,25,26,27,28,29,30,31,32:B1,2,3,4,5,6,7,8,9,10,11,12",
            Move = "10-15"
        });
        Assert.True(validInitial.Legal);

        var invalidRes = service.ValidateMove(new MoveValidateRequest
        {
            Position = "B:W18,19,22,25,27,28,30,32:B1,5,6,7,10,12,14,16",
            Move = "1-10" // Impossible move
        });
        Assert.False(invalidRes.Legal);
    }

    [Fact]
    public async Task WorkerPool_ConcurrentExecution_DistributesSafely()
    {
        var workerPool = new EngineWorkerPool(2, _ => new BuiltInCheckersEngine());
        await workerPool.InitializeAsync();

        var tasks = Enumerable.Range(0, 20).Select(i =>
            workerPool.ExecuteAsync((worker, ct) => Task.FromResult(worker.Id))
        ).ToArray();

        var results = await Task.WhenAll(tasks);
        Assert.Equal(20, results.Length);
        Assert.Contains(1, results);
        Assert.Contains(2, results);
    }
}
