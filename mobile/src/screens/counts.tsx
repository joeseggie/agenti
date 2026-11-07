import { Button, Column, Host } from '@expo/ui';
import { useQuery } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useApi } from '@/auth/session-provider';
import { Detail, Message, QueryState, Screen } from '@/components/content';
import { countStatus } from '@/utils/format';
import { spacing } from '@/theme';

export function CountsScreen() {
  const api = useApi();
  const query = useQuery({
    queryKey: ['current-count'],
    queryFn: ({ signal }) => api.currentCount(signal),
  });
  return (
    <Screen
      refreshing={query.isFetching}
      onRefresh={() => {
        void query.refetch();
      }}
    >
      <QueryState query={query}>
        {(current) => (
          <>
            <Message title={current.statusText}>
              {current.blockReason ||
                'Counts follow your branch workflow and server approval rules.'}
            </Message>
            <Detail label="Opening count" value={countStatus(current.openingCountStatus)} />
            <Detail label="Closing count" value={countStatus(current.closingCountStatus)} />
            <Host matchContents>
              <Column spacing={spacing.md}>
                {current.openingCountId && (
                  <Button
                    label="View opening count"
                    variant="outlined"
                    onPress={() =>
                      router.push({
                        pathname: '/cash-count',
                        params: { id: current.openingCountId },
                      })
                    }
                  />
                )}
                {current.closingCountId && (
                  <Button
                    label="View closing count"
                    variant="outlined"
                    onPress={() =>
                      router.push({
                        pathname: '/cash-count',
                        params: { id: current.closingCountId },
                      })
                    }
                  />
                )}
                {current.canPerformOpeningCount && (
                  <Button
                    label="Record opening count"
                    onPress={() =>
                      router.push({
                        pathname: '/cash-count',
                        params: {
                          opening: 'true',
                          ...(current.openingCountId ? { id: current.openingCountId } : {}),
                        },
                      })
                    }
                  />
                )}
                {current.canPerformClosingCount && (
                  <Button
                    label="Record closing count"
                    onPress={() =>
                      router.push({
                        pathname: '/cash-count',
                        params: {
                          opening: 'false',
                          ...(current.closingCountId ? { id: current.closingCountId } : {}),
                        },
                      })
                    }
                  />
                )}
              </Column>
            </Host>
            <Message title="Approvals and corrections">
              Use the existing web workspace for cash-count approval, bank reconciliation, and
              historical corrections.
            </Message>
          </>
        )}
      </QueryState>
    </Screen>
  );
}
