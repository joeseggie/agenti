import { useRef, useState } from 'react';
import { Alert, Keyboard, Text, View } from 'react-native';
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
  const query = useQuery({
    queryKey: ['cash-count', id, isOpening],
    queryFn: ({ signal }) => (id ? api.count(id, signal) : api.initializeCount(isOpening, signal)),
    staleTime: Infinity,
    refetchOnReconnect: false,
    refetchOnWindowFocus: false,
  });
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
  const saving = useRef(false);

  function update(id: number, change: Partial<WalletDraft>) {
    setDrafts((previous) => {
      const entry = previous[id];
      return entry ? { ...previous, [id]: { ...entry, ...change } } : previous;
    });
    setNotice(null);
  }

  async function save(submit: boolean) {
    if (saving.current || form.isReadOnly) return;
    saving.current = true;
    setPending(true);
    setError(null);
    setNotice(null);
    Keyboard.dismiss();
    try {
      const prepared = prepareCount(form, drafts, explanation);
      const saved = await api.saveCount(prepared);
      const persisted = {
        ...prepared,
        cashCountId: saved.cashCountId,
        cashSessionId: saved.cashSessionId,
      };
      setForm(persisted);
      // Persist the returned ID before submission so failed submission can safely retry the same count.
      if (submit) {
        await api.submitCount(saved.cashCountId);
        await queries.invalidateQueries();
        router.replace('/counts');
      } else {
        setNotice('Draft saved on the server. Submit it when you are ready for approval.');
        await queries.invalidateQueries({ queryKey: ['current-count'] });
      }
    } catch (cause) {
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
                        editable={!pending && !form.isReadOnly}
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
                keyboardType="decimal-pad"
                editable={!pending && !form.isReadOnly}
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
              editable={!pending && !form.isReadOnly}
              onChangeText={(text) => update(wallet.walletId, { observedAt: text })}
            />
            {!form.isReadOnly && (
              <Host matchContents>
                <Button
                  variant="outlined"
                  label={`Observed ${wallet.walletName} now`}
                  disabled={pending}
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
        onChangeText={setExplanation}
        multiline
        editable={!pending && !form.isReadOnly}
      />
      {error && <ErrorNotice error={error} />}
      {notice && <Message title="Draft saved">{notice}</Message>}
      {!form.isReadOnly && form.walletEntries.length > 0 && (
        <Host matchContents>
          <Column spacing={spacing.md}>
            <Button
              label={pending ? 'Saving...' : 'Save draft'}
              variant="outlined"
              disabled={pending}
              onPress={() => {
                void save(false);
              }}
            />
            <Button
              label="Save and submit"
              disabled={pending}
              onPress={() =>
                Alert.alert(
                  'Submit this count?',
                  'The server will apply your branch workflow. Submission may settle float or require approval.',
                  [
                    { text: 'Cancel', style: 'cancel' },
                    {
                      text: 'Submit',
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
