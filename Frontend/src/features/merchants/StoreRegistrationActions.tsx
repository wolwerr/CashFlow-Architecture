import { useState } from 'react';
import type { CashFlowController } from '../app/useCashFlowController';
import type { Merchant } from '../../api/cashflow';
import { validMerchantUsername } from '../../api/cashflow';

const isStoreAdmin = (items: Merchant[], id: string) =>
  items.some(item => item.id === id && item.role === 'ADMIN');

export function StoreRegistrationActions({ controller }: { controller: CashFlowController }) {
  const [openForm, setOpenForm] = useState<'store' | 'admin' | null>(null);
  const { merchants, merchant, newMerchantName, setNewMerchantName,
    merchantUsername, setMerchantUsername, merchantPassword, setMerchantPassword,
    registerMerchant, registerAdmin, adminUsername, setAdminUsername,
    adminPassword, setAdminPassword, busy } = controller;
  const mayAddAdmin = isStoreAdmin(merchants, merchant);

  return <div className="merchant-directory-tools">
    <div className="merchant-row-actions">
      <button type="button" className="outline" disabled={busy}
        aria-expanded={openForm === 'store'}
        onClick={() => setOpenForm(current => current === 'store' ? null : 'store')}>
        {openForm === 'store' ? 'Cancelar cadastro de loja' : 'Cadastrar nova loja'}
      </button>
      {mayAddAdmin && <button type="button" className="outline" disabled={busy}
        aria-expanded={openForm === 'admin'}
        onClick={() => setOpenForm(current => current === 'admin' ? null : 'admin')}>
        {openForm === 'admin' ? 'Cancelar administrador' : 'Adicionar administrador'}
      </button>}
    </div>
    {openForm === 'store' && <form className="merchant-action" onSubmit={registerMerchant}>
      <h3>Cadastrar nova loja</h3>
      <label>Nome da nova loja
        <input aria-label="Nome da nova loja" maxLength={100} placeholder="Ex.: Loja Campinas"
          value={newMerchantName} onChange={event => setNewMerchantName(event.target.value)} />
      </label>
      <label>Usuário de acesso da loja
        <input aria-label="Usuário de acesso da loja" maxLength={64}
          autoComplete="off" placeholder="Ex.: loja.campinas"
          value={merchantUsername} onChange={event => setMerchantUsername(event.target.value)} />
        <small className="muted">Login sem espaços, de 3 a 64 caracteres; inicie com uma letra.</small>
      </label>
      <label>Senha inicial do usuário da loja
        <input aria-label="Senha inicial do usuário da loja" type="password" minLength={12} maxLength={128}
          autoComplete="new-password" placeholder="Mínimo de 12 caracteres"
          value={merchantPassword} onChange={event => setMerchantPassword(event.target.value)} />
      </label>
      <button type="submit" disabled={busy || !newMerchantName.trim() ||
        !validMerchantUsername(merchantUsername) || merchantPassword.length < 12 || merchantPassword.length > 128}>
        Cadastrar loja e usuário
      </button>
      <p className="muted">Apenas o administrador da plataforma pode cadastrar lojas e usuários. O login de cada loja é independente.</p>
    </form>}
    {openForm === 'admin' && mayAddAdmin && <form className="merchant-action" onSubmit={registerAdmin}>
      <h3>Adicionar administrador à loja selecionada</h3>
      <label>Usuário no Keycloak
        <input aria-label="Usuário administrador" maxLength={100} placeholder="Nome de usuário"
          value={adminUsername} onChange={event => setAdminUsername(event.target.value)} />
      </label>
      <label>Senha temporária (somente para criar novo usuário)
        <input aria-label="Senha temporária" type="password" autoComplete="new-password"
          value={adminPassword} onChange={event => setAdminPassword(event.target.value)} />
      </label>
      <button type="submit" disabled={busy || !adminUsername.trim() ||
        Boolean(adminPassword && adminPassword.length < 12)}>Adicionar ADMIN</button>
    </form>}
  </div>;
}
