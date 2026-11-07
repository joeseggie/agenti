import { useQuery } from '@tanstack/react-query';
import { useApi } from '@/auth/session-provider';
import { Detail, Message, QueryState, Screen } from '@/components/content';
import { countStatus, formatMoney, formatTime, sessionStatus } from '@/utils/format';

export function SessionScreen({ id }: { id: number }) {
  const api = useApi();
  const query = useQuery({
    queryKey: ['session', id],
    queryFn: ({ signal }) => api.session(id, signal),
  });
  return (
    <Screen
      refreshing={query.isFetching}
      onRefresh={() => {
        void query.refetch();
      }}
    >
      <QueryState query={query}>
        {(session) => (
          <>
            <Detail
              label="Business date / status"
              value={`${session.sessionDate} / ${sessionStatus(session.status)}`}
            />
            <Detail label="Opened" value={formatTime(session.openedAt)} />
            <Detail
              label="Operational trading ended"
              value={formatTime(session.operationalClosedAt)}
            />
            <Detail label="Closed" value={formatTime(session.closedAt)} />
            {session.agentSummaries.length === 0 && <Message title="No counts recorded" />}
            {session.agentSummaries.map((agent) => (
              <Message key={agent.agentId} title={`${agent.agentName} (${agent.agentCode})`}>
                {[
                  { label: 'Opening', count: agent.openingCount },
                  { label: 'Closing', count: agent.closingCount },
                ].map(({ label, count }) => {
                  return (
                    <Message
                      key={String(label)}
                      title={`${label}: ${countStatus(count?.status ?? null)}`}
                    >
                      {count?.walletEntries.map((wallet) => (
                        <Detail
                          key={wallet.walletId}
                          label={wallet.walletName}
                          value={formatMoney(wallet.amount)}
                        />
                      ))}
                      {count?.explanation && (
                        <Detail label="Explanation" value={count.explanation} />
                      )}
                      {count?.rejectionReason && (
                        <Detail label="Rejection reason" value={count.rejectionReason} />
                      )}
                    </Message>
                  );
                })}
              </Message>
            ))}
          </>
        )}
      </QueryState>
    </Screen>
  );
}
