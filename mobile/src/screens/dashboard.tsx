import { Text } from 'react-native';
import { useQuery } from '@tanstack/react-query';
import { Button, Host } from '@expo/ui';
import { router } from 'expo-router';
import { useApi, useSession } from '@/auth/session-provider';
import { Detail, Message, QueryState, Screen } from '@/components/content';
import { formatMoney } from '@/utils/format';
import { type } from '@/theme';

export function DashboardScreen() {
  const api = useApi();
  const { user } = useSession();
  const query = useQuery({
    queryKey: ['dashboard'],
    queryFn: ({ signal }) => api.dashboard(signal),
  });
  return (
    <Screen
      refreshing={query.isFetching}
      onRefresh={() => {
        void query.refetch();
      }}
    >
      <QueryState query={query}>
        {(data) => (
          <>
            <Message title={user?.fullName || 'Your branch'}>
              {data.sessionStatus.statusDisplay}
            </Message>
            <Text selectable style={type.caption}>
              {data.sessionStatus.balanceBasis}
            </Text>
            {data.wallets.length === 0 ? (
              <Message title="No active wallets">
                Ask your administrator to configure your agent wallets.
              </Message>
            ) : (
              data.wallets.map((wallet) => (
                <Detail
                  key={wallet.walletId}
                  label={`${wallet.walletName} / ${wallet.walletTypeName}`}
                  value={formatMoney(wallet.balance, wallet.currency)}
                />
              ))
            )}
            {user?.role !== 'Agent' && (
              <>
                <Detail label="Branch vault (UGX)" value={formatMoney(data.vaultBalance)} />
                <Host matchContents>
                  <Button
                    label="View vault"
                    variant="outlined"
                    onPress={() => router.push('/vault')}
                  />
                </Host>
              </>
            )}
            <Host matchContents>
              <Button label="View my cash counts" onPress={() => router.navigate('/counts')} />
            </Host>
          </>
        )}
      </QueryState>
    </Screen>
  );
}
