import { describe, expect, it } from 'vitest';
import { merchantDirectoryRows } from './merchantDirectoryRows';
import type { Merchant, MerchantMember } from '../../api/cashflow';

const merchants: Merchant[] = [{
  id: '68a5baac-d97f-45b4-9119-b4c5b9de7c9b',
  name: 'Teste', role: 'ADMIN'
}];

describe('merchant directory rows', () => {
  it('creates one table row per member of the same shop', () => {
    const members: MerchantMember[] = [
      { username: 'cashflow.admin', role: 'ADMIN' },
      { username: 'teste', role: 'OPERATOR' },
      { username: 'teste3', role: 'OPERATOR' }
    ];
    const rows = merchantDirectoryRows(merchants, { [merchants[0].id]: members });
    expect(rows).toHaveLength(2);
    expect(rows.map(row => row.member?.username)).toEqual(['teste', 'teste3']);
    expect(rows.map(row => row.first)).toEqual([true, false]);
    expect(rows.map(row => row.span)).toEqual([2, 2]);
    expect(rows.every(row => row.merchant.id === merchants[0].id)).toBe(true);
  });

  it('orders stores alphabetically without changing their source array', () => {
    const sample: Merchant[] = [
      { id: '00000000-0000-4000-8000-000000000003', name: 'Loja Z', role: 'ADMIN' },
      { id: '00000000-0000-4000-8000-000000000002', name: 'Árvore', role: 'ADMIN' },
      { id: '00000000-0000-4000-8000-000000000001', name: 'loja A', role: 'ADMIN' }
    ];
    const originalNames = sample.map(item => item.name);
    const ordered = merchantDirectoryRows(sample, {});
    expect(ordered.map(row => row.merchant.name)).toEqual(['Árvore', 'loja A', 'Loja Z']);
    expect(sample.map(item => item.name)).toEqual(originalNames);
  });

  it('shows a placeholder when a shop has no visible account', () => {
    const rows = merchantDirectoryRows(merchants, {
      [merchants[0].id]: [{ username: 'cashflow.admin', role: 'ADMIN' }]
    });
    expect(rows).toHaveLength(1);
    expect(rows[0].member).toBeNull();
    expect(rows[0].loading).toBe(false);
    expect(rows[0].unavailable).toBe(false);
  });

  it('distinguishes pending and failed member lookups', () => {
    expect(merchantDirectoryRows(merchants, {})[0].loading).toBe(true);
    expect(merchantDirectoryRows(merchants, { [merchants[0].id]: null })[0].unavailable).toBe(true);
  });
});
