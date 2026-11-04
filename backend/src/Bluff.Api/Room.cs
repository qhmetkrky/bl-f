using Bluff.Core;

namespace Bluff.Api;

public sealed class Seat
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public BotLevel? BotLevel { get; set; }
    public int Connections { get; set; }
    public int Wins { get; set; }

    /// <summary>A human who left mid-game; a bot finishes the game in their seat.</summary>
    public bool Abandoned { get; set; }

    public bool IsBot => BotLevel is not null;
    public bool IsActiveHuman => !IsBot;
}

public sealed record GameOutcome(IReadOnlyList<string> WinnerIds, IReadOnlyList<string> LoserIds);

public enum RoomStatus { Lobby, Playing, Finished }

/// <summary>
/// A table: seats plus the current game. Not thread-safe; lock <see cref="Gate"/>.
/// Every state change bumps <see cref="Version"/> so clients can drop out-of-order updates.
/// </summary>
public sealed class Room(string code, string hostId, int turnSeconds, Random rng, DateTimeOffset now)
{
    public static readonly TimeSpan RevealPause = TimeSpan.FromSeconds(3.5);
    public static readonly TimeSpan DisconnectGrace = TimeSpan.FromSeconds(5);
    private const int LogLength = 6;

    private static readonly string[] BotNames = ["Kurnaz Kemal", "Sessiz Selin", "Hesapçı Hakan", "Şanslı Şule"];

    private readonly Dictionary<string, BotBrain> _brains = [];
    private readonly List<string> _log = [];
    private ChallengeView? _lastChallenge;
    private int _challengeSeq;
    private DateTimeOffset? _turnEndsAt;
    private DateTimeOffset _autoMoveAt;
    private GameOutcome? _unrecordedOutcome;
    private DateTimeOffset _lastActivity = now;

    public object Gate { get; } = new();
    public string Code => code;
    public string HostId { get; private set; } = hostId;
    public RoomStatus Status { get; private set; } = RoomStatus.Lobby;
    public List<Seat> Seats { get; } = [];
    public Game? Game { get; private set; }
    public long Version { get; private set; }

    public bool HasConnectedHuman => Seats.Any(s => s.IsActiveHuman && s.Connections > 0);
    public bool HasHuman => Seats.Any(s => s.IsActiveHuman);
    public bool HasSeat(string playerId) => Seats.Any(s => s.Id == playerId);
    public IEnumerable<string> HumanIds => Seats.Where(s => s.IsActiveHuman).Select(s => s.Id);

    public bool IsAbandoned(DateTimeOffset time, TimeSpan idleLimit) =>
        !HasHuman || (!HasConnectedHuman && time - _lastActivity > idleLimit);

    public void AddHuman(string id, string name)
    {
        if (Status != RoomStatus.Lobby)
            throw new GameRuleException("Oyun başladı, bu odaya katılamazsın.");
        EnsureRoomForOneMore();
        Seats.Add(new Seat { Id = id, Name = name });
        Changed($"{name} masaya oturdu.");
    }

    public void AddBot(string actorId, BotLevel level)
    {
        EnsureHost(actorId);
        if (Status != RoomStatus.Lobby)
            throw new GameRuleException("Bot yalnızca oyun başlamadan eklenir.");
        EnsureRoomForOneMore();

        var name = BotNames.First(n => Seats.All(s => s.Name != n));
        Seats.Add(new Seat { Id = $"bot-{Guid.NewGuid():N}", Name = name, BotLevel = level });
        Changed($"{name} masaya oturdu.");
    }

    public void RemoveSeat(string actorId, string seatId)
    {
        EnsureHost(actorId);
        if (Status != RoomStatus.Lobby)
            throw new GameRuleException("Oyuncu yalnızca oyun başlamadan çıkarılır.");
        if (seatId == HostId)
            throw new GameRuleException("Kendini çıkaramazsın.");

        var seat = Seats.Find(s => s.Id == seatId) ?? throw new GameRuleException("Oyuncu bulunamadı.");
        Seats.Remove(seat);
        Changed($"{seat.Name} masadan çıkarıldı.");
    }

    public void Start(string actorId, DateTimeOffset time)
    {
        EnsureHost(actorId);
        if (Status == RoomStatus.Playing)
            throw new GameRuleException("Oyun zaten sürüyor.");

        Seats.RemoveAll(s => s.Abandoned);
        var ids = Seats.Select(s => s.Id).ToList();
        Game = new Game(ids, rng);

        _brains.Clear();
        foreach (var seat in Seats)
            _brains[seat.Id] = new BotBrain(seat.Id, seat.BotLevel ?? BotLevel.Easy, new Random(rng.Next()));

        _lastChallenge = null;
        _unrecordedOutcome = null;
        _log.Clear();
        Status = RoomStatus.Playing;
        ScheduleTurn(time, TimeSpan.Zero);
        Changed($"Kartlar dağıtıldı. {SeatName(Game.CurrentPlayerId)} başlıyor.");
    }

    public void Play(string actorId, IReadOnlyList<string> cardIds, string declaredRank, DateTimeOffset time)
    {
        var game = RunningGame();
        if (!RankExtensions.TryParseCode(declaredRank, out var rank))
            throw new GameRuleException("Geçersiz kart değeri.");

        var cards = new List<Card>(cardIds.Count);
        foreach (var id in cardIds)
        {
            if (!Card.TryParse(id, out var card))
                throw new GameRuleException("Geçersiz kart.");
            cards.Add(card);
        }

        game.Play(actorId, cards, rank);
        AfterPlay(actorId, cards.Count, rank, time);
    }

    public void CallBluff(string actorId, DateTimeOffset time)
    {
        var game = RunningGame();
        var result = game.CallBluff(actorId);

        foreach (var brain in _brains.Values)
            brain.Observe(result);

        _lastChallenge = new ChallengeView(
            ++_challengeSeq,
            result.ChallengerId,
            result.AccusedId,
            result.DeclaredRank.Code(),
            result.Revealed.Select(c => c.Id).ToList(),
            result.WasBluff,
            result.LoserId,
            result.CardsTaken);

        var text = result.WasBluff
            ? $"{SeatName(result.AccusedId)} blöf yaparken yakalandı, {result.CardsTaken} kart aldı."
            : $"{SeatName(result.AccusedId)} doğru söylemişti. {SeatName(result.ChallengerId)} {result.CardsTaken} kart aldı.";

        if (game.Phase == GamePhase.Finished)
            FinishGame(text);
        else
        {
            ScheduleTurn(time, RevealPause);
            Changed(text);
        }
    }

    public void AcceptFinish(string actorId)
    {
        var game = RunningGame();
        game.AcceptFinish(actorId);
        FinishGame($"{SeatName(actorId)} itiraz etmedi.");
    }

    public void Leave(string playerId, DateTimeOffset time)
    {
        var seat = Seats.Find(s => s.Id == playerId);
        if (seat is null) return;

        if (Status == RoomStatus.Playing)
        {
            seat.BotLevel = BotLevel.Medium;
            seat.Abandoned = true;
            if (Game!.CurrentPlayerId == playerId)
                ScheduleTurn(time, TimeSpan.Zero);
        }
        else
        {
            Seats.Remove(seat);
        }

        if (HostId == playerId && HumanIds.FirstOrDefault() is { } nextHost)
            HostId = nextHost;

        Changed($"{seat.Name} masadan ayrıldı.");
    }

    public void ConnectionChanged(string playerId, int delta, DateTimeOffset time)
    {
        if (Seats.Find(s => s.Id == playerId) is not { } seat) return;

        var wasConnected = seat.Connections > 0;
        seat.Connections = Math.Max(0, seat.Connections + delta);
        if (wasConnected == seat.Connections > 0) return;

        if (Status == RoomStatus.Playing && Game!.CurrentPlayerId == playerId && _turnEndsAt is { } end)
            _autoMoveAt = seat.Connections > 0 ? end : Min(end, time + DisconnectGrace);

        Changed(null, time);
    }

    /// <summary>Plays due bot moves and expired turns. Returns whether anything changed.</summary>
    public bool Tick(DateTimeOffset time)
    {
        // Nobody is watching: hold the game so a returning player has not missed it.
        if (Status != RoomStatus.Playing || time < _autoMoveAt || !HasConnectedHuman)
            return false;

        var game = Game!;
        var playerId = game.CurrentPlayerId;
        switch (_brains[playerId].Decide(game.ViewFor(playerId)))
        {
            case BotDecision.Play play:
                game.Play(playerId, play.Cards, play.DeclaredRank);
                AfterPlay(playerId, play.Cards.Count, play.DeclaredRank, time);
                break;
            case BotDecision.Challenge:
                CallBluff(playerId, time);
                break;
            case BotDecision.Accept:
                AcceptFinish(playerId);
                break;
        }
        return true;
    }

    /// <summary>The finished game's result, once, for the score table.</summary>
    public GameOutcome? TakeOutcome()
    {
        var outcome = _unrecordedOutcome;
        _unrecordedOutcome = null;
        return outcome;
    }

    public void Touch(DateTimeOffset time) => _lastActivity = time;

    public IReadOnlyList<(string PlayerId, RoomView View)> Snapshot(DateTimeOffset time) =>
        HumanIds.Select(id => (id, ViewFor(id, time))).ToList();

    public RoomView ViewFor(string playerId, DateTimeOffset time) => new(
        Code,
        Status.ToString().ToLowerInvariant(),
        Version,
        HostId,
        playerId,
        TurnSeconds: turnSeconds,
        Seats.Select(s => new SeatView(
            s.Id,
            s.Name,
            s.IsBot,
            s.BotLevel?.ToString().ToLowerInvariant(),
            s.IsBot || s.Connections > 0,
            Game is not null && Status != RoomStatus.Lobby && Game.PlayerOrder.Contains(s.Id) ? Game.HandOf(s.Id).Count : 0,
            s.Wins)).ToList(),
        Game is null ? null : GameViewFor(playerId, Game, time),
        [.. _log]);

    private GameView GameViewFor(string playerId, Game game, DateTimeOffset time) => new(
        game.CurrentPlayerId,
        game.RoundRank?.Code(),
        game.PileCount,
        game.DeclaredInRound,
        game.LastMove is { } m ? new ClaimView(m.PlayerId, m.Cards.Count, m.DeclaredRank.Code()) : null,
        game.AwaitingFinish,
        _turnEndsAt is { } end && game.Phase == GamePhase.Playing
            ? (long)Math.Max(0, (end - time).TotalMilliseconds)
            : null,
        game.PlayerOrder.Contains(playerId) ? game.HandOf(playerId).Select(c => c.Id).ToList() : [],
        game.WinnerId,
        _lastChallenge);

    private void AfterPlay(string playerId, int count, Rank rank, DateTimeOffset time)
    {
        ScheduleTurn(time, TimeSpan.Zero);
        Changed($"{SeatName(playerId)}: \"{count} tane {RankNames.Turkish(rank)} attım.\"");
    }

    private void FinishGame(string text)
    {
        var game = Game!;
        var winner = Seats.Find(s => s.Id == game.WinnerId)!;
        winner.Wins++;
        Status = RoomStatus.Finished;
        _turnEndsAt = null;

        var humans = Seats.Where(s => s.IsActiveHuman || s.Abandoned).Select(s => s.Id).ToList();
        _unrecordedOutcome = new GameOutcome(
            humans.Where(id => id == winner.Id && !winner.Abandoned).ToList(),
            humans.Where(id => id != winner.Id || winner.Abandoned).ToList());

        Changed(text);
        Changed($"{winner.Name} kazandı!");
    }

    private void ScheduleTurn(DateTimeOffset time, TimeSpan pause)
    {
        var seat = Seats.Find(s => s.Id == Game!.CurrentPlayerId)!;
        var start = time + pause;

        if (seat.IsBot)
        {
            _turnEndsAt = null;
            _autoMoveAt = start + TimeSpan.FromMilliseconds(rng.Next(1200, 2600));
            return;
        }

        var end = start + TimeSpan.FromSeconds(turnSeconds);
        _turnEndsAt = end;
        _autoMoveAt = seat.Connections > 0 ? end : start + DisconnectGrace;
    }

    private Game RunningGame() =>
        Status == RoomStatus.Playing ? Game! : throw new GameRuleException("Şu an oyun sürmüyor.");

    private void EnsureHost(string actorId)
    {
        if (actorId != HostId)
            throw new GameRuleException("Bunu yalnızca oda sahibi yapabilir.");
    }

    private void EnsureRoomForOneMore()
    {
        if (Seats.Count >= Game.MaxPlayers)
            throw new GameRuleException("Masa dolu.");
    }

    private string SeatName(string id) => Seats.Find(s => s.Id == id)?.Name ?? "?";

    private void Changed(string? logLine, DateTimeOffset? time = null)
    {
        Version++;
        if (time is { } t) _lastActivity = t;
        if (logLine is null) return;

        _log.Add(logLine);
        if (_log.Count > LogLength)
            _log.RemoveAt(0);
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}

public static class RankNames
{
    public static string Turkish(Rank rank) => rank switch
    {
        Rank.Jack => "Vale",
        Rank.Queen => "Kız",
        Rank.King => "Papaz",
        Rank.Ace => "As",
        _ => ((int)rank).ToString(),
    };
}

public sealed record SeatView(string Id, string Name, bool IsBot, string? BotLevel, bool Connected, int CardCount, int Wins);
public sealed record ClaimView(string PlayerId, int Count, string Rank);
public sealed record ChallengeView(
    int Seq, string ChallengerId, string AccusedId, string DeclaredRank,
    IReadOnlyList<string> Revealed, bool WasBluff, string LoserId, int CardsTaken);
public sealed record GameView(
    string CurrentPlayerId, string? RoundRank, int PileCount, int DeclaredInRound, ClaimView? LastClaim,
    bool AwaitingFinish, long? TurnRemainingMs, IReadOnlyList<string> Hand, string? WinnerId, ChallengeView? LastChallenge);
public sealed record RoomView(
    string Code, string Status, long Version, string HostId, string You, int TurnSeconds,
    IReadOnlyList<SeatView> Seats, GameView? Game, IReadOnlyList<string> Log);
