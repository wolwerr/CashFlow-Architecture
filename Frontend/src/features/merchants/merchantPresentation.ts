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

/** Stable, immutable ordering shared by both store selectors and the directory. */
export function sortedMerchants(items: Merchant[]): Merchant[] {
  return [...items].sort((left, right) =>
    left.name.localeCompare(right.name, 'pt-BR', { sensitivity: 'base', numeric: true }) ||
    left.id.localeCompare(right.id));
}
