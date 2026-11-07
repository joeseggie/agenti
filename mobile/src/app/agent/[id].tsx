import { useLocalSearchParams } from 'expo-router';
import { AgentScreen } from '@/screens/agent';
import { routeId } from '@/utils/format';
import { Message, Screen } from '@/components/content';

export default function Route() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const agentId = routeId(id);
  return agentId ? (
    <AgentScreen id={agentId} />
  ) : (
    <Screen>
      <Message title="Invalid agent link" />
    </Screen>
  );
}
