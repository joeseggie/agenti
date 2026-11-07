import { useLocalSearchParams } from 'expo-router';
import { SessionScreen } from '@/screens/session';
import { routeId } from '@/utils/format';
import { Message, Screen } from '@/components/content';

export default function Route() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const sessionId = routeId(id);
  return sessionId ? (
    <SessionScreen id={sessionId} />
  ) : (
    <Screen>
      <Message title="Invalid session link" />
    </Screen>
  );
}
