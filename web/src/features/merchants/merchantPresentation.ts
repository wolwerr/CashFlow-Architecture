import type { Merchant, MerchantMember } from '../../api/cashflow';

export function merchantRoleLabel(role: Merchant['role'] | MerchantMember['role']): string {
  if (role === 'INACTIVE') return 'Inativo';
  return role === 'ADMIN' ? 'ADMIN' : 'Comerciante';
}

export function merchantLabel(merchant: Merchant): string {
  return `${merchant.name} (${merchantRoleLabel(merchant.role)})`;
}

export function selectedMerchant(items: Merchant[], id: string): string {
  return items.some(item => item.id === id) ? id : '';
}
