import type { CashFlowController } from '../app/useCashFlowController';
import { validMerchant } from '../../api/cashflow';

const formatMoney = (value: string) => new Intl.NumberFormat('pt-BR', {
  style: 'currency', currency: 'BRL'
}).format(Number(value));

export function BalancePanel({ controller }: { controller: CashFlowController }) {
  const { canManageMerchants, merchants, merchant, balance, date, setDate, clearFinancialData,
    busy, showBalance, pendingProjection } = controller;
  return <section className="panel">
          <h2>Saldo diário</h2>
          {!canManageMerchants && <>
            <p className="muted">{merchants.find(item => item.id === merchant)?.name ?? 'Nenhum comerciante autorizado'}</p>
            <label>Data de referência
              <input aria-label="Data de referência" type="date" value={date}
                onChange={event => { setDate(event.target.value); clearFinancialData(); }} />
            </label>
            <button type="button" disabled={busy || !validMerchant(merchant)}
              onClick={() => showBalance()}>Consultar</button>
          </>}
          {balance ? <div className="metrics">
            <div><span>Créditos</span><strong>{formatMoney(balance.credit_total)}</strong></div>
            <div><span>Débitos</span><strong>{formatMoney(balance.debit_total)}</strong></div>
            <div><span>Líquido do dia</span><strong>{formatMoney(balance.net_amount)}</strong></div>
            <div className="total"><span>Saldo acumulado</span><strong>{formatMoney(balance.closing_balance)}</strong></div>
          </div> : <p className="muted">Consulte um comerciante e uma data para visualizar o saldo.</p>}
          {pendingProjection && <p className="muted" role="status">
            Aguardando a consolidação do último lançamento. O histórico pode atualizar antes do saldo.
          </p>}
          <p className="muted">O Consolidation é atualizado de forma assíncrona após o registro.</p>
        </section>;
}
