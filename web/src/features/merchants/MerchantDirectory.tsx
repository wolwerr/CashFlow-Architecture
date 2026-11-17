import { useEffect, useState } from 'react';
import { listMerchantMembers } from '../../api/cashflow';
import { executeMerchantMutation } from './merchantMutations';
import type { MerchantAction } from './merchantMutations';
import type { Merchant, MerchantMember } from '../../api/cashflow';
import { merchantDirectoryRows } from './merchantDirectoryRows';
import { MerchantMemberActions } from './MerchantMemberActions';

function mutationMessage(action: MerchantAction): string {
  if (action === 'remove') {
    return 'Usuário desativado nesta loja. É possível reativar sem perder o histórico.';
  }
  if (action === 'restore') return 'Comerciante reativado nesta loja.';
  if (action === 'username') return 'Nome de usuário atualizado no Keycloak.';
  return 'Alteração realizada.';
}

type MerchantDirectoryProps = {
  items: Merchant[];
  selectedId: string;
  token: string;
  onSelect: (merchantId: string) => void;
  onRefresh: () => Promise<void>;
};

export function MerchantDirectory({ items, selectedId, token, onSelect, onRefresh }: MerchantDirectoryProps) {
  const [members, setMembers] = useState<Record<string, MerchantMember[] | null>>({});
  const [failed, setFailed] = useState(false);
  const [revision, setRevision] = useState(0);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [feedbackAccount, setFeedbackAccount] = useState('');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [editedName, setEditedName] = useState('');
  const [editingUsername, setEditingUsername] = useState('');
  const [editingAccount, setEditingAccount] = useState('');
  const [newUsername, setNewUsername] = useState('');
  const [nextRole, setNextRole] = useState<'ADMIN' | 'OPERATOR'>('OPERATOR');
  const [activeTool, setActiveTool] = useState<'rename' | 'add' | null>(null);
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
    setFeedbackAccount(account ?? '');
    setWorking(true);
    try {
      const changed = await executeMerchantMutation({
        action, token, merchantId: selectedId, merchantName: selected.name, account,
        username, password, editedName, nextRole, newUsername,
        confirmDeactivate: message => window.confirm(message)
      });
      if (!changed) return;
      if (action === 'add') {
        setUsername('');
        setPassword('');
        setActiveTool(null);
      }
      if (action === 'role') setEditingUsername('');
      if (action === 'username') { setEditingAccount(''); setNewUsername(''); }
      if (action === 'rename') { setEditedName(''); setActiveTool(null); }
      await onRefresh();
      setRevision(previous => previous + 1);
      setMessage(mutationMessage(action));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Falha ao atualizar a loja.');
    } finally {
      setWorking(false);
    }
  }

  return <div className="merchant-directory">
    <h3>Comerciantes cadastrados</h3>
    <p className="muted">Cada loja pode ter vários usuários. Selecionar uma loja habilita as ações de seus usuários. A conta técnica não aparece.</p>
    {failed && <p className="muted" role="status">Não foi possível consultar todos os usuários vinculados.</p>}
    {message && !feedbackAccount && <p role="status">{message}</p>}
    {error && !feedbackAccount && <p role="alert">{error}</p>}
    {items.length === 0
      ? <p className="muted">Nenhum comerciante associado à sua conta.</p>
      : <div className="table-wrap">
        <table>
          <thead><tr><th>Comerciante</th><th>Usuário da loja</th><th>Cargo</th><th>Status</th><th>Ações</th></tr></thead>
          <tbody>
            {merchantDirectoryRows(items, members).map(({ merchant: item, member: account, first, span, loading, unavailable }) =>
              <tr key={item.id + ':' + (account?.username ?? 'empty')}>
                {first && <td rowSpan={span}>{item.name}</td>}
                <td>{account?.username ?? (loading ? 'Carregando usuários...' :
                  unavailable ? 'Indisponível' : 'Sem usuário da loja')}</td>
                <td>{account ? (account.role === 'ADMIN' ? 'ADMIN' : 'Comerciante') : '-'}</td>
                <td>{account ? (account.role === 'INACTIVE' ? 'Inativo' : 'Ativo') : '-'}</td>
                <td><div className="merchant-row-actions">
                  {first && <button type="button" className="outline"
                    disabled={item.id === selectedId || working}
                    onClick={() => { onSelect(item.id); setError(''); setMessage(''); setFeedbackAccount(''); setEditingAccount(''); setEditingUsername(''); setActiveTool(null); }}>
                    {item.id === selectedId ? 'Selecionado' : 'Selecionar'}
                  </button>}
                  {item.id === selectedId && account && <MerchantMemberActions
                    account={account} working={working} editingAccount={editingAccount}
                    editingUsername={editingUsername} newUsername={newUsername} nextRole={nextRole}
                    onNewUsername={setNewUsername} onNextRole={setNextRole}
                    onEditingAccount={setEditingAccount} onEditingUsername={setEditingUsername}
                    onAction={(action, user) => void perform(action, user)}
                  />}
                  {account?.username === feedbackAccount && error && <p role="alert">{error}</p>}
                  {account?.username === feedbackAccount && message && <p role="status">{message}</p>}
                </div></td>
              </tr>)}
          </tbody>
        </table>
      </div>}
    {selected && <div className="merchant-directory-tools">
      <h3>Gerenciar {selected.name}</h3>
      <p className="muted">A loja já está cadastrada. Use estas ações apenas para alterar seu nome ou criar mais um acesso para a mesma loja.</p>
      <div className="merchant-row-actions">
        <button type="button" className="outline" disabled={working}
          onClick={() => setActiveTool(current => current === 'rename' ? null : 'rename')}>
          {activeTool === 'rename' ? 'Cancelar alteração de nome' : 'Alterar nome da loja'}
        </button>
        <button type="button" className="outline" disabled={working}
          onClick={() => setActiveTool(current => current === 'add' ? null : 'add')}>
          {activeTool === 'add' ? 'Cancelar novo usuário' : 'Adicionar usuário à loja'}
        </button>
      </div>
      {activeTool === 'rename' && <form onSubmit={event => { event.preventDefault(); void perform('rename'); }}>
        <label>Atualizar nome da loja
          <input aria-label="Novo nome da loja" maxLength={100} value={editedName}
            onChange={event => setEditedName(event.target.value)} placeholder={selected.name} />
        </label>
        <button type="submit" disabled={working || !editedName.trim()}>Salvar nome</button>
      </form>}
      {activeTool === 'add' && <form onSubmit={event => { event.preventDefault(); void perform('add'); }}>
        <h3>Novo usuário de {selected.name}</h3>
        <p className="muted">Adiciona um usuário à loja atual, sem criar outra loja.</p>
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
          Adicionar usuário
        </button>
      </form>}
    </div>}
  </div>;
}
