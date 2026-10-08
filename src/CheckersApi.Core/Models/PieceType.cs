namespace CheckersApi.Core.Models;

[Flags]
public enum PieceType : byte
{
    None = 0,
    Black = 1,
    White = 2,
    King = 4,

    BlackMan = Black,
    BlackKing = Black | King,
    WhiteMan = White,
    WhiteKing = White | King
}

public static class PieceTypeExtensions
{
    public static bool IsEmpty(this PieceType piece) => piece == PieceType.None;
    public static bool IsBlack(this PieceType piece) => (piece & PieceType.Black) != 0;
    public static bool IsWhite(this PieceType piece) => (piece & PieceType.White) != 0;
    public static bool IsKing(this PieceType piece) => (piece & PieceType.King) != 0;

    public static GameColor? GetColor(this PieceType piece)
    {
        if (piece.IsBlack()) return GameColor.Black;
        if (piece.IsWhite()) return GameColor.White;
        return null;
    }
}
