import { useCallback, useEffect, useState } from 'react';
import { ActivityIndicator, BackHandler, FlatList, Pressable, StyleSheet, Text, View } from 'react-native';
import { fetchLeaderboard, Leader } from '../api';
import { Button } from '../components/ui';
import { useGame } from '../GameContext';
import { colors, serif } from '../theme';

export function LeaderboardScreen({ onBack }: { onBack(): void }) {
  const { apiUrl, profile } = useGame();
  const [rows, setRows] = useState<Leader[] | null>(null);
  const [failed, setFailed] = useState(false);

  const load = useCallback(async () => {
    setFailed(false);
    setRows(null);
    try {
      setRows(await fetchLeaderboard(apiUrl));
    } catch {
      setFailed(true);
    }
  }, [apiUrl]);

  useEffect(() => {
    load();
  }, [load]);

  useEffect(() => {
    const subscription = BackHandler.addEventListener('hardwareBackPress', () => {
      onBack();
      return true;
    });
    return () => subscription.remove();
  }, [onBack]);

  return (
    <View style={styles.screen}>
      <Pressable onPress={onBack} hitSlop={12} accessibilityRole="button">
        <Text style={styles.back}>‹ Geri</Text>
      </Pressable>
      <Text style={styles.title}>Skor tablosu</Text>

      {failed ? (
        <View style={styles.center}>
          <Text style={styles.empty}>Sunucuya ulaşılamadı.</Text>
          <Button label="Tekrar dene" variant="secondary" onPress={load} />
        </View>
      ) : rows === null ? (
        <ActivityIndicator color={colors.brass} style={styles.center} />
      ) : (
        <FlatList
          data={rows}
          keyExtractor={(row) => row.id}
          ListEmptyComponent={<Text style={styles.empty}>Henüz oynanmış oyun yok.</Text>}
          renderItem={({ item, index }) => (
            <View style={[styles.row, item.id === profile?.id && styles.rowMine]}>
              <Text style={styles.place}>{index + 1}</Text>
              <Text style={styles.name} numberOfLines={1}>
                {item.name}
              </Text>
              <Text style={styles.wins}>{item.wins}G</Text>
              <Text style={styles.losses}>{item.losses}M</Text>
            </View>
          )}
        />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, padding: 20, gap: 10, width: '100%', maxWidth: 480, alignSelf: 'center' },
  back: { color: colors.creamDim, fontSize: 16, fontWeight: '600' },
  title: { fontFamily: serif, color: colors.cream, fontSize: 32, fontWeight: '800', marginBottom: 6 },
  center: { flex: 1, justifyContent: 'center', gap: 12 },
  empty: { color: colors.creamDim, textAlign: 'center', fontSize: 15 },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    minHeight: 52,
    paddingHorizontal: 14,
    backgroundColor: colors.surface,
    borderRadius: 10,
    marginBottom: 6,
  },
  rowMine: { borderWidth: 1, borderColor: colors.brass },
  place: { color: colors.brass, fontWeight: '800', width: 24 },
  name: { color: colors.cream, fontWeight: '700', flex: 1, fontSize: 15 },
  wins: { color: colors.green, fontWeight: '700' },
  losses: { color: colors.red, fontWeight: '700' },
});
