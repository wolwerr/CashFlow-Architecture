import { beforeEach, describe, expect, it, vi } from 'vitest';
import { addMerchantOperator, removeMerchantMember, renameMerchant, updateMerchantMember, renameMerchantMember } from '../../api/cashflow';
import { executeMerchantMutation } from './merchantMutations';
import type { MerchantCommand } from './merchantMutations';

vi.mock('../../api/cashflow', () => ({
  addMerchantOperator: vi.fn(),
  removeMerchantMember: vi.fn(),
  renameMerchant: vi.fn(),
  updateMerchantMember: vi.fn(),
  renameMerchantMember: vi.fn()
}));

const command = (action: MerchantCommand['action'], account?: string): MerchantCommand => ({
  action, token: 'token', merchantId: '68a5baac-d97f-45b4-9119-b4c5b9de7c9b',
  merchantName: 'Loja Teste', account, username: ' novo.operador ', password: 'senha-completa',
  editedName: ' Loja Renomeada ', nextRole: 'ADMIN', newUsername: ' loja.nova ', confirmDeactivate: vi.fn().mockReturnValue(true)
});

beforeEach(() => vi.clearAllMocks());

describe('merchant mutations', () => {
  it('adds operator without creating a second merchant', async () => {
    expect(await executeMerchantMutation(command('add'))).toBe(true);
    expect(addMerchantOperator).toHaveBeenCalledWith('token',
      '68a5baac-d97f-45b4-9119-b4c5b9de7c9b', 'novo.operador', 'senha-completa');
  });
  it('changes role for an actual account', async () => {
    expect(await executeMerchantMutation(command('role', 'loja.teste'))).toBe(true);
    expect(updateMerchantMember).toHaveBeenCalledWith('token',
      '68a5baac-d97f-45b4-9119-b4c5b9de7c9b', 'loja.teste', 'ADMIN');
  });
  it('rejects missing account for a role change', async () => {
    expect(await executeMerchantMutation(command('role'))).toBe(false);
    expect(updateMerchantMember).not.toHaveBeenCalled();
  });
  it('keeps a member active when soft deletion is cancelled', async () => {
    const cmd = command('remove', 'loja.teste');
    cmd.confirmDeactivate = vi.fn().mockReturnValue(false);
    expect(await executeMerchantMutation(cmd)).toBe(false);
    expect(removeMerchantMember).not.toHaveBeenCalled();
  });
  it('soft-deletes only after confirmation', async () => {
    const cmd = command('remove', 'loja.teste');
    expect(await executeMerchantMutation(cmd)).toBe(true);
    expect(cmd.confirmDeactivate).toHaveBeenCalledWith(expect.stringContaining('Loja Teste'));
    expect(removeMerchantMember).toHaveBeenCalledWith('token',
      '68a5baac-d97f-45b4-9119-b4c5b9de7c9b', 'loja.teste');
  });
  it('rejects soft delete without a selected account', async () => {
    expect(await executeMerchantMutation(command('remove'))).toBe(false);
    expect(removeMerchantMember).not.toHaveBeenCalled();
  });
  it('reactivates a dormant account as operator', async () => {
    expect(await executeMerchantMutation(command('restore', 'loja.teste'))).toBe(true);
    expect(updateMerchantMember).toHaveBeenCalledWith('token',
      '68a5baac-d97f-45b4-9119-b4c5b9de7c9b', 'loja.teste', 'OPERATOR');
  });
  it('does not reactivate without an account', async () => {
    expect(await executeMerchantMutation(command('restore'))).toBe(false);
  });
  it('updates a member username without changing their identity or merchant', async () => {
    expect(await executeMerchantMutation(command('username', 'loja.teste'))).toBe(true);
    expect(renameMerchantMember).toHaveBeenCalledWith('token',
      '68a5baac-d97f-45b4-9119-b4c5b9de7c9b', 'loja.teste', 'loja.nova');
  });
  it('does not update username without a selected user', async () => {
    expect(await executeMerchantMutation(command('username'))).toBe(false);
    expect(renameMerchantMember).not.toHaveBeenCalled();
  });
  it('updates only the merchant display name', async () => {
    expect(await executeMerchantMutation(command('rename'))).toBe(true);
    expect(renameMerchant).toHaveBeenCalledWith('token',
      '68a5baac-d97f-45b4-9119-b4c5b9de7c9b', 'Loja Renomeada');
  });
  it('propagates a refused mutation instead of marking the member active', async () => {
    vi.mocked(updateMerchantMember).mockRejectedValue(new Error('403'));
    await expect(executeMerchantMutation(command('restore', 'loja.teste')))
      .rejects.toThrow('403');
  });
});
