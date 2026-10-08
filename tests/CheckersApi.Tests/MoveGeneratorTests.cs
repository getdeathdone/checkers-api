using CheckersApi.Core.Logic;
using CheckersApi.Core.Models;
using Xunit;

namespace CheckersApi.Tests;

public class MoveGeneratorTests
{
    [Fact]
    public void GenerateLegalMoves_InitialPosition_ReturnsSevenMoves()
    {
        var board = BoardPosition.CreateInitial();
        var moves = MoveGenerator.GenerateLegalMoves(board);

        // From initial position, Black has men on 9, 10, 11, 12 that can move:
        // 9-13, 9-14, 10-14, 10-15, 11-15, 11-16, 12-16 => 7 moves
        Assert.Equal(7, moves.Count);
        Assert.All(moves, m => Assert.False(m.IsJump));
    }

    [Fact]
    public void GenerateLegalMoves_MandatoryJump_OnlyReturnsJumps()
    {
        // Black man on 10, White man on 14, square 17 empty.
        // Also Black man on 1 (which would normally move 1-5 or 1-6).
        // Since 10x17 is a jump, only the jump MUST be returned!
        var board = new BoardPosition(GameColor.Black);
        board.SetPiece(10, PieceType.BlackMan);
        board.SetPiece(14, PieceType.WhiteMan);
        board.SetPiece(1, PieceType.BlackMan);

        var moves = MoveGenerator.GenerateLegalMoves(board);

        Assert.Single(moves);
        var move = moves[0];
        Assert.True(move.IsJump);
        Assert.Equal(10, move.From);
        Assert.Equal(17, move.To);
        Assert.Contains(14, move.CapturedSquares);
    }

    [Fact]
    public void GenerateLegalMoves_MultiJump_GeneratesFullChain()
    {
        // Black on 10, White on 14 and 22.
        // Jump: 10 over 14 to 17, then 17 over 22 to 26.
        var board = new BoardPosition(GameColor.Black);
        board.SetPiece(10, PieceType.BlackMan);
        board.SetPiece(14, PieceType.WhiteMan);
        board.SetPiece(22, PieceType.WhiteMan);

        var moves = MoveGenerator.GenerateLegalMoves(board);

        Assert.Single(moves);
        var move = moves[0];
        Assert.True(move.IsJump);
        Assert.Equal(10, move.From);
        Assert.Equal(26, move.To);
        Assert.Equal(2, move.CapturedSquares.Count);
        Assert.Contains(14, move.CapturedSquares);
        Assert.Contains(22, move.CapturedSquares);
    }

    [Fact]
    public void IsMoveLegal_ValidAndInvalidMoves_ValidatedProperly()
    {
        var board = BoardPosition.CreateInitial();

        bool legal1 = MoveGenerator.IsMoveLegal(board, "11-15", out var move1);
        Assert.True(legal1);
        Assert.NotNull(move1);
        Assert.Equal("11-15", move1!.ToPdn());

        bool legal2 = MoveGenerator.IsMoveLegal(board, "1-5", out _);
        Assert.False(legal2); // Square 1 is blocked by square 5 and 6
    }
}
