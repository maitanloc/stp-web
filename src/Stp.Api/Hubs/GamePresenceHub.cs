using System.Collections.Concurrent;
using Stp.Core.Games.Caro;
using Stp.Core.Games.Ludo;
using Microsoft.AspNetCore.SignalR;

namespace Stp.Api.Hubs;

public sealed record UserPresence(
    string ConnectionId,
    string UserName,
    string Avatar,
    string GameSlug,
    string Status, // "Idle", "InGame", "Waiting"
    DateTime ConnectedAt
);

public sealed record GameChallenge(
    string ChallengeId,
    string FromConnectionId,
    string FromName,
    string TargetConnectionId,
    string GameSlug,
    DateTime CreatedAt
);

/// <summary>
/// Hub SignalR quản lý Trạng thái Trực tuyến (Presence), Thách đấu (Lobby) và Phòng chơi (Room).
/// Server là NGUỒN SỰ THẬT xác thực toàn bộ nước đi theo đúng quy tắc an toàn công sở.
/// </summary>
public sealed class GamePresenceHub : Hub
{
    private static readonly ConcurrentDictionary<string, UserPresence> Users = new();
    private static readonly ConcurrentDictionary<string, GameChallenge> Challenges = new();
    private static readonly ConcurrentDictionary<string, CaroOnlineSession> CaroRooms = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, LudoOnlineSession> LudoRooms = new(StringComparer.OrdinalIgnoreCase);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Users.TryRemove(Context.ConnectionId, out var user))
        {
            await Clients.Group(user.GameSlug).SendAsync("UserLeft", Context.ConnectionId);
        }

        // Dọn dẹp các lời mời thách đấu liên quan
        var expired = Challenges.Values
            .Where(c => c.FromConnectionId == Context.ConnectionId || c.TargetConnectionId == Context.ConnectionId)
            .ToList();
        foreach (var c in expired)
        {
            Challenges.TryRemove(c.ChallengeId, out _);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Tham gia sảnh chờ trực tuyến của một game cụ thể (caro, co-ca-ngua).
    /// </summary>
    public async Task JoinLobby(string gameSlug, string userName, string avatar)
    {
        var presence = new UserPresence(
            Context.ConnectionId,
            string.IsNullOrWhiteSpace(userName) ? "Đồng nghiệp" : userName.Trim(),
            string.IsNullOrWhiteSpace(avatar) ? "chick_0" : avatar,
            gameSlug,
            "Idle",
            DateTime.UtcNow
        );

        Users[Context.ConnectionId] = presence;
        await Groups.AddToGroupAsync(Context.ConnectionId, gameSlug);

        // Trả về danh sách người đang online trong sảnh này
        var onlineList = Users.Values.Where(u => u.GameSlug == gameSlug).ToList();
        await Clients.Caller.SendAsync("LobbyState", onlineList);
        await Clients.GroupExcept(gameSlug, Context.ConnectionId).SendAsync("UserJoined", presence);
    }

    /// <summary>
    /// Gửi lời mời thách đấu tới một đồng nghiệp cụ thể (hết hạn sau 30s).
    /// </summary>
    public async Task SendChallenge(string targetConnectionId, string gameSlug)
    {
        if (!Users.TryGetValue(Context.ConnectionId, out var sender)) return;
        if (!Users.TryGetValue(targetConnectionId, out var target)) return;

        var challengeId = Guid.NewGuid().ToString("N")[..8];
        var challenge = new GameChallenge(
            challengeId,
            Context.ConnectionId,
            sender.UserName,
            targetConnectionId,
            gameSlug,
            DateTime.UtcNow
        );

        Challenges[challengeId] = challenge;

        // Báo cho đối thủ hiển thị Toast Thách đấu (30s)
        await Clients.Client(targetConnectionId).SendAsync("ChallengeReceived", new
        {
            challengeId,
            fromId = Context.ConnectionId,
            fromName = sender.UserName,
            gameSlug,
            timeoutSec = 30
        });
    }

    /// <summary>
    /// Chấp nhận thách đấu: tự động tạo phòng chơi và kéo cả hai người vào.
    /// </summary>
    public async Task AcceptChallenge(string challengeId)
    {
        if (!Challenges.TryRemove(challengeId, out var challenge)) return;

        // Kiểm tra hết hạn 30s
        if ((DateTime.UtcNow - challenge.CreatedAt).TotalSeconds > 30)
        {
            await Clients.Caller.SendAsync("ChallengeExpired");
            return;
        }

        var roomId = "ROOM_" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        if (challenge.GameSlug == "caro")
        {
            var session = new CaroOnlineSession(roomId, challenge.FromConnectionId, Context.ConnectionId);
            CaroRooms[roomId] = session;
        }
        else if (challenge.GameSlug == "co-ca-ngua")
        {
            var session = new LudoOnlineSession(roomId, [challenge.FromConnectionId, Context.ConnectionId]);
            LudoRooms[roomId] = session;
        }

        await Groups.AddToGroupAsync(challenge.FromConnectionId, roomId);
        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);

        // Cập nhật trạng thái người dùng
        if (Users.TryGetValue(challenge.FromConnectionId, out var u1)) Users[challenge.FromConnectionId] = u1 with { Status = "InGame" };
        if (Users.TryGetValue(Context.ConnectionId, out var u2)) Users[Context.ConnectionId] = u2 with { Status = "InGame" };

        await Clients.Group(challenge.GameSlug).SendAsync("UserStatusChanged", challenge.FromConnectionId, "InGame");
        await Clients.Group(challenge.GameSlug).SendAsync("UserStatusChanged", Context.ConnectionId, "InGame");

        // Báo cho cả 2 người bắt đầu vào phòng
        await Clients.Group(roomId).SendAsync("GameStarted", new
        {
            roomId,
            gameSlug = challenge.GameSlug,
            hostId = challenge.FromConnectionId,
            guestId = Context.ConnectionId
        });
    }

    /// <summary>
    /// Từ chối thách đấu.
    /// </summary>
    public async Task DeclineChallenge(string challengeId)
    {
        if (Challenges.TryRemove(challengeId, out var challenge))
        {
            await Clients.Client(challenge.FromConnectionId).SendAsync("ChallengeDeclined");
        }
    }

    /// <summary>
    /// Chat nhanh bằng bộ emoji pixel có sẵn (1..10, chống chat tự do theo chỉ thị [13]).
    /// </summary>
    public async Task SendQuickEmoji(string roomId, int emojiId)
    {
        if (emojiId < 1 || emojiId > 10) return;
        string senderName = Users.TryGetValue(Context.ConnectionId, out var u) ? u.UserName : "Ẩn danh";
        await Clients.Group(roomId).SendAsync("EmojiReceived", senderName, emojiId);
    }

    // ==========================================
    // CARO ONLINE (Server xác thực tuyệt đối)
    // ==========================================
    public async Task MakeCaroMove(string roomId, int r, int c)
    {
        if (!CaroRooms.TryGetValue(roomId, out var room)) return;

        char piece = Context.ConnectionId == room.HostId ? 'X' : 'O';
        if (room.Engine.CurrentTurn != piece) return;

        bool success = room.Engine.MakeMove(r, c);
        if (success)
        {
            await Clients.Group(roomId).SendAsync("CaroStateUpdated", room.Engine.ToDto());
        }
    }

    public async Task SurrenderCaro(string roomId)
    {
        if (!CaroRooms.TryGetValue(roomId, out var room)) return;
        char loser = Context.ConnectionId == room.HostId ? 'X' : 'O';
        char winner = loser == 'X' ? 'O' : 'X';
        await Clients.Group(roomId).SendAsync("CaroGameOver", new { winner = winner.ToString(), reason = "Đối thủ đã đầu hàng!" });
    }

    // ==========================================
    // LUDO ONLINE (Server xác thực tuyệt đối)
    // ==========================================
    public async Task RollLudoDice(string roomId)
    {
        if (!LudoRooms.TryGetValue(roomId, out var room)) return;
        int playerIdx = room.PlayerIds.IndexOf(Context.ConnectionId);
        if (playerIdx != room.Engine.CurrentPlayerIndex) return;

        var (dice, moves) = room.Engine.RollDice();
        await Clients.Group(roomId).SendAsync("LudoDiceRolled", dice, moves);
    }

    public async Task SelectLudoMove(string roomId, int pieceId)
    {
        if (!LudoRooms.TryGetValue(roomId, out var room)) return;
        int playerIdx = room.PlayerIds.IndexOf(Context.ConnectionId);
        if (playerIdx != room.Engine.CurrentPlayerIndex) return;

        var legalMoves = room.Engine.GetLegalMoves(room.Engine.CurrentTurnColor, room.Engine.LastDiceRoll);
        var move = legalMoves.FirstOrDefault(m => m.PieceId == pieceId);
        if (move != null)
        {
            room.Engine.ApplyMove(move);
            await Clients.Group(roomId).SendAsync("LudoStateUpdated", room.Engine.ToDto());
        }
    }
}

public sealed class CaroOnlineSession
{
    public string RoomId { get; }
    public string HostId { get; }
    public string GuestId { get; }
    public CaroEngine Engine { get; } = new() { Mode = CaroGameMode.Online };
    public DateTime LastMoveAt { get; set; } = DateTime.UtcNow;

    public CaroOnlineSession(string roomId, string hostId, string guestId)
    {
        RoomId = roomId;
        HostId = hostId;
        GuestId = guestId;
    }
}

public sealed class LudoOnlineSession
{
    public string RoomId { get; }
    public List<string> PlayerIds { get; }
    public LudoEngine Engine { get; } = new() { Mode = LudoGameMode.Online };
    public DateTime LastMoveAt { get; set; } = DateTime.UtcNow;

    public LudoOnlineSession(string roomId, List<string> playerIds)
    {
        RoomId = roomId;
        PlayerIds = playerIds;
        Engine.InitializePlayers(playerIds.Count, includeBots: false);
    }
}
