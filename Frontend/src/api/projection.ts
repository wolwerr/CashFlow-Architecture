import type { Balance } from './cashflow';

export type Projection = {
  merchantId: string;
  referenceDate: string;
  type: 'CREDIT' | 'DEBIT';
  amount: string; // normalized invariant BRL amount
  before: Balance | null;
};

const cents = (value: string): bigint => BigInt(value.replace('.', ''));

export async function pollProjection(
  projection: Projection,
  readBalance: () => Promise<Balance>,
  onBalance: (balance: Balance) => void,
  isCurrent: () => boolean,
  delay: (milliseconds: number) => Promise<void>,
  attempts = 6
): Promise<boolean> {
  const field = projection.type === 'CREDIT' ? 'credit_total' : 'debit_total';
  const prior = projection.before?.merchant_id === projection.merchantId &&
    projection.before.reference_date === projection.referenceDate
    ? cents(projection.before[field]) : 0n;
  const expected = prior + cents(projection.amount);

  for (let attempt = 0; attempt < attempts; attempt++) {
    await delay(2000);
    if (!isCurrent()) return false;
    try {
      const next = await readBalance();
      if (!isCurrent()) return false;
      onBalance(next);
      if (next.merchant_id === projection.merchantId &&
          next.reference_date === projection.referenceDate &&
          cents(next[field]) >= expected) return true;
    } catch {
      // Transient retrieval errors do not mean a financially recorded transaction failed.
      // Retry within a bounded period; never post the transaction again.
    }
  }
  return false;
}
