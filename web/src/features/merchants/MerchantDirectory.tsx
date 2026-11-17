import { useEffect, useState } from 'react';
import { listMerchantMembers } from '../../api/cashflow';
import type { Merchant, MerchantMember } from '../../api/cashflow';
import { merchantRoleLabel } from './merchantPresentation';

type MerchantDirectoryProps = {
  items: Merchant[];
  selectedId: string;
  token: string;
  onSelect: (merchantId: string) => void;
};

export function MerchantDirectory({ items, selectedId, token, onSelect }: MerchantDirectoryProps) {
  const [members, setMembers] = useState<Record<string, MerchantMember[]>>({});
  const [failed, setFailed] = useState(false);
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
      setMembers(Object.fromEntries(results.filter(result => result.accounts !== null)
        .map(result => [result.id, result.accounts as MerchantMember[]])));
    });
    return () => { active = false; };
  }, [merchantIds, token]);

  return <div className="merchant-directory">
    <h3>Comerciantes cadastrados</h3>
    <p className="muted">Cargo dos usuários vinculados à loja, não o papel da sua sessão administrativa.</p>
    {failed && <p className="muted" role="status">Não foi possível consultar todos os usuários vinculados.</p>}
    {items.length === 0
      ? <p className="muted">Nenhum comerciante associado à sua conta.</p>
      : <div className="table-wrap">
        <table>
          <thead><tr><th>Comerciante</th><th>Usuário</th><th>Cargo do usuário</th><th>Ação</th></tr></thead>
          <tbody>
            {items.map(item => {
              const accounts = members[item.id];
              return <tr key={item.id}>
                <td>{item.name}</td>
                <td>{accounts === undefined ? 'Carregando usuários...' :
                  accounts.length === 0 ? 'Nenhum usuário vinculado' :
                    <div className="merchant-accounts">
                      {accounts.map(account => <span key={account.username + account.role}>{account.username}</span>)}
                    </div>}</td>
                <td>{accounts === undefined ? '...' :
                  <div className="merchant-accounts">
                    {accounts.map(account => <span key={account.username + account.role}>
                      {merchantRoleLabel(account.role)}
                    </span>)}
                  </div>}</td>
                <td><button type="button" className="outline"
                  disabled={item.id === selectedId}
                  onClick={() => onSelect(item.id)}>
                  {item.id === selectedId ? 'Selecionado' : 'Selecionar'}
                </button></td>
              </tr>;
            })}
          </tbody>
        </table>
      </div>}
  </div>;
}
