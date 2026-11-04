using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Bluff.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;

namespace Bluff.Tests;

public sealed class BluffApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"bluff-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Bluff"] = $"Data Source={_dbPath};Pooling=False",
        }));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        File.Delete(_dbPath);
    }
}

public sealed class ApiTests(BluffApiFactory factory) : IClassFixture<BluffApiFactory>
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private sealed record Leader(string Id, string Name, int Wins, int Losses);

    private sealed class TestPlayer : IAsyncDisposable
    {
        private readonly ConcurrentQueue<RoomView> _updates = new();
        private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public required string Id { get; init; }
        public required string Token { get; init; }
        public required HubConnection Hub { get; init; }
        public string? ClosedReason { get; private set; }
        public RoomView? Room => _updates.LastOrDefault();

        public void Attach()
        {
            Hub.On<RoomView>("RoomUpdated", view =>
            {
                _updates.Enqueue(view);
                _changed.TrySetResult();
            });
            Hub.On<string>("RoomClosed", reason =>
            {
                ClosedReason = reason;
                _changed.TrySetResult();
            });
        }

        public async Task<RoomView> WaitForAsync(Func<RoomView, bool> condition)
        {
            using var timeout = new CancellationTokenSource(Wait);
            while (true)
            {
                var signal = _changed.Task;
                if (_updates.LastOrDefault(condition) is { } view)
                    return view;

                await signal.WaitAsync(timeout.Token);
                _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public ValueTask DisposeAsync() => Hub.DisposeAsync();
    }

    private async Task<TestPlayer> ConnectAsync(string name)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/players", new { name });
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<NewPlayerResponse>())!;

        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/game"), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                o.AccessTokenProvider = () => Task.FromResult<string?>(created.Token);
            })
            .Build();

        var player = new TestPlayer { Id = created.Id, Token = created.Token, Hub = hub };
        player.Attach();
        await hub.StartAsync();
        return player;
    }

    [Fact]
    public async Task Hub_rejects_connections_without_a_valid_token()
    {
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/game"), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                o.AccessTokenProvider = () => Task.FromResult<string?>("sahte");
            })
            .Build();

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => hub.StartAsync());
    }

    [Fact]
    public async Task Invalid_name_is_rejected()
    {
        var client = factory.CreateClient();

        var empty = await client.PostAsJsonAsync("/api/players", new { name = "  " });
        var longName = await client.PostAsJsonAsync("/api/players", new { name = new string('x', 17) });

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, longName.StatusCode);
    }

    [Fact]
    public async Task Rename_requires_the_token()
    {
        var client = factory.CreateClient();
        var anonymous = await client.PutAsJsonAsync("/api/players/me", new { name = "Yeni" });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        await using var player = await ConnectAsync("Eski");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", player.Token);
        var renamed = await client.PutAsJsonAsync("/api/players/me", new { name = "Yeni" });

        Assert.Equal("Yeni", (await renamed.Content.ReadFromJsonAsync<PlayerResponse>())!.Name);
    }

    [Fact]
    public async Task Rule_errors_reach_the_client_in_turkish()
    {
        await using var player = await ConnectAsync("Hatalı");

        var error = await Assert.ThrowsAsync<HubException>(() => player.Hub.InvokeAsync<object>("JoinRoom", "YOKYK"));

        Assert.Contains("Oda bulunamadı", error.Message);
    }

    [Fact]
    public async Task Two_players_play_through_lobby_challenge_and_finish_with_scores_saved()
    {
        await using var ali = await ConnectAsync("Ali");
        await using var ayse = await ConnectAsync("Ayşe");

        var code = await ali.Hub.InvokeAsync<string>("CreateRoom", 15);
        await ayse.Hub.InvokeAsync("JoinRoom", code.ToLowerInvariant());
        await ali.WaitForAsync(r => r.Seats.Count == 2);

        await ali.Hub.InvokeAsync("StartGame");
        var view = await ali.WaitForAsync(r => r.Status == "playing");
        Assert.Equal(26, view.Game!.Hand.Count);
        Assert.Equal(26, view.Seats.Single(s => s.Id == ayse.Id).CardCount);

        // Drive the whole game over the hub: open, or call whatever the opponent claimed.
        var players = new[] { ali, ayse };
        for (var moves = 0; moves < 400; moves++)
        {
            var state = await ali.WaitForAsync(r => r.Version > view.Version || r.Status == "finished");
            view = state;
            if (state.Status == "finished") break;

            var current = players.Single(p => p.Id == state.Game!.CurrentPlayerId);
            var mine = await current.WaitForAsync(r => r.Version >= state.Version);
            var game = mine.Game!;

            if (game.LastClaim is { } claim)
            {
                await current.Hub.InvokeAsync("CallBluff");
            }
            else
            {
                var card = game.Hand[0];
                await current.Hub.InvokeAsync("PlayCards", new[] { card }, card.Split('-')[0]);
            }
        }

        var final = await ali.WaitForAsync(r => r.Status == "finished");
        Assert.NotNull(final.Game!.WinnerId);

        var board = await factory.CreateClient().GetFromJsonAsync<List<Leader>>("/api/leaderboard");
        var rows = board!.Where(l => l.Id == ali.Id || l.Id == ayse.Id).ToList();
        Assert.Equal(1, rows.Sum(r => r.Wins));
        Assert.Equal(1, rows.Sum(r => r.Losses));
    }

    [Fact]
    public async Task Quick_play_starts_against_bots_and_bots_answer_in_real_time()
    {
        await using var player = await ConnectAsync("Solo");

        await player.Hub.InvokeAsync<string>("QuickPlay", 3, "hard");

        var view = await player.WaitForAsync(r => r.Status == "playing");
        Assert.Equal(4, view.Seats.Count);
        Assert.Equal(3, view.Seats.Count(s => s.IsBot));
        Assert.Equal(13, view.Game!.Hand.Count);

        if (view.Game.CurrentPlayerId == player.Id)
        {
            var card = view.Game.Hand[0];
            await player.Hub.InvokeAsync("PlayCards", new[] { card }, card.Split('-')[0]);
        }

        await player.WaitForAsync(r => r.Game!.CurrentPlayerId == player.Id && r.Version > view.Version);
    }

    [Fact]
    public async Task Reconnecting_with_the_same_token_resumes_the_room()
    {
        await using var player = await ConnectAsync("Geri Dönen");
        var code = await player.Hub.InvokeAsync<string>("QuickPlay", 1, "easy");
        await player.WaitForAsync(r => r.Status == "playing");

        await player.Hub.StopAsync();
        await player.Hub.StartAsync();

        var view = await player.WaitForAsync(r => r.Seats.Single(s => s.Id == player.Id).Connected);
        Assert.Equal(code, view.Code);
    }

    [Fact]
    public async Task Kicked_player_is_told_and_loses_the_room()
    {
        await using var host = await ConnectAsync("Sahip");
        await using var guest = await ConnectAsync("Misafir");
        var code = await host.Hub.InvokeAsync<string>("CreateRoom", 30);
        await guest.Hub.InvokeAsync("JoinRoom", code);

        await host.Hub.InvokeAsync("RemoveSeat", guest.Id);

        await guest.WaitForAsync(_ => guest.ClosedReason is { Length: > 0 });
        await Assert.ThrowsAsync<HubException>(() => guest.Hub.InvokeAsync("StartGame"));
    }
}
