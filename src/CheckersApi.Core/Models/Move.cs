using System.Text;

namespace CheckersApi.Core.Models;

public record Move(
    int From,
    int To,
    IReadOnlyList<int> Steps,
    IReadOnlyList<int> CapturedSquares)
{
    public bool IsJump => CapturedSquares.Count > 0;

    public string ToPdn()
    {
        if (!IsJump)
        {
            return $"{From}-{To}";
        }

        var sb = new StringBuilder();
        sb.Append(From);
        for (int i = 1; i < Steps.Count; i++)
        {
            sb.Append('x').Append(Steps[i]);
        }
        return sb.ToString();
    }

    public override string ToString() => ToPdn();
}
