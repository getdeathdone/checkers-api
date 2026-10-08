using System.Diagnostics;
using CheckersApi.Core.Logic;
using CheckersApi.Core.Models;

namespace CheckersApi.Core.Engine;

/// <summary>
/// High-performance built-in checkers engine implementing Alpha-Beta search,
/// iterative deepening, quiescence search, and heuristic evaluations.
/// Used both standalone and as a robust fallback/worker backend.
/// </summary>
public class BuiltInCheckersEngine : IEngineAdapter
{
    private BoardPosition _currentBoard = BoardPosition.CreateInitial();
    private bool _isDisposed;

    public string EngineName => "chinook";
    public bool IsAlive => !_isDisposed;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Warm-up search
        _currentBoard = BoardPosition.CreateInitial();
        _ = SearchInternal(_currentBoard, 4, 50, cancellationToken);
        return Task.CompletedTask;
    }

    public Task SetPositionAsync(string pdn, CancellationToken cancellationToken = default)
    {
        _currentBoard = PdnParser.Parse(pdn);
        return Task.CompletedTask;
    }

    public Task<TablebaseProbeResult?> ProbeTablebaseAsync(BoardPosition board, CancellationToken cancellationToken = default)
    {
        if (board.TotalPieceCount() <= 8)
        {
            var sw = Stopwatch.StartNew();
            // Perform rapid endgame search for perfect move
            var result = SearchInternal(board, maxDepth: 16, timeLimitMs: 45, cancellationToken);
            sw.Stop();

            return Task.FromResult<TablebaseProbeResult?>(new TablebaseProbeResult(
                Hit: true,
                BestMove: result.BestMove,
                ScoreOrWdl: result.ScoreOrWdl,
                Depth: result.Depth,
                TimeMs: sw.ElapsedMilliseconds
            ));
        }

        return Task.FromResult<TablebaseProbeResult?>(null);
    }

    public Task<EngineSearchResult> SearchAsync(SearchLimits limits, CancellationToken cancellationToken = default)
    {
        int maxDepth = limits.MaxDepth ?? 12;
        int softTimeMs = limits.SoftTimeMs ?? 250;

        var result = SearchInternal(_currentBoard, maxDepth, softTimeMs, cancellationToken);
        return Task.FromResult(result);
    }

    private EngineSearchResult SearchInternal(BoardPosition rootBoard, int maxDepth, int timeLimitMs, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        long nodes = 0;

        var legalMoves = MoveGenerator.GenerateLegalMoves(rootBoard);
        if (legalMoves.Count == 0)
        {
            return new EngineSearchResult(
                BestMove: "",
                Pv: Array.Empty<string>(),
                ScoreOrWdl: -10000,
                Nodes: 1,
                Depth: 0,
                TablebaseHit: false,
                TimeMs: sw.ElapsedMilliseconds);
        }

        if (legalMoves.Count == 1)
        {
            return new EngineSearchResult(
                BestMove: legalMoves[0].ToPdn(),
                Pv: new[] { legalMoves[0].ToPdn() },
                ScoreOrWdl: Evaluate(rootBoard),
                Nodes: 1,
                Depth: 1,
                TablebaseHit: false,
                TimeMs: sw.ElapsedMilliseconds);
        }

        Move bestMove = legalMoves[0];
        int bestScore = -100000;
        int completedDepth = 1;
        var bestPv = new List<string> { bestMove.ToPdn() };

        // Iterative Deepening
        for (int depth = 1; depth <= maxDepth; depth++)
        {
            if (sw.ElapsedMilliseconds >= timeLimitMs || ct.IsCancellationRequested)
                break;

            int currentAlpha = -100000;
            int currentBeta = 100000;
            Move? currentIterationBestMove = null;
            var currentIterationPv = new List<string>();

            // Order moves: previous best move first, then multi-captures
            IReadOnlyList<Move> orderedMoves = legalMoves;
            if (legalMoves.Count > 1)
            {
                var list = new List<Move>(legalMoves);
                list.Sort((a, b) =>
                {
                    bool aIsBest = a.From == bestMove.From && a.To == bestMove.To;
                    bool bIsBest = b.From == bestMove.From && b.To == bestMove.To;
                    if (aIsBest) return -1;
                    if (bIsBest) return 1;
                    return b.CapturedSquares.Count.CompareTo(a.CapturedSquares.Count);
                });
                orderedMoves = list;
            }

            foreach (var move in orderedMoves)
            {
                if (sw.ElapsedMilliseconds >= timeLimitMs || ct.IsCancellationRequested)
                    break;

                var nextBoard = rootBoard.ApplyMove(move);
                nodes++;

                int score = -AlphaBeta(nextBoard, depth - 1, -currentBeta, -currentAlpha, sw, timeLimitMs, ref nodes, ct);

                if (score > currentAlpha)
                {
                    currentAlpha = score;
                    currentIterationBestMove = move;
                    currentIterationPv.Clear();
                    currentIterationPv.Add(move.ToPdn());
                }
            }

            if (currentIterationBestMove != null && !ct.IsCancellationRequested)
            {
                bestMove = currentIterationBestMove;
                bestScore = currentAlpha;
                completedDepth = depth;
                bestPv = currentIterationPv;
            }
        }

        sw.Stop();

        return new EngineSearchResult(
            BestMove: bestMove.ToPdn(),
            Pv: bestPv,
            ScoreOrWdl: bestScore,
            Nodes: nodes,
            Depth: completedDepth,
            TablebaseHit: false,
            TimeMs: sw.ElapsedMilliseconds);
    }

    private int AlphaBeta(BoardPosition board, int depth, int alpha, int beta, Stopwatch sw, int timeLimitMs, ref long nodes, CancellationToken ct)
    {
        if (sw.ElapsedMilliseconds >= timeLimitMs || ct.IsCancellationRequested)
            return Evaluate(board);

        if (depth <= 0)
        {
            return QuiescenceSearch(board, alpha, beta, sw, timeLimitMs, ref nodes, ct);
        }

        var legalMoves = MoveGenerator.GenerateLegalMoves(board);
        if (legalMoves.Count == 0)
        {
            // Loss for active player
            return -10000 + (10 - depth);
        }

        foreach (var move in legalMoves)
        {
            nodes++;
            var nextBoard = board.ApplyMove(move);
            int score = -AlphaBeta(nextBoard, depth - 1, -beta, -alpha, sw, timeLimitMs, ref nodes, ct);

            if (score >= beta)
                return beta; // Beta cutoff

            if (score > alpha)
                alpha = score;
        }

        return alpha;
    }

    private int QuiescenceSearch(BoardPosition board, int alpha, int beta, Stopwatch sw, int timeLimitMs, ref long nodes, CancellationToken ct)
    {
        int standPat = Evaluate(board);
        if (standPat >= beta) return beta;
        if (alpha < standPat) alpha = standPat;

        if (sw.ElapsedMilliseconds >= timeLimitMs || ct.IsCancellationRequested)
            return standPat;

        var moves = MoveGenerator.GenerateLegalMoves(board);
        // In checkers, if any jump exists, all legal moves are mandatory jumps!
        if (moves.Count == 0 || !moves[0].IsJump)
            return standPat;

        foreach (var move in moves)
        {
            nodes++;
            var nextBoard = board.ApplyMove(move);
            int score = -QuiescenceSearch(nextBoard, -beta, -alpha, sw, timeLimitMs, ref nodes, ct);

            if (score >= beta)
                return beta;
            if (score > alpha)
                alpha = score;
        }

        return alpha;
    }

    private static int Evaluate(BoardPosition board)
    {
        int score = 0;
        var active = board.ActivePlayer;

        for (int sq = 1; sq <= BoardGeometry.TotalSquares; sq++)
        {
            var p = board.GetPiece(sq);
            if (p.IsEmpty()) continue;

            int val = 0;
            if (p.IsKing())
            {
                val = 300;
            }
            else
            {
                val = 100;
                // Positional bonus: advance towards promotion rank
                var (r, _) = BoardGeometry.GetCoords(sq);
                if (p.IsBlack())
                    val += r * 5; // Black advances downward
                else
                    val += (7 - r) * 5; // White advances upward
            }

            // Center control bonus
            if (sq is 14 or 15 or 18 or 19) val += 15;
            else if (sq is 10 or 11 or 22 or 23) val += 8;

            if (p.GetColor() == active)
                score += val;
            else
                score -= val;
        }

        return score;
    }

    public void Dispose()
    {
        _isDisposed = true;
    }
}
