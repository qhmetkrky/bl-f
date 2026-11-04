using System.Security.Claims;
using Bluff.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Bluff.Api;

[Authorize]
public sealed class GameHub(RoomService rooms, BluffDb db) : Hub<IGameClient>
{
    private string PlayerId => Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomService.PlayerGroup(PlayerId));
        await rooms.ConnectedAsync(PlayerId);
    }

    public override Task OnDisconnectedAsync(Exception? exception) => rooms.DisconnectedAsync(PlayerId);

    public async Task<string> CreateRoom(int turnSeconds) =>
        await rooms.CreateRoomAsync(PlayerId, await PlayerNameAsync(), turnSeconds);

    public async Task<string> QuickPlay(int bots, string level) =>
        await rooms.QuickPlayAsync(PlayerId, await PlayerNameAsync(), bots, ParseLevel(level));

    public async Task JoinRoom(string code) =>
        await rooms.JoinRoomAsync(PlayerId, await PlayerNameAsync(), code);

    public Task AddBot(string level) => rooms.AddBotAsync(PlayerId, ParseLevel(level));

    public Task RemoveSeat(string seatId) => rooms.RemoveSeatAsync(PlayerId, seatId);

    public Task StartGame() => rooms.StartAsync(PlayerId);

    public Task PlayCards(string[] cardIds, string declaredRank) => rooms.PlayAsync(PlayerId, cardIds, declaredRank);

    public Task CallBluff() => rooms.CallBluffAsync(PlayerId);

    public Task AcceptFinish() => rooms.AcceptFinishAsync(PlayerId);

    public Task LeaveRoom() => rooms.LeaveAsync(PlayerId);

    private async Task<string> PlayerNameAsync()
    {
        var player = await db.Players.FindAsync(PlayerId);
        return player?.Name ?? throw new GameRuleException("Oyuncu bulunamadı.");
    }

    private static BotLevel ParseLevel(string level) =>
        Enum.TryParse<BotLevel>(level, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new GameRuleException("Geçersiz zorluk.");
}

/// <summary>Lets rule messages reach the player; SignalR hides other exception text.</summary>
public sealed class GameRuleFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext context,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(context);
        }
        catch (GameRuleException ex)
        {
            throw new HubException(ex.Message);
        }
    }
}
