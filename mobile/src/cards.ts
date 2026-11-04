export const RANKS = ['2', '3', '4', '5', '6', '7', '8', '9', '10', 'J', 'Q', 'K', 'A'] as const;

const RANK_NAMES: Record<string, string> = { J: 'Vale', Q: 'Kız', K: 'Papaz', A: 'As' };

// U+FE0E keeps Android from drawing suits as colored emoji.
const SUITS: Record<string, { glyph: string; red: boolean }> = {
  C: { glyph: '♣︎', red: false },
  D: { glyph: '♦︎', red: true },
  H: { glyph: '♥︎', red: true },
  S: { glyph: '♠︎', red: false },
};

export const rankOf = (cardId: string) => cardId.split('-')[0];
export const suitOf = (cardId: string) => SUITS[cardId.split('-')[1]];
export const rankName = (rank: string) => RANK_NAMES[rank] ?? rank;

export const sortHand = (hand: string[]) =>
  [...hand].sort(
    (a, b) =>
      RANKS.indexOf(rankOf(a) as (typeof RANKS)[number]) - RANKS.indexOf(rankOf(b) as (typeof RANKS)[number]) ||
      a.localeCompare(b),
  );
