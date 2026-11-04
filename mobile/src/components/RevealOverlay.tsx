import { useEffect, useRef } from 'react';
import { Animated, Pressable, StyleSheet, Text, View } from 'react-native';
import { ChallengeView, SeatView } from '../api';
import { rankName, rankOf } from '../cards';
import { colors, serif } from '../theme';
import { PlayingCard } from './PlayingCard';

const FLIP_MS = 260;
const CARD_DELAY_MS = 180;

function FlippingCard({ id, index, honest }: { id: string; index: number; honest: boolean }) {
  const flip = useRef(new Animated.Value(0)).current;

  useEffect(() => {
    Animated.timing(flip, {
      toValue: 1,
      duration: FLIP_MS,
      delay: 250 + index * CARD_DELAY_MS,
      useNativeDriver: true,
    }).start();
  }, [flip, index]);

  return (
    <Animated.View style={{ transform: [{ scaleX: flip }] }}>
      <PlayingCard id={id} />
      <View style={[styles.mark, { backgroundColor: honest ? colors.green : colors.red }]}>
        <Text style={styles.markText}>{honest ? '✓' : '✕'}</Text>
      </View>
    </Animated.View>
  );
}

type Props = {
  challenge: ChallengeView;
  seats: SeatView[];
  you: string;
  onDismiss(): void;
};

export function RevealOverlay({ challenge, seats, you, onDismiss }: Props) {
  const nameOf = (id: string) => (id === you ? 'Sen' : (seats.find((s) => s.id === id)?.name ?? 'Oyuncu'));
  const count = challenge.revealed.length;

  return (
    <Pressable style={styles.overlay} onPress={onDismiss} accessibilityLabel="Sonucu kapat">
      <Text style={[styles.title, { color: challenge.wasBluff ? colors.red : colors.green }]}>
        {challenge.wasBluff ? 'BLÖF YAKALANDI!' : 'DOĞRU SÖYLEDİ!'}
      </Text>
      <Text style={styles.claim}>
        {nameOf(challenge.accusedId)}: {count} tane {rankName(challenge.declaredRank)}
      </Text>
      <View style={styles.cards}>
        {challenge.revealed.map((id, index) => (
          <FlippingCard key={id} id={id} index={index} honest={rankOf(id) === challenge.declaredRank} />
        ))}
      </View>
      <Text style={styles.result}>
        {nameOf(challenge.loserId)} {challenge.cardsTaken} kart aldı
      </Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  overlay: {
    ...StyleSheet.absoluteFill,
    backgroundColor: 'rgba(6,18,16,0.92)',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 14,
    padding: 20,
    zIndex: 20,
  },
  title: { fontFamily: serif, fontSize: 30, fontWeight: '800', textAlign: 'center' },
  claim: { color: colors.creamDim, fontSize: 16, textAlign: 'center' },
  cards: { flexDirection: 'row', gap: 8, marginVertical: 8, flexWrap: 'wrap', justifyContent: 'center' },
  mark: {
    position: 'absolute',
    bottom: -8,
    alignSelf: 'center',
    width: 24,
    height: 24,
    borderRadius: 12,
    alignItems: 'center',
    justifyContent: 'center',
  },
  markText: { color: '#fff', fontWeight: '800', fontSize: 13 },
  result: { color: colors.cream, fontSize: 18, fontWeight: '700', marginTop: 6, textAlign: 'center' },
});
