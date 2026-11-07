import type React from 'react';

jest.mock('expo/fetch', () => ({ fetch: jest.fn() }));

// Native rendering is checked on iPhone; component tests exercise the same actions and disabled states.
jest.mock('@expo/ui', () => {
  const { Pressable, Text, View } =
    jest.requireActual<typeof import('react-native')>('react-native');
  return {
    Host: ({ children }: React.PropsWithChildren) => <View>{children}</View>,
    Column: ({ children }: React.PropsWithChildren) => <View>{children}</View>,
    Button: ({
      label,
      onPress,
      disabled,
    }: {
      label: string;
      onPress: () => void;
      disabled?: boolean;
    }) => (
      <Pressable
        accessibilityRole="button"
        accessibilityState={{ disabled: !!disabled }}
        disabled={disabled}
        onPress={onPress}
      >
        <Text>{label}</Text>
      </Pressable>
    ),
  };
});
