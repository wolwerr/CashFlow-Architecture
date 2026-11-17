import { describe, expect, it } from 'vitest';
import { merchantLabel, merchantRoleLabel, selectedMerchant } from './merchantPresentation';
import type { Merchant } from '../../api/cashflow';

const merchants: Merchant[] = [
  { id: '7051cab6-36b5-4b80-bc79-a8897a627e2e', name: 'Loja Campinas', role: 'ADMIN' },
  { id: '59d0b4c5-4e64-46e1-ac52-ad946d2b4865', name: 'Loja Teste', role: 'OPERATOR' }
];

describe('merchant presentation', () => {
  it('shows ADMIN for local admin groups', () => {
    expect(merchantRoleLabel('ADMIN')).toBe('ADMIN');
    expect(merchantLabel(merchants[0])).toBe('Loja Campinas (ADMIN)');
  });

  it('shows Comerciante rather than the internal OPERATOR role', () => {
    expect(merchantRoleLabel('OPERATOR')).toBe('Comerciante');
    expect(merchantLabel(merchants[1])).toBe('Loja Teste (Comerciante)');
  });

  it('selects only merchant UUIDs in the authorized API directory', () => {
    expect(selectedMerchant(merchants, merchants[1].id)).toBe(merchants[1].id);
    expect(selectedMerchant(merchants, '123')).toBe('');
    expect(selectedMerchant([], merchants[0].id)).toBe('');
  });
});
