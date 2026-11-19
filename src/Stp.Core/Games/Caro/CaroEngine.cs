namespace Stp.Core.Games.Caro;

/// <summary>
/// Engine quản lý toàn bộ bàn cờ Caro 15x15, phán đoán 5 quân thắng cuộc và AI 3 cấp độ.
/// Độc lập hoàn toàn với UI, hỗ trợ kiểm thử Unit Test.
/// </summary>
public sealed class CaroEngine
{
    public const int BoardSize = 15;
    private readonly char[,] _board = new char[BoardSize, BoardSize];
    private readonly List<CaroMove> _history = new();
    private readonly Random _random;

    public char CurrentTurn { get; private set; } = 'X';
    public CaroGameMode Mode { get; set; } = CaroGameMode.VsAi;
    public CaroAiLevel AiLevel { get; set; } = CaroAiLevel.Medium;
    public bool IsThinking { get; set; }
    public bool IsGameOver { get; private set; }
    public CaroWinResult? WinResult { get; private set; }
    public CaroMove? LastMove => _history.Count > 0 ? _history.Last() : null;
    public IReadOnlyList<CaroMove> History => _history.AsReadOnly();

    public char this[int r, int c] => _board[r, c];

    public CaroEngine(Random? random = null)
    {
        _random = random ?? Random.Shared;
        Reset();
    }

    public void Reset()
    {
        for (int r = 0; r < BoardSize; r++)
            for (int c = 0; c < BoardSize; c++)
                _board[r, c] = ' ';

        _history.Clear();
        CurrentTurn = 'X';
        IsGameOver = false;
        WinResult = null;
        IsThinking = false;
    }

    public bool MakeMove(int r, int c) => PlacePiece(r, c);

    public (int R, int C)? MakeAiMove()
    {
        if (IsGameOver) return null;
        var (r, c) = ComputeBestAiMove();
        PlacePiece(r, c);
        return (r, c);
    }

    /// <summary>
    /// Đặt quân cờ tại toạ độ (r, c).
    /// </summary>
    public bool PlacePiece(int r, int c)
    {
        if (IsGameOver || IsThinking) return false;
        if (r < 0 || r >= BoardSize || c < 0 || c >= BoardSize) return false;
        if (_board[r, c] != ' ') return false;

        char piece = CurrentTurn;
        _board[r, c] = piece;
        var move = new CaroMove(r, c, piece);
        _history.Add(move);

        // Kiểm tra xem nước đi này có tạo chuỗi 5 quân liên tiếp không
        var win = CheckWinAt(r, c);
        if (win != null)
        {
            WinResult = win;
            IsGameOver = true;
            return true;
        }

        // Kiểm tra bàn cờ đã đầy (Hòa)
        if (_history.Count >= BoardSize * BoardSize)
        {
            IsGameOver = true;
            return true;
        }

        // Đổi lượt đi
        CurrentTurn = CurrentTurn == 'X' ? 'O' : 'X';
        return true;
    }

    /// <summary>
    /// Đi lại (Undo). Trong chế độ đánh với máy, hoàn tác cả nước của máy lẫn người chơi (2 nước).
    /// </summary>
    public bool Undo()
    {
        if (_history.Count == 0 || IsThinking) return false;

        int movesToUndo = (Mode == CaroGameMode.VsAi && _history.Count >= 2) ? 2 : 1;
        for (int i = 0; i < movesToUndo; i++)
        {
            if (_history.Count == 0) break;
            var last = _history[^1];
            _board[last.R, last.C] = ' ';
            _history.RemoveAt(_history.Count - 1);
        }

        CurrentTurn = _history.Count % 2 == 0 ? 'X' : 'O';
        IsGameOver = false;
        WinResult = null;
        return true;
    }

    /// <summary>
    /// Tính toán nước đi của AI theo 3 mức độ (Easy, Medium, Hard).
    /// </summary>
    public (int R, int C) ComputeBestAiMove()
    {
        var candidates = GetCandidateMoves(2);
        if (candidates.Count == 0)
        {
            return (BoardSize / 2, BoardSize / 2); // Đi tâm bàn cờ
        }

        return AiLevel switch
        {
            CaroAiLevel.Easy => ComputeEasyMove(candidates),
            CaroAiLevel.Medium => ComputeHeuristicMove(candidates),
            CaroAiLevel.Hard => ComputeMinimaxMove(candidates, depth: 2),
            _ => ComputeHeuristicMove(candidates)
        };
    }

    private (int R, int C) ComputeEasyMove(List<(int R, int C)> candidates)
    {
        // Chọn ngẫu nhiên trong vùng lân cận các quân đã có
        return candidates[_random.Next(candidates.Count)];
    }

    /// <summary>
    /// AI mức Vừa: chấm điểm heuristic theo các mẫu cờ tấn công và phòng ngự.
    /// </summary>
    private (int R, int C) ComputeHeuristicMove(List<(int R, int C)> candidates)
    {
        (int R, int C) bestMove = candidates[0];
        long maxScore = long.MinValue;

        foreach (var (r, c) in candidates)
        {
            long attackScore = EvaluateCellPatterns(r, c, 'O');
            long defenseScore = EvaluateCellPatterns(r, c, 'X');

            // Tổng hợp điểm: ưu tiên tấn công dứt điểm hoặc chặn nước nguy hiểm của đối thủ
            long totalScore = attackScore * 12 / 10 + defenseScore;

            if (totalScore > maxScore)
            {
                maxScore = totalScore;
                bestMove = (r, c);
            }
        }

        return bestMove;
    }

    /// <summary>
    /// AI mức Khó: Minimax kết hợp cắt tỉa Alpha-Beta trên tập nước đi triển vọng.
    /// </summary>
    private (int R, int C) ComputeMinimaxMove(List<(int R, int C)> candidates, int depth)
    {
        // 1. Kiểm tra nếu AI thắng ngay lập tức
        foreach (var (r, c) in candidates)
        {
            _board[r, c] = 'O';
            if (CheckWinAt(r, c) != null)
            {
                _board[r, c] = ' ';
                return (r, c);
            }
            _board[r, c] = ' ';
        }

        // 2. Kiểm tra nếu đối thủ chuẩn bị thắng -> chặn ngay lập tức
        foreach (var (r, c) in candidates)
        {
            _board[r, c] = 'X';
            if (CheckWinAt(r, c) != null)
            {
                _board[r, c] = ' ';
                return (r, c);
            }
            _board[r, c] = ' ';
        }

        // 3. Sắp xếp sơ bộ các ứng viên bằng heuristic để tăng hiệu quả cắt tỉa Alpha-Beta
        var scoredCandidates = candidates
            .Select(pos => new { Pos = pos, Score = EvaluateCellPatterns(pos.R, pos.C, 'O') + EvaluateCellPatterns(pos.R, pos.C, 'X') })
            .OrderByDescending(x => x.Score)
            .Take(12) // Giới hạn 12 nước đi tốt nhất để tính sâu
            .Select(x => x.Pos)
            .ToList();

        (int R, int C) bestMove = scoredCandidates[0];
        long alpha = long.MinValue;
        long beta = long.MaxValue;

        foreach (var (r, c) in scoredCandidates)
        {
            _board[r, c] = 'O';
            long val = Minimax(depth - 1, false, alpha, beta);
            _board[r, c] = ' ';

            if (val > alpha)
            {
                alpha = val;
                bestMove = (r, c);
            }
        }

        return bestMove;
    }

    private long Minimax(int depth, bool isMaximizing, long alpha, long beta)
    {
        if (depth == 0)
        {
            return EvaluateWholeBoard();
        }

        var candidates = GetCandidateMoves(1).Take(8).ToList();
        if (candidates.Count == 0) return 0;

        char piece = isMaximizing ? 'O' : 'X';

        if (isMaximizing)
        {
            long maxEval = long.MinValue;
            foreach (var (r, c) in candidates)
            {
                _board[r, c] = piece;
                if (CheckWinAt(r, c) != null)
                {
                    _board[r, c] = ' ';
                    return 1_000_000 + depth;
                }

                long eval = Minimax(depth - 1, false, alpha, beta);
                _board[r, c] = ' ';
                maxEval = Math.Max(maxEval, eval);
                alpha = Math.Max(alpha, eval);
                if (beta <= alpha) break;
            }
            return maxEval;
        }
        else
        {
            long minEval = long.MaxValue;
            foreach (var (r, c) in candidates)
            {
                _board[r, c] = piece;
                if (CheckWinAt(r, c) != null)
                {
                    _board[r, c] = ' ';
                    return -1_000_000 - depth;
                }

                long eval = Minimax(depth - 1, true, alpha, beta);
                _board[r, c] = ' ';
                minEval = Math.Min(minEval, eval);
                beta = Math.Min(beta, eval);
                if (beta <= alpha) break;
            }
            return minEval;
        }
    }

    private long EvaluateWholeBoard()
    {
        long score = 0;
        for (int r = 0; r < BoardSize; r++)
        {
            for (int c = 0; c < BoardSize; c++)
            {
                if (_board[r, c] == 'O') score += EvaluateCellPatterns(r, c, 'O');
                else if (_board[r, c] == 'X') score -= EvaluateCellPatterns(r, c, 'X');
            }
        }
        return score;
    }

    private long EvaluateCellPatterns(int r, int c, char piece)
    {
        long score = 0;
        int[][] directions = [ [0, 1], [1, 0], [1, 1], [1, -1] ];

        foreach (var dir in directions)
        {
            int count = 1;
            int openEnds = 0;

            // Kiểm tra hướng tiến
            int step = 1;
            while (true)
            {
                int nr = r + dir[0] * step;
                int nc = c + dir[1] * step;
                if (nr < 0 || nr >= BoardSize || nc < 0 || nc >= BoardSize) break;
                if (_board[nr, nc] == piece) count++;
                else
                {
                    if (_board[nr, nc] == ' ') openEnds++;
                    break;
                }
                step++;
            }

            // Kiểm tra hướng lùi
            step = 1;
            while (true)
            {
                int nr = r - dir[0] * step;
                int nc = c - dir[1] * step;
                if (nr < 0 || nr >= BoardSize || nc < 0 || nc >= BoardSize) break;
                if (_board[nr, nc] == piece) count++;
                else
                {
                    if (_board[nr, nc] == ' ') openEnds++;
                    break;
                }
                step++;
            }

            // Chấm điểm mẫu
            if (count >= 5) score += 500_000;
            else if (count == 4) score += openEnds == 2 ? 50_000 : (openEnds == 1 ? 5_000 : 0);
            else if (count == 3) score += openEnds == 2 ? 3_000 : (openEnds == 1 ? 500 : 0);
            else if (count == 2) score += openEnds == 2 ? 300 : (openEnds == 1 ? 50 : 0);
        }

        return score;
    }

    /// <summary>
    /// Tìm danh sách các ô trống nằm trong bán kính radius quanh các quân cờ đã có.
    /// </summary>
    public List<(int R, int C)> GetCandidateMoves(int radius = 2)
    {
        var candidates = new HashSet<(int R, int C)>();
        bool hasPieces = false;

        for (int r = 0; r < BoardSize; r++)
        {
            for (int c = 0; c < BoardSize; c++)
            {
                if (_board[r, c] != ' ')
                {
                    hasPieces = true;
                    for (int dr = -radius; dr <= radius; dr++)
                    {
                        for (int dc = -radius; dc <= radius; dc++)
                        {
                            int nr = r + dr;
                            int nc = c + dc;
                            if (nr >= 0 && nr < BoardSize && nc >= 0 && nc < BoardSize && _board[nr, nc] == ' ')
                            {
                                candidates.Add((nr, nc));
                            }
                        }
                    }
                }
            }
        }

        if (!hasPieces)
        {
            return new List<(int R, int C)> { (BoardSize / 2, BoardSize / 2) };
        }

        return candidates.ToList();
    }

    /// <summary>
    /// Kiểm tra chiến thắng: tìm 5 quân liên tiếp theo 4 hướng (ngang, dọc, 2 đường chéo).
    /// </summary>
    public CaroWinResult? CheckWinAt(int lastR, int lastC)
    {
        if (lastR < 0 || lastR >= BoardSize || lastC < 0 || lastC >= BoardSize) return null;
        char piece = _board[lastR, lastC];
        if (piece == ' ') return null;

        int[][] directions = [ [0, 1], [1, 0], [1, 1], [1, -1] ];

        foreach (var dir in directions)
        {
            var line = new List<(int R, int C)> { (lastR, lastC) };

            // Tiến
            int step = 1;
            while (true)
            {
                int nr = lastR + dir[0] * step;
                int nc = lastC + dir[1] * step;
                if (nr < 0 || nr >= BoardSize || nc < 0 || nc >= BoardSize || _board[nr, nc] != piece) break;
                line.Add((nr, nc));
                step++;
            }

            // Lùi
            step = 1;
            while (true)
            {
                int nr = lastR - dir[0] * step;
                int nc = lastC - dir[1] * step;
                if (nr < 0 || nr >= BoardSize || nc < 0 || nc >= BoardSize || _board[nr, nc] != piece) break;
                line.Add((nr, nc));
                step++;
            }

            if (line.Count >= 5)
            {
                return new CaroWinResult(true, piece, line.OrderBy(p => p.R).ThenBy(p => p.C).ToList());
            }
        }

        return null;
    }

    public void SetBoardForTesting(char[,] customBoard)
    {
        Array.Copy(customBoard, _board, customBoard.Length);
    }

    public object ToDto()
    {
        var boardList = new string[BoardSize];
        for (int r = 0; r < BoardSize; r++)
        {
            var chars = new char[BoardSize];
            for (int c = 0; c < BoardSize; c++)
                chars[c] = _board[r, c];
            boardList[r] = new string(chars);
        }

        return new
        {
            board = boardList,
            currentTurn = CurrentTurn.ToString(),
            mode = Mode.ToString(),
            aiLevel = AiLevel.ToString(),
            isThinking = IsThinking,
            isGameOver = IsGameOver,
            hasWon = WinResult?.HasWon ?? false,
            winner = WinResult?.Winner.ToString(),
            winningLine = WinResult?.WinningLine?.Select(p => new { r = p.R, c = p.C }).ToArray(),
            lastMove = LastMove != null ? new { r = LastMove.R, c = LastMove.C, player = LastMove.Player.ToString() } : null,
            moveCount = _history.Count
        };
    }

    public CaroSnapshot GetSnapshot() => new(
        (char[,])_board.Clone(),
        CurrentTurn,
        Mode,
        AiLevel,
        IsThinking,
        IsGameOver,
        WinResult,
        LastMove,
        _history.Count
    );
}
