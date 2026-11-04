import { useEffect, useState } from 'react';
import { KeyboardAvoidingView, Platform, Pressable, ScrollView, StyleSheet, Text, TextInput, View } from 'react-native';
import { BotLevel } from '../api';
import { Button, Segments, Sheet } from '../components/ui';
import { CardBack } from '../components/PlayingCard';
import { useGame } from '../GameContext';
import { colors, serif } from '../theme';

const STATUS_LABEL = {
  idle: 'Bağlı değil',
  connecting: 'Bağlanıyor…',
  connected: 'Bağlı',
  offline: 'Ulaşılamıyor',
};

type Action = 'bots' | 'create' | 'join';

export function HomeScreen({ onLeaderboard, onRules }: { onLeaderboard(): void; onRules(): void }) {
  const { profile, status, apiUrl, enter, quickPlay, createRoom, joinRoom, setApiUrl } = useGame();
  const [name, setName] = useState('');
  const [code, setCode] = useState('');
  const [bots, setBots] = useState(3);
  const [level, setLevel] = useState<BotLevel>('medium');
  const [busy, setBusy] = useState<Action | null>(null);
  const [showBots, setShowBots] = useState(false);
  const [showServer, setShowServer] = useState(false);
  const [urlDraft, setUrlDraft] = useState(apiUrl);

  useEffect(() => {
    if (profile) setName((current) => current || profile.name);
  }, [profile]);

  const run = async (action: Action, work: () => Promise<void>) => {
    setBusy(action);
    try {
      if (await enter(name.trim())) await work();
    } finally {
      setBusy(null);
    }
  };

  const hasName = name.trim().length > 0;

  return (
    <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : undefined} style={styles.flex}>
      <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
        <View style={styles.logo}>
          <View style={styles.fan}>
            <View style={styles.cardLeft}>
              <CardBack width={44} />
            </View>
            <View style={styles.cardRight}>
              <CardBack width={44} />
            </View>
          </View>
          <Text style={styles.title}>Blöf</Text>
          <Text style={styles.tagline}>Söylediğine inandır, kartını bitir.</Text>
        </View>

        <View style={styles.block}>
          <Text style={styles.label}>Adın</Text>
          <TextInput
            value={name}
            onChangeText={setName}
            placeholder="Masada görünecek isim"
            placeholderTextColor={colors.creamDim}
            maxLength={16}
            style={styles.input}
          />
        </View>

        <Button label="Botlara karşı oyna" disabled={!hasName} onPress={() => setShowBots(true)} />
        <Button
          label="Oda kur"
          variant="secondary"
          disabled={!hasName}
          busy={busy === 'create'}
          onPress={() => run('create', createRoom)}
        />

        <View style={styles.joinRow}>
          <TextInput
            value={code}
            onChangeText={(text) => setCode(text.toUpperCase())}
            placeholder="Oda kodu"
            placeholderTextColor={colors.creamDim}
            autoCapitalize="characters"
            autoCorrect={false}
            maxLength={5}
            style={[styles.input, styles.codeInput]}
          />
          <Button
            label="Katıl"
            variant="secondary"
            disabled={!hasName || code.length !== 5}
            busy={busy === 'join'}
            onPress={() => run('join', () => joinRoom(code))}
          />
        </View>

        <View style={styles.links}>
          <Pressable onPress={onLeaderboard} hitSlop={10} accessibilityRole="button">
            <Text style={styles.link}>Skor tablosu</Text>
          </Pressable>
          <Pressable onPress={onRules} hitSlop={10} accessibilityRole="button">
            <Text style={styles.link}>Nasıl oynanır?</Text>
          </Pressable>
        </View>

        <Pressable
          // The production site always uses the server it was loaded from.
          disabled={Platform.OS === 'web' && !__DEV__}
          onPress={() => {
            setUrlDraft(apiUrl);
            setShowServer(true);
          }}
          hitSlop={10}
          accessibilityRole="button"
          style={styles.server}
        >
          <View style={[styles.dot, status === 'connected' && styles.dotOn, status === 'offline' && styles.dotOff]} />
          <Text style={styles.serverText} numberOfLines={1}>
            {STATUS_LABEL[status]} · {apiUrl.replace(/^https?:\/\//, '')}
          </Text>
        </Pressable>
      </ScrollView>

      <Sheet visible={showBots} onClose={() => setShowBots(false)}>
        <Text style={styles.sheetTitle}>Rakiplerini seç</Text>
        <Text style={styles.label}>Bot sayısı</Text>
        <Segments
          value={bots}
          onChange={setBots}
          options={[
            { value: 1, label: '1' },
            { value: 2, label: '2' },
            { value: 3, label: '3' },
          ]}
        />
        <Text style={styles.label}>Zorluk</Text>
        <Segments
          value={level}
          onChange={setLevel}
          options={[
            { value: 'easy', label: 'Kolay' },
            { value: 'medium', label: 'Orta' },
            { value: 'hard', label: 'Zor' },
          ]}
        />
        <Button
          label="Başla"
          busy={busy === 'bots'}
          onPress={() =>
            run('bots', async () => {
              setShowBots(false);
              await quickPlay(bots, level);
            })
          }
        />
      </Sheet>

      <Sheet visible={showServer} onClose={() => setShowServer(false)}>
        <Text style={styles.sheetTitle}>Sunucu adresi</Text>
        <TextInput
          value={urlDraft}
          onChangeText={setUrlDraft}
          autoCapitalize="none"
          autoCorrect={false}
          keyboardType="url"
          style={styles.input}
        />
        <Button
          label="Kaydet"
          onPress={async () => {
            setShowServer(false);
            await setApiUrl(urlDraft);
          }}
        />
      </Sheet>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  content: { flexGrow: 1, justifyContent: 'center', padding: 22, gap: 12, width: '100%', maxWidth: 480, alignSelf: 'center' },
  logo: { alignItems: 'center', marginBottom: 18 },
  fan: { height: 76, width: 90, marginBottom: 6 },
  cardLeft: { position: 'absolute', left: 10, top: 4, transform: [{ rotate: '-12deg' }] },
  cardRight: { position: 'absolute', right: 10, top: 4, transform: [{ rotate: '12deg' }] },
  title: { fontFamily: serif, color: colors.cream, fontSize: 56, fontWeight: '800', lineHeight: 64 },
  tagline: { color: colors.creamDim, fontSize: 15 },
  block: { gap: 6 },
  label: { color: colors.creamDim, fontSize: 13, fontWeight: '600' },
  input: {
    minHeight: 50,
    backgroundColor: colors.surface,
    color: colors.cream,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: colors.feltLine,
    paddingHorizontal: 14,
    fontSize: 16,
  },
  joinRow: { flexDirection: 'row', gap: 10 },
  codeInput: { flex: 1, letterSpacing: 3, fontWeight: '700' },
  links: { flexDirection: 'row', justifyContent: 'center', gap: 28, marginTop: 10 },
  link: { color: colors.brass, fontSize: 15, fontWeight: '600' },
  server: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 7, marginTop: 14 },
  dot: { width: 8, height: 8, borderRadius: 4, backgroundColor: colors.creamDim },
  dotOn: { backgroundColor: colors.green },
  dotOff: { backgroundColor: colors.red },
  serverText: { color: colors.creamDim, fontSize: 12, flexShrink: 1 },
  sheetTitle: { fontFamily: serif, color: colors.cream, fontSize: 22, fontWeight: '700' },
});
