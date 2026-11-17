import { addMerchantOperator, removeMerchantMember, renameMerchant,
  updateMerchantMember, renameMerchantMember } from '../../api/cashflow';

export type MerchantAction = 'add' | 'role' | 'remove' | 'restore' | 'rename' | 'username';
export type MerchantCommand = {
  action: MerchantAction;
  token: string;
  merchantId: string;
  merchantName: string;
  account?: string;
  username: string;
  password: string;
  editedName: string;
  nextRole: 'ADMIN' | 'OPERATOR';
  newUsername: string;
  confirmDeactivate: (message: string) => boolean;
};

/** false means cancellation, not successful mutation. */
export async function executeMerchantMutation(command: MerchantCommand): Promise<boolean> {
  switch (command.action) {
    case 'add':
      await addMerchantOperator(command.token, command.merchantId,
        command.username.trim(), command.password);
      return true;
    case 'role':
      if (!command.account) return false;
      await updateMerchantMember(command.token, command.merchantId, command.account, command.nextRole);
      return true;
    case 'username':
      if (!command.account || !command.newUsername.trim()) return false;
      await renameMerchantMember(command.token, command.merchantId,
        command.account, command.newUsername.trim());
      return true;
    case 'remove':
      if (!command.account || !command.confirmDeactivate(
        `Desativar ${command.account} na loja ${command.merchantName}? Será possível reativar sem perder histórico.`
      )) return false;
      await removeMerchantMember(command.token, command.merchantId, command.account);
      return true;
    case 'restore':
      if (!command.account) return false;
      await updateMerchantMember(command.token, command.merchantId, command.account, 'OPERATOR');
      return true;
    case 'rename':
      await renameMerchant(command.token, command.merchantId, command.editedName.trim());
      return true;
  }
}
