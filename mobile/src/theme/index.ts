import { Color } from 'expo-router';
import { Platform, TextStyle } from 'react-native';

export const colors = {
  label: Platform.select({ ios: Color.ios.label, default: '#111827' }),
  secondary: Platform.select({ ios: Color.ios.secondaryLabel, default: '#4b5563' }),
  background: Platform.select({ ios: Color.ios.systemGroupedBackground, default: '#f2f2f7' }),
  surface: Platform.select({ ios: Color.ios.secondarySystemGroupedBackground, default: '#ffffff' }),
  separator: Platform.select({ ios: Color.ios.separator, default: '#d1d5db' }),
  accent: Platform.select({ ios: Color.ios.systemBlue, default: '#007aff' }),
  error: Platform.select({ ios: Color.ios.systemRed, default: '#b91c1c' }),
};

export const spacing = { xs: 4, sm: 8, md: 16, lg: 24, xl: 32, xxl: 48 } as const;
export const radius = { md: 12 } as const;
export const type = {
  title: { fontSize: 22, fontWeight: '600', color: colors.label },
  headline: { fontSize: 17, fontWeight: '600', color: colors.label },
  body: { fontSize: 17, color: colors.label },
  caption: { fontSize: 14, color: colors.secondary },
} as const satisfies Record<string, TextStyle>;
