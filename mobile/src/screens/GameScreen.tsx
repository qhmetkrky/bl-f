import * as Haptics from 'expo-haptics';
import { useEffect, useMemo, useRef, useState } from 'react';
import { Animated, BackHandler, Easing, Platform, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import { ClaimView, GameView, SeatView } from '../api';
import { RANKS, rankName, rankOf, sortHand } from '../cards';
import { Hand } from '../components/Hand';
import { CardBack } from '../components/PlayingCard';
import { RevealOverlay } from '../components/RevealOverlay';
import { Button, Sheet } from '../components/ui';
import { LiveRoom, useGame } from '../GameContext';
import { colors, GAME_MAX_WIDTH, serif } from '../theme';

const MAX_SELECTED = 4;
const REVEAL_MS = 3200;
const MAX_VISIBLE_PILE = 6;

const buzz = (style: Haptics.ImpactFeedbackStyle) => {
  if (Platform.OS !== 'web') Haptics.impactAsync(style).catch(() => undefined);
};

function Opponent({ seat, active, thinking }: { seat: SeatView; active: boolean; thinking: boolean }) {
  return (
    <View style={[styles.opponent, active && styles.opponentActive]}>
      <Text style={styles.opponentName} numberOfLines={1}>
        {seat.name}
      </Text>
      <View style={styles.opponentCards}>
        <CardBack width={16} />
        <Text style={styles.opponentCount}>{seat.cardCount}</Text>
      </View>
      <Text style={styles.opponentMeta} numberOfLines={1}>
        {!seat.connected ? 'Bağlantı koptu' : thinking ? 'Düşünüyor…' : seat.isBot ? 'Bot' : ' '}
      </Text>
    </View>
  );
}

function Pile({ count }: { count: number }) {
  const bump = useRef(new Animated.Value(1)).current;

  useEffect(() => {
    if (count === 0) return;
    bump.setValue(1.12);
    Animated.spring(bump, { toValue: 1, friction: 4, useNativeDriver: true }).start();
  }, [bump, count]);

  const visible = Math.min(count, MAX_VISIBLE_PILE);
  return (
    <Animated.View style={[styles.pile, { transform: [{ scale: bump }] }]}>
      {count === 0 ? (
        <View style={styles.pileEmpty} />
      ) : (
        Array.from({ length: visible }, (_, i) => (
          <View
            key={i}
            style={[styles.pileCard, { transform: [{ rotate: `${(i % 3) * 6 - 6}deg` }, { translateY: -i * 2 }] }]}
          >
            <CardBack width={58} />
          </View>
        ))
      )}
    </Animated.View>
  );
}

function TurnBar({ endsAt, totalMs, urgent }: { endsAt: number; totalMs: number; urgent: boolean }) {
  const progress = useRef(new Animated.Value(1)).current;

  useEffect(() => {
    const remainingMs = Math.max(0, endsAt - Date.now());
    progress.setValue(Math.min(1, remainingMs / totalMs));
    const animation = Animated.timing(progress, {
      toValue: 0,
      duration: remainingMs,
      easing: Easing.linear,
      useNativeDriver: false,
    });
    animation.start();
    return () => animation.stop();
  }, [progress, endsAt, totalMs]);

  const width = progress.interpolate({ inputRange: [0, 1], outputRange: ['0%', '100%'] });
  return (
    <View style={styles.barTrack}>
      <Animated.View style={[styles.barFill, { width }, urgent && { backgroundColor: colors.brass }]} />
    </View>
  );
}

function claimText(claim: ClaimView, seats: SeatView[], you: string) {
  if (claim.playerId === you) return `Sen: ${claim.count} tane ${rankName(claim.rank)} attın`;
  const name = seats.find((s) => s.id === claim.playerId)?.name ?? 'Oyuncu';
  return `${name}: "${claim.count} tane ${rankName(claim.rank)} attım"`;
}

function promptFor(game: GameView, isMyTurn: boolean, seats: SeatView[]) {
  if (!isMyTurn) {
    const name = seats.find((s) => s.id === game.currentPlayerId)?.name ?? 'Rakip';
    return `Sıra ${name} oyuncusunda`;
  }
  if (game.awaitingFinish) return 'Rakip kartlarını bitirdi. İnanıyor musun?';
  if (!game.roundRank) return 'Yeni tur senin: kart seç, değerini söyle';
  return `${rankName(game.roundRank)} at ya da BLÖF de`;
}

export function GameScreen({ room }: { room: LiveRoom }) {
  const { playCards, callBluff, acceptFinish, startGame, leaveRoom } = useGame();
  const game = room.game!;
  const isMyTurn = room.status === 'playing' && game.currentPlayerId === room.you;
  const isFinished = room.status === 'finished';

  const [selected, setSelected] = useState<string[]>([]);
  const [openerRank, setOpenerRank] = useState<string | null>(null);
  const [revealSeq, setRevealSeq] = useState<number | null>(null);
  const [confirmLeave, setConfirmLeave] = useState(false);
  const [sending, setSending] = useState(false);
  const seenSeq = useRef(game.lastChallenge?.seq ?? 0);

  const hand = useMemo(() => sortHand(game.hand), [game.hand]);

  // Show each challenge once, and only until the server moves play on.
  useEffect(() => {
    const seq = game.lastChallenge?.seq;
    if (!seq || seq === seenSeq.current) return;

    seenSeq.current = seq;
    setRevealSeq(seq);
    buzz(Haptics.ImpactFeedbackStyle.Heavy);
    const timer = setTimeout(() => setRevealSeq(null), REVEAL_MS);
    return () => clearTimeout(timer);
  }, [game.lastChallenge?.seq]);

  // Cards leave the hand when played; a rematch deals a new one.
  useEffect(() => {
    setSelected((current) => current.filter((id) => game.hand.includes(id)));
  }, [game.hand]);

  useEffect(() => {
    if (!isMyTurn) setOpenerRank(null);
  }, [isMyTurn]);

  useEffect(() => {
    const subscription = BackHandler.addEventListener('hardwareBackPress', () => {
      setConfirmLeave(true);
      return true;
    });
    return () => subscription.remove();
  }, []);

  const selectedRanks = [...new Set(selected.map(rankOf))];
  const naturalRank = selectedRanks.length === 1 ? selectedRanks[0] : null;
  const declared = game.roundRank ?? openerRank ?? naturalRank;
  const isBluffing = declared !== null && selected.some((id) => rankOf(id) !== declared);
  const canPlay = isMyTurn && !game.awaitingFinish && selected.length > 0 && declared !== null && !sending;

  const toggleCard = (id: string) => {
    buzz(Haptics.ImpactFeedbackStyle.Light);
    setSelected((current) => {
      if (current.includes(id)) return current.filter((card) => card !== id);
      return current.length >= MAX_SELECTED ? current : [...current, id];
    });
  };

  const send = async (action: () => Promise<unknown>) => {
    setSending(true);
    try {
      await action();
    } finally {
      setSending(false);
    }
  };

  const play = () =>
    send(async () => {
      if (await playCards(selected, declared!)) {
        buzz(Haptics.ImpactFeedbackStyle.Medium);
        setSelected([]);
        setOpenerRank(null);
      }
    });

  const myIndex = room.seats.findIndex((s) => s.id === room.you);
  const opponents = [...room.seats.slice(myIndex + 1), ...room.seats.slice(0, myIndex)];
  const me = room.seats[myIndex];
  const currentSeat = room.seats.find((s) => s.id === game.currentPlayerId);
  const winner = room.seats.find((s) => s.id === game.winnerId);
  const challenge = revealSeq !== null && game.lastChallenge?.seq === revealSeq ? game.lastChallenge : null;
  const isHost = room.hostId === room.you;
  const turnEndsAt = game.turnRemainingMs === null ? null : room.receivedAt + game.turnRemainingMs;

  return (
    <View style={styles.screen}>
      <View style={styles.topBar}>
        <Pressable onPress={() => setConfirmLeave(true)} hitSlop={12} accessibilityRole="button">
          <Text style={styles.leave}>‹ Çık</Text>
        </Pressable>
        <Text style={styles.roomCode}>Oda {room.code}</Text>
      </View>

      <View style={styles.opponents}>
        {opponents.map((seat) => (
          <Opponent
            key={seat.id}
            seat={seat}
            active={!isFinished && seat.id === game.currentPlayerId}
            thinking={!isFinished && seat.id === game.currentPlayerId && seat.isBot}
          />
        ))}
      </View>

      <View style={styles.table}>
        <View style={styles.tableRow}>
          <Pile count={game.pileCount} />
          <View style={styles.tableInfo}>
            <Text style={styles.tableLabel}>Masada</Text>
            <Text style={styles.tableValue}>{game.pileCount} kart</Text>
            <Text style={styles.tableLabel}>Bu tur</Text>
            <Text style={[styles.tableValue, styles.rankValue]}>
              {game.roundRank ? rankName(game.roundRank) : 'Serbest'}
            </Text>
          </View>
        </View>
        <Text style={styles.claim} numberOfLines={2}>
          {game.lastClaim ? claimText(game.lastClaim, room.seats, room.you) : (room.log.at(-1) ?? ' ')}
        </Text>
      </View>

      {turnEndsAt !== null && !challenge ? (
        <TurnBar endsAt={turnEndsAt} totalMs={room.turnSeconds * 1000} urgent={!isMyTurn} />
      ) : (
        <View style={styles.barTrack} />
      )}

      <View style={styles.actions}>
        <Text style={[styles.prompt, isMyTurn && styles.promptActive, isMyTurn && isBluffing && styles.promptBluff]} numberOfLines={2}>
          {isMyTurn && isBluffing ? 'Blöf yapıyorsun: seçtiğin kartlar söylediğin değil.' : promptFor(game, isMyTurn, room.seats)}
        </Text>

        {isMyTurn && !game.roundRank && (
          <ScrollView horizontal showsHorizontalScrollIndicator={Platform.OS === 'web'} contentContainerStyle={styles.rankRow}>
            {RANKS.map((rank) => {
              const active = declared === rank;
              return (
                <Pressable
                  key={rank}
                  accessibilityRole="button"
                  accessibilityLabel={rankName(rank)}
                  accessibilityState={{ selected: active }}
                  onPress={() => setOpenerRank(rank)}
                  style={[styles.rankChip, active && styles.rankChipActive]}
                >
                  <Text style={[styles.rankChipText, active && styles.rankChipTextActive]}>{rank}</Text>
                </Pressable>
              );
            })}
          </ScrollView>
        )}

        {isMyTurn && (
          <View style={styles.buttons}>
            {game.awaitingFinish ? (
              <Button label="Kabul et" variant="secondary" style={styles.grow} disabled={sending} onPress={() => send(acceptFinish)} />
            ) : (
              <Button
                label={
                  selected.length > 0 && declared
                    ? `${selected.length} tane ${rankName(declared)} at`
                    : 'Kart seç'
                }
                style={styles.grow}
                disabled={!canPlay}
                onPress={play}
              />
            )}
            {game.lastClaim && (
              <Button label="BLÖF!" variant="danger" disabled={sending} onPress={() => send(callBluff)} />
            )}
          </View>
        )}
      </View>

      <View style={styles.handHeader}>
        <Text style={styles.handLabel}>Elin · {me?.cardCount ?? 0} kart</Text>
        {selected.length > 0 && <Text style={styles.handLabel}>{selected.length}/{MAX_SELECTED} seçili</Text>}
      </View>
      <Hand cards={hand} selected={selected} matchRank={game.roundRank} onToggle={toggleCard} />

      {challenge && (
        <RevealOverlay challenge={challenge} seats={room.seats} you={room.you} onDismiss={() => setRevealSeq(null)} />
      )}

      {isFinished && !challenge && (
        <View style={styles.finished}>
          <Text style={styles.finishedTitle}>{game.winnerId === room.you ? 'Kazandın!' : `${winner?.name ?? 'Rakip'} kazandı`}</Text>
          <View style={styles.scores}>
            {[...room.seats]
              .sort((a, b) => b.wins - a.wins)
              .map((seat) => (
                <View key={seat.id} style={styles.scoreRow}>
                  <Text style={styles.scoreName} numberOfLines={1}>
                    {seat.name}
                  </Text>
                  <Text style={styles.scoreValue}>
                    {seat.wins} galibiyet · {seat.cardCount} kart
                  </Text>
                </View>
              ))}
          </View>
          {isHost ? (
            <Button label="Tekrar oyna" style={styles.fullWidth} onPress={startGame} />
          ) : (
            <Text style={styles.waiting}>Oda sahibinin yeni eli başlatması bekleniyor…</Text>
          )}
          <Button label="Odadan çık" variant="secondary" style={styles.fullWidth} onPress={leaveRoom} />
        </View>
      )}

      <Sheet visible={confirmLeave} onClose={() => setConfirmLeave(false)}>
        <Text style={styles.sheetTitle}>Masadan kalkılsın mı?</Text>
        <Text style={styles.sheetText}>
          {isFinished ? 'Odadan çıkacaksın.' : 'Oyun bot ile devam eder, sen mağlubiyet alırsın.'}
        </Text>
        <Button label="Masadan kalk" variant="danger" onPress={leaveRoom} />
        <Button label="Vazgeç" variant="secondary" onPress={() => setConfirmLeave(false)} />
      </Sheet>
    </View>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, paddingBottom: 10, width: '100%', maxWidth: GAME_MAX_WIDTH, alignSelf: 'center' },
  topBar: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', paddingHorizontal: 16, paddingVertical: 8 },
  leave: { color: colors.creamDim, fontSize: 16, fontWeight: '600' },
  roomCode: { color: colors.creamDim, fontSize: 13, letterSpacing: 1 },
  opponents: { flexDirection: 'row', gap: 8, paddingHorizontal: 12 },
  opponent: {
    flex: 1,
    minWidth: 0,
    alignItems: 'center',
    gap: 3,
    paddingVertical: 8,
    paddingHorizontal: 6,
    borderRadius: 12,
    backgroundColor: colors.surface,
    borderWidth: 2,
    borderColor: 'transparent',
  },
  opponentActive: { borderColor: colors.brass },
  opponentName: { color: colors.cream, fontSize: 13, fontWeight: '700' },
  opponentCards: { flexDirection: 'row', alignItems: 'center', gap: 6 },
  opponentCount: { color: colors.cream, fontSize: 20, fontWeight: '800' },
  opponentMeta: { color: colors.creamDim, fontSize: 11 },
  table: { flex: 1, justifyContent: 'center', gap: 14, paddingHorizontal: 20, minHeight: 150 },
  tableRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 28 },
  pile: { width: 96, height: 104, alignItems: 'center', justifyContent: 'center' },
  pileCard: { position: 'absolute' },
  pileEmpty: {
    width: 58,
    height: 83,
    borderRadius: 8,
    borderWidth: 2,
    borderStyle: 'dashed',
    borderColor: colors.feltLine,
  },
  tableInfo: { gap: 1 },
  tableLabel: { color: colors.creamDim, fontSize: 12 },
  tableValue: { color: colors.cream, fontSize: 20, fontWeight: '800', marginBottom: 6 },
  rankValue: { fontFamily: serif, color: colors.brass, fontSize: 26 },
  claim: { color: colors.cream, fontSize: 16, textAlign: 'center', fontStyle: 'italic', minHeight: 44 },
  barTrack: { height: 4, marginHorizontal: 16, backgroundColor: colors.surface, borderRadius: 2, overflow: 'hidden' },
  barFill: { height: 4, backgroundColor: colors.green },
  actions: { paddingHorizontal: 12, paddingTop: 10, gap: 8, minHeight: 112, justifyContent: 'flex-start' },
  prompt: { color: colors.creamDim, fontSize: 14, textAlign: 'center' },
  promptActive: { color: colors.cream, fontWeight: '700' },
  rankRow: { gap: 6, paddingHorizontal: 2 },
  rankChip: {
    minWidth: 42,
    height: 42,
    borderRadius: 10,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.feltLine,
  },
  rankChipActive: { backgroundColor: colors.brass, borderColor: colors.brass },
  rankChipText: { color: colors.cream, fontWeight: '700', fontSize: 15 },
  rankChipTextActive: { color: colors.ink },
  buttons: { flexDirection: 'row', gap: 8 },
  grow: { flex: 1 },
  fullWidth: { alignSelf: 'stretch' },
  promptBluff: { color: colors.brass },
  handHeader: { flexDirection: 'row', justifyContent: 'space-between', paddingHorizontal: 16, marginTop: 6 },
  handLabel: { color: colors.creamDim, fontSize: 12 },
  finished: {
    ...StyleSheet.absoluteFill,
    backgroundColor: 'rgba(6,18,16,0.95)',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 12,
    padding: 24,
    zIndex: 10,
  },
  finishedTitle: { fontFamily: serif, color: colors.brass, fontSize: 36, fontWeight: '800', textAlign: 'center' },
  scores: { alignSelf: 'stretch', gap: 6, marginVertical: 8 },
  scoreRow: { flexDirection: 'row', justifyContent: 'space-between', gap: 12 },
  scoreName: { color: colors.cream, fontSize: 15, fontWeight: '700', flexShrink: 1 },
  scoreValue: { color: colors.creamDim, fontSize: 14 },
  waiting: { color: colors.creamDim, textAlign: 'center' },
  sheetTitle: { fontFamily: serif, color: colors.cream, fontSize: 22, fontWeight: '700' },
  sheetText: { color: colors.creamDim, fontSize: 15 },
});
