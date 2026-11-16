import type { Merchant } from '../../api/cashflow';

export function merchantRoleLabel(role: Merchant['role']): string {
  return role === 'ADMIN' ? 'ADMIN' : 'Comerciante';
}

export function merchantLabel(merchant: Merchant): string {
  return `${merchant.name} (${merchantRoleLabel(merchant.role)})`;
}

export function selectedMerchant(items: Merchant[], id: string): string {
  return items.some(item => item.id === id) ? id : '';
}
