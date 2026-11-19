namespace Stp.Api.Hubs;

public interface IGameRoom
{
    string RoomId { get; }
    string GameSlug { get; }
    bool IsStarted { get; }
    bool IsFinished { get; }
    int TurnSecondsLeft { get; }
    List<RoomPlayerInfo> Players { get; }
    List<string> Spectators { get; }

    Task<bool> AddPlayerAsync(string connectionId, string name, string avatar);
    Task RemovePlayerAsync(string connectionId);
    Task<bool> HandleMoveAsync(string connectionId, object moveData);
    Task<bool> HandleSurrenderAsync(string connectionId);
    Task<bool> HandleDrawRequestAsync(string connectionId, bool accept);
    object GetRoomStateDto();
}

public sealed record RoomPlayerInfo(
    string ConnectionId,
    string Name,
    string Avatar,
    int SeatIndex,
    bool IsActive,
    bool IsDisconnected,
    DateTime? DisconnectedAt
);
