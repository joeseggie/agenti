import { useQuery } from '@tanstack/react-query';
import { Stack } from 'expo-router/stack';
import { useApi } from '@/auth/session-provider';
import { Detail, Message, QueryState, Screen } from '@/components/content';
import { formatMoney } from '@/utils/format';

export function AgentScreen({ id }: { id: number }) {
  const api = useApi();
  const query = useQuery({
    queryKey: ['agent', id],
    queryFn: ({ signal }) => api.agent(id, signal),
  });
  return (
    <Screen
      refreshing={query.isFetching}
      onRefresh={() => {
        void query.refetch();
      }}
    >
      <QueryState query={query}>
        {(agent) => (
          <>
            <Stack.Screen options={{ title: agent.fullName || agent.code }} />
            <Detail label="Agent code" value={agent.code} />
            <Detail label="Status" value={agent.isActive ? 'Active' : 'Inactive'} />
            <Detail label="Email" value={agent.email || 'Not provided'} />
            <Detail label="Phone" value={agent.phoneNumber || 'Not provided'} />
            {agent.wallets.length === 0 ? (
              <Message title="No wallets assigned" />
            ) : (
              agent.wallets.map((wallet) => (
                <Detail
                  key={wallet.id}
                  label={`${wallet.name} / ${wallet.walletTypeName}${wallet.isActive ? '' : ' (inactive)'}`}
                  value={formatMoney(wallet.balance, wallet.currency)}
                />
              ))
            )}
          </>
        )}
      </QueryState>
    </Screen>
  );
}
