import { ScrollView, StyleSheet, Text } from 'react-native';
import { colors, serif } from '../theme';
import { Button, Sheet } from './ui';

const RULES = [
  'Amaç: elindeki bütün kartları herkesten önce bitirmek.',
  'Sıran geldiğinde 1-4 kartı kapalı atar ve değerini söylersin. Yalan söyleyebilirsin.',
  'Turu açan oyuncu değeri seçer; tur boyunca herkes aynı değeri söyler.',
  'Önceki oyuncuya inanmıyorsan kart atmak yerine "BLÖF!" de. Son atılan kartlar açılır.',
  'Yalan çıkarsa o oyuncu, doğru çıkarsa sen masadaki bütün kartları alırsın.',
  'İtirazı kazanan yeni turu açar ve değeri seçer.',
  'Son kartını atan oyuncu, sıradaki oyuncu itiraz edip haksız çıkarsa ya da kabul ederse kazanır. Blöfü yakalanırsa kartları alır.',
];

export function RulesSheet({ visible, onClose }: { visible: boolean; onClose(): void }) {
  return (
    <Sheet visible={visible} onClose={onClose}>
      <Text style={styles.title}>Nasıl oynanır?</Text>
      <ScrollView>
        {RULES.map((rule) => (
          <Text key={rule} style={styles.rule}>
            {rule}
          </Text>
        ))}
      </ScrollView>
      <Button label="Anladım" onPress={onClose} />
    </Sheet>
  );
}

const styles = StyleSheet.create({
  title: { fontFamily: serif, color: colors.cream, fontSize: 22, fontWeight: '700' },
  rule: { color: colors.cream, fontSize: 15, lineHeight: 22, marginBottom: 10 },
});
