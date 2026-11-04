using Bluff.Core;

namespace Bluff.Tests;

public class GameRulesTests
{
    private static Card C(Rank rank, Suit suit = Suit.Spades) => new(rank, suit);

    private static Game NewGame(List<Card> a, List<Card> b, List<Card>? c = null)
    {
        var hands = new Dictionary<string, List<Card>> { ["a"] = a, ["b"] = b };
        if (c is not null) hands["c"] = c;
        return new Game([.. hands.Keys], hands, firstPlayerIndex: 0);
    }

    [Theory]
    [InlineData(2, 26, 26)]
    [InlineData(3, 18, 17)]
    [InlineData(4, 13, 13)]
    public void Deal_uses_whole_deck_as_evenly_as_possible(int players, int most, int least)
    {
        var ids = Enumerable.Range(0, players).Select(i => $"p{i}").ToList();
        var game = new Game(ids, new Random(1));

        var hands = ids.Select(game.HandOf).ToList();
        Assert.Equal(most, hands.Max(h => h.Count));
        Assert.Equal(least, hands.Min(h => h.Count));
        Assert.Equal(52, hands.SelectMany(h => h).Distinct().Count());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Player_count_must_be_two_to_four(int players)
    {
        var ids = Enumerable.Range(0, players).Select(i => $"p{i}").ToList();
        Assert.Throws<GameRuleException>(() => new Game(ids, new Random(1)));
    }

    [Fact]
    public void Player_may_declare_a_rank_they_did_not_play()
    {
        var game = NewGame([C(Rank.Ace), C(Rank.Ace, Suit.Hearts), C(Rank.Two)], [C(Rank.Three)]);

        game.Play("a", [C(Rank.Ace), C(Rank.Ace, Suit.Hearts)], Rank.King);

        Assert.False(game.LastMove!.IsTruthful);
        Assert.Equal(Rank.King, game.RoundRank);
        Assert.Equal("b", game.CurrentPlayerId);
    }

    [Fact]
    public void Followers_must_declare_the_round_rank()
    {
        var game = NewGame([C(Rank.Ace), C(Rank.Two)], [C(Rank.Three), C(Rank.Four)]);
        game.Play("a", [C(Rank.Ace)], Rank.King);

        Assert.Throws<GameRuleException>(() => game.Play("b", [C(Rank.Three)], Rank.Queen));
    }

    [Fact]
    public void Moves_out_of_turn_are_rejected()
    {
        var game = NewGame([C(Rank.Ace), C(Rank.Two)], [C(Rank.Three)]);

        Assert.Throws<GameRuleException>(() => game.Play("b", [C(Rank.Three)], Rank.Three));
        Assert.Throws<GameRuleException>(() => game.CallBluff("b"));
    }

    [Fact]
    public void Cannot_play_a_card_you_do_not_hold()
    {
        var game = NewGame([C(Rank.Ace), C(Rank.Two)], [C(Rank.Three)]);

        Assert.Throws<GameRuleException>(() => game.Play("a", [C(Rank.Three)], Rank.Three));
        Assert.Equal(2, game.HandOf("a").Count);
    }

    [Fact]
    public void Move_size_is_one_to_four_distinct_cards()
    {
        var hand = Deck.Standard52().Take(6).ToList();
        var game = NewGame(hand, [C(Rank.Three, Suit.Hearts)]);

        Assert.Throws<GameRuleException>(() => game.Play("a", [], Rank.Two));
        Assert.Throws<GameRuleException>(() => game.Play("a", [.. hand.Take(5)], Rank.Two));
        Assert.Throws<GameRuleException>(() => game.Play("a", [hand[0], hand[0]], Rank.Two));
    }

    [Fact]
    public void Cannot_challenge_before_any_card_is_played()
    {
        var game = NewGame([C(Rank.Ace)], [C(Rank.Three)]);

        Assert.Throws<GameRuleException>(() => game.CallBluff("a"));
    }

    [Fact]
    public void Caught_bluffer_takes_pile_and_challenger_opens_next_round()
    {
        var game = NewGame([C(Rank.Ace), C(Rank.Two)], [C(Rank.Three), C(Rank.Four)]);
        game.Play("a", [C(Rank.Ace)], Rank.King);

        var result = game.CallBluff("b");

        Assert.True(result.WasBluff);
        Assert.Equal("a", result.LoserId);
        Assert.Equal(2, game.HandOf("a").Count);
        Assert.Equal(0, game.PileCount);
        Assert.Null(game.RoundRank);
        Assert.Equal("b", game.CurrentPlayerId);
    }

    [Fact]
    public void Wrong_challenger_takes_pile_and_truthful_player_opens_next_round()
    {
        var game = NewGame([C(Rank.King), C(Rank.Two)], [C(Rank.Three)]);
        game.Play("a", [C(Rank.King)], Rank.King);

        var result = game.CallBluff("b");

        Assert.False(result.WasBluff);
        Assert.Equal("b", result.LoserId);
        Assert.Equal(2, game.HandOf("b").Count);
        Assert.Equal("a", game.CurrentPlayerId);
    }

    [Fact]
    public void Challenge_reveals_only_the_last_move_but_transfers_the_whole_pile()
    {
        var game = NewGame([C(Rank.King), C(Rank.Five)], [C(Rank.Queen), C(Rank.Six)]);
        game.Play("a", [C(Rank.King)], Rank.King);
        game.Play("b", [C(Rank.Queen)], Rank.King);

        var result = game.CallBluff("a");

        Assert.Equal([C(Rank.Queen)], result.Revealed);
        Assert.Equal(2, result.CardsTaken);
        Assert.Equal(3, game.HandOf("b").Count);
    }

    [Fact]
    public void One_bad_card_in_a_move_makes_it_a_bluff()
    {
        var game = NewGame([C(Rank.King), C(Rank.Two), C(Rank.Five)], [C(Rank.Three)]);
        game.Play("a", [C(Rank.King), C(Rank.Two)], Rank.King);

        Assert.True(game.CallBluff("b").WasBluff);
    }

    [Fact]
    public void Emptied_hand_does_not_win_until_next_player_decides()
    {
        var game = NewGame([C(Rank.King)], [C(Rank.Three)]);
        game.Play("a", [C(Rank.King)], Rank.King);

        Assert.Equal(GamePhase.Playing, game.Phase);
        Assert.True(game.AwaitingFinish);
        Assert.Throws<GameRuleException>(() => game.Play("b", [C(Rank.Three)], Rank.King));
    }

    [Fact]
    public void Accepting_a_finish_ends_the_game()
    {
        var game = NewGame([C(Rank.King)], [C(Rank.Three)]);
        game.Play("a", [C(Rank.King)], Rank.King);

        game.AcceptFinish("b");

        Assert.Equal(GamePhase.Finished, game.Phase);
        Assert.Equal("a", game.WinnerId);
    }

    [Fact]
    public void Truthful_finish_survives_a_challenge()
    {
        var game = NewGame([C(Rank.King)], [C(Rank.Three)]);
        game.Play("a", [C(Rank.King)], Rank.King);

        game.CallBluff("b");

        Assert.Equal("a", game.WinnerId);
    }

    [Fact]
    public void Bluffed_finish_caught_by_challenge_continues_the_game()
    {
        var game = NewGame([C(Rank.Ace)], [C(Rank.Three)], [C(Rank.Four)]);
        game.Play("a", [C(Rank.Ace)], Rank.King);

        game.CallBluff("b");

        Assert.Equal(GamePhase.Playing, game.Phase);
        Assert.Null(game.WinnerId);
        Assert.Single(game.HandOf("a"));
        Assert.Equal("b", game.CurrentPlayerId);
    }

    [Fact]
    public void Accept_needs_a_finish_to_accept()
    {
        var game = NewGame([C(Rank.King), C(Rank.Two)], [C(Rank.Three)]);
        game.Play("a", [C(Rank.King)], Rank.King);

        Assert.Throws<GameRuleException>(() => game.AcceptFinish("b"));
    }

    [Fact]
    public void Finished_game_rejects_further_moves()
    {
        var game = NewGame([C(Rank.King)], [C(Rank.Three)]);
        game.Play("a", [C(Rank.King)], Rank.King);
        game.AcceptFinish("b");

        Assert.Throws<GameRuleException>(() => game.Play("b", [C(Rank.Three)], Rank.Three));
    }

    [Theory]
    [InlineData("K-S", true)]
    [InlineData("10-H", true)]
    [InlineData("2-C", true)]
    [InlineData("1-S", false)]
    [InlineData("K-X", false)]
    [InlineData("K", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Card_ids_parse_strictly(string? id, bool valid)
    {
        Assert.Equal(valid, Card.TryParse(id, out var card));
        if (valid) Assert.Equal(id, card.Id);
    }
}
