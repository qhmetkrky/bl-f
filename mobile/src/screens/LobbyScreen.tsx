import { useState } from 'react';
import { Platform, Pressable, ScrollView, Share, StyleSheet, Text, View } from 'react-native';
import { BotLevel } from '../api';
import { Button, Segments } from '../components/ui';
import { LiveRoom, useGame } from '../GameContext';
import { colors, serif } from '../theme';

const MAX_SEATS = 4;
const LEVEL_LABEL: Record<BotLevel, string> = { easy: 'Kolay bot', medium: 'Orta bot', hard: 'Zor bot' };

export function LobbyScreen({ room }: { room: LiveRoom }) {
  const { addBot, removeSeat, startGame, leaveRoom, notify } = useGame();
  const [level, setLevel] = useState<BotLevel>('medium');
  const [showBots, setShowBots] = useState(false);
  const isHost = room.hostId === room.you;
  const emptySeats = MAX_SEATS - room.seats.length;
  const isAlone = room.seats.length < 2;

  const shareCode = async () => {
    if (Platform.OS !== 'web') {
      await Share.share({ message: `Blöf oynayalım! Oda kodu: ${room.code}` }).catch(() => undefined);
      return;
    }
    // Browsers only offer the clipboard on HTTPS and localhost.
    try {
      await navigator.clipboard.writeText(room.code);
      notify('Oda kodu kopyalandı.');
    } catch {
      notify(`Oda kodunu arkadaşına söyle: ${room.code}`);
    }
  };

  return (
    <ScrollView contentContainerStyle={styles.content}>
      <Text style={styles.heading}>{isAlone ? 'Arkadaşını bekliyorsun' : 'Masa hazır'}</Text>
      <Text style={styles.caption}>
        {isAlone
          ? 'Bu kodu arkadaşına ver. Ana ekranda Oda kodu kutusuna yazıp Katıl düğmesine bassın.'
          : 'Oda kodu'}
      </Text>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel="Oda kodunu paylaş"
        onPress={shareCode}
      >
        <Text style={styles.code}>{room.code}</Text>
        <Text style={styles.share}>{Platform.OS === 'web' ? 'Kopyalamak için tıkla' : 'Paylaşmak için dokun'}</Text>
      </Pressable>

      <View style={styles.seats}>
        {room.seats.map((seat) => (
          <View key={seat.id} style={styles.seat}>
            <View style={styles.avatar}>
              <Text style={styles.avatarText}>{seat.name.charAt(0).toLocaleUpperCase('tr')}</Text>
            </View>
            <View style={styles.seatText}>
              <Text style={styles.seatName} numberOfLines={1}>
                {seat.name}
                {seat.id === room.you ? ' (sen)' : ''}
              </Text>
              <Text style={styles.seatMeta}>
                {seat.botLevel
                  ? LEVEL_LABEL[seat.botLevel]
                  : seat.id === room.hostId
                    ? 'Oda sahibi'
                    : seat.connected
                      ? 'Hazır'
                      : 'Bağlantı koptu'}
              </Text>
            </View>
            {isHost && seat.id !== room.you && (
              <Pressable
                accessibilityRole="button"
                accessibilityLabel={`${seat.name} oyuncusunu çıkar`}
                hitSlop={10}
                onPress={() => removeSeat(seat.id)}
              >
                <Text style={styles.remove}>Çıkar</Text>
              </Pressable>
            )}
          </View>
        ))}
        {Array.from({ length: emptySeats }, (_, i) => (
          <View key={i} style={[styles.seat, styles.emptySeat]}>
            <Text style={styles.seatMeta}>Boş koltuk</Text>
          </View>
        ))}
      </View>

      {isHost && emptySeats > 0 && !showBots && (
        <Pressable accessibilityRole="button" hitSlop={10} onPress={() => setShowBots(true)}>
          <Text style={styles.botLink}>{isAlone ? 'Arkadaşın yoksa bot ekle' : 'Boş koltuğa bot ekle'}</Text>
        </Pressable>
      )}

      {isHost && emptySeats > 0 && showBots && (
        <View style={styles.addBot}>
          <Segments
            value={level}
            onChange={setLevel}
            options={[
              { value: 'easy', label: 'Kolay' },
              { value: 'medium', label: 'Orta' },
              { value: 'hard', label: 'Zor' },
            ]}
          />
          <Button label="Bot ekle" variant="secondary" onPress={() => addBot(level)} />
        </View>
      )}

      {isHost ? (
        <Button label={isAlone ? 'En az 2 oyuncu gerekli' : 'Oyunu başlat'} disabled={isAlone} onPress={startGame} />
      ) : (
        <Text style={styles.waiting}>Oda sahibinin başlatması bekleniyor…</Text>
      )}
      <Button label="Odadan çık" variant="secondary" onPress={leaveRoom} />
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  content: { flexGrow: 1, justifyContent: 'center', padding: 22, gap: 12, width: '100%', maxWidth: 480, alignSelf: 'center' },
  heading: { fontFamily: serif, color: colors.cream, fontSize: 26, fontWeight: '800', textAlign: 'center' },
  caption: { color: colors.creamDim, textAlign: 'center', fontSize: 14, lineHeight: 20 },
  botLink: { color: colors.brass, textAlign: 'center', fontSize: 15, fontWeight: '600', paddingVertical: 4 },
  code: { fontFamily: serif, color: colors.brass, fontSize: 48, fontWeight: '800', letterSpacing: 6, textAlign: 'center' },
  share: { color: colors.creamDim, textAlign: 'center', fontSize: 12, marginBottom: 8 },
  seats: { gap: 8 },
  seat: {
    minHeight: 60,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    backgroundColor: colors.surface,
    borderRadius: 12,
    paddingHorizontal: 12,
  },
  emptySeat: { backgroundColor: 'transparent', borderWidth: 1, borderColor: colors.feltLine, borderStyle: 'dashed', justifyContent: 'center' },
  avatar: {
    width: 38,
    height: 38,
    borderRadius: 19,
    backgroundColor: colors.brassDark,
    alignItems: 'center',
    justifyContent: 'center',
  },
  avatarText: { color: colors.cream, fontWeight: '800', fontSize: 17 },
  seatText: { flex: 1 },
  seatName: { color: colors.cream, fontWeight: '700', fontSize: 15 },
  seatMeta: { color: colors.creamDim, fontSize: 13 },
  remove: { color: colors.red, fontWeight: '700', fontSize: 14 },
  addBot: { gap: 8, marginTop: 6 },
  waiting: { color: colors.creamDim, textAlign: 'center', marginVertical: 12 },
});
