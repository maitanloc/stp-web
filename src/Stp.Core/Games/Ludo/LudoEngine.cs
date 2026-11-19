using System.Security.Cryptography;

namespace Stp.Core.Games.Ludo;

/// <summary>
/// Engine quản lý logic Cờ Cá Ngựa Nông Trại (Ludo Farm).
/// Logic thuần C#, độc lập UI, hỗ trợ 2-4 người chơi, bot AI và chơi online thời gian thực.
/// </summary>
public sealed class LudoEngine
{
    public static readonly int[] StartTiles = [0, 13, 26, 39]; // Đỏ(0), Xanh dương(13), Vàng(26), Xanh lá(39)

    private readonly IDiceRoller _diceRoller;
    public LudoRuleOptions Rules { get; }

    public List<LudoPlayer> Players { get; private set; } = new();
    public int CurrentPlayerIndex { get; private set; } = 0;
    public LudoPlayer CurrentPlayer => Players[CurrentPlayerIndex];
    public LudoColor CurrentTurnColor => CurrentPlayer.Color;

    public int LastDiceRoll { get; private set; } = 0;
    public bool CanRoll { get; private set; } = true;
    public bool HasRolled => !CanRoll && LastDiceRoll > 0;
    public int ConsecutiveSixes { get; private set; } = 0;

    public bool IsGameOver { get; private set; } = false;
    public LudoPlayer? Winner { get; private set; } = null;
    public List<LudoPlayer> Rankings { get; private set; } = new();

    public LudoGameMode Mode { get; set; } = LudoGameMode.VsBots;
    public BotDifficulty BotLevel { get; set; } = BotDifficulty.Normal;

    public LudoMove? LastMove { get; private set; } = null;
    public List<LudoMove> History { get; private set; } = new();

    public LudoEngine(IDiceRoller? diceRoller = null, LudoRuleOptions? rules = null)
    {
        _diceRoller = diceRoller ?? new RandomDiceRoller();
        Rules = rules ?? new LudoRuleOptions();
        InitializePlayers(4);
    }

    /// <summary>
    /// Khởi tạo số lượng người chơi (2 đến 4 người).
    /// </summary>
    public void InitializePlayers(int playerCount, List<string>? customNames = null, bool includeBots = true)
    {
        playerCount = Math.Clamp(playerCount, 2, 4);
        Players.Clear();
        Rankings.Clear();
        History.Clear();
        Winner = null;
        IsGameOver = false;
        CurrentPlayerIndex = 0;
        CanRoll = true;
        LastDiceRoll = 0;
        ConsecutiveSixes = 0;
        LastMove = null;

        string[] defaultNames = ["Nông dân Đỏ", "Nông dân Lam", "Nông dân Vàng", "Nông dân Lục"];
        var colors = new[] { LudoColor.Red, LudoColor.Blue, LudoColor.Yellow, LudoColor.Green };

        for (int i = 0; i < playerCount; i++)
        {
            var p = new LudoPlayer
            {
                Color = colors[i],
                Name = customNames != null && i < customNames.Count ? customNames[i] : defaultNames[i],
                IsBot = includeBots && i > 0, // Người 0 là player, còn lại là bot nếu bật
                IsActive = true,
                Pieces = Enumerable.Range(0, 4).Select(id => new LudoPiece
                {
                    Id = id,
                    Color = colors[i],
                    State = LudoPieceState.InBarn,
                    TrackPosition = -1,
                    HomeTrackStep = -1,
                    StepsTraveled = 0
                }).ToList()
            };
            Players.Add(p);
        }
    }

    /// <summary>
    /// Đổ xúc xắc.
    /// </summary>
    public (int Dice, List<LudoMove> LegalMoves) RollDice()
    {
        if (!CanRoll || IsGameOver) return (LastDiceRoll, new List<LudoMove>());

        int roll = _diceRoller.Roll();
        LastDiceRoll = roll;
        CanRoll = false;

        if (roll == 6)
        {
            ConsecutiveSixes++;
            if (Rules.ThreeSixesLoseTurn && ConsecutiveSixes >= 3)
            {
                // Phạt 3 lần 6 liên tiếp: mất lượt
                ConsecutiveSixes = 0;
                NextTurn();
                return (roll, new List<LudoMove>());
            }
        }
        else
        {
            ConsecutiveSixes = 0;
        }

        var legalMoves = GetLegalMoves(CurrentTurnColor, roll);

        if (legalMoves.Count == 0)
        {
            // Không có nước đi hợp lệ -> Tự động chuyển lượt
            NextTurn();
        }

        return (roll, legalMoves);
    }

    /// <summary>
    /// Lấy danh sách tất cả các nước đi hợp lệ của một người chơi với giá trị xúc xắc đã đổ.
    /// </summary>
    public List<LudoMove> GetLegalMoves(LudoColor playerColor, int dice)
    {
        var player = Players.FirstOrDefault(p => p.Color == playerColor);
        if (player == null || IsGameOver) return new List<LudoMove>();

        var moves = new List<LudoMove>();
        int startTile = StartTiles[(int)playerColor];

        // 1. Nước ra quân từ chuồng (Cần xúc xắc 6)
        if (dice == 6)
        {
            var barnPiece = player.Pieces.FirstOrDefault(p => p.State == LudoPieceState.InBarn);
            if (barnPiece != null)
            {
                // Kiểm tra ô xuất phát có quân cùng màu chặn không
                var pieceAtStart = GetPieceAtTrack(startTile);
                if (pieceAtStart == null || pieceAtStart.Color != playerColor)
                {
                    bool isKick = pieceAtStart != null && pieceAtStart.Color != playerColor;
                    moves.Add(new LudoMove(
                        barnPiece.Id,
                        playerColor,
                        IsDeploy: true,
                        TargetPosition: startTile,
                        IsHomeTrack: false,
                        IsKick: isKick,
                        KickedPlayer: isKick ? pieceAtStart!.Color : null,
                        KickedPieceId: isKick ? pieceAtStart!.Id : null
                    ));
                }
            }
        }

        // 2. Nước đi của các quân đang trên bàn hoặc đang về chuồng
        foreach (var piece in player.Pieces)
        {
            if (piece.State == LudoPieceState.OnTrack)
            {
                int newSteps = piece.StepsTraveled + dice;
                if (newSteps < Rules.TotalTrackCells)
                {
                    // Vẫn còn đi trên đường chính 52 ô
                    int targetTile = (piece.TrackPosition + dice) % Rules.TotalTrackCells;
                    var pieceAtTarget = GetPieceAtTrack(targetTile);

                    if (pieceAtTarget == null)
                    {
                        moves.Add(new LudoMove(piece.Id, playerColor, false, targetTile, false, false));
                    }
                    else if (pieceAtTarget.Color != playerColor)
                    {
                        // Kiểm tra ô an toàn (ô xuất phát của đối thủ có thể cấu hình an toàn)
                        bool isSafeStart = Rules.SafeStartingSquare && IsOpponentStartTile(targetTile, pieceAtTarget.Color);
                        if (!isSafeStart)
                        {
                            moves.Add(new LudoMove(
                                piece.Id,
                                playerColor,
                                false,
                                targetTile,
                                false,
                                IsKick: true,
                                KickedPlayer: pieceAtTarget.Color,
                                KickedPieceId: pieceAtTarget.Id
                            ));
                        }
                    }
                }
                else
                {
                    // Chuẩn bị vào chuồng đích (HomeTrack)
                    int homeStep = newSteps - Rules.TotalTrackCells;
                    if (homeStep < Rules.HomeTrackCells)
                    {
                        // Kiểm tra bậc này có bị quân mình chiếm không
                        var pieceAtHome = player.Pieces.FirstOrDefault(p => p.State == LudoPieceState.InHomeTrack && p.HomeTrackStep == homeStep);
                        if (pieceAtHome == null)
                        {
                            moves.Add(new LudoMove(piece.Id, playerColor, false, homeStep, true, false));
                        }
                    }
                }
            }
            else if (piece.State == LudoPieceState.InHomeTrack)
            {
                // Đi tiếp trong đường về chuồng
                int targetStep = piece.HomeTrackStep + dice;
                if (targetStep < Rules.HomeTrackCells)
                {
                    var pieceAtHome = player.Pieces.FirstOrDefault(p => p.State == LudoPieceState.InHomeTrack && p.HomeTrackStep == targetStep);
                    if (pieceAtHome == null)
                    {
                        moves.Add(new LudoMove(piece.Id, playerColor, false, targetStep, true, false));
                    }
                }
            }
        }

        return moves;
    }

    /// <summary>
    /// Thực hiện một nước đi hợp lệ.
    /// </summary>
    public bool ApplyMove(LudoMove move)
    {
        var player = Players.FirstOrDefault(p => p.Color == move.Player);
        if (player == null || move.PieceId < 0 || move.PieceId >= player.Pieces.Count) return false;

        var piece = player.Pieces[move.PieceId];

        // 1. Xử lý đá quân đối phương nếu có
        if (move.IsKick && move.KickedPlayer.HasValue && move.KickedPieceId.HasValue)
        {
            var oppPlayer = Players.FirstOrDefault(p => p.Color == move.KickedPlayer.Value);
            var oppPiece = oppPlayer?.Pieces.FirstOrDefault(p => p.Id == move.KickedPieceId.Value);
            if (oppPiece != null)
            {
                // Bị đá bay về chuồng (-1)
                oppPiece.State = LudoPieceState.InBarn;
                oppPiece.TrackPosition = -1;
                oppPiece.HomeTrackStep = -1;
                oppPiece.StepsTraveled = 0;
            }
        }

        // 2. Cập nhật vị trí quân di chuyển
        if (move.IsDeploy)
        {
            piece.State = LudoPieceState.OnTrack;
            piece.TrackPosition = move.TargetPosition;
            piece.HomeTrackStep = -1;
            piece.StepsTraveled = 0;
        }
        else if (move.IsHomeTrack)
        {
            piece.State = LudoPieceState.InHomeTrack;
            piece.TrackPosition = -1;
            piece.HomeTrackStep = move.TargetPosition;
            piece.StepsTraveled = Rules.TotalTrackCells + move.TargetPosition;

            // Nếu đến bậc cao nhất (bậc 6 -> index 5) thì hoàn thành
            if (piece.HomeTrackStep == Rules.HomeTrackCells - 1)
            {
                piece.State = LudoPieceState.Finished;
            }
        }
        else
        {
            piece.State = LudoPieceState.OnTrack;
            piece.TrackPosition = move.TargetPosition;
            piece.StepsTraveled += LastDiceRoll;
        }

        LastMove = move;
        History.Add(move);

        // 3. Kiểm tra điều kiện thắng
        if (player.HasWon)
        {
            if (!Rankings.Contains(player))
            {
                Rankings.Add(player);
            }
            if (Winner == null)
            {
                Winner = player;
                IsGameOver = true;
                return true;
            }
        }

        // 4. Quyết định lượt tiếp theo: Đổ 6 được đi thêm lượt
        if (LastDiceRoll == 6 && !IsGameOver)
        {
            CanRoll = true;
            // Giữ nguyên CurrentPlayerIndex
        }
        else
        {
            NextTurn();
        }

        return true;
    }

    /// <summary>
    /// Chuyển lượt sang người chơi tiếp theo còn hoạt động.
    /// </summary>
    public void NextTurn()
    {
        CanRoll = true;
        LastDiceRoll = 0;
        ConsecutiveSixes = 0;

        if (IsGameOver || Players.Count == 0) return;

        int tries = 0;
        do
        {
            CurrentPlayerIndex = (CurrentPlayerIndex + 1) % Players.Count;
            tries++;
        } while ((!CurrentPlayer.IsActive || CurrentPlayer.HasWon) && tries <= Players.Count);

        if (tries > Players.Count)
        {
            IsGameOver = true;
        }
    }

    /// <summary>
    /// Trí tuệ nhân tạo (Bot AI) tự động tính toán nước đi tốt nhất.
    /// </summary>
    public LudoMove? SelectBotMove(List<LudoMove> legalMoves)
    {
        if (legalMoves.Count == 0) return null;
        if (BotLevel == BotDifficulty.Easy)
        {
            return legalMoves[Random.Shared.Next(legalMoves.Count)];
        }

        // Bot Thường (Chiến thuật): Ưu tiên Đá quân > Về đích > Ra quân > Đi xa nhất
        var kickMove = legalMoves.FirstOrDefault(m => m.IsKick);
        if (kickMove != null) return kickMove;

        var finishMove = legalMoves.FirstOrDefault(m => m.IsHomeTrack && m.TargetPosition == Rules.HomeTrackCells - 1);
        if (finishMove != null) return finishMove;

        var homeMove = legalMoves.FirstOrDefault(m => m.IsHomeTrack);
        if (homeMove != null) return homeMove;

        var deployMove = legalMoves.FirstOrDefault(m => m.IsDeploy);
        if (deployMove != null) return deployMove;

        // Đi quân xa nhất
        return legalMoves.OrderByDescending(m => m.TargetPosition).First();
    }

    private LudoPiece? GetPieceAtTrack(int trackPos)
    {
        foreach (var p in Players)
        {
            var piece = p.Pieces.FirstOrDefault(pc => pc.State == LudoPieceState.OnTrack && pc.TrackPosition == trackPos);
            if (piece != null) return piece;
        }
        return null;
    }

    private static bool IsOpponentStartTile(int tile, LudoColor color)
    {
        return StartTiles[(int)color] == tile;
    }

    /// <summary>
    /// Đóng gói trạng thái DTO nhỏ gọn gửi sang Canvas JS Renderer.
    /// </summary>
    public object ToDto()
    {
        return new
        {
            currentPlayer = CurrentPlayerIndex,
            currentTurnColor = (int)CurrentTurnColor,
            canRoll = CanRoll,
            lastDiceRoll = LastDiceRoll,
            isGameOver = IsGameOver,
            winner = Winner?.Name,
            players = Players.Select(p => new
            {
                color = (int)p.Color,
                name = p.Name,
                isBot = p.IsBot,
                finishedCount = p.FinishedCount,
                hasWon = p.HasWon,
                pieces = p.Pieces.Select(pc => new
                {
                    id = pc.Id,
                    state = (int)pc.State,
                    trackPos = pc.TrackPosition,
                    homeStep = pc.HomeTrackStep
                }).ToArray()
            }).ToArray(),
            lastMove = LastMove != null ? new
            {
                pieceId = LastMove.PieceId,
                player = (int)LastMove.Player,
                isDeploy = LastMove.IsDeploy,
                targetPos = LastMove.TargetPosition,
                isHomeTrack = LastMove.IsHomeTrack,
                isKick = LastMove.IsKick,
                kickedPlayer = LastMove.KickedPlayer.HasValue ? (int?)LastMove.KickedPlayer.Value : null,
                kickedPieceId = LastMove.KickedPieceId
            } : null
        };
    }
}
