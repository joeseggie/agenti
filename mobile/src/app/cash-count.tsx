import { useLocalSearchParams } from 'expo-router';
import { CashCountScreen } from '@/screens/cash-count';
import { routeId } from '@/utils/format';
import { Message, Screen } from '@/components/content';

export default function Route() {
  const { id, opening } = useLocalSearchParams<{ id?: string; opening?: string }>();
  const countId = routeId(id);
  if ((id && !countId) || (!id && opening !== 'true' && opening !== 'false')) {
    return (
      <Screen>
        <Message title="Invalid cash count link" />
      </Screen>
    );
  }
  return <CashCountScreen id={countId} isOpening={opening === 'true'} />;
}
