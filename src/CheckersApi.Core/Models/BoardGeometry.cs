namespace CheckersApi.Core.Models;

public static class BoardGeometry
{
    public const int TotalSquares = 32;

    private static readonly (int Row, int Col)[] SquareCoords = new (int Row, int Col)[TotalSquares + 1];
    private static readonly int[,] CoordToSquare = new int[8, 8];

    static BoardGeometry()
    {
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
                CoordToSquare[r, c] = 0;

        for (int s = 1; s <= TotalSquares; s++)
        {
            int idx = s - 1;
            int r = idx / 4;
            int c = (r % 2 == 0) ? (idx % 4) * 2 + 1 : (idx % 4) * 2;
            SquareCoords[s] = (r, c);
            CoordToSquare[r, c] = s;
        }
    }

    public static (int Row, int Col) GetCoords(int square)
    {
        if (square < 1 || square > TotalSquares)
            return (-1, -1);
        return SquareCoords[square];
    }

    public static int GetSquare(int row, int col)
    {
        if (row < 0 || row >= 8 || col < 0 || col >= 8)
            return 0;
        return CoordToSquare[row, col];
    }

    public static bool IsValidSquare(int square) => square >= 1 && square <= TotalSquares;

    public static bool IsKingRow(int square, GameColor color)
    {
        return color switch
        {
            GameColor.Black => square >= 29 && square <= 32, // Reached White's back rank
            GameColor.White => square >= 1 && square <= 4,   // Reached Black's back rank
            _ => false
        };
    }
}
