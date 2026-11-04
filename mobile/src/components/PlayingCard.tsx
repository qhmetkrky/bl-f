import { StyleSheet, Text, View } from 'react-native';
import { rankOf, suitOf } from '../cards';
import { colors } from '../theme';

export const CARD_WIDTH = 66;
export const CARD_HEIGHT = 94;

export function PlayingCard({ id, highlight }: { id: string; highlight?: boolean }) {
  const suit = suitOf(id);
  const color = suit.red ? colors.cardRed : colors.ink;
  return (
    <View style={[styles.card, highlight && styles.highlight]}>
      <Text style={[styles.rank, { color }]}>{rankOf(id)}</Text>
      <Text style={[styles.smallSuit, { color }]}>{suit.glyph}</Text>
      <Text style={[styles.bigSuit, { color }]}>{suit.glyph}</Text>
    </View>
  );
}

export function CardBack({ width = 56 }: { width?: number }) {
  return (
    <View style={[styles.back, { width, height: width * (CARD_HEIGHT / CARD_WIDTH), padding: width * 0.06 }]}>
      <View style={styles.backInner} />
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    width: CARD_WIDTH,
    height: CARD_HEIGHT,
    backgroundColor: colors.cream,
    borderRadius: 8,
    borderWidth: 1,
    borderColor: 'rgba(0,0,0,0.3)',
    paddingLeft: 5,
    paddingTop: 2,
  },
  highlight: { borderColor: colors.brass, borderWidth: 2 },
  rank: { fontSize: 17, fontWeight: '800', lineHeight: 21 },
  smallSuit: { fontSize: 13, lineHeight: 15 },
  bigSuit: { position: 'absolute', right: 6, bottom: 2, fontSize: 30 },
  back: {
    backgroundColor: colors.cream,
    borderRadius: 7,
    borderWidth: 1,
    borderColor: 'rgba(0,0,0,0.35)',
  },
  backInner: {
    flex: 1,
    backgroundColor: colors.cardBack,
    borderRadius: 4,
    borderWidth: 1,
    borderColor: colors.brass,
  },
});
