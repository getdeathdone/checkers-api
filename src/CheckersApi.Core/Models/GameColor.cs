namespace CheckersApi.Core.Models;

public enum GameColor
{
    Black = 1,
    White = 2
}

public static class GameColorExtensions
{
    public static GameColor Opponent(this GameColor color) =>
        color == GameColor.Black ? GameColor.White : GameColor.Black;

    public static string ToPdnChar(this GameColor color) =>
        color == GameColor.Black ? "B" : "W";
}
