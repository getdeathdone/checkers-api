using System.Text;

namespace CheckersApi.Core.Models;

public class BoardPosition : IEquatable<BoardPosition>
{
    private readonly PieceType[] _squares = new PieceType[BoardGeometry.TotalSquares + 1];
    private int _blackCount;
    private int _whiteCount;

    public GameColor ActivePlayer { get; set; } = GameColor.Black;

    public BoardPosition()
    {
    }

    public BoardPosition(GameColor activePlayer)
    {
        ActivePlayer = activePlayer;
    }

    public PieceType GetPiece(int square)
    {
        if (square < 1 || square > BoardGeometry.TotalSquares)
            return PieceType.None;
        return _squares[square];
    }

    public void SetPiece(int square, PieceType piece)
    {
        if (square < 1 || square > BoardGeometry.TotalSquares) return;

        var old = _squares[square];
        if (old.IsBlack()) _blackCount--;
        else if (old.IsWhite()) _whiteCount--;

        _squares[square] = piece;

        if (piece.IsBlack()) _blackCount++;
        else if (piece.IsWhite()) _whiteCount++;
    }

    public int TotalPieceCount() => _blackCount + _whiteCount;

    public int PieceCount(GameColor color) =>
        color == GameColor.Black ? _blackCount : _whiteCount;

    public BoardPosition Clone()
    {
        var clone = new BoardPosition(ActivePlayer)
        {
            _blackCount = _blackCount,
            _whiteCount = _whiteCount
        };
        Array.Copy(_squares, clone._squares, _squares.Length);
        return clone;
    }

    public BoardPosition ApplyMove(Move move)
    {
        var next = Clone();
        var piece = next._squares[move.From];
        next._squares[move.From] = PieceType.None;

        foreach (var cap in move.CapturedSquares)
        {
            next._squares[cap] = PieceType.None;
        }

        // Check promotion to King
        if (!piece.IsKing() && BoardGeometry.IsKingRow(move.To, ActivePlayer))
        {
            piece = ActivePlayer == GameColor.Black ? PieceType.BlackKing : PieceType.WhiteKing;
        }

        next._squares[move.To] = piece;
        next.ActivePlayer = ActivePlayer.Opponent();
        return next;
    }

    /// <summary>
    /// Returns canonical PDN format:
    /// {ActivePlayer}:W{white_pieces}:B{black_pieces}
    /// with squares sorted numerically and Kings prefixed by 'K'.
    /// Example: B:W18,19,22,25,27,28,30,32:B1,5,6,7,10,12,14,16
    /// </summary>
    public string ToCanonicalPdn()
    {
        var whitePieces = new List<string>();
        var blackPieces = new List<string>();

        for (int s = 1; s <= BoardGeometry.TotalSquares; s++)
        {
            var p = _squares[s];
            if (p.IsEmpty()) continue;

            string label = p.IsKing() ? $"K{s}" : $"{s}";
            if (p.IsWhite())
                whitePieces.Add(label);
            else if (p.IsBlack())
                blackPieces.Add(label);
        }

        var sb = new StringBuilder();
        sb.Append(ActivePlayer.ToPdnChar());
        sb.Append(":W");
        sb.Append(string.Join(",", whitePieces));
        sb.Append(":B");
        sb.Append(string.Join(",", blackPieces));
        return sb.ToString();
    }

    public static BoardPosition CreateInitial()
    {
        var board = new BoardPosition(GameColor.Black);
        for (int s = 1; s <= 12; s++)
        {
            board.SetPiece(s, PieceType.BlackMan);
        }
        for (int s = 21; s <= 32; s++)
        {
            board.SetPiece(s, PieceType.WhiteMan);
        }
        return board;
    }

    public bool Equals(BoardPosition? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (ActivePlayer != other.ActivePlayer) return false;
        for (int i = 1; i <= BoardGeometry.TotalSquares; i++)
        {
            if (_squares[i] != other._squares[i]) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as BoardPosition);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ActivePlayer);
        for (int i = 1; i <= BoardGeometry.TotalSquares; i++)
        {
            hash.Add(_squares[i]);
        }
        return hash.ToHashCode();
    }

    public override string ToString() => ToCanonicalPdn();
}
