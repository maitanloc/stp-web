namespace Stp.Core.Games.Game2048;

/// <summary>
/// Engine quản lý toàn bộ logic tính toán của game 2048.
/// Độc lập hoàn toàn với UI, hỗ trợ kiểm thử Unit Test.
/// </summary>
public sealed class Game2048Engine
{
    public const int Size = 4;
    private readonly int[,] _board = new int[Size, Size];
    private readonly int[,] _previousBoard = new int[Size, Size];
    private int _previousScore;
    private readonly Random _random;

    public int Score { get; private set; }
    public int HighScore { get; set; }
    public bool CanUndo { get; private set; }
    public bool HasWon { get; private set; }
    public bool ContinueAfter2048 { get; set; }
    public bool IsGameOver { get; private set; }
    public bool IsPaused { get; private set; }

    // Theo dõi ô mới sinh và ô vừa gộp để gắn animation CSS
    public (int R, int C)? LastSpawnedTile { get; private set; }
    public HashSet<(int R, int C)> LastMergedTiles { get; } = new();

    public int this[int r, int c] => _board[r, c];

    public Game2048Engine(Random? random = null)
    {
        _random = random ?? Random.Shared;
        Reset();
    }

    /// <summary>
    /// Bắt đầu lại ván cờ với 2 ô ngẫu nhiên đầu tiên.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_board, 0, _board.Length);
        Array.Clear(_previousBoard, 0, _previousBoard.Length);
        Score = 0;
        CanUndo = false;
        HasWon = false;
        ContinueAfter2048 = false;
        IsGameOver = false;
        IsPaused = false;
        LastSpawnedTile = null;
        LastMergedTiles.Clear();

        SpawnRandomTile();
        SpawnRandomTile();
    }

    public void Pause()
    {
        if (!IsGameOver) IsPaused = true;
    }

    public void Resume()
    {
        if (!IsGameOver) IsPaused = false;
    }

    public void TogglePause()
    {
        if (!IsGameOver) IsPaused = !IsPaused;
    }

    /// <summary>
    /// Thực hiện nước đi theo 4 hướng. Trả về MoveResult chứa thông tin thay đổi.
    /// </summary>
    public MoveResult Move(Direction2048 direction, bool spawnTile = true)
    {
        if (IsGameOver || IsPaused)
        {
            return new MoveResult(false, 0, false, IsGameOver);
        }

        LastMergedTiles.Clear();
        LastSpawnedTile = null;

        // Lưu snapshot để hỗ trợ tính năng Undo 1 bước
        var snapshotBoard = (int[,])_board.Clone();
        int snapshotScore = Score;

        bool changed = false;
        int scoreAdded = 0;

        // Xử lý từng hàng / cột theo hướng di chuyển
        switch (direction)
        {
            case Direction2048.Left:
                for (int r = 0; r < Size; r++)
                {
                    var (newLine, lineScore, lineChanged, mergedCols) = ProcessLine(GetRow(r));
                    if (lineChanged)
                    {
                        changed = true;
                        SetRow(r, newLine);
                        scoreAdded += lineScore;
                        foreach (var c in mergedCols) LastMergedTiles.Add((r, c));
                    }
                }
                break;

            case Direction2048.Right:
                for (int r = 0; r < Size; r++)
                {
                    var original = GetRow(r);
                    Array.Reverse(original);
                    var (newLine, lineScore, lineChanged, mergedCols) = ProcessLine(original);
                    if (lineChanged)
                    {
                        changed = true;
                        Array.Reverse(newLine);
                        SetRow(r, newLine);
                        scoreAdded += lineScore;
                        foreach (var c in mergedCols) LastMergedTiles.Add((r, Size - 1 - c));
                    }
                }
                break;

            case Direction2048.Up:
                for (int c = 0; c < Size; c++)
                {
                    var (newLine, lineScore, lineChanged, mergedRows) = ProcessLine(GetColumn(c));
                    if (lineChanged)
                    {
                        changed = true;
                        SetColumn(c, newLine);
                        scoreAdded += lineScore;
                        foreach (var r in mergedRows) LastMergedTiles.Add((r, c));
                    }
                }
                break;

            case Direction2048.Down:
                for (int c = 0; c < Size; c++)
                {
                    var original = GetColumn(c);
                    Array.Reverse(original);
                    var (newLine, lineScore, lineChanged, mergedRows) = ProcessLine(original);
                    if (lineChanged)
                    {
                        changed = true;
                        Array.Reverse(newLine);
                        SetColumn(c, newLine);
                        scoreAdded += lineScore;
                        foreach (var r in mergedRows) LastMergedTiles.Add((Size - 1 - r, c));
                    }
                }
                break;
        }

        bool reached2048 = false;

        if (changed)
        {
            // Lưu trạng thái trước nước đi để Undo
            Array.Copy(snapshotBoard, _previousBoard, snapshotBoard.Length);
            _previousScore = snapshotScore;
            CanUndo = true;

            Score += scoreAdded;
            if (Score > HighScore)
            {
                HighScore = Score;
            }

            // Sinh 1 ô mới (90% là 2, 10% là 4)
            if (spawnTile)
            {
                SpawnRandomTile();
            }

            // Kiểm tra chiến thắng (chạm mốc 2048)
            if (!HasWon && !ContinueAfter2048 && HasTile(2048))
            {
                HasWon = true;
                reached2048 = true;
            }

            // Kiểm tra hết nước đi (Game Over)
            if (!CanMoveAnywhere())
            {
                IsGameOver = true;
            }
        }

        return new MoveResult(changed, scoreAdded, reached2048, IsGameOver);
    }

    /// <summary>
    /// Thuật toán trượt và gộp 1 hàng 4 ô: mỗi ô chỉ gộp tối đa 1 lần trong 1 lượt.
    /// </summary>
    private static (int[] Line, int ScoreAdded, bool Changed, List<int> MergedIndices) ProcessLine(int[] original)
    {
        var nonZero = original.Where(x => x > 0).ToList();
        var merged = new List<int>();
        var mergedIndices = new List<int>();
        int score = 0;

        for (int i = 0; i < nonZero.Count; i++)
        {
            if (i + 1 < nonZero.Count && nonZero[i] == nonZero[i + 1])
            {
                int combined = nonZero[i] * 2;
                merged.Add(combined);
                mergedIndices.Add(merged.Count - 1);
                score += combined;
                i++; // Bỏ qua ô kế tiếp vì đã gộp
            }
            else
            {
                merged.Add(nonZero[i]);
            }
        }

        while (merged.Count < Size)
        {
            merged.Add(0);
        }

        var result = merged.ToArray();
        bool changed = !original.SequenceEqual(result);
        return (result, score, changed, mergedIndices);
    }

    /// <summary>
    /// Hoàn tác (Undo) lại 1 bước di chuyển gần nhất.
    /// </summary>
    public bool Undo()
    {
        if (!CanUndo) return false;

        Array.Copy(_previousBoard, _board, _previousBoard.Length);
        Score = _previousScore;
        CanUndo = false;
        IsGameOver = false;
        LastSpawnedTile = null;
        LastMergedTiles.Clear();
        return true;
    }

    public void ContinuePlaying()
    {
        ContinueAfter2048 = true;
        HasWon = false;
    }

    /// <summary>
    /// Sinh ngẫu nhiên ô số 2 (90%) hoặc 4 (10%) tại ô trống.
    /// </summary>
    public bool SpawnRandomTile()
    {
        var empty = new List<(int R, int C)>();
        for (int r = 0; r < Size; r++)
        {
            for (int c = 0; c < Size; c++)
            {
                if (_board[r, c] == 0) empty.Add((r, c));
            }
        }

        if (empty.Count == 0) return false;

        var (rr, cc) = empty[_random.Next(empty.Count)];
        int val = _random.Next(10) == 0 ? 4 : 2; // 10% là 4, 90% là 2
        _board[rr, cc] = val;
        LastSpawnedTile = (rr, cc);
        return true;
    }

    public bool HasTile(int target)
    {
        for (int r = 0; r < Size; r++)
            for (int c = 0; c < Size; c++)
                if (_board[r, c] == target) return true;
        return false;
    }

    /// <summary>
    /// Kiểm tra xem còn nước đi hợp lệ nào không.
    /// </summary>
    public bool CanMoveAnywhere()
    {
        // 1. Còn ô trống -> còn đi được
        for (int r = 0; r < Size; r++)
            for (int c = 0; c < Size; c++)
                if (_board[r, c] == 0) return true;

        // 2. Còn 2 ô kề nhau cùng giá trị -> còn gộp được
        for (int r = 0; r < Size; r++)
        {
            for (int c = 0; c < Size; c++)
            {
                int current = _board[r, c];
                if (r + 1 < Size && _board[r + 1, c] == current) return true;
                if (c + 1 < Size && _board[r, c + 1] == current) return true;
            }
        }

        return false;
    }

    public void SetBoardForTesting(int[,] testBoard, int score = 0)
    {
        Array.Copy(testBoard, _board, testBoard.Length);
        Score = score;
    }

    private int[] GetRow(int r) => Enumerable.Range(0, Size).Select(c => _board[r, c]).ToArray();
    private void SetRow(int r, int[] line)
    {
        for (int c = 0; c < Size; c++) _board[r, c] = line[c];
    }
    private int[] GetColumn(int c) => Enumerable.Range(0, Size).Select(r => _board[r, c]).ToArray();
    private void SetColumn(int c, int[] line)
    {
        for (int r = 0; r < Size; r++) _board[r, c] = line[r];
    }

    public Game2048Snapshot GetSnapshot() => new(
        (int[,])_board.Clone(),
        Score,
        HighScore,
        CanUndo,
        HasWon,
        ContinueAfter2048,
        IsGameOver,
        IsPaused
    );

    /// <summary>
    /// Chuyển đổi giá trị thành cấp nông sản (1..11).
    /// </summary>
    public static int ValueToTier(int val)
    {
        return val switch
        {
            2 => 1,
            4 => 2,
            8 => 3,
            16 => 4,
            32 => 5,
            64 => 6,
            128 => 7,
            256 => 8,
            512 => 9,
            1024 => 10,
            2048 => 11,
            _ => val > 2048 ? 11 : 0
        };
    }

    public object ToDto()
    {
        var rawBoard = new int[Size][];
        var tierBoard = new int[Size][];
        for (int r = 0; r < Size; r++)
        {
            rawBoard[r] = new int[Size];
            tierBoard[r] = new int[Size];
            for (int c = 0; c < Size; c++)
            {
                rawBoard[r][c] = _board[r, c];
                tierBoard[r][c] = ValueToTier(_board[r, c]);
            }
        }

        return new
        {
            board = rawBoard,
            tierBoard = tierBoard,
            score = Score,
            highScore = HighScore,
            canUndo = CanUndo,
            hasWon = HasWon,
            isGameOver = IsGameOver,
            isPaused = IsPaused,
            lastMerged = LastMergedTiles.Select(t => new { r = t.R, c = t.C }).ToArray(),
            lastSpawned = LastSpawnedTile != null ? new { r = LastSpawnedTile.Value.R, c = LastSpawnedTile.Value.C, val = _board[LastSpawnedTile.Value.R, LastSpawnedTile.Value.C] } : null
        };
    }
}
