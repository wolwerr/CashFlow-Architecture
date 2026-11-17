import type { MerchantMember } from '../../api/cashflow';
import type { MerchantAction } from './merchantMutations';

type MemberActionProps = {
  account: MerchantMember;
  working: boolean;
  editingAccount: string;
  editingUsername: string;
  newUsername: string;
  nextRole: 'ADMIN' | 'OPERATOR';
  onNewUsername: (value: string) => void;
  onNextRole: (role: 'ADMIN' | 'OPERATOR') => void;
  onEditingAccount: (username: string) => void;
  onEditingUsername: (username: string) => void;
  onAction: (action: MerchantAction, username: string) => void;
};

function UsernameActions(props: MemberActionProps) {
  const { account, working, newUsername, editingAccount } = props;
  if (editingAccount !== account.username) {
    return <button type="button" className="outline" disabled={working}
      onClick={() => {
        props.onEditingAccount(account.username);
        props.onEditingUsername('');
        props.onNewUsername(account.username);
      }}>Editar usuário</button>;
  }

  const valid = /^[a-zA-Z][a-zA-Z0-9._-]{2,63}$/.test(newUsername);
  return <div className="merchant-member-actions">
    <input aria-label={`Novo usuário de ${account.username}`}
      value={newUsername} maxLength={64} autoComplete="off"
      onChange={event => props.onNewUsername(event.target.value)} />
    <button type="button" disabled={working || !valid}
      onClick={() => props.onAction('username', account.username)}>Salvar usuário</button>
    <button type="button" className="outline"
      onClick={() => props.onEditingAccount('')}>Cancelar</button>
  </div>;
}

function RoleActions(props: MemberActionProps) {
  const { account, working, editingUsername, nextRole } = props;
  if (account.role === 'INACTIVE') {
    return <button type="button" className="outline" disabled={working}
      onClick={() => props.onAction('restore', account.username)}>Reativar</button>;
  }
  if (editingUsername === account.username) {
    return <div className="merchant-member-actions">
      <select aria-label={`Novo cargo de ${account.username}`} value={nextRole}
        onChange={event => props.onNextRole(event.target.value as 'ADMIN' | 'OPERATOR')}>
        <option value="OPERATOR">Comerciante</option>
        <option value="ADMIN">ADMIN</option>
      </select>
      <button type="button" disabled={working}
        onClick={() => props.onAction('role', account.username)}>Salvar cargo</button>
      <button type="button" className="outline"
        onClick={() => props.onEditingUsername('')}>Cancelar</button>
    </div>;
  }
  return <>
    <button type="button" className="outline" disabled={working}
      onClick={() => {
        props.onEditingUsername(account.username);
        props.onEditingAccount('');
        props.onNextRole(account.role === 'ADMIN' ? 'ADMIN' : 'OPERATOR');
      }}>Editar cargo</button>
    <button type="button" className="outline" disabled={working}
      onClick={() => props.onAction('remove', account.username)}>Desativar</button>
  </>;
}

export function MerchantMemberActions(props: MemberActionProps) {
  return <>
    <UsernameActions {...props} />
    <RoleActions {...props} />
  </>;
}
