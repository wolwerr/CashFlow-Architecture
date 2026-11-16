import { describe, expect, it, vi } from 'vitest';
import { normalizeBrl, validBrl } from './brl';
import { pollProjection } from './projection';
import type { Balance } from './cashflow';

describe('Brazilian BRL input', () => {
  it.each([
    ['100', '100.00'], ['100,00', '100.00'], ['100.00', '100.00'],
    ['25', '25.00'], ['25,5', '25.50'], ['0,01', '0.01'],
    ['1.234,56', '1234.56'], [' 99,90 ', '99.90'],
    ['99999999999999999,99', '99999999999999999.99']
  ])('normalizes %s to the decimal API contract', (entered, normalized) => {
    expect(normalizeBrl(entered)).toBe(normalized);
    expect(validBrl(entered)).toBe(true);
  });

  it.each([
    '', '0', '0,00', '0.00', '-1', '1,234', '1.2.3',
    '1,000.00', '1.234.56', 'abc', 'Infinity', '100,999',
    '999999999999999999,00', '1 000,00'
  ])('rejects ambiguous, invalid or out of-range amount %s', value => {
    expect(normalizeBrl(value)).toBeNull();
    expect(validBrl(value)).toBe(false);
  });
});

const merchantId = '7051cab6-36b5-4b80-bc79-a8897a627e2e';
const referenceDate = '2026-11-16';
const balance = (credit_total: string, debit_total: string): Balance => ({
  merchant_id: merchantId, reference_date: referenceDate, currency: 'BRL',
  credit_total, debit_total, net_amount: '0.00', closing_balance: '0.00'
});
const immediate = async () => undefined;

describe('eventual consolidation refresh', () => {
  it('waits for a recorded debit instead of treating a stale balance as success', async () => {
    const read = vi.fn()
      .mockResolvedValueOnce(balance('100.00', '0.00'))
      .mockResolvedValueOnce(balance('100.00', '25.00'));
    const seen: Balance[] = [];
    const projection = {
      merchantId, referenceDate, type: 'DEBIT' as const,
      amount: '25.00', before: balance('100.00', '0.00')
    };
    expect(await pollProjection(projection, read, b => seen.push(b), () => true, immediate)).toBe(true);
    expect(read).toHaveBeenCalledTimes(2);
    expect(seen.at(-1)?.debit_total).toBe('25.00');
  });

  it('detects a credit after eventual projection', async () => {
    const updated = vi.fn();
    expect(await pollProjection({
      merchantId, referenceDate, type: 'CREDIT', amount: '100.00',
      before: balance('0.00', '0.00')
    }, async () => balance('100.00', '0.00'), updated, () => true, immediate)).toBe(true);
    expect(updated).toHaveBeenCalledOnce();
  });

  it('never retries a transaction when reading the projection fails', async () => {
    const read = vi.fn().mockRejectedValue(new Error('Temporary outage'));
    const onBalance = vi.fn();
    expect(await pollProjection({
      merchantId, referenceDate, type: 'DEBIT', amount: '25.00', before: null
    }, read, onBalance, () => true, immediate, 3)).toBe(false);
    expect(read).toHaveBeenCalledTimes(3);
    expect(onBalance).not.toHaveBeenCalled();
  });

  it('stops when selection or authentication changes', async () => {
    const read = vi.fn();
    expect(await pollProjection({
      merchantId, referenceDate, type: 'DEBIT', amount: '25.00', before: null
    }, read, vi.fn(), () => false, immediate)).toBe(false);
    expect(read).not.toHaveBeenCalled();
  });

  it('ignores a response for a different merchant', async () => {
    const read = vi.fn()
      .mockResolvedValueOnce({ ...balance('100.00', '25.00'), merchant_id: 'other' })
      .mockResolvedValueOnce(balance('100.00', '25.00'));
    expect(await pollProjection({
      merchantId, referenceDate, type: 'DEBIT', amount: '25.00', before: null
    }, read, vi.fn(), () => true, immediate)).toBe(true);
    expect(read).toHaveBeenCalledTimes(2);
  });
});
