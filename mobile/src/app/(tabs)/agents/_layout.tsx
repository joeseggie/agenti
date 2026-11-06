import { Stack } from 'expo-router/stack';

export default function Layout() {
  return (
    <Stack screenOptions={{ headerLargeTitleEnabled: true }}>
      <Stack.Screen name="index" options={{ title: 'Agents' }} />
    </Stack>
  );
}
