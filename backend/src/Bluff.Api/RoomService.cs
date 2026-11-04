using System.Collections.Concurrent;
using System.Security.Cryptography;
using Bluff.Core;
using Microsoft.AspNetCore.SignalR;

namespace Bluff.Api;

public interface IGameClient
{
    Task RoomUpdated(RoomView room);

    /// <summary>The player no longer has a room. An empty reason means nothing needs explaining.</summary>
    Task RoomClosed(string reason);
}

/// <summary>Owns the live rooms. Each room's state is guarded by its lock; updates go out after it is released.</summary>
public sealed class RoomService(IHubContext<GameHub, IGameClient> hub, ScoreRecorder scores, TimeProvider clock)
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 5;
    private static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(10);
    private static readonly int[] AllowedTurnSeconds = [15, 30, 60];

    private readonly ConcurrentDictionary<string, Room> _rooms = new();
    private readonly ConcurrentDictionary<string, Room> _playerRooms = new();

    public static string PlayerGroup(string playerId) => $"player:{playerId}";

    private DateTimeOffset Now => clock.GetUtcNow();

    public async Task<string> CreateRoomAsync(string playerId, string name, int turnSeconds)
    {
        if (!AllowedTurnSeconds.Contains(turnSeconds))
            throw new GameRuleException("Geçersiz tur süresi.");

        await LeaveCurrentRoomAsync(playerId);

        var room = NewRoom(playerId, turnSeconds);
        await UpdateAsync(room, r =>
        {
            r.AddHuman(playerId, name);
            r.ConnectionChanged(playerId, 1, Now);
            _playerRooms[playerId] = r;
        });
        return room.Code;
    }

    public async Task<string> QuickPlayAsync(string playerId, string name, int bots, BotLevel level)
    {
        if (bots is < 1 or >= Game.MaxPlayers)
            throw new GameRuleException("Geçersiz bot sayısı.");

        await LeaveCurrentRoomAsync(playerId);

        var room = NewRoom(playerId, turnSeconds: 30);
        await UpdateAsync(room, r =>
        {
            r.AddHuman(playerId, name);
            r.ConnectionChanged(playerId, 1, Now);
            _playerRooms[playerId] = r;
            for (var i = 0; i < bots; i++)
                r.AddBot(playerId, level);
            r.Start(playerId, Now);
        });
        return room.Code;
    }

    public async Task JoinRoomAsync(string playerId, string name, string code)
    {
        var normalized = code.Trim().ToUpperInvariant();
        if (!_rooms.TryGetValue(normalized, out var room))
            throw new GameRuleException("Oda bulunamadı.");

        if (_playerRooms.TryGetValue(playerId, out var current) && current == room)
        {
            await SendCurrentAsync(room, playerId);
            return;
        }

        await LeaveCurrentRoomAsync(playerId);
        await UpdateAsync(room, r =>
        {
            r.AddHuman(playerId, name);
            r.ConnectionChanged(playerId, 1, Now);
            _playerRooms[playerId] = r;
        });
    }

    public Task AddBotAsync(string playerId, BotLevel level) =>
        UpdateAsync(RoomOf(playerId), r => r.AddBot(playerId, level));

    public async Task RemoveSeatAsync(string playerId, string seatId)
    {
        await UpdateAsync(RoomOf(playerId), r => r.RemoveSeat(playerId, seatId));
        if (_playerRooms.TryRemove(seatId, out _))
            await hub.Clients.Group(PlayerGroup(seatId)).RoomClosed("Oda sahibi seni masadan çıkardı.");
    }

    public Task StartAsync(string playerId) =>
        UpdateAsync(RoomOf(playerId), r => r.Start(playerId, Now));

    public Task PlayAsync(string playerId, IReadOnlyList<string> cardIds, string declaredRank) =>
        UpdateAsync(RoomOf(playerId), r => r.Play(playerId, cardIds, declaredRank, Now));

    public Task CallBluffAsync(string playerId) =>
        UpdateAsync(RoomOf(playerId), r => r.CallBluff(playerId, Now));

    public Task AcceptFinishAsync(string playerId) =>
        UpdateAsync(RoomOf(playerId), r => r.AcceptFinish(playerId));

    public Task LeaveAsync(string playerId) => LeaveCurrentRoomAsync(playerId);

    public async Task ConnectedAsync(string playerId)
    {
        if (_playerRooms.TryGetValue(playerId, out var room))
            await UpdateAsync(room, r => r.ConnectionChanged(playerId, 1, Now));
        else
            await hub.Clients.Group(PlayerGroup(playerId)).RoomClosed("");
    }

    public async Task DisconnectedAsync(string playerId)
    {
        if (_playerRooms.TryGetValue(playerId, out var room))
            await UpdateAsync(room, r => r.ConnectionChanged(playerId, -1, Now));
    }

    /// <summary>One game-loop pass: due bot moves, expired turns, dead rooms.</summary>
    public async Task TickAsync()
    {
        foreach (var room in _rooms.Values)
        {
            bool changed, abandoned;
            lock (room.Gate)
            {
                changed = room.Tick(Now);
                abandoned = room.IsAbandoned(Now, IdleLimit);
            }

            if (changed)
                await PublishAsync(room);
            if (abandoned)
                Remove(room);
        }
    }

    private Room NewRoom(string hostId, int turnSeconds)
    {
        while (true)
        {
            var code = RandomNumberGenerator.GetString(CodeAlphabet, CodeLength);
            var room = new Room(code, hostId, turnSeconds, new Random(), Now);
            if (_rooms.TryAdd(code, room))
                return room;
        }
    }

    private Room RoomOf(string playerId) =>
        _playerRooms.TryGetValue(playerId, out var room) ? room : throw new GameRuleException("Bir odada değilsin.");

    private async Task UpdateAsync(Room room, Action<Room> change)
    {
        lock (room.Gate)
        {
            change(room);
            room.Touch(Now);
        }
        await PublishAsync(room);
    }

    private async Task PublishAsync(Room room)
    {
        IReadOnlyList<(string PlayerId, RoomView View)> views;
        GameOutcome? outcome;
        lock (room.Gate)
        {
            views = room.Snapshot(Now);
            outcome = room.TakeOutcome();
        }

        foreach (var (playerId, view) in views)
            await hub.Clients.Group(PlayerGroup(playerId)).RoomUpdated(view);

        if (outcome is not null)
            await scores.RecordAsync(outcome);
    }

    private async Task SendCurrentAsync(Room room, string playerId)
    {
        RoomView view;
        lock (room.Gate)
            view = room.ViewFor(playerId, Now);
        await hub.Clients.Group(PlayerGroup(playerId)).RoomUpdated(view);
    }

    private async Task LeaveCurrentRoomAsync(string playerId)
    {
        if (!_playerRooms.TryRemove(playerId, out var room))
            return;

        bool empty;
        lock (room.Gate)
        {
            room.Leave(playerId, Now);
            empty = !room.HasHuman;
        }

        if (empty)
            Remove(room);
        else
            await PublishAsync(room);
    }

    private void Remove(Room room)
    {
        _rooms.TryRemove(room.Code, out _);
        foreach (var (playerId, value) in _playerRooms)
        {
            if (value == room)
                _playerRooms.TryRemove(playerId, out _);
        }
    }
}

public sealed class GameLoopService(RoomService rooms, ILogger<GameLoopService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await rooms.TickAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Oyun döngüsü hatası.");
            }
        }
    }
}
