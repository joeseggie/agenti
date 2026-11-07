import { useRef, useState } from 'react';
import { ActivityIndicator, Alert, Keyboard, Text, View } from 'react-native';
import { Button, Column, Host } from '@expo/ui';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { Stack } from 'expo-router/stack';
import { useApi } from '@/auth/session-provider';
import { ApiError } from '@/api/client';
import { CashCountForm } from '@/api/contracts';
import { Detail, ErrorNotice, Message, QueryState, Screen } from '@/components/content';
import { Field } from '@/components/field';
import { formatMoney } from '@/utils/format';
import { spacing, type } from '@/theme';
import { createDrafts, prepareCount, WalletDraft } from './form';

export function CashCountScreen({ id, isOpening }: { id: number | null; isOpening: boolean }) {
  const api = useApi();
  const [hasFreshForm, setHasFreshForm] = useState(false);
  const query = useQuery({
    queryKey: ['cash-count', id, isOpening],
    queryFn: ({ signal }) => (id ? api.count(id, signal) : api.initializeCount(isOpening, signal)),
    refetchOnMount: 'always',
    refetchOnReconnect: false,
    refetchOnWindowFocus: false,
  });

  // Once initialized, cache refreshes must not unmount the editor and discard its draft.
  if (!hasFreshForm && query.isFetchedAfterMount && query.isSuccess && !query.isFetching) {
    setHasFreshForm(true);
  }

  if (!hasFreshForm) {
    return (
      <Screen>
        {query.isFetchedAfterMount && query.error ? (
          <ErrorNotice
            error={query.error.message}
            onRetry={() => {
              void query.refetch();
            }}
          />
        ) : (
          <>
            <ActivityIndicator accessibilityLabel="Loading cash count" />
            <Text style={type.caption}>Loading the latest cash count...</Text>
          </>
        )}
      </Screen>
    );
  }
  return <QueryState query={query}>{(form) => <CashCountEditor initial={form} />}</QueryState>;
}

export function CashCountEditor({ initial }: { initial: CashCountForm }) {
  const api = useApi();
  const queries = useQueryClient();
  const [form, setForm] = useState(initial);
  const [drafts, setDrafts] = useState(() => createDrafts(initial));
  const [explanation, setExplanation] = useState(initial.explanation ?? '');
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [submissionRetryId, setSubmissionRetryId] = useState<number | null>(null);
  const saving = useRef(false);

  function update(id: number, change: Partial<WalletDraft>) {
    setDrafts((previous) => {
      const entry = previous[id];
      return entry ? { ...previous, [id]: { ...entry, ...change } } : previous;
    });
    setNotice(null);
  }

  async function save(submit: boolean) {
    if (saving.current || form.isReadOnly || (submissionRetryId !== null && !submit)) return;
    saving.current = true;
    setPending(true);
    setError(null);
    setNotice(null);
    Keyboard.dismiss();
    try {
      if (submit && submissionRetryId !== null) {
        await api.submitCount(submissionRetryId);
        setSubmissionRetryId(null);
        await queries.invalidateQueries();
        router.replace('/counts');
        return;
      }

      const prepared = prepareCount(form, drafts, explanation);
      const saved = await api.saveCount(prepared);
      const persisted = {
        ...prepared,
        cashCountId: saved.cashCountId,
        cashSessionId: saved.cashSessionId,
      };
      setForm(persisted);
      await queries.invalidateQueries({ queryKey: ['cash-count'] });
      // Persist the returned ID before submission so failed submission can safely retry the same count.
      if (submit) {
        setSubmissionRetryId(saved.cashCountId);
        await api.submitCount(saved.cashCountId);
        setSubmissionRetryId(null);
        await queries.invalidateQueries();
        router.replace('/counts');
      } else {
        setNotice('Draft saved on the server. Submit it when you are ready for approval.');
        await queries.invalidateQueries({ queryKey: ['current-count'] });
      }
    } catch (cause) {
      if (cause instanceof ApiError && cause.status === 400) {
        setSubmissionRetryId(null);
      }
      setError(
        cause instanceof ApiError || cause instanceof Error
          ? cause.message
          : 'The count could not be saved.',
      );
    } finally {
      saving.current = false;
      setPending(false);
    }
  }

  return (
    <Screen>
      <Stack.Screen options={{ title: `${form.isOpening ? 'Opening' : 'Closing'} count` }} />
      <Detail label="Count date" value={form.countDate ?? 'Server business date'} />
      {form.isReadOnly && (
        <Message title="Read-only count">
          This count cannot be edited. Review or corrections remain in the web workspace.
        </Message>
      )}
      {form.previousClosingTotal !== null && (
        <Detail
          label={`Previous approved closing (${form.previousClosingDate})`}
          value={formatMoney(form.previousClosingTotal)}
        />
      )}
      {form.walletEntries.length === 0 && (
        <Message title="No wallets available">
          Contact your administrator before recording a count.
        </Message>
      )}
      {form.walletEntries.map((wallet) => {
        const draft = drafts[wallet.walletId];
        if (!draft) return null;
        return (
          <View key={wallet.walletId} style={{ gap: spacing.md }}>
            <Message title={`${wallet.walletName} / ${wallet.walletTypeName}`}>
              {`Expected: ${formatMoney(wallet.expectedBalance)}`}
            </Message>
            {wallet.supportsDenominations && wallet.denominations ? (
              (['notes', 'coins'] as const).map((kind) => (
                <View key={kind} style={{ gap: spacing.sm }}>
                  <Text style={type.headline}>{kind === 'notes' ? 'Notes' : 'Coins'}</Text>
                  {Object.keys(draft[kind])
                    .sort((a, b) => Number(b) - Number(a))
                    .map((value) => (
                      <Field
                        key={value}
                        label={`${wallet.walletName}: ${value} ${kind}`}
                        value={draft[kind][value] ?? '0'}
                        keyboardType="number-pad"
                        editable={!pending && !form.isReadOnly && submissionRetryId === null}
                        selectTextOnFocus
                        onChangeText={(text) =>
                          update(wallet.walletId, { [kind]: { ...draft[kind], [value]: text } })
                        }
                      />
                    ))}
                </View>
              ))
            ) : (
              <Field
                label={`${wallet.walletName} amount`}
                value={draft.amount}
                keyboardType={
                  wallet.walletTypeName === 'Cash' ? 'decimal-pad' : 'numbers-and-punctuation'
                }
                editable={!pending && !form.isReadOnly && submissionRetryId === null}
                selectTextOnFocus
                onChangeText={(text) => update(wallet.walletId, { amount: text })}
              />
            )}
            <Field
              label={`${wallet.walletName} observed at (ISO timestamp with timezone)`}
              value={draft.observedAt}
              autoCorrect={false}
              autoCapitalize="none"
              placeholder="Required for bank reconciliation counts"
              editable={!pending && !form.isReadOnly && submissionRetryId === null}
              onChangeText={(text) => update(wallet.walletId, { observedAt: text })}
            />
            {!form.isReadOnly && (
              <Host matchContents>
                <Button
                  variant="outlined"
                  label={`Observed ${wallet.walletName} now`}
                  disabled={pending || submissionRetryId !== null}
                  onPress={() => update(wallet.walletId, { observedAt: new Date().toISOString() })}
                />
              </Host>
            )}
          </View>
        );
      })}
      <Field
        label="Explanation / discrepancy notes"
        value={explanation}
        onChangeText={(text) => {
          setExplanation(text);
          setNotice(null);
        }}
        multiline
        editable={!pending && !form.isReadOnly && submissionRetryId === null}
      />
      {error && <ErrorNotice error={error} />}
      {notice && <Message title="Draft saved">{notice}</Message>}
      {!form.isReadOnly && form.walletEntries.length > 0 && (
        <Host matchContents>
          <Column spacing={spacing.md}>
            <Button
              label={pending ? 'Saving...' : 'Save draft'}
              variant="outlined"
              disabled={pending || submissionRetryId !== null}
              onPress={() => {
                void save(false);
              }}
            />
            <Button
              label={submissionRetryId !== null ? 'Retry submission' : 'Save and submit'}
              disabled={pending}
              onPress={() =>
                Alert.alert(
                  submissionRetryId !== null ? 'Retry submission?' : 'Submit this count?',
                  submissionRetryId !== null
                    ? 'Retry submission for the saved count before making further changes.'
                    : 'The server will apply your branch workflow. Submission may settle float or require approval.',
                  [
                    { text: 'Cancel', style: 'cancel' },
                    {
                      text: submissionRetryId !== null ? 'Retry' : 'Submit',
                      onPress: () => {
                        void save(true);
                      },
                    },
                  ],
                )
              }
            />
          </Column>
        </Host>
      )}
    </Screen>
  );
}
