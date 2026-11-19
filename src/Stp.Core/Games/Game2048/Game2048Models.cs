namespace Stp.Core.Games.Game2048;

public enum Direction2048
{
    Up,
    Down,
    Left,
    Right
}

/// <summary>
/// Kết quả sau khi thực hiện 1 thao tác vuốt / di chuyển trên bàn cờ 2048.
/// </summary>
public sealed record MoveResult(
    bool Changed,
    int ScoreAdded,
    bool Reached2048,
    bool IsGameOver
);

/// <summary>
/// Trạng thái chụp của ván game 2048.
/// </summary>
public sealed record Game2048Snapshot(
    int[,] Board,
    int Score,
    int HighScore,
    bool CanUndo,
    bool HasWon,
    bool ContinueAfter2048,
    bool IsGameOver,
    bool IsPaused
);
