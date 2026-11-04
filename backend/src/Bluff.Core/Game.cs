namespace Bluff.Core;

/// <summary>A rule violation. The message is shown to the player as-is.</summary>
public sealed class GameRuleException(string message) : Exception(message);

public enum GamePhase { Playing, Finished }

public sealed record PlayedMove(string PlayerId, IReadOnlyList<Card> Cards, Rank DeclaredRank)
{
    public bool IsTruthful => Cards.All(c => c.Rank == DeclaredRank);
}

/// <summary>What everyone at the table can see about the last move.</summary>
public sealed record PublicClaim(string PlayerId, int Count, Rank Rank);

public sealed record ChallengeResult(
    string ChallengerId,
    string AccusedId,
    Rank DeclaredRank,
    IReadOnlyList<Card> Revealed,
    bool WasBluff,
    string LoserId,
    int CardsTaken);

/// <summary>
/// One seat's legal view of the table. It holds no opponent cards and no pile
/// contents, so a bot driven from this view cannot cheat.
/// </summary>
public sealed record PlayerView(
    string SelfId,
    IReadOnlyList<Card> Hand,
    Rank? RoundRank,
    PublicClaim? LastClaim,
    int DeclaredInRound,
    int PileCount,
    IReadOnlyDictionary<string, int> CardCounts,
    bool AwaitingFinish);

/// <summary>
/// The rules of Blöf. Not thread-safe; callers serialize access.
///
/// A turn is one of: play 1-4 cards face down and declare a rank, or call the
/// previous player's bluff. The round opener picks the rank; everyone follows it
/// until a challenge clears the pile. The winner of a challenge opens the next
/// round. A player who empties their hand wins only once the next player has
/// accepted, or challenged and found the move truthful.
/// </summary>
public sealed class Game
{
    public const int MinPlayers = 2;
    public const int MaxPlayers = 4;
    public const int MaxCardsPerMove = 4;

    private readonly List<string> _order;
    private readonly Dictionary<string, List<Card>> _hands;
    private readonly List<Card> _pile = [];
    private int _currentIndex;

    public Game(IReadOnlyList<string> playerIds, Random rng)
        : this(playerIds, Deal(playerIds, rng), rng.Next(playerIds.Count))
    {
    }

    /// <summary>Starts from a fixed deal. Hands need not use a full deck.</summary>
    public Game(IReadOnlyList<string> playerIds, IReadOnlyDictionary<string, List<Card>> hands, int firstPlayerIndex)
    {
        Validate(playerIds);
        _order = [.. playerIds];
        _hands = _order.ToDictionary(id => id, id => new List<Card>(hands[id]));
        _currentIndex = firstPlayerIndex;
    }

    public GamePhase Phase { get; private set; } = GamePhase.Playing;
    public string CurrentPlayerId => _order[_currentIndex];
    public string? WinnerId { get; private set; }
    public Rank? RoundRank { get; private set; }
    public PlayedMove? LastMove { get; private set; }
    public int DeclaredInRound { get; private set; }
    public int PileCount => _pile.Count;
    public IReadOnlyList<string> PlayerOrder => _order;

    /// <summary>The last player emptied their hand; the current player must challenge or accept.</summary>
    public bool AwaitingFinish => Phase == GamePhase.Playing && LastMove is { } m && _hands[m.PlayerId].Count == 0;

    public IReadOnlyList<Card> HandOf(string playerId) => _hands[playerId];

    public PlayerView ViewFor(string playerId) => new(
        playerId,
        [.. _hands[playerId]],
        RoundRank,
        LastMove is { } m ? new PublicClaim(m.PlayerId, m.Cards.Count, m.DeclaredRank) : null,
        DeclaredInRound,
        _pile.Count,
        _hands.ToDictionary(h => h.Key, h => h.Value.Count),
        AwaitingFinish);

    public void Play(string playerId, IReadOnlyCollection<Card> cards, Rank declaredRank)
    {
        EnsureTurn(playerId);
        if (AwaitingFinish)
            throw new GameRuleException("Rakip kartlarını bitirdi: Blöf de ya da kabul et.");
        if (cards.Count is < 1 or > MaxCardsPerMove)
            throw new GameRuleException($"Bir hamlede 1-{MaxCardsPerMove} kart atılabilir.");
        if (cards.Distinct().Count() != cards.Count)
            throw new GameRuleException("Aynı kart iki kez seçilemez.");
        if (RoundRank is { } required && declaredRank != required)
            throw new GameRuleException("Bu turda yalnızca masadaki kart değeri söylenebilir.");

        var hand = _hands[playerId];
        if (!cards.All(hand.Contains))
            throw new GameRuleException("Seçilen kart elinde yok.");

        hand.RemoveAll(cards.Contains);
        _pile.AddRange(cards);

        RoundRank = declaredRank;
        DeclaredInRound += cards.Count;
        LastMove = new PlayedMove(playerId, [.. cards], declaredRank);
        _currentIndex = (_currentIndex + 1) % _order.Count;
    }

    public ChallengeResult CallBluff(string playerId)
    {
        EnsureTurn(playerId);
        if (LastMove is not { } move)
            throw new GameRuleException("İtiraz edilecek bir hamle yok.");

        var wasBluff = !move.IsTruthful;
        var loserId = wasBluff ? move.PlayerId : playerId;
        var winnerId = wasBluff ? playerId : move.PlayerId;
        var result = new ChallengeResult(
            playerId, move.PlayerId, move.DeclaredRank, move.Cards, wasBluff, loserId, _pile.Count);

        _hands[loserId].AddRange(_pile);
        _pile.Clear();
        RoundRank = null;
        LastMove = null;
        DeclaredInRound = 0;
        _currentIndex = _order.IndexOf(winnerId);

        if (_hands[winnerId].Count == 0)
            Finish(winnerId);

        return result;
    }

    public void AcceptFinish(string playerId)
    {
        EnsureTurn(playerId);
        if (!AwaitingFinish)
            throw new GameRuleException("Kabul edilecek bir bitiş yok.");

        Finish(LastMove!.PlayerId);
    }

    private void Finish(string winnerId)
    {
        WinnerId = winnerId;
        Phase = GamePhase.Finished;
    }

    private void EnsureTurn(string playerId)
    {
        if (Phase != GamePhase.Playing)
            throw new GameRuleException("Oyun bitti.");
        if (playerId != CurrentPlayerId)
            throw new GameRuleException("Sıra sende değil.");
    }

    private static Dictionary<string, List<Card>> Deal(IReadOnlyList<string> playerIds, Random rng)
    {
        Validate(playerIds);
        var hands = playerIds.ToDictionary(id => id, _ => new List<Card>());
        var deck = Deck.Shuffled(rng);
        for (var i = 0; i < deck.Count; i++)
            hands[playerIds[i % playerIds.Count]].Add(deck[i]);
        return hands;
    }

    private static void Validate(IReadOnlyList<string> playerIds)
    {
        if (playerIds.Count is < MinPlayers or > MaxPlayers)
            throw new GameRuleException($"Blöf {MinPlayers}-{MaxPlayers} oyuncu ile oynanır.");
        if (playerIds.Distinct().Count() != playerIds.Count)
            throw new ArgumentException("Oyuncu kimlikleri benzersiz olmalı.", nameof(playerIds));
    }
}
