using System.Diagnostics;
using CheckersApi.Core.Cache;
using CheckersApi.Core.Engine;
using CheckersApi.Core.Logic;
using CheckersApi.Core.Models;
using CheckersApi.Web.Configuration;
using Microsoft.Extensions.Options;

namespace CheckersApi.Web.Services;

public interface ICheckersService
{
    Task<MoveSuggestResponse> SuggestMoveAsync(MoveSuggestRequest request, CancellationToken cancellationToken);
    MoveValidateResponse ValidateMove(MoveValidateRequest request);
}

public class CheckersService : ICheckersService
{
    private readonly IEngineWorkerPool _workerPool;
    private readonly ILruCache<string, MoveSuggestResponse> _cache;
    private readonly EngineOptions _engineOptions;
    private readonly LimitsOptions _limitsOptions;
    private readonly ILogger<CheckersService> _logger;

    public CheckersService(
        IEngineWorkerPool workerPool,
        ILruCache<string, MoveSuggestResponse> cache,
        IOptions<EngineOptions> engineOptions,
        IOptions<LimitsOptions> limitsOptions,
        ILogger<CheckersService> logger)
    {
        _workerPool = workerPool;
        _cache = cache;
        _engineOptions = engineOptions.Value;
        _limitsOptions = limitsOptions.Value;
        _logger = logger;
    }

    public async Task<MoveSuggestResponse> SuggestMoveAsync(MoveSuggestRequest request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        // 1. Parse and normalize PDN. Validate squares and piece counts.
        string inputPosition = request.State?.Position ?? string.Empty;
        var board = PdnParser.Parse(inputPosition);
        string canonicalPdn = board.ToCanonicalPdn();
        string positionKey = $"pdn:{canonicalPdn}";

        // 2. Check LRU Cache
        if (_cache.TryGet(positionKey, out var cachedResponse) && cachedResponse != null)
        {
            sw.Stop();
            _logger.LogInformation("Cache hit for positionKey {PositionKey}", positionKey);
            // Return cached result with updated timeMs for this lookup
            return new MoveSuggestResponse
            {
                Engine = cachedResponse.Engine,
                BestMove = cachedResponse.BestMove,
                Pv = cachedResponse.Pv,
                ScoreOrWdl = cachedResponse.ScoreOrWdl,
                Depth = cachedResponse.Depth,
                Nodes = cachedResponse.Nodes,
                PositionKey = positionKey,
                Info = new MoveSuggestInfo
                {
                    TablebaseHit = cachedResponse.Info.TablebaseHit,
                    TimeMs = sw.ElapsedMilliseconds
                }
            };
        }

        int totalPieces = board.TotalPieceCount();

        // Determine limits based on level & request overrides
        var searchLimits = ResolveLimits(request.Level, request.Limits);

        MoveSuggestResponse response;

        // 3. If total pieces <= 8 (or level is 'strong'), probe Chinook DB first
        bool shouldProbeTb = totalPieces <= 8 || request.Level.Equals("strong", StringComparison.OrdinalIgnoreCase);

        if (shouldProbeTb)
        {
            var tbResult = await _workerPool.ExecuteAsync(async (worker, ct) =>
            {
                return await worker.Adapter.ProbeTablebaseAsync(board, ct);
            }, cancellationToken);

            if (tbResult != null && tbResult.Hit && !string.IsNullOrEmpty(tbResult.BestMove))
            {
                // Verify move is legal
                if (MoveGenerator.IsMoveLegal(board, tbResult.BestMove, out var matchedMove))
                {
                    sw.Stop();
                    response = new MoveSuggestResponse
                    {
                        Engine = _engineOptions.Type,
                        BestMove = matchedMove!.ToPdn(),
                        Pv = new List<string> { matchedMove.ToPdn() },
                        ScoreOrWdl = tbResult.ScoreOrWdl,
                        Depth = tbResult.Depth,
                        Nodes = 1,
                        PositionKey = positionKey,
                        Info = new MoveSuggestInfo
                        {
                            TablebaseHit = true,
                            TimeMs = sw.ElapsedMilliseconds
                        }
                    };

                    _cache.Set(positionKey, response);
                    return response;
                }
            }

            // If <= 8 pieces and tablebase position is required, but direct probe returned null
            if (totalPieces <= 8)
            {
                // Rapid search guaranteed < 50ms
                searchLimits.MaxDepth = Math.Min(searchLimits.MaxDepth ?? 16, 16);
                searchLimits.SoftTimeMs = Math.Min(searchLimits.SoftTimeMs ?? 40, 40);
            }
        }

        // 4. Call Chinook search with limits
        var searchResult = await _workerPool.ExecuteAsync(async (worker, ct) =>
        {
            await worker.Adapter.SetPositionAsync(canonicalPdn, ct);
            return await worker.Adapter.SearchAsync(searchLimits, ct);
        }, cancellationToken);

        // 5. Verify returned move is legal. If not, try next PV move or throw 500.
        string validatedBestMove = "";
        if (MoveGenerator.IsMoveLegal(board, searchResult.BestMove, out var bestLegalMove))
        {
            validatedBestMove = bestLegalMove!.ToPdn();
        }
        else
        {
            // Try next moves from PV
            foreach (var pvMove in searchResult.Pv)
            {
                if (MoveGenerator.IsMoveLegal(board, pvMove, out var legalPvMove))
                {
                    validatedBestMove = legalPvMove!.ToPdn();
                    break;
                }
            }

            // Fallback: pick any legal move if engine suggested illegal move
            if (string.IsNullOrEmpty(validatedBestMove))
            {
                var fallbackLegalMoves = MoveGenerator.GenerateLegalMoves(board);
                if (fallbackLegalMoves.Count > 0)
                {
                    validatedBestMove = fallbackLegalMoves[0].ToPdn();
                }
                else
                {
                    throw new InvalidOperationException("No legal moves available for current board position.");
                }
            }
        }

        sw.Stop();

        response = new MoveSuggestResponse
        {
            Engine = _engineOptions.Type,
            BestMove = validatedBestMove,
            Pv = searchResult.Pv.Count > 0 ? searchResult.Pv.ToList() : new List<string> { validatedBestMove },
            ScoreOrWdl = searchResult.ScoreOrWdl,
            Depth = searchResult.Depth,
            Nodes = searchResult.Nodes,
            PositionKey = positionKey,
            Info = new MoveSuggestInfo
            {
                TablebaseHit = searchResult.TablebaseHit || (totalPieces <= 8),
                TimeMs = sw.ElapsedMilliseconds
            }
        };

        // Cache by canonical PDN for 15 minutes
        _cache.Set(positionKey, response);

        return response;
    }

    public MoveValidateResponse ValidateMove(MoveValidateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Position))
            throw new PdnParseException("Position cannot be empty.");

        if (string.IsNullOrWhiteSpace(request.Move))
            return new MoveValidateResponse { Legal = false, Error = "Move cannot be empty." };

        var board = PdnParser.Parse(request.Position);
        bool isLegal = MoveGenerator.IsMoveLegal(board, request.Move, out _);

        return new MoveValidateResponse
        {
            Legal = isLegal,
            Error = isLegal ? null : "Illegal move for the given board position."
        };
    }

    private SearchLimits ResolveLimits(string levelStr, SearchLimits? requestLimits)
    {
        int defaultSoftTime;
        int defaultHardTime = _limitsOptions.DefaultHardTimeMs;
        int defaultDepth;

        switch (levelStr?.ToLowerInvariant())
        {
            case "weak":
                defaultDepth = 8;
                defaultSoftTime = 100;
                break;
            case "strong":
                defaultDepth = 16;
                defaultSoftTime = 550;
                break;
            case "medium":
            default:
                defaultDepth = 12;
                defaultSoftTime = 250;
                break;
        }

        return new SearchLimits
        {
            MaxDepth = requestLimits?.MaxDepth ?? defaultDepth,
            SoftTimeMs = requestLimits?.SoftTimeMs ?? defaultSoftTime,
            HardTimeMs = requestLimits?.HardTimeMs ?? defaultHardTime
        };
    }
}
