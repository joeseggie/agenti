import { CashCountForm } from '@/api/contracts';
import { createDrafts, parseAmount, prepareCount } from './form';

export const form: CashCountForm = {
  cashCountId: null,
  cashSessionId: null,
  isOpening: true,
  isReadOnly: false,
  countDate: '2026-11-06',
  explanation: null,
  previousClosingDate: null,
  previousClosingTotal: null,
  walletEntries: [
    {
      walletId: 1,
      walletName: 'Cash',
      walletTypeName: 'Cash',
      supportsDenominations: true,
      expectedBalance: 0,
      countedAmount: 0,
      observedAt: null,
      denominations: { notes: { '50000': 0 }, coins: { '1000': 0 } },
    },
    {
      walletId: 2,
      walletName: 'Bank',
      walletTypeName: 'Bank',
      supportsDenominations: false,
      expectedBalance: 0,
      countedAmount: 0,
      observedAt: null,
      denominations: null,
    },
  ],
};

test('maps entered denominations and observed times without changing server wallet identities', () => {
  const drafts = createDrafts(form);
  drafts[1]!.notes['50000'] = '2';
  drafts[1]!.coins['1000'] = '3';
  drafts[2]!.amount = '100.25';
  drafts[2]!.observedAt = '2026-11-06T10:00:00+03:00';
  const result = prepareCount(form, drafts, 'Count confirmed');
  expect(result.walletEntries[0]?.countedAmount).toBe(103000);
  expect(result.walletEntries[1]).toMatchObject({
    walletId: 2,
    countedAmount: 100.25,
    observedAt: '2026-11-06T07:00:00.000Z',
  });
  expect(form.walletEntries[0]?.countedAmount).toBe(0);
});

test.each(['', '-1', '1e3', 'NaN', '10.123', '1000000000000', '1,000'])(
  'rejects unsupported amount %s',
  (value) => {
    expect(() => parseAmount(value)).toThrow();
  },
);

test('preserves invalid drafts and rejects fractional quantities or timestamps without timezones', () => {
  const drafts = createDrafts(form);
  drafts[1]!.notes['50000'] = '1.5';
  expect(() => prepareCount(form, drafts, '')).toThrow('whole quantity');
  expect(drafts[1]!.notes['50000']).toBe('1.5');
  drafts[1]!.notes['50000'] = '1';
  drafts[2]!.observedAt = '2026-11-06T10:00:00';
  expect(() => prepareCount(form, drafts, '')).toThrow('timestamp with timezone');
});
