import { Text, TextInput, TextInputProps, View } from 'react-native';
import { colors, radius, spacing, type } from '@/theme';

// Controlled, individually labelled fields keep financial drafts and VoiceOver labels in sync.
export function Field({ label, ...props }: TextInputProps & { label: string }) {
  return (
    <View style={{ gap: spacing.sm }}>
      <Text style={type.caption}>{label}</Text>
      <TextInput
        accessibilityLabel={label}
        placeholderTextColor={colors.secondary}
        {...props}
        style={[
          type.body,
          {
            minHeight: spacing.xxl,
            padding: spacing.md,
            backgroundColor: colors.surface,
            borderRadius: radius.md,
            borderCurve: 'continuous',
          },
          props.style,
        ]}
      />
    </View>
  );
}
