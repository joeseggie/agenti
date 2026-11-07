export function formatMoney(amount: number, currency = 'UGX') {
  return new Intl.NumberFormat('en-UG', {
    style: 'currency',
    currency,
    maximumFractionDigits: 2,
  }).format(amount);
}

export function formatTime(value: string | null) {
  if (!value) return 'Not recorded';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

export function routeId(value: string | string[] | undefined): number | null {
  if (typeof value !== 'string' || !/^[1-9]\d*$/.test(value)) return null;
  const id = Number(value);
  return Number.isSafeInteger(id) ? id : null;
}

export function countStatus(value: number | null) {
  return value === null
    ? 'Not recorded'
    : (['Draft', 'Pending approval', 'Approved', 'Rejected'][value] ?? 'Unknown status');
}

export function sessionStatus(value: number) {
  return (
    ['Closed', 'Open', 'Pending', 'Under review', 'Completed', 'Blocked'][value] ?? 'Unknown status'
  );
}
