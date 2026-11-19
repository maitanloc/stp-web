namespace Stp.Core.Games.Ludo;

/// <summary>
/// Giao diện đổ xúc xắc phục vụ Unit Test và chống gian lận (DI).
/// </summary>
public interface IDiceRoller
{
    int Roll();
}

public sealed class RandomDiceRoller : IDiceRoller
{
    public int Roll() => Random.Shared.Next(1, 7);
}

public sealed class FixedDiceRoller : IDiceRoller
{
    private readonly Queue<int> _rolls = new();

    public FixedDiceRoller(params int[] rolls)
    {
        foreach (var r in rolls) _rolls.Enqueue(r);
    }

    public void Enqueue(params int[] rolls)
    {
        foreach (var r in rolls) _rolls.Enqueue(r);
    }

    public int Roll()
    {
        return _rolls.Count > 0 ? _rolls.Dequeue() : 1;
    }
}
