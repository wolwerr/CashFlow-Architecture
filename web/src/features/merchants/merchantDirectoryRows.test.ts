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
    expect(rows.every(row => row.merchant.id === merchants[0].id)).toBe(true);
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
