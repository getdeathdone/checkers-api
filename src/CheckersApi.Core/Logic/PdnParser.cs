using System.Text.RegularExpressions;
using CheckersApi.Core.Models;

namespace CheckersApi.Core.Logic;

public class PdnParseException : Exception
{
    public PdnParseException(string message) : base(message) { }
}

public static class PdnParser
{
    // Regex for PDN position format:
    // e.g. "B:W18,19,22,25,27,28,30,32:B1,5,6,7,10,12,14,16" or "[FEN \"...\"]"
    private static readonly Regex FenTagRegex = new(@"\[FEN\s+""([^""]+)""\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static BoardPosition Parse(string pdn)
    {
        if (string.IsNullOrWhiteSpace(pdn))
            throw new PdnParseException("Position string cannot be empty.");

        string trimmed = pdn.Trim();

        // Extract inside [FEN "..."] tag if present
        var fenMatch = FenTagRegex.Match(trimmed);
        if (fenMatch.Success)
        {
            trimmed = fenMatch.Groups[1].Value.Trim();
        }

        // PDN FEN tokens are separated by ':'
        // Usually: [ActiveColor]:[W pieces]:[B pieces] or [ActiveColor]:[B pieces]:[W pieces]
        var tokens = trimmed.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 3)
        {
            throw new PdnParseException($"Invalid PDN format '{pdn}'. Expected 3 segments separated by colons (Turn:White:Black).");
        }

        // Token 0: Active turn ('B' or 'W')
        string turnToken = tokens[0].ToUpperInvariant();
        GameColor activeColor;
        if (turnToken == "B" || turnToken.StartsWith("B"))
            activeColor = GameColor.Black;
        else if (turnToken == "W" || turnToken.StartsWith("W"))
            activeColor = GameColor.White;
        else
            throw new PdnParseException($"Invalid active player turn '{tokens[0]}'. Must be 'B' or 'W'.");

        var board = new BoardPosition(activeColor);
        var occupiedSquares = new HashSet<int>();

        for (int i = 1; i < tokens.Length; i++)
        {
            string pieceSection = tokens[i].Trim();
            if (string.IsNullOrEmpty(pieceSection)) continue;

            char colorChar = char.ToUpperInvariant(pieceSection[0]);
            GameColor sectionColor;
            if (colorChar == 'W')
                sectionColor = GameColor.White;
            else if (colorChar == 'B')
                sectionColor = GameColor.Black;
            else
                throw new PdnParseException($"Invalid piece color indicator '{pieceSection[0]}' in '{pieceSection}'. Must start with W or B.");

            string pieceListStr = pieceSection.Substring(1).Trim();
            if (string.IsNullOrEmpty(pieceListStr)) continue;

            string[] pieceTokens = pieceListStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var pt in pieceTokens)
            {
                bool isKing = false;
                string sqStr = pt;
                if (sqStr.StartsWith("K", StringComparison.OrdinalIgnoreCase))
                {
                    isKing = true;
                    sqStr = sqStr.Substring(1);
                }

                if (!int.TryParse(sqStr, out int sq) || sq < 1 || sq > BoardGeometry.TotalSquares)
                {
                    throw new PdnParseException($"Invalid square number '{pt}'. Squares must be between 1 and 32.");
                }

                if (!occupiedSquares.Add(sq))
                {
                    throw new PdnParseException($"Square {sq} is occupied more than once.");
                }

                PieceType piece = sectionColor switch
                {
                    GameColor.White => isKing ? PieceType.WhiteKing : PieceType.WhiteMan,
                    GameColor.Black => isKing ? PieceType.BlackKing : PieceType.BlackMan,
                    _ => PieceType.None
                };

                board.SetPiece(sq, piece);
            }
        }

        ValidateBoard(board);
        return board;
    }

    public static void ValidateBoard(BoardPosition board)
    {
        int blackCount = board.PieceCount(GameColor.Black);
        int whiteCount = board.PieceCount(GameColor.White);

        if (blackCount == 0 && whiteCount == 0)
            throw new PdnParseException("Board has no pieces.");

        if (blackCount > 12)
            throw new PdnParseException($"Too many Black pieces: {blackCount} (max 12).");

        if (whiteCount > 12)
            throw new PdnParseException($"Too many White pieces: {whiteCount} (max 12).");
    }

    /// <summary>
    /// Parses a move string into a list of visited squares.
    /// Handles "22-18", "18x11", "22x15x6", "22-18x11-7", etc.
    /// </summary>
    public static List<int> ParseMoveSquares(string moveStr)
    {
        if (string.IsNullOrWhiteSpace(moveStr))
            throw new PdnParseException("Move string cannot be empty.");

        // Extract all numbers separated by '-', 'x', or spaces
        var parts = Regex.Split(moveStr.Trim(), @"[-x\s]+");
        var squares = new List<int>();

        foreach (var p in parts)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            if (!int.TryParse(p, out int sq) || sq < 1 || sq > BoardGeometry.TotalSquares)
                throw new PdnParseException($"Invalid square '{p}' in move string '{moveStr}'.");
            
            // Avoid duplicate consecutive squares if move format was e.g. "22-18 18-11"
            if (squares.Count == 0 || squares[^1] != sq)
            {
                squares.Add(sq);
            }
        }

        if (squares.Count < 2)
            throw new PdnParseException($"Invalid move format '{moveStr}'. A move must contain at least from and to squares.");

        return squares;
    }
}
