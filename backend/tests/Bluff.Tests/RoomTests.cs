using System.Text.Json;
using Bluff.Api;
using Bluff.Core;

namespace Bluff.Tests;

public class RoomTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static Room NewRoom(int humans = 2)
    {
        var room = new Room("ABCDE", "h0", 30, new Random(7), T0);
        for (var i = 0; i < humans; i++)
        {
            room.AddHuman($"h{i}", $"Oyuncu {i}");
            room.ConnectionChanged($"h{i}", 1, T0);
        }
        return room;
    }

    private static string FirstCard(Room room, string id) => room.Game!.HandOf(id)[0].Id;

    [Fact]
    public void Only_the_host_can_start_and_a_lone_player_cannot()
    {
        var alone = NewRoom(1);
        Assert.Throws<GameRuleException>(() => alone.Start("h0", T0));

        var room = NewRoom();
        Assert.Throws<GameRuleException>(() => room.Start("h1", T0));
    }

    [Fact]
    public void Table_holds_four_seats()
    {
        var room = NewRoom(4);

        Assert.Throws<GameRuleException>(() => room.AddHuman("h4", "Fazla"));
        Assert.Throws<GameRuleException>(() => room.AddBot("h0", BotLevel.Easy));
    }

    [Fact]
    public void Cannot_join_a_running_game()
    {
        var room = NewRoom();
        room.Start("h0", T0);

        Assert.Throws<GameRuleException>(() => room.AddHuman("h2", "Geç Kalan"));
    }

    [Fact]
    public void A_view_never_contains_opponent_cards_or_the_pile()
    {
        var room = NewRoom();
        room.Start("h0", T0);
        var current = room.Game!.CurrentPlayerId;
        var other = room.Game.PlayerOrder.Single(id => id != current);
        var played = FirstCard(room, current);
        room.Play(current, [played], "K", T0);

        var json = JsonSerializer.Serialize(room.ViewFor(other, T0));

        Assert.DoesNotContain($"\"{played}\"", json);
        foreach (var card in room.Game.HandOf(current))
            Assert.DoesNotContain($"\"{card.Id}\"", json);
        foreach (var card in room.Game.HandOf(other))
            Assert.Contains($"\"{card.Id}\"", json);
    }

    [Fact]
    public void Silent_human_is_moved_for_when_the_turn_timer_runs_out()
    {
        var room = NewRoom();
        room.Start("h0", T0);
        var first = room.Game!.CurrentPlayerId;

        Assert.False(room.Tick(T0.AddSeconds(29)));
        Assert.Equal(first, room.Game.CurrentPlayerId);

        Assert.True(room.Tick(T0.AddSeconds(31)));
        Assert.NotEqual(first, room.Game.CurrentPlayerId);
    }

    [Fact]
    public void Disconnected_player_gets_a_short_grace_period_only()
    {
        var room = NewRoom();
        room.Start("h0", T0);
        var current = room.Game!.CurrentPlayerId;

        room.ConnectionChanged(current, -1, T0);

        Assert.False(room.Tick(T0.AddSeconds(4)));
        Assert.True(room.Tick(T0.AddSeconds(6)));
    }

    [Fact]
    public void Reconnecting_restores_the_full_turn_timer()
    {
        var room = NewRoom();
        room.Start("h0", T0);
        var current = room.Game!.CurrentPlayerId;

        room.ConnectionChanged(current, -1, T0);
        room.ConnectionChanged(current, 1, T0.AddSeconds(2));

        Assert.False(room.Tick(T0.AddSeconds(10)));
        Assert.Equal(current, room.Game.CurrentPlayerId);
    }

    [Fact]
    public void Game_holds_while_no_human_is_connected()
    {
        var room = NewRoom(1);
        room.AddBot("h0", BotLevel.Easy);
        room.Start("h0", T0);
        room.ConnectionChanged("h0", -1, T0);

        Assert.False(room.Tick(T0.AddMinutes(5)));
    }

    [Fact]
    public void Bot_takes_over_a_seat_left_mid_game_and_the_leaver_scores_a_loss()
    {
        var room = NewRoom(3);
        room.Start("h0", T0);

        room.Leave("h2", T0);

        var seat = room.Seats.Single(s => s.Id == "h2");
        Assert.True(seat.IsBot);
        Assert.DoesNotContain("h2", room.HumanIds);

        var time = T0;
        for (var i = 0; i < 20_000 && room.Status == RoomStatus.Playing; i++)
        {
            time = time.AddSeconds(40);
            room.Tick(time);
        }

        Assert.Equal(RoomStatus.Finished, room.Status);
        var outcome = room.TakeOutcome()!;
        Assert.Contains("h2", outcome.LoserIds);
        Assert.DoesNotContain("h2", outcome.WinnerIds);
        Assert.Null(room.TakeOutcome());
    }

    [Fact]
    public void Host_role_passes_on_when_the_host_leaves()
    {
        var room = NewRoom();

        room.Leave("h0", T0);

        Assert.Equal("h1", room.HostId);
    }

    [Fact]
    public void Rematch_deals_a_new_game_and_keeps_table_wins()
    {
        var room = NewRoom();
        room.Start("h0", T0);
        var time = T0;
        while (room.Status == RoomStatus.Playing)
        {
            time = time.AddSeconds(40);
            room.Tick(time);
        }
        Assert.Equal(1, room.Seats.Sum(s => s.Wins));

        room.Start("h0", time);

        Assert.Equal(RoomStatus.Playing, room.Status);
        Assert.Equal(1, room.Seats.Sum(s => s.Wins));
        Assert.Equal(26, room.Game!.HandOf("h0").Count);
    }

    [Fact]
    public void Versions_increase_with_every_change()
    {
        var room = NewRoom();
        var before = room.Version;

        room.Start("h0", T0);

        Assert.True(room.Version > before);
    }
}
