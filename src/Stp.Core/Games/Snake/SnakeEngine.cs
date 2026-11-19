namespace Stp.Core.Games.Snake;

/// <summary>
/// Engine quản lý toàn bộ logic tính toán của game Rắn săn mồi (Snake).
/// Độc lập hoàn toàn với UI, hỗ trợ kiểm thử Unit Test.
/// </summary>
public sealed class SnakeEngine
{
    public const int GridSize = 20; // Lưới 20x20
    public const int InitialSpeedMs = 150;
    public const int MinSpeedMs = 70;
    public const int SpeedStepMs = 10;
    public const int PointsPerFood = 10;

    private readonly List<GridPoint> _body = new();
    private readonly Queue<SnakeDirection> _inputQueue = new(2);
    private readonly Random _random;

    public SnakeDirection CurrentDirection { get; private set; } = SnakeDirection.Right;
    public GridPoint Food { get; private set; }
    public int Score { get; private set; }
    public int HighScore { get; set; }
    public int SpeedMs { get; private set; } = InitialSpeedMs;
    public int FoodEatenCount { get; private set; }
    public bool IsGameOver { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsPaused { get; private set; }
    public bool PassThroughWalls { get; set; } = false;
    public int CurrentCropIndex { get; private set; } = 0; // 0=Carrot, 1=Strawberry, 2=Corn, 3=Pumpkin

    public IReadOnlyList<GridPoint> Body => _body.AsReadOnly();
    public GridPoint Head => _body.Count > 0 ? _body[0] : new GridPoint(0, 0);

    public SnakeEngine(Random? random = null)
    {
        _random = random ?? Random.Shared;
        Reset();
    }

    /// <summary>
    /// Khởi động lại trạng thái ban đầu của game.
    /// </summary>
    public void Reset()
    {
        _body.Clear();
        _inputQueue.Clear();

        // Rắn khởi đầu ở giữa lưới với độ dài 3 ô, hướng sang phải
        _body.Add(new GridPoint(10, 10)); // Đầu
        _body.Add(new GridPoint(9, 10));  // Thân
        _body.Add(new GridPoint(8, 10));  // Đuôi

        CurrentDirection = SnakeDirection.Right;
        Score = 0;
        FoodEatenCount = 0;
        SpeedMs = InitialSpeedMs;
        IsGameOver = false;
        IsRunning = false;
        IsPaused = false;

        SpawnFood();
    }

    public void Start()
    {
        if (IsGameOver) Reset();
        IsRunning = true;
        IsPaused = false;
    }

    public void Pause()
    {
        if (IsRunning && !IsGameOver)
        {
            IsPaused = true;
        }
    }

    public void Resume()
    {
        if (IsRunning && !IsGameOver)
        {
            IsPaused = false;
        }
    }

    public void TogglePause()
    {
        if (!IsRunning || IsGameOver) return;
        IsPaused = !IsPaused;
    }

    /// <summary>
    /// Hàng đợi lệnh điều hướng (tối đa 2 lệnh) chống nuốt phím khi bấm nhanh,
    /// đồng thời chặn hành vi quay đầu 180 độ gây tự sát tức thì.
    /// </summary>
    public bool ChangeDirection(SnakeDirection newDir)
    {
        if (IsGameOver) return false;

        // Xác định hướng tham chiếu để chống quay đầu 180 độ
        var lastDirection = _inputQueue.Count > 0 ? _inputQueue.Last() : CurrentDirection;
        if (IsOpposite(lastDirection, newDir))
        {
            return false;
        }

        // Tối đa 2 lệnh chờ xử lý
        if (_inputQueue.Count < 2)
        {
            _inputQueue.Enqueue(newDir);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Bước nhảy thời gian (Tick): di chuyển rắn 1 ô, xử lý ăn mồi, va chạm tường và tự đâm thân.
    /// </summary>
    public bool Tick()
    {
        if (!IsRunning || IsPaused || IsGameOver) return false;

        // Lấy lệnh tiếp theo trong hàng đợi nếu có
        if (_inputQueue.TryDequeue(out var nextDir))
        {
            CurrentDirection = nextDir;
        }

        var head = Head;
        var nextX = head.X;
        var nextY = head.Y;

        switch (CurrentDirection)
        {
            case SnakeDirection.Up: nextY--; break;
            case SnakeDirection.Down: nextY++; break;
            case SnakeDirection.Left: nextX--; break;
            case SnakeDirection.Right: nextX++; break;
        }

        // 1. Kiểm tra va chạm biên tường
        if (nextX < 0 || nextX >= GridSize || nextY < 0 || nextY >= GridSize)
        {
            if (PassThroughWalls)
            {
                // Chế độ Xuyên tường: tự động vòng sang phía đối diện
                nextX = (nextX + GridSize) % GridSize;
                nextY = (nextY + GridSize) % GridSize;
            }
            else
            {
                TriggerGameOver();
                return false;
            }
        }

        var nextPoint = new GridPoint(nextX, nextY);
        bool isEating = nextPoint == Food;

        // 2. Kiểm tra va chạm thân rắn
        // Nếu không ăn mồi thì đuôi sẽ thụt lại 1 ô (không tính va chạm với đuôi hiện tại)
        var bodyToCheck = isEating ? _body : _body.Take(_body.Count - 1);
        if (bodyToCheck.Contains(nextPoint))
        {
            TriggerGameOver();
            return false;
        }

        // 3. Tiến hành di chuyển đầu rắn
        _body.Insert(0, nextPoint);

        if (isEating)
        {
            Score += PointsPerFood;
            if (Score > HighScore)
            {
                HighScore = Score;
            }

            FoodEatenCount++;
            CurrentCropIndex = (CurrentCropIndex + 1) % 4;
            // Tốc độ: cứ ăn 5 mồi giảm 10ms, tối thiểu 70ms
            int speedReductions = FoodEatenCount / 5;
            SpeedMs = Math.Max(MinSpeedMs, InitialSpeedMs - (speedReductions * SpeedStepMs));

            SpawnFood();
        }
        else
        {
            // Bỏ đuôi cũ
            _body.RemoveAt(_body.Count - 1);
        }

        return true;
    }

    /// <summary>
    /// Sinh mồi ngẫu nhiên tại ô trống trên bàn cờ.
    /// </summary>
    public void SpawnFood()
    {
        var occupied = new HashSet<GridPoint>(_body);
        var freeCells = new List<GridPoint>(GridSize * GridSize);

        for (int x = 0; x < GridSize; x++)
        {
            for (int y = 0; y < GridSize; y++)
            {
                var pt = new GridPoint(x, y);
                if (!occupied.Contains(pt))
                {
                    freeCells.Add(pt);
                }
            }
        }

        if (freeCells.Count == 0)
        {
            // Đã lắp đầy toàn bộ bàn cờ (thắng tuyệt đối)
            IsGameOver = true;
            IsRunning = false;
            return;
        }

        Food = freeCells[_random.Next(freeCells.Count)];
    }

    private void TriggerGameOver()
    {
        IsGameOver = true;
        IsRunning = false;
        IsPaused = false;
    }

    private static bool IsOpposite(SnakeDirection d1, SnakeDirection d2)
    {
        return (d1 == SnakeDirection.Up && d2 == SnakeDirection.Down) ||
               (d1 == SnakeDirection.Down && d2 == SnakeDirection.Up) ||
               (d1 == SnakeDirection.Left && d2 == SnakeDirection.Right) ||
               (d1 == SnakeDirection.Right && d2 == SnakeDirection.Left);
    }

    public SnakeSnapshot GetSnapshot() => new(
        _body.ToList(),
        Food,
        Score,
        HighScore,
        SpeedMs,
        FoodEatenCount,
        IsGameOver,
        IsRunning,
        IsPaused,
        PassThroughWalls,
        CurrentDirection
    );

    public object ToDto()
    {
        return new
        {
            head = new { x = Head.X, y = Head.Y },
            body = _body.Select(pt => new { x = pt.X, y = pt.Y }).ToArray(),
            food = new { x = Food.X, y = Food.Y },
            cropType = CurrentCropIndex, // 0=Carrot, 1=Strawberry, 2=Corn, 3=Pumpkin
            score = Score,
            highScore = HighScore,
            speedMs = SpeedMs,
            foodEaten = FoodEatenCount,
            isGameOver = IsGameOver,
            isRunning = IsRunning,
            isPaused = IsPaused,
            passThroughWalls = PassThroughWalls,
            direction = CurrentDirection.ToString().ToLowerInvariant()
        };
    }
}
