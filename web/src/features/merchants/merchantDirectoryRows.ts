import type { Merchant, MerchantMember } from '../../api/cashflow';
import { sortedMerchants } from './merchantPresentation';

export type DirectoryRow = {
  merchant: Merchant;
  member: MerchantMember | null;
  first: boolean;
  span: number;
  loading: boolean;
  unavailable: boolean;
};

export function merchantDirectoryRows(
  items: Merchant[],
  members: Record<string, MerchantMember[] | null>
): DirectoryRow[] {
  return sortedMerchants(items).flatMap(merchant => {
    const accounts = members[merchant.id];
    const visible = accounts?.filter(account =>
      account.username.toLowerCase() !== 'cashflow.admin') ?? [];
    const rows: Array<MerchantMember | null> = visible.length ? visible : [null];
    return rows.map((member, index) => ({
      merchant, member, first: index === 0, span: rows.length,
      loading: accounts === undefined,
      unavailable: accounts === null
    }));
  });
}
