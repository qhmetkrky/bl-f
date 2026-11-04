import { ReactNode } from 'react';
import { ActivityIndicator, Modal, Pressable, StyleProp, StyleSheet, Text, View, ViewStyle } from 'react-native';
import { colors } from '../theme';

type ButtonProps = {
  label: string;
  onPress(): void;
  variant?: 'primary' | 'secondary' | 'danger';
  disabled?: boolean;
  busy?: boolean;
  style?: StyleProp<ViewStyle>;
};

export function Button({ label, onPress, variant = 'primary', disabled, busy, style }: ButtonProps) {
  const inactive = disabled || busy;
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled: !!inactive }}
      disabled={inactive}
      onPress={onPress}
      style={({ pressed }) => [
        styles.button,
        styles[variant],
        inactive && styles.disabled,
        pressed && styles.pressed,
        style,
      ]}
    >
      {busy ? (
        <ActivityIndicator color={variant === 'primary' ? colors.ink : colors.cream} />
      ) : (
        <Text style={[styles.label, variant === 'primary' && styles.labelDark]} numberOfLines={1}>
          {label}
        </Text>
      )}
    </Pressable>
  );
}

type SegmentProps<T extends string | number> = {
  options: { value: NoInfer<T>; label: string }[];
  value: T;
  onChange(value: NoInfer<T>): void;
};

export function Segments<T extends string | number>({ options, value, onChange }: SegmentProps<T>) {
  return (
    <View style={styles.segments}>
      {options.map((option) => {
        const active = option.value === value;
        return (
          <Pressable
            key={String(option.value)}
            accessibilityRole="button"
            accessibilityState={{ selected: active }}
            onPress={() => onChange(option.value)}
            style={[styles.segment, active && styles.segmentActive]}
          >
            <Text style={[styles.segmentText, active && styles.segmentTextActive]}>{option.label}</Text>
          </Pressable>
        );
      })}
    </View>
  );
}

export function Sheet({ visible, onClose, children }: { visible: boolean; onClose(): void; children: ReactNode }) {
  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onClose}>
      <Pressable style={styles.backdrop} onPress={onClose}>
        <Pressable style={styles.sheet} onPress={() => undefined}>
          {children}
        </Pressable>
      </Pressable>
    </Modal>
  );
}

const styles = StyleSheet.create({
  button: {
    minHeight: 50,
    paddingHorizontal: 18,
    borderRadius: 12,
    alignItems: 'center',
    justifyContent: 'center',
  },
  primary: { backgroundColor: colors.brass },
  secondary: { backgroundColor: colors.feltLight, borderWidth: 1, borderColor: colors.feltLine },
  danger: { backgroundColor: colors.red },
  disabled: { opacity: 0.45 },
  pressed: { opacity: 0.8, transform: [{ scale: 0.98 }] },
  label: { color: colors.cream, fontSize: 16, fontWeight: '700' },
  labelDark: { color: colors.ink },
  segments: {
    flexDirection: 'row',
    backgroundColor: colors.felt,
    borderRadius: 10,
    padding: 3,
    borderWidth: 1,
    borderColor: colors.feltLine,
  },
  segment: { flex: 1, minHeight: 40, alignItems: 'center', justifyContent: 'center', borderRadius: 8 },
  segmentActive: { backgroundColor: colors.brass },
  segmentText: { color: colors.creamDim, fontSize: 14, fontWeight: '600' },
  segmentTextActive: { color: colors.ink },
  backdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.6)', justifyContent: 'center', padding: 20 },
  sheet: {
    backgroundColor: colors.surface,
    borderRadius: 16,
    padding: 20,
    gap: 12,
    borderWidth: 1,
    borderColor: colors.feltLine,
    maxHeight: '90%',
    width: '100%',
    maxWidth: 480,
    alignSelf: 'center',
  },
});
