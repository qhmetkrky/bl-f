namespace Bluff.Core;

public enum BotLevel { Easy, Medium, Hard }

public abstract record BotDecision
{
    public sealed record Play(IReadOnlyList<Card> Cards, Rank DeclaredRank) : BotDecision;
    public sealed record Challenge : BotDecision;
    public sealed record Accept : BotDecision;
}

/// <summary>
/// Computer opponent. Decides only from a <see cref="PlayerView"/> plus the cards
/// revealed publicly at challenges, so it plays with the same information a human has.
/// </summary>
public sealed class BotBrain(string selfId, BotLevel level, Random rng)
{
    /// <summary>Cards seen at a challenge, and who picked them up. Stale once they replay them.</summary>
    private readonly Dictionary<Card, string> _lastSeenWith = [];

    public void Observe(ChallengeResult result)
    {
        foreach (var card in result.Revealed)
            _lastSeenWith[card] = result.LoserId;
    }

    public BotDecision Decide(PlayerView view)
    {
        if (view.LastClaim is { } claim)
        {
            if (rng.NextDouble() < ChallengeProbability(view, claim))
                return new BotDecision.Challenge();
            if (view.AwaitingFinish)
                return new BotDecision.Accept();
        }
        return ChoosePlay(view);
    }

    internal double ChallengeProbability(PlayerView view, PublicClaim claim)
    {
        var mine = view.Hand.Count(c => c.Rank == claim.Rank);
        var provablyFalse = claim.Count + mine > Deck.CopiesPerRank;

        // Accepting a finish loses on the spot, so a sensible player always challenges it.
        if (provablyFalse || view.AwaitingFinish)
            return level == BotLevel.Easy ? 0.6 : 1.0;
        if (level == BotLevel.Easy)
            return 0.12;

        var suspicion = 0.10 + 0.12 * (claim.Count - 1) + 0.12 * mine;

        // The table has declared more of this rank than can exist outside this hand.
        if (view.DeclaredInRound + mine > Deck.CopiesPerRank)
            suspicion += 0.35;

        if (view.CardCounts[claim.PlayerId] <= 2)
            suspicion += 0.20;

        // Holding no card of the rank means the alternative to challenging is to bluff.
        if (mine == 0)
            suspicion += view.Hand.Count == 1 ? 0.50 : 0.08;

        // Being wrong costs the whole pile.
        suspicion -= Math.Min(0.20, view.PileCount * 0.01);

        if (level == BotLevel.Hard)
        {
            var seen = _lastSeenWith.Where(s => s.Key.Rank == claim.Rank && s.Value != selfId).ToList();
            suspicion += 0.15 * seen.Count(s => s.Value != claim.PlayerId);
            suspicion -= 0.20 * seen.Count(s => s.Value == claim.PlayerId);
        }
        else
        {
            suspicion *= 0.8;
        }

        return Math.Clamp(suspicion, 0.02, 0.95);
    }

    private BotDecision ChoosePlay(PlayerView view)
    {
        var bluffChance = level switch
        {
            BotLevel.Easy => 0.10,
            BotLevel.Medium => 0.25,
            _ => 0.30,
        };

        if (view.RoundRank is not { } rank)
        {
            var groups = view.Hand.GroupBy(c => c.Rank).ToList();
            var size = groups.Max(g => g.Count());
            var options = groups.Where(g => g.Count() == size).ToList();
            rank = options[rng.Next(options.Count)].Key;
        }

        var cards = view.Hand.Where(c => c.Rank == rank).Take(Game.MaxCardsPerMove).ToList();

        if (cards.Count == 0)
        {
            // Forced to bluff. Keep one card back when possible: an emptied hand gets challenged.
            var wanted = view.Hand.Count >= 4 && rng.NextDouble() < 0.25 ? 2 : 1;
            cards = JunkCards(view.Hand, Math.Max(1, Math.Min(wanted, view.Hand.Count - 1)));
        }
        else if (cards.Count < Game.MaxCardsPerMove
                 && view.Hand.Count > cards.Count + 1
                 && rng.NextDouble() < bluffChance)
        {
            cards.AddRange(JunkCards(view.Hand.Except(cards).ToList(), 1));
        }

        return new BotDecision.Play(cards, rank);
    }

    /// <summary>Cards the hand can spare: ranks held the fewest times go first.</summary>
    private List<Card> JunkCards(IReadOnlyList<Card> hand, int count) =>
        hand.OrderBy(c => hand.Count(o => o.Rank == c.Rank))
            .ThenBy(_ => rng.Next())
            .Take(count)
            .ToList();
}
