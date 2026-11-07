import { useRef, useState } from 'react';
import { Alert, Text, View } from 'react-native';
import { Button, Column, Host } from '@expo/ui';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '@/api/client';
import { vaultRequestGuard, VaultRequestScope } from '@/api/vault-request-guard';
import { useApi, useSession } from '@/auth/session-provider';
import { Detail, ErrorNotice, Message, QueryState, Screen } from '@/components/content';
import { Field } from '@/components/field';
import { formatMoney, formatTime } from '@/utils/format';
import { spacing, type } from '@/theme';
import { parseAmount } from './cash-count/form';

const transactionTypes = [
  'Unknown',
  'Opening',
  'Closing',
  'Manual deposit',
  'Manual withdrawal',
  'Adjustment',
  'Surplus deposit',
];
const transactionStatuses = ['Unknown', 'Pending', 'Completed', 'Rejected', 'Expired'];

export function VaultScreen() {
  const { user, controller } = useSession();
  if (!user?.branchId || user.role === 'Agent')
    return (
      <Screen>
        <Message title="Vault unavailable">
          An administrator or supervisor branch assignment is required.
        </Message>
      </Screen>
    );
  return (
    <BranchVault
      key={`${user.userId}:${user.branchId}`}
      scope={{ apiUrl: controller.apiUrl, userId: user.userId, branchId: user.branchId }}
      canApprove={user.role === 'Admin'}
    />
  );
}

function BranchVault({ scope, canApprove }: { scope: VaultRequestScope; canApprove: boolean }) {
  const { branchId } = scope;
  const api = useApi();
  const { controller } = useSession();
  const queries = useQueryClient();
  const pendingKey = ['vault-pending', scope.apiUrl, scope.userId, branchId];
  const savedRequest = useQuery({
    queryKey: pendingKey,
    queryFn: () => vaultRequestGuard.isPending(scope),
    refetchOnMount: 'always',
  });
  const query = useQuery({
    queryKey: ['vault', branchId],
    queryFn: async ({ signal }) => {
      const [vault, transactions] = await Promise.all([
        api.vault(branchId, signal),
        api.vaultTransactions(branchId, signal),
      ]);
      return { vault, transactions };
    },
  });
  const [amount, setAmount] = useState('');
  const [notes, setNotes] = useState('');
  const [pending, setPending] = useState(false);
  const [activityChecked, setActivityChecked] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const submitting = useRef(false);
  const uncertain = savedRequest.data === true;
  const creationBlocked =
    pending || savedRequest.isFetching || savedRequest.isError || savedRequest.data !== false;

  async function run(operation: () => Promise<unknown>, message: string, creating = false) {
    if (submitting.current) return;
    submitting.current = true;
    setPending(true);
    setError(null);
    setNotice(null);
    if (creating) setActivityChecked(false);
    try {
      if (creating) {
        await vaultRequestGuard.execute(scope, async () => {
          const current = controller.getSnapshot();
          if (
            current.status !== 'signedIn' ||
            current.user.userId !== scope.userId ||
            current.user.branchId !== branchId
          )
            throw new ApiError(
              'Your account assignment changed. Reopen the vault before requesting an adjustment.',
              401,
            );
          return operation();
        });
      } else {
        await operation();
      }
      if (creating) {
        setAmount('');
        setNotes('');
      }
      setNotice(message);
      await queries.invalidateQueries({ queryKey: ['vault', branchId] });
      await queries.invalidateQueries({ queryKey: ['dashboard'] });
    } catch (cause) {
      setError(
        cause instanceof Error ? cause.message : 'The vault operation could not be completed.',
      );
    } finally {
      if (creating) await queries.invalidateQueries({ queryKey: pendingKey });
      submitting.current = false;
      setPending(false);
    }
  }

  async function refreshActivity() {
    setActivityChecked(false);
    const result = await query.refetch();
    if (result.isSuccess) setActivityChecked(true);
  }

  async function acknowledgeRequest() {
    if (!activityChecked || query.isFetching || query.isError) return;
    setPending(true);
    setError(null);
    try {
      await vaultRequestGuard.acknowledge(scope);
      await queries.invalidateQueries({ queryKey: pendingKey });
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Cannot clear the saved request.');
    } finally {
      setPending(false);
    }
  }

  function request(isDeposit: boolean) {
    let value: number;
    try {
      value = parseAmount(amount);
      if (value <= 0) throw new Error('The amount must be greater than zero.');
      if (notes.trim().length < 10)
        throw new Error('Provide at least 10 characters of audit notes.');
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Check the adjustment fields.');
      return;
    }
    Alert.alert(
      `Request ${isDeposit ? 'deposit' : 'withdrawal'}?`,
      `${formatMoney(value)}. Another administrator must approve this request.`,
      [
        { text: 'Cancel', style: 'cancel' },
        {
          text: 'Request',
          onPress: () => {
            void run(
              () =>
                api.requestAdjustment(branchId, { amount: value, isDeposit, notes: notes.trim() }),
              'Adjustment requested. A different administrator must approve it before expiry.',
              true,
            );
          },
        },
      ],
    );
  }

  return (
    <Screen
      refreshing={query.isFetching}
      onRefresh={() => {
        void query.refetch();
      }}
    >
      <QueryState query={query}>
        {({ vault, transactions }) => (
          <>
            <Detail label={vault.branchName} value={formatMoney(vault.currentBalance)} />
            {error && <ErrorNotice error={error} />}
            {savedRequest.error && (
              <ErrorNotice
                error={savedRequest.error.message}
                onRetry={() => {
                  void savedRequest.refetch();
                }}
              />
            )}
            {notice && <Message title="Request processed">{notice}</Message>}
            <Message title="Request a manual adjustment">
              Requests require at least 10 characters of notes and expire after 12 hours. The server
              enforces dual approval.
            </Message>
            <Field
              label="Adjustment amount (UGX)"
              value={amount}
              onChangeText={setAmount}
              keyboardType="decimal-pad"
              editable={!creationBlocked}
            />
            <Field
              label="Audit notes"
              value={notes}
              onChangeText={setNotes}
              multiline
              editable={!creationBlocked}
            />
            {uncertain && (
              <Message title="Check activity before retrying">
                <Text style={type.body}>
                  A previous request may have reached the server. This check is retained across app
                  restarts. Refresh and inspect activity; use the web history if the request is not
                  in the latest 50 transactions.
                </Text>
                <Host matchContents>
                  <Button
                    label="Refresh activity"
                    variant="outlined"
                    disabled={pending || query.isFetching}
                    onPress={() => {
                      void refreshActivity();
                    }}
                  />
                </Host>
                <Host matchContents>
                  <Button
                    label="I checked activity; allow another request"
                    variant="outlined"
                    disabled={pending || !activityChecked || query.isFetching || query.isError}
                    onPress={() => {
                      void acknowledgeRequest();
                    }}
                  />
                </Host>
              </Message>
            )}
            <Host matchContents>
              <Column spacing={spacing.sm}>
                <Button
                  label="Request deposit"
                  disabled={creationBlocked}
                  onPress={() => request(true)}
                />
                <Button
                  label="Request withdrawal"
                  variant="outlined"
                  disabled={creationBlocked}
                  onPress={() => request(false)}
                />
              </Column>
            </Host>
            <Message title="Recent activity">
              Latest 50 transactions, excluding expired requests. Pull to refresh.
            </Message>
            {transactions.length === 0 && <Message title="No vault activity" />}
            {transactions.map((transaction) => (
              <View key={transaction.id} style={{ gap: spacing.sm }}>
                <Detail
                  label={`${transactionTypes[transaction.type] ?? 'Unknown type'} / ${transactionStatuses[transaction.status] ?? 'Unknown status'}`}
                  value={formatMoney(transaction.amount)}
                />
                <Text selectable style={type.caption}>
                  {formatTime(transaction.createdAt)} / {transaction.createdBy}
                </Text>
                {transaction.notes && (
                  <Text selectable style={type.body}>
                    {transaction.notes}
                  </Text>
                )}
                {transaction.approvedBy && (
                  <Text style={type.caption}>Approved by {transaction.approvedBy}</Text>
                )}
                {transaction.expiresAt && transaction.status === 1 && (
                  <Text style={type.caption}>Expires {formatTime(transaction.expiresAt)}</Text>
                )}
                {canApprove && transaction.status === 1 && (
                  <Host matchContents>
                    <Column spacing={spacing.sm}>
                      <Button
                        label={`Approve request ${transaction.id}`}
                        disabled={pending}
                        onPress={() =>
                          Alert.alert(
                            'Approve adjustment?',
                            'The server rejects self-approval and expired requests.',
                            [
                              { text: 'Cancel', style: 'cancel' },
                              {
                                text: 'Approve',
                                onPress: () => {
                                  void run(
                                    () => api.reviewAdjustment(transaction.id, true),
                                    'Adjustment approved.',
                                  );
                                },
                              },
                            ],
                          )
                        }
                      />
                      <Button
                        label={`Reject request ${transaction.id}`}
                        variant="outlined"
                        disabled={pending}
                        onPress={() =>
                          Alert.alert(
                            'Reject adjustment?',
                            'The request will not be posted to the vault.',
                            [
                              { text: 'Cancel', style: 'cancel' },
                              {
                                text: 'Reject',
                                style: 'destructive',
                                onPress: () => {
                                  void run(
                                    () => api.reviewAdjustment(transaction.id, false),
                                    'Adjustment rejected.',
                                  );
                                },
                              },
                            ],
                          )
                        }
                      />
                    </Column>
                  </Host>
                )}
              </View>
            ))}
          </>
        )}
      </QueryState>
    </Screen>
  );
}
