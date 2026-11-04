import { Platform, Pressable, ScrollView, StyleSheet, useWindowDimensions, View } from 'react-native';
import { rankOf } from '../cards';
import { colors, GAME_MAX_WIDTH } from '../theme';
import { CARD_HEIGHT, CARD_WIDTH, PlayingCard } from './PlayingCard';

const SIDE_PADDING = 12;
const MIN_STEP = 25;
const LIFT = 14;

type Props = {
  cards: string[];
  selected: string[];
  matchRank: string | null;
  onToggle(cardId: string): void;
};

export function Hand({ cards, selected, matchRank, onToggle }: Props) {
  const width = Math.min(useWindowDimensions().width, GAME_MAX_WIDTH);

  // Fan the hand to fit the screen; past the minimum overlap it scrolls instead.
  const available = width - SIDE_PADDING * 2 - CARD_WIDTH;
  const step =
    cards.length > 1 ? Math.max(MIN_STEP, Math.min(CARD_WIDTH + 4, available / (cards.length - 1))) : CARD_WIDTH;
  const contentWidth = step * Math.max(0, cards.length - 1) + CARD_WIDTH;

  return (
    <ScrollView
      horizontal
      // A mouse has no swipe; the scrollbar is how a browser reaches the rest of a big hand.
      showsHorizontalScrollIndicator={Platform.OS === 'web'}
      style={styles.scroll}
      contentContainerStyle={[styles.content, { minWidth: width }]}
    >
      <View style={{ width: contentWidth, height: CARD_HEIGHT + LIFT }}>
        {cards.map((id, index) => {
          const isSelected = selected.includes(id);
          return (
            <Pressable
              key={id}
              accessibilityRole="button"
              accessibilityState={{ selected: isSelected }}
              accessibilityLabel={`Kart ${id}`}
              onPress={() => onToggle(id)}
              style={[styles.slot, { left: index * step, top: isSelected ? 0 : LIFT }]}
            >
              <PlayingCard id={id} highlight={isSelected || rankOf(id) === matchRank} />
              {isSelected && <View style={styles.tick} />}
            </Pressable>
          );
        })}
      </View>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  scroll: { flexGrow: 0 },
  content: { paddingHorizontal: SIDE_PADDING, justifyContent: 'center' },
  slot: { position: 'absolute' },
  tick: {
    position: 'absolute',
    top: 6,
    right: 6,
    width: 10,
    height: 10,
    borderRadius: 5,
    backgroundColor: colors.brass,
  },
});
