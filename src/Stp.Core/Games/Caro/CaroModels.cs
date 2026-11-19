namespace Stp.Core.Games.Caro;

public enum CaroGameMode
{
    VsAi,
    PassAndPlay, // 2 người chơi chung 1 máy
    Online       // Chơi online qua SignalR với đồng nghiệp
}

public enum CaroAiLevel
{
    Easy,
    Medium,
    Hard
}

public sealed record CaroWinResult(
    bool HasWon,
    char Winner,
    IReadOnlyList<(int R, int C)> WinningLine
);

public sealed record CaroMove(int R, int C, char Player);

public sealed record CaroSnapshot(
    char[,] Board,
    char CurrentTurn,
    CaroGameMode Mode,
    CaroAiLevel AiLevel,
    bool IsThinking,
    bool IsGameOver,
    CaroWinResult? WinResult,
    CaroMove? LastMove,
    int MoveCount
);
