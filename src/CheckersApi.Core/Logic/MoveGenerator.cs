using CheckersApi.Core.Models;

namespace CheckersApi.Core.Logic;

public static class MoveGenerator
{
    private static readonly (int dr, int dc)[] AllDirections =
    {
        (-1, -1), // Up-Left
        (-1,  1), // Up-Right
        ( 1, -1), // Down-Left
        ( 1,  1)  // Down-Right
    };

    private static readonly (int dr, int dc)[] BlackManDirections =
    {
        ( 1, -1), // Down-Left
        ( 1,  1)  // Down-Right
    };

    private static readonly (int dr, int dc)[] WhiteManDirections =
    {
        (-1, -1), // Up-Left
        (-1,  1)  // Up-Right
    };

    public static IReadOnlyList<Move> GenerateLegalMoves(BoardPosition board)
    {
        var jumps = new List<Move>();
        var normalMoves = new List<Move>();
        var activeColor = board.ActivePlayer;

        // 1. Check all jumps first
        for (int sq = 1; sq <= BoardGeometry.TotalSquares; sq++)
        {
            var piece = board.GetPiece(sq);
            if (piece.IsEmpty() || piece.GetColor() != activeColor)
                continue;

            var pieceJumps = FindJumpsForPiece(board, sq, piece);
            jumps.AddRange(pieceJumps);
        }

        // In English checkers, jumping is mandatory!
        if (jumps.Count > 0)
        {
            return jumps;
        }

        // 2. If no jumps available, generate normal moves
        for (int sq = 1; sq <= BoardGeometry.TotalSquares; sq++)
        {
            var piece = board.GetPiece(sq);
            if (piece.IsEmpty() || piece.GetColor() != activeColor)
                continue;

            var (r, c) = BoardGeometry.GetCoords(sq);
            var directions = GetDirections(piece);

            foreach (var (dr, dc) in directions)
            {
                int nr = r + dr;
                int nc = c + dc;
                int destSq = BoardGeometry.GetSquare(nr, nc);

                if (destSq > 0 && board.GetPiece(destSq).IsEmpty())
                {
                    normalMoves.Add(new Move(sq, destSq, new[] { sq, destSq }, Array.Empty<int>()));
                }
            }
        }

        return normalMoves.Count > 0 ? normalMoves : Array.Empty<Move>();
    }

    private static (int dr, int dc)[] GetDirections(PieceType piece)
    {
        if (piece.IsKing()) return AllDirections;
        if (piece.IsBlack()) return BlackManDirections;
        return WhiteManDirections;
    }

    private static List<Move> FindJumpsForPiece(BoardPosition board, int startSq, PieceType startPiece)
    {
        var completeJumps = new List<Move>();
        var visitedCaptured = new HashSet<int>();
        var path = new List<int> { startSq };

        void SearchJumps(int currentSq, PieceType currentPiece, BoardPosition currentBoard)
        {
            var (r, c) = BoardGeometry.GetCoords(currentSq);
            var directions = GetDirections(currentPiece);
                foreach (var (dr, dc) in directions)
            {
                int midR = r + dr;
                int midC = c + dc;
                int midSq = BoardGeometry.GetSquare(midR, midC);
                if (midSq == 0 || visitedCaptured.Contains(midSq)) continue;

                var midPiece = currentBoard.GetPiece(midSq);
                if (midPiece.IsEmpty() || midPiece.GetColor() == currentPiece.GetColor())
                    continue;

                int destR = r + 2 * dr;
                int destC = c + 2 * dc;
                int destSq = BoardGeometry.GetSquare(destR, destC);
                if (destSq == 0) continue;

                var destPiece = currentBoard.GetPiece(destSq);
                // Destination must be empty (or the original starting square vacated in a loop)
                if (!destPiece.IsEmpty() && destSq != startSq) continue;

                visitedCaptured.Add(midSq);
                path.Add(destSq);

                // Check crowning during jump
                bool crowned = !currentPiece.IsKing() && BoardGeometry.IsKingRow(destSq, currentPiece.GetColor()!.Value);

                if (crowned)
                {
                    // In English Checkers rules, crowning terminates the turn immediately
                    completeJumps.Add(new Move(startSq, destSq, path.ToArray(), visitedCaptured.ToArray()));
                }
                else
                {
                    // Create speculative board to continue jump
                    var nextBoard = currentBoard.Clone();
                    nextBoard.SetPiece(currentSq, PieceType.None);
                    nextBoard.SetPiece(midSq, PieceType.None);
                    nextBoard.SetPiece(destSq, currentPiece);

                    int countBefore = completeJumps.Count;
                    SearchJumps(destSq, currentPiece, nextBoard);

                    // If no further jumps were possible from destSq, this was a completed jump
                    if (completeJumps.Count == countBefore)
                    {
                        completeJumps.Add(new Move(startSq, destSq, path.ToArray(), visitedCaptured.ToArray()));
                    }
                }

                // Backtrack
                path.RemoveAt(path.Count - 1);
                visitedCaptured.Remove(midSq);
            }
        }

        SearchJumps(startSq, startPiece, board);
        return completeJumps;
    }

    /// <summary>
    /// Checks if a move string is legal in the given board position.
    /// Handles notations like "22-18", "18x11", "22-18x11-7", "22x15x6", "22-18".
    /// </summary>
    public static bool IsMoveLegal(BoardPosition board, string moveStr, out Move? matchedMove)
    {
        matchedMove = null;
        if (string.IsNullOrWhiteSpace(moveStr)) return false;

        var legalMoves = GenerateLegalMoves(board);
        if (legalMoves.Count == 0) return false;

        // Try exact PDN string match
        var trimmed = moveStr.Trim();
        foreach (var m in legalMoves)
        {
            if (string.Equals(m.ToPdn(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                matchedMove = m;
                return true;
            }
        }

        // Try matching by parsed squares sequence
        try
        {
            var parsedSquares = PdnParser.ParseMoveSquares(trimmed);
            if (parsedSquares.Count >= 2)
            {
                int from = parsedSquares[0];
                int to = parsedSquares[^1];

                foreach (var m in legalMoves)
                {
                    if (m.From == from && m.To == to)
                    {
                        // If it's a simple move
                        if (!m.IsJump && parsedSquares.Count == 2)
                        {
                            matchedMove = m;
                            return true;
                        }

                        // If jump steps match
                        if (m.IsJump)
                        {
                            if (parsedSquares.Count == 2 || m.Steps.SequenceEqual(parsedSquares))
                            {
                                matchedMove = m;
                                return true;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}
