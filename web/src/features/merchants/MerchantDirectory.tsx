import { useEffect, useState } from 'react';
import { addMerchantOperator, listMerchantMembers, removeMerchantMember,
  renameMerchant, updateMerchantMember } from '../../api/cashflow';
import type { Merchant, MerchantMember } from '../../api/cashflow';
import { merchantRoleLabel } from './merchantPresentation';

type MerchantDirectoryProps = {
  items: Merchant[];
  selectedId: string;
  token: string;
  onSelect: (merchantId: string) => void;
  onRefresh: () => Promise<void>;
};

type MerchantAction = 'add' | 'role' | 'remove' | 'restore' | 'rename';

export function MerchantDirectory({ items, selectedId, token, onSelect, onRefresh }: MerchantDirectoryProps) {
  const [members, setMembers] = useState<Record<string, MerchantMember[] | null>>({});
  const [failed, setFailed] = useState(false);
  const [revision, setRevision] = useState(0);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [editedName, setEditedName] = useState('');
  const [editingUsername, setEditingUsername] = useState('');
  const [nextRole, setNextRole] = useState<'ADMIN' | 'OPERATOR'>('OPERATOR');
  const merchantIds = items.map(item => item.id).join(',');

  useEffect(() => {
    let active = true;
    setMembers({});
    setFailed(false);
    if (!token || !merchantIds) return;
    const ids = merchantIds.split(',');
    void Promise.all(ids.map(async id => {
      try {
        const accounts = await listMerchantMembers(token, id);
        return { id, accounts };
      } catch {
        return { id, accounts: null };
      }
    })).then(results => {
      if (!active) return;
      setFailed(results.some(result => result.accounts === null));
      setMembers(Object.fromEntries(results.map(result => [result.id, result.accounts])));
    });
    return () => { active = false; };
  }, [merchantIds, token, revision]);

  const selected = items.find(item => item.id === selectedId);

  async function perform(action: MerchantAction, account?: string) {
    if (!selected || !token || working) return;
    setError('');
    setMessage('');
    setWorking(true);
    try {
      if (action === 'add') {
        await addMerchantOperator(token, selectedId, username.trim(), password);
        setUsername('');
        setPassword('');
      } else if (action === 'role' && account) {
        await updateMerchantMember(token, selectedId, account, nextRole);
        setEditingUsername('');
      } else if (action === 'remove' && account) {
        if (!window.confirm(`Desativar ${account} na loja ${selected.name}? Será possível reativar sem perder histórico.`)) return;
        await removeMerchantMember(token, selectedId, account);
      } else if (action === 'restore' && account) {
        await updateMerchantMember(token, selectedId, account, 'OPERATOR');
      } else if (action === 'rename') {
        await renameMerchant(token, selectedId, editedName.trim());
        setEditedName('');
      }
      await onRefresh();
      setRevision(previous => previous + 1);
      setMessage(action === 'remove'
        ? 'Usuário desativado nesta loja. É possível reativar sem perder o histórico.'
        : action === 'restore' ? 'Comerciante reativado nesta loja.' : 'Alteração realizada.');
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Falha ao atualizar a loja.');
    } finally {
      setWorking(false);
    }
  }

  return <div className="merchant-directory">
    <h3>Comerciantes cadastrados</h3>
    <p className="muted">Usuários das lojas e seus cargos. A conta técnica da plataforma não aparece nesta lista.</p>
    {failed && <p className="muted" role="status">Não foi possível consultar todos os usuários vinculados.</p>}
    {items.length === 0
      ? <p className="muted">Nenhum comerciante associado à sua conta.</p>
      : <div className="table-wrap">
        <table>
          <thead><tr><th>Comerciante</th><th>Usuário da loja</th><th>Cargo</th><th>Ações</th></tr></thead>
          <tbody>
            {items.map(item => {
              const accounts = members[item.id];
              // The bootstrap administrator must remain in Keycloak to manage the
              // merchant, but is not an operator account shown in the directory.
              const visible = accounts?.filter(account =>
                account.username.toLowerCase() !== 'cashflow.admin') ?? [];
              return <tr key={item.id}>
                <td>{item.name}</td>
                <td>{accounts === undefined ? 'Carregando usuários...' :
                  accounts === null ? 'Indisponível' : visible.length === 0 ? 'Sem usuário da loja' :
                    <div className="merchant-accounts">
                      {visible.map(account => <span key={account.username + account.role}>{account.username}</span>)}
                    </div>}</td>
                <td>{accounts === undefined ? '...' : accounts === null ? 'Indisponível' :
                  <div className="merchant-accounts">
                    {visible.map(account => <span key={account.username + account.role}>
                      {merchantRoleLabel(account.role)}
                    </span>)}
                  </div>}</td>
                <td><div className="merchant-row-actions">
                  <button type="button" className="outline"
                    disabled={item.id === selectedId || working}
                    onClick={() => { onSelect(item.id); setError(''); setMessage(''); }}>
                    {item.id === selectedId ? 'Selecionado' : 'Selecionar'}
                  </button>
                  {item.id === selectedId && visible.map(account =>
                    <div key={account.username} className="merchant-member-actions">
                      {account.role === 'INACTIVE' ?
                        <button type="button" className="outline" disabled={working}
                          onClick={() => void perform('restore', account.username)}>Reativar</button>
                      : editingUsername === account.username ? <>
                        <select aria-label={`Novo cargo de ${account.username}`} value={nextRole}
                          onChange={event => setNextRole(event.target.value as 'ADMIN' | 'OPERATOR')}>
                          <option value="OPERATOR">Comerciante</option>
                          <option value="ADMIN">ADMIN</option>
                        </select>
                        <button type="button" disabled={working}
                          onClick={() => void perform('role', account.username)}>Salvar cargo</button>
                        <button type="button" className="outline"
                          onClick={() => setEditingUsername('')}>Cancelar</button>
                      </> : <>
                        <button type="button" className="outline" disabled={working}
                          onClick={() => { setEditingUsername(account.username); setNextRole(account.role === 'ADMIN' ? 'ADMIN' : 'OPERATOR'); }}>
                          Editar cargo
                        </button>
                        <button type="button" className="outline" disabled={working}
                          onClick={() => void perform('remove', account.username)}>Desativar</button>
                      </>}
                    </div>)}
                </div></td>
              </tr>;
            })}
          </tbody>
        </table>
      </div>}
    {selected && <div className="merchant-directory-tools">
      <h3>Gerenciar {selected.name}</h3>
      <form onSubmit={event => { event.preventDefault(); void perform('rename'); }}>
        <label>Atualizar nome da loja
          <input aria-label="Novo nome da loja" maxLength={100} value={editedName}
            onChange={event => setEditedName(event.target.value)} placeholder={selected.name} />
        </label>
        <button type="submit" disabled={working || !editedName.trim()}>Salvar nome</button>
      </form>
      <form onSubmit={event => { event.preventDefault(); void perform('add'); }}>
        <h3>Adicionar comerciante à loja existente</h3>
        <label>Usuário
          <input aria-label="Novo usuário da loja" value={username} maxLength={64}
            autoComplete="off" onChange={event => setUsername(event.target.value)} />
        </label>
        <label>Senha inicial
          <input aria-label="Senha do novo usuário" type="password" value={password}
            minLength={12} maxLength={128} autoComplete="new-password"
            onChange={event => setPassword(event.target.value)} />
        </label>
        <button type="submit" disabled={working || username.trim().length < 3 || password.length < 12}>
          Adicionar comerciante
        </button>
      </form>
    </div>}
    {message && <p role="status">{message}</p>}
    {error && <p role="alert">{error}</p>}
  </div>;
}
