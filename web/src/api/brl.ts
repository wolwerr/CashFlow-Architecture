/** Convert Brazilian money input into the invariant decimal API contract. No floating-point arithmetic. */
export function normalizeBrl(raw: string): string | null {
  const value = raw.trim();
  const plain = /^(?:0|[1-9]\d{0,16})(?:[,.]\d{1,2})?$/;
  const grouped = /^[1-9]\d{0,2}(?:\.\d{3})+,\d{1,2}$/;
  if (!plain.test(value) && !grouped.test(value)) return null;

  const invariant = grouped.test(value)
    ? value.replace(/\./g, '').replace(',', '.')
    : value.replace(',', '.');
  const [whole, fraction = ''] = invariant.split('.');
  if (BigInt(whole) > 99999999999999999n) return null;
  const decimals = fraction.padEnd(2, '0');
  if (BigInt(whole) === 0n && decimals === '00') return null;
  return whole + '.' + decimals;
}

export function validBrl(value: string): boolean {
  return normalizeBrl(value) !== null;
}
