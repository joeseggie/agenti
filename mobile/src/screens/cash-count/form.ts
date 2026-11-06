import { CashCountForm } from '@/api/contracts';

export function parseAmount(text: string): number {
  const normalized = text.trim();
  if (!/^\d{1,12}(?:\.\d{1,2})?$/.test(normalized)) {
    throw new Error(
      'Enter a non-negative amount with at most 12 whole digits and 2 decimal places.',
    );
  }
  return Number(normalized);
}

export function parseQuantity(text: string): number {
  if (!/^\d{1,6}$/.test(text.trim())) throw new Error('Enter a whole quantity from 0 to 999999.');
  return Number(text);
}

export type WalletDraft = {
  amount: string;
  notes: Record<string, string>;
  coins: Record<string, string>;
  observedAt: string;
};

export function createDrafts(form: CashCountForm): Record<number, WalletDraft> {
  return Object.fromEntries(
    form.walletEntries.map((wallet) => [
      wallet.walletId,
      {
        amount: String(wallet.countedAmount),
        notes: Object.fromEntries(
          Object.entries(wallet.denominations?.notes ?? {}).map(([value, count]) => [
            value,
            String(count),
          ]),
        ),
        coins: Object.fromEntries(
          Object.entries(wallet.denominations?.coins ?? {}).map(([value, count]) => [
            value,
            String(count),
          ]),
        ),
        observedAt: wallet.observedAt ?? '',
      },
    ]),
  );
}

export function prepareCount(
  form: CashCountForm,
  drafts: Record<number, WalletDraft>,
  explanation: string,
): CashCountForm {
  return {
    ...form,
    explanation: explanation.trim() || null,
    walletEntries: form.walletEntries.map((wallet) => {
      const draft = drafts[wallet.walletId];
      if (!draft) throw new Error('A wallet is missing from this count. Reopen the form.');
      let denominations = wallet.denominations;
      let countedAmount: number;
      if (wallet.supportsDenominations && denominations) {
        const notes = Object.fromEntries(
          Object.entries(draft.notes).map(([value, text]) => [value, parseQuantity(text)]),
        );
        const coins = Object.fromEntries(
          Object.entries(draft.coins).map(([value, text]) => [value, parseQuantity(text)]),
        );
        denominations = { notes, coins };
        countedAmount = [...Object.entries(notes), ...Object.entries(coins)].reduce(
          (total, [value, quantity]) => total + Number(value) * quantity,
          0,
        );
      } else {
        countedAmount = parseAmount(draft.amount);
      }
      const observed = draft.observedAt.trim();
      if (
        observed &&
        (!/^\d{4}-\d{2}-\d{2}T.+(?:Z|[+-]\d{2}:\d{2})$/.test(observed) ||
          Number.isNaN(Date.parse(observed)))
      ) {
        throw new Error(
          `Enter a valid observation timestamp with timezone for ${wallet.walletName}, or use the current time.`,
        );
      }
      return {
        ...wallet,
        countedAmount,
        denominations,
        observedAt: observed ? new Date(observed).toISOString() : null,
      };
    }),
  };
}
