namespace Stp.Core.Games.Snake;

/// <summary>
/// Hướng di chuyển của rắn trong không gian 2D.
/// </summary>
public enum SnakeDirection
{
    Up,
    Down,
    Left,
    Right
}

/// <summary>
/// Toạ độ 1 ô trên lưới 20x20.
/// </summary>
public readonly record struct GridPoint(int X, int Y);

/// <summary>
/// Trạng thái chụp của ván game Snake phục vụ hiển thị UI.
/// </summary>
public sealed record SnakeSnapshot(
    IReadOnlyList<GridPoint> Body,
    GridPoint Food,
    int Score,
    int HighScore,
    int SpeedMs,
    int FoodEaten,
    bool IsGameOver,
    bool IsRunning,
    bool IsPaused,
    bool PassThroughWalls,
    SnakeDirection CurrentDirection
);
