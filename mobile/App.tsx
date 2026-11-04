import { StatusBar } from 'expo-status-bar';
import { useState } from 'react';
import { ActivityIndicator, StyleSheet, Text, View } from 'react-native';
import { SafeAreaProvider, SafeAreaView } from 'react-native-safe-area-context';
import { RulesSheet } from './src/components/RulesSheet';
import { GameProvider, useGame } from './src/GameContext';
import { GameScreen } from './src/screens/GameScreen';
import { HomeScreen } from './src/screens/HomeScreen';
import { LeaderboardScreen } from './src/screens/LeaderboardScreen';
import { LobbyScreen } from './src/screens/LobbyScreen';
import { colors } from './src/theme';

function Root() {
  const { ready, room, notice, status } = useGame();
  const [leaderboard, setLeaderboard] = useState(false);
  const [rules, setRules] = useState(false);

  if (!ready) return <ActivityIndicator color={colors.brass} style={styles.loading} />;

  return (
    <>
      {room?.status === 'lobby' ? (
        <LobbyScreen room={room} />
      ) : room ? (
        <GameScreen room={room} />
      ) : leaderboard ? (
        <LeaderboardScreen onBack={() => setLeaderboard(false)} />
      ) : (
        <HomeScreen onLeaderboard={() => setLeaderboard(true)} onRules={() => setRules(true)} />
      )}

      <RulesSheet visible={rules} onClose={() => setRules(false)} />

      {room && status !== 'connected' && (
        <View style={styles.banner}>
          <Text style={styles.bannerText}>Bağlantı koptu, yeniden bağlanılıyor…</Text>
        </View>
      )}
      {notice && (
        <View style={styles.toast} accessibilityLiveRegion="polite">
          <Text style={styles.toastText}>{notice}</Text>
        </View>
      )}
    </>
  );
}

export default function App() {
  return (
    <SafeAreaProvider>
      <GameProvider>
        <SafeAreaView style={styles.app}>
          <StatusBar style="light" />
          <Root />
        </SafeAreaView>
      </GameProvider>
    </SafeAreaProvider>
  );
}

const styles = StyleSheet.create({
  app: { flex: 1, backgroundColor: colors.felt },
  loading: { flex: 1 },
  banner: { position: 'absolute', top: 40, left: 16, right: 16, backgroundColor: colors.redDark, borderRadius: 10, padding: 10, zIndex: 40 },
  bannerText: { color: colors.cream, textAlign: 'center', fontWeight: '600' },
  toast: {
    position: 'absolute',
    bottom: 32,
    left: 20,
    right: 20,
    backgroundColor: colors.cream,
    borderRadius: 12,
    padding: 14,
    zIndex: 50,
  },
  toastText: { color: colors.ink, textAlign: 'center', fontWeight: '600' },
});
