using CheckersApi.Core.Models;

namespace CheckersApi.Core.Engine;

public record EngineSearchResult(
    string BestMove,
    IReadOnlyList<string> Pv,
    int ScoreOrWdl,
    long Nodes,
    int Depth,
    bool TablebaseHit,
    long TimeMs);

public record TablebaseProbeResult(
    bool Hit,
    string BestMove,
    int ScoreOrWdl,
    int Depth,
    long TimeMs);

public interface IEngineAdapter : IDisposable
{
    string EngineName { get; }
    bool IsAlive { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SetPositionAsync(string pdn, CancellationToken cancellationToken = default);
    Task<EngineSearchResult> SearchAsync(SearchLimits limits, CancellationToken cancellationToken = default);
    Task<TablebaseProbeResult?> ProbeTablebaseAsync(BoardPosition board, CancellationToken cancellationToken = default);
}
