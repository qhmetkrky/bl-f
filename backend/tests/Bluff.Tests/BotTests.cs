using Bluff.Core;

namespace Bluff.Tests;

public class BotTests
{
    private const int MoveLimit = 5000;

    private static PlayerView View(
        List<Card> hand, PublicClaim? claim, int declared = 0, int pile = 0, bool awaitingFinish = false, int claimantCards = 10) =>
        new("me", hand, claim?.Rank, claim, declared, pile,
            new Dictionary<string, int> { ["me"] = hand.Count, ["other"] = claimantCards }, awaitingFinish);

    private static (string? Winner, int Moves) Simulate(int seed, BotLevel[] levels)
    {
        var rng = new Random(seed);
        var ids = levels.Select((_, i) => $"p{i}").ToList();
        var game = new Game(ids, rng);
        var brains = ids.Select((id, i) => new BotBrain(id, levels[i], new Random(rng.Next()))).ToList();

        for (var move = 0; move < MoveLimit; move++)
        {
            if (game.Phase == GamePhase.Finished)
                return (game.WinnerId, move);

            var id = game.CurrentPlayerId;
            var total = ids.Sum(i => game.HandOf(i).Count) + game.PileCount;
            Assert.Equal(52, total);

            switch (brains[ids.IndexOf(id)].Decide(game.ViewFor(id)))
            {
                case BotDecision.Play p:
                    game.Play(id, p.Cards, p.DeclaredRank);
                    break;
                case BotDecision.Challenge:
                    var result = game.CallBluff(id);
                    brains.ForEach(b => b.Observe(result));
                    break;
                case BotDecision.Accept:
                    game.AcceptFinish(id);
                    break;
            }
        }
        return (null, MoveLimit);
    }

    [Theory]
    [InlineData(BotLevel.Easy, 2)]
    [InlineData(BotLevel.Medium, 2)]
    [InlineData(BotLevel.Hard, 2)]
    [InlineData(BotLevel.Easy, 4)]
    [InlineData(BotLevel.Medium, 4)]
    [InlineData(BotLevel.Hard, 4)]
    public void Bots_only_make_legal_moves_and_every_game_ends(BotLevel level, int players)
    {
        for (var seed = 0; seed < 300; seed++)
        {
            var (winner, _) = Simulate(seed, Enumerable.Repeat(level, players).ToArray());
            Assert.NotNull(winner);
        }
    }

    [Fact]
    public void Hard_bot_beats_easy_bot_most_of_the_time()
    {
        const int games = 1000;
        var hardWins = Enumerable.Range(0, games)
            .Count(seed => Simulate(seed, seed % 2 == 0
                ? [BotLevel.Hard, BotLevel.Easy]
                : [BotLevel.Easy, BotLevel.Hard]).Winner == (seed % 2 == 0 ? "p0" : "p1"));

        Assert.True(hardWins > games * 0.6, $"Zor bot {games} oyunun {hardWins} tanesini kazandı.");
    }

    [Fact]
    public void Always_challenges_a_claim_that_is_provably_false()
    {
        var hand = new List<Card>
        {
            new(Rank.King, Suit.Spades), new(Rank.King, Suit.Hearts), new(Rank.Two, Suit.Clubs),
        };
        var brain = new BotBrain("me", BotLevel.Medium, new Random(1));

        var decision = brain.Decide(View(hand, new PublicClaim("other", 3, Rank.King), declared: 3));

        Assert.IsType<BotDecision.Challenge>(decision);
    }

    [Fact]
    public void Medium_bot_always_challenges_a_finishing_move()
    {
        var hand = new List<Card> { new(Rank.Two, Suit.Clubs) };
        var brain = new BotBrain("me", BotLevel.Medium, new Random(1));

        var decision = brain.Decide(View(hand, new PublicClaim("other", 1, Rank.King), awaitingFinish: true, claimantCards: 0));

        Assert.IsType<BotDecision.Challenge>(decision);
    }

    [Fact]
    public void Bigger_pile_makes_a_bot_more_cautious()
    {
        var hand = new List<Card> { new(Rank.Two, Suit.Clubs), new(Rank.Three, Suit.Clubs) };
        var brain = new BotBrain("me", BotLevel.Hard, new Random(1));
        var claim = new PublicClaim("other", 1, Rank.King);

        Assert.True(
            brain.ChallengeProbability(View(hand, claim, pile: 20), claim) <
            brain.ChallengeProbability(View(hand, claim, pile: 1), claim));
    }

    [Fact]
    public void Hard_bot_remembers_cards_revealed_at_a_challenge()
    {
        var hand = new List<Card> { new(Rank.Two, Suit.Clubs), new(Rank.Three, Suit.Clubs) };
        var brain = new BotBrain("me", BotLevel.Hard, new Random(1));
        var claim = new PublicClaim("other", 1, Rank.King);
        var before = brain.ChallengeProbability(View(hand, claim), claim);

        brain.Observe(new ChallengeResult(
            "third", "third", Rank.King, [new(Rank.King, Suit.Spades), new(Rank.King, Suit.Hearts)],
            WasBluff: false, LoserId: "third", CardsTaken: 2));

        Assert.True(brain.ChallengeProbability(View(hand, claim), claim) > before);
    }
}
