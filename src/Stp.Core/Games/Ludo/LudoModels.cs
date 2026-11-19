namespace Stp.Core.Games.Ludo;

public enum LudoColor
{
    Red = 0,    // Đỏ
    Blue = 1,   // Xanh dương
    Yellow = 2, // Vàng
    Green = 3   // Xanh lá
}

public enum LudoPieceState
{
    InBarn = 0,      // Trong chuồng góc (-1)
    OnTrack = 1,     // Đang chạy trên đường vòng 52 ô
    InHomeTrack = 2, // Đang lên chuồng đích 6 bậc
    Finished = 3     // Đã về đích an toàn
}

public sealed class LudoPiece
{
    public int Id { get; set; }
    public LudoColor Color { get; set; }
    public LudoPieceState State { get; set; } = LudoPieceState.InBarn;
    public int TrackPosition { get; set; } = -1; // 0..51
    public int HomeTrackStep { get; set; } = -1; // 0..5 (Bậc 1 đến 6)
    public int StepsTraveled { get; set; } = 0;  // Tổng số bước đã đi (tối đa 51 trước khi vào HomeTrack)

    public LudoPiece Clone() => new()
    {
        Id = Id,
        Color = Color,
        State = State,
        TrackPosition = TrackPosition,
        HomeTrackStep = HomeTrackStep,
        StepsTraveled = StepsTraveled
    };
}

public sealed record LudoMove(
    int PieceId,
    LudoColor Player,
    bool IsDeploy,
    int TargetPosition,
    bool IsHomeTrack,
    bool IsKick,
    LudoColor? KickedPlayer = null,
    int? KickedPieceId = null
);

public sealed class LudoPlayer
{
    public LudoColor Color { get; set; }
    public string Name { get; set; } = "";
    public bool IsBot { get; set; }
    public bool IsActive { get; set; } = true;
    public List<LudoPiece> Pieces { get; set; } = new();

    public int FinishedCount => Pieces.Count(p => p.State == LudoPieceState.Finished);
    public bool HasWon => FinishedCount == 4;

    public LudoPlayer Clone() => new()
    {
        Color = Color,
        Name = Name,
        IsBot = IsBot,
        IsActive = IsActive,
        Pieces = Pieces.Select(p => p.Clone()).ToList()
    };
}

public enum LudoGameMode
{
    VsBots,
    PassAndPlay,
    Online
}

public enum BotDifficulty
{
    Easy,   // Chọn nước ngẫu nhiên
    Normal  // Ưu tiên: đá quân > vào đích > ra quân > đi xa nhất
}

public sealed class LudoRuleOptions
{
    public bool ThreeSixesLoseTurn { get; set; } = true;     // 3 lần 6 liên tiếp mất lượt
    public bool SafeStartingSquare { get; set; } = true;     // Ô xuất phát là an toàn (không bị đá)
    public int TotalTrackCells { get; set; } = 52;           // 52 ô vòng quanh
    public int HomeTrackCells { get; set; } = 6;             // 6 bậc về chuồng
}
