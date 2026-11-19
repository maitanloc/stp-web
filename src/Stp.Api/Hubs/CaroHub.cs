using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace Stp.Api.Hubs;

/// <summary>
/// Hub SignalR phục vụ chế độ đánh Caro Online thời gian thực giữa anh em đồng nghiệp công sở.
/// </summary>
public sealed class CaroHub : Hub
{
    // Quản lý các phòng chơi đang hoạt động trên bộ nhớ máy chủ
    private static readonly ConcurrentDictionary<string, CaroOnlineRoom> Rooms = new(StringComparer.OrdinalIgnoreCase);

    public record RoomSummary(string RoomCode, string HostName, int PlayerCount, DateTime CreatedAt);

    /// <summary>
    /// Lấy danh sách các phòng đang chờ người chơi tham gia.
    /// </summary>
    public Task<List<RoomSummary>> GetWaitingRooms()
    {
        var list = Rooms.Values
            .Where(r => r.GuestConnectionId == null)
            .Select(r => new RoomSummary(r.RoomCode, r.HostName, 1, r.CreatedAt))
            .OrderByDescending(r => r.CreatedAt)
            .Take(10)
            .ToList();
        return Task.FromResult(list);
    }

    /// <summary>
    /// Tạo một phòng chơi mới với mã phòng ngẫu nhiên 4 ký tự.
    /// </summary>
    public async Task<string> CreateRoom(string playerName)
    {
        var roomCode = GenerateRoomCode();
        var room = new CaroOnlineRoom
        {
            RoomCode = roomCode,
            HostConnectionId = Context.ConnectionId,
            HostName = string.IsNullOrWhiteSpace(playerName) ? "Người chơi X" : playerName.Trim(),
            CurrentTurn = 'X'
        };

        Rooms[roomCode] = room;
        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);
        return roomCode;
    }

    /// <summary>
    /// Tham gia vào một phòng chơi đã có.
    /// </summary>
    public async Task<bool> JoinRoom(string roomCode, string playerName)
    {
        if (Rooms.TryGetValue(roomCode, out var room) && room.GuestConnectionId == null)
        {
            room.GuestConnectionId = Context.ConnectionId;
            room.GuestName = string.IsNullOrWhiteSpace(playerName) ? "Người chơi O" : playerName.Trim();

            await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

            // Báo cho cả 2 bên trận đấu đã bắt đầu
            await Clients.Group(roomCode).SendAsync("GameStarted", new
            {
                room.RoomCode,
                room.HostName,
                room.GuestName,
                YourPiece = Context.ConnectionId == room.HostConnectionId ? "X" : "O"
            });
            return true;
        }
        return false;
    }

    /// <summary>
    /// Người chơi thực hiện nước đi trên bàn cờ.
    /// </summary>
    public async Task MakeMove(string roomCode, int r, int c)
    {
        if (!Rooms.TryGetValue(roomCode, out var room)) return;

        char playerPiece = Context.ConnectionId == room.HostConnectionId ? 'X' : 'O';
        if (room.CurrentTurn != playerPiece) return;
        if (r < 0 || r >= 15 || c < 0 || c >= 15) return;
        if (room.Board[r, c] != ' ') return;

        room.Board[r, c] = playerPiece;
        room.CurrentTurn = playerPiece == 'X' ? 'O' : 'X';

        // Gửi nước đi tới cả 2 người chơi trong phòng
        await Clients.Group(roomCode).SendAsync("MoveReceived", r, c, playerPiece.ToString());
    }

    /// <summary>
    /// Chơi lại ván mới trong cùng phòng.
    /// </summary>
    public async Task RestartRoom(string roomCode)
    {
        if (Rooms.TryGetValue(roomCode, out var room))
        {
            room.Reset();
            await Clients.Group(roomCode).SendAsync("RoomRestarted");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Dọn dẹp phòng khi người chơi ngắt kết nối
        var userRooms = Rooms.Values.Where(r => r.HostConnectionId == Context.ConnectionId || r.GuestConnectionId == Context.ConnectionId).ToList();
        foreach (var r in userRooms)
        {
            await Clients.Group(r.RoomCode).SendAsync("OpponentDisconnected");
            Rooms.TryRemove(r.RoomCode, out _);
        }
        await base.OnDisconnectedAsync(exception);
    }

    private static string GenerateRoomCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var rng = Random.Shared;
        string code;
        do
        {
            code = new string(Enumerable.Range(0, 4).Select(_ => chars[rng.Next(chars.Length)]).ToArray());
        } while (Rooms.ContainsKey(code));
        return code;
    }
}

public sealed class CaroOnlineRoom
{
    public required string RoomCode { get; set; }
    public required string HostConnectionId { get; set; }
    public string HostName { get; set; } = "Người chơi X";
    public string? GuestConnectionId { get; set; }
    public string? GuestName { get; set; }
    public char[,] Board { get; } = new char[15, 15];
    public char CurrentTurn { get; set; } = 'X';
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    public CaroOnlineRoom()
    {
        Reset();
    }

    public void Reset()
    {
        for (int r = 0; r < 15; r++)
            for (int c = 0; c < 15; c++)
                Board[r, c] = ' ';
        CurrentTurn = 'X';
    }
}
