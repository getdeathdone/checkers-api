using CheckersApi.Core.Logic;
using CheckersApi.Core.Models;
using Xunit;

namespace CheckersApi.Tests;

public class PdnParserTests
{
    [Fact]
    public void Parse_ValidSpecPosition_Succeeds()
    {
        string pdn = "B:W18,19,22,25,27,28,30,32:B1,5,6,7,10,12,14,16";
        var board = PdnParser.Parse(pdn);

        Assert.Equal(GameColor.Black, board.ActivePlayer);
        Assert.Equal(8, board.PieceCount(GameColor.White));
        Assert.Equal(8, board.PieceCount(GameColor.Black));
        Assert.Equal(16, board.TotalPieceCount());

        Assert.Equal(PieceType.WhiteMan, board.GetPiece(18));
        Assert.Equal(PieceType.BlackMan, board.GetPiece(1));
    }

    [Fact]
    public void Parse_WithKings_RecognizesKings()
    {
        string pdn = "W:WK1,5,10:BK12,15";
        var board = PdnParser.Parse(pdn);

        Assert.Equal(GameColor.White, board.ActivePlayer);
        Assert.Equal(PieceType.WhiteKing, board.GetPiece(1));
        Assert.Equal(PieceType.WhiteMan, board.GetPiece(5));
        Assert.Equal(PieceType.BlackKing, board.GetPiece(12));
        Assert.Equal(PieceType.BlackMan, board.GetPiece(15));
    }

    [Fact]
    public void Parse_FenTagFormat_ParsesSuccessfully()
    {
        string pdn = "[FEN \"B:W18,19:B1,5\"]";
        var board = PdnParser.Parse(pdn);

        Assert.Equal(GameColor.Black, board.ActivePlayer);
        Assert.Equal(2, board.PieceCount(GameColor.White));
        Assert.Equal(2, board.PieceCount(GameColor.Black));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Invalid")]
    [InlineData("B:W33:B1")] // Square out of bounds
    [InlineData("B:W0:B1")]  // Square out of bounds
    [InlineData("X:W1:B2")]  // Invalid active turn
    [InlineData("B:W1:B1")]  // Overlapping square
    public void Parse_InvalidInput_ThrowsPdnParseException(string invalidPdn)
    {
        Assert.Throws<PdnParseException>(() => PdnParser.Parse(invalidPdn));
    }

    [Fact]
    public void ParseMoveSquares_ValidFormats_ExtractsSquares()
    {
        var squares1 = PdnParser.ParseMoveSquares("22-18");
        Assert.Equal(new[] { 22, 18 }, squares1);

        var squares2 = PdnParser.ParseMoveSquares("18x11");
        Assert.Equal(new[] { 18, 11 }, squares2);

        var squares3 = PdnParser.ParseMoveSquares("22-18x11-7");
        Assert.Equal(new[] { 22, 18, 11, 7 }, squares3);
    }
}
