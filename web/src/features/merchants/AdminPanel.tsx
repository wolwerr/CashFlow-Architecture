import type { CashFlowController } from '../app/useCashFlowController';
import type { Merchant } from '../../api/cashflow';
import { validMerchant } from '../../api/cashflow';
import { MerchantSelector } from './MerchantSelector';
import { MerchantDirectory } from './MerchantDirectory';

const isMerchantAdmin = (items: Merchant[], id: string) =>
  items.some(item => item.id === id && item.role === 'ADMIN');

export function AdminPanel({ controller }: { controller: CashFlowController }) {
  const { merchants, merchant, changeMerchant, newMerchantName, setNewMerchantName,
    merchantUsername, setMerchantUsername, merchantPassword, setMerchantPassword,
    registerMerchant, registerAdmin, adminUsername, setAdminUsername,
    adminPassword, setAdminPassword, busy, date, setDate, clearFinancialData,
    showBalance, currentToken } = controller;
  return <>
      <section className="panel">
        <h2>Gestão de comerciantes</h2>
        <div className="fields merchant-fields">
          <MerchantSelector items={merchants} value={merchant}
            onChange={changeMerchant} label="Selecione um comerciante"
            ariaLabel="Selecionar comerciante" />
        </div>
        <MerchantDirectory items={merchants} selectedId={merchant} token={currentToken ?? ''} onSelect={changeMerchant} />
        <form className="merchant-action" onSubmit={registerMerchant}>
          <label>Nome do novo comerciante
            <input aria-label="Nome do comerciante" maxLength={100} placeholder="Ex.: Loja Campinas"
              value={newMerchantName} onChange={event => setNewMerchantName(event.target.value)} />
          </label>
          <label>Usuário do comerciante
            <input aria-label="Usuário do comerciante" maxLength={64}
              autoComplete="off" placeholder="Ex.: loja.campinas"
              value={merchantUsername} onChange={event => setMerchantUsername(event.target.value)} />
          </label>
          <label>Senha de acesso do comerciante
            <input aria-label="Senha do comerciante" type="password" minLength={12} maxLength={128}
              autoComplete="new-password" placeholder="Mínimo de 12 caracteres"
              value={merchantPassword} onChange={event => setMerchantPassword(event.target.value)} />
          </label>
          <button type="submit" disabled={busy || !newMerchantName.trim() ||
            merchantUsername.trim().length < 3 || merchantPassword.length < 12}>
            Cadastrar comerciante e acesso
          </button>
        </form>
        <p className="muted">Apenas o administrador da plataforma pode cadastrar lojas e usuários. O login de cada loja é independente.</p>
        {isMerchantAdmin(merchants, merchant) && <form className="merchant-action" onSubmit={registerAdmin}>
          <h2>Adicionar administrador</h2>
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
      </section>
      <section className="panel">
        <h2>Comerciante e competência</h2>
        <div className="fields">
          <MerchantSelector items={merchants} value={merchant}
            onChange={changeMerchant} label="Nome do comerciante"
            ariaLabel="Comerciante da consulta" />
          <label>Data de referência
            <input aria-label="Data de referência" type="date" value={date}
              onChange={event => { setDate(event.target.value); clearFinancialData(); }} />
          </label>
          <button type="button" disabled={busy || !validMerchant(merchant)} onClick={() => showBalance()}>
            Consultar
          </button>
        </div>
        <p className="muted">O acesso ao comerciante é validado nas APIs pelo vínculo atual no Keycloak.</p>
      </section>
  </>;
}
