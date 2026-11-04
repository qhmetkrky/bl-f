namespace Bluff.Core;

public enum Suit { Clubs, Diamonds, Hearts, Spades }

public enum Rank
{
    Two = 2, Three, Four, Five, Six, Seven, Eight, Nine, Ten,
    Jack, Queen, King, Ace,
}

public readonly record struct Card(Rank Rank, Suit Suit)
{
    /// <summary>Wire format, e.g. "K-S" or "10-H".</summary>
    public string Id => $"{Rank.Code()}-{Suit.ToString()[0]}";

    public static bool TryParse(string? id, out Card card)
    {
        card = default;
        var parts = id?.Split('-');
        if (parts is not { Length: 2 } || !RankExtensions.TryParseCode(parts[0], out var rank))
            return false;

        Suit? suit = parts[1] switch
        {
            "C" => Suit.Clubs,
            "D" => Suit.Diamonds,
            "H" => Suit.Hearts,
            "S" => Suit.Spades,
            _ => null,
        };
        if (suit is null) return false;

        card = new Card(rank, suit.Value);
        return true;
    }
}

public static class RankExtensions
{
    public static string Code(this Rank rank) => rank switch
    {
        Rank.Jack => "J",
        Rank.Queen => "Q",
        Rank.King => "K",
        Rank.Ace => "A",
        _ => ((int)rank).ToString(),
    };

    public static bool TryParseCode(string? code, out Rank rank)
    {
        foreach (var candidate in Enum.GetValues<Rank>())
        {
            if (candidate.Code() == code)
            {
                rank = candidate;
                return true;
            }
        }
        rank = default;
        return false;
    }
}

public static class Deck
{
    public const int CopiesPerRank = 4;

    public static List<Card> Standard52() =>
        (from suit in Enum.GetValues<Suit>()
         from rank in Enum.GetValues<Rank>()
         select new Card(rank, suit)).ToList();

    public static List<Card> Shuffled(Random rng)
    {
        var cards = Standard52().ToArray();
        rng.Shuffle(cards);
        return [.. cards];
    }
}
