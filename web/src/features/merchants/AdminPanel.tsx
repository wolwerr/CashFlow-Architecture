import type { CashFlowController } from '../app/useCashFlowController';
import { validMerchant } from '../../api/cashflow';
import { MerchantSelector } from './MerchantSelector';
import { MerchantDirectory } from './MerchantDirectory';
import { StoreRegistrationActions } from './StoreRegistrationActions';

export function AdminPanel({ controller }: { controller: CashFlowController }) {
  const { merchants, merchant, changeMerchant, busy, date, setDate,
    clearFinancialData, showBalance, currentToken, refreshMerchants } = controller;
  return <>
      <section className="panel">
        <h2>Gestão de lojas</h2>
        <div className="fields merchant-fields">
          <MerchantSelector items={merchants} value={merchant}
            onChange={changeMerchant} label="Selecione uma loja"
            ariaLabel="Selecionar loja" />
        </div>
        <MerchantDirectory items={merchants} selectedId={merchant} token={currentToken ?? ''} onSelect={changeMerchant} onRefresh={refreshMerchants} />
        <StoreRegistrationActions controller={controller} />
      </section>
      <section className="panel">
        <h2>Lojas e competência</h2>
        <div className="fields">
          <MerchantSelector items={merchants} value={merchant}
            onChange={changeMerchant} label="Nome da loja"
            ariaLabel="Comerciante da consulta" />
          <label>Data de referência
            <input aria-label="Data de referência" type="date" value={date}
              onChange={event => { setDate(event.target.value); clearFinancialData(); }} />
          </label>
          <button type="button" disabled={busy || !validMerchant(merchant)} onClick={() => showBalance()}>
            Consultar
          </button>
        </div>
        <p className="muted">O acesso à loja é validado nas APIs pelo vínculo atual no Keycloak.</p>
      </section>
  </>;
}
