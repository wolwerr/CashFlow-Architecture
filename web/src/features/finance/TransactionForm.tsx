import type { CashFlowController } from '../app/useCashFlowController';
import { validBrl, validMerchant } from '../../api/cashflow';

export function TransactionForm({ controller }: { controller: CashFlowController }) {
  const { merchant, type, setType, amount, changeInput, busy, submitTransaction } = controller;
  return <section className="panel">
          <h2>Novo lançamento</h2>
          <form onSubmit={submitTransaction}>
            <label>Tipo
              <select value={type} onChange={event => { setType(event.target.value as 'CREDIT' | 'DEBIT'); changeInput(amount); }}>
                <option value="CREDIT">Crédito</option>
                <option value="DEBIT">Débito</option>
              </select>
            </label>
            <label>Valor (BRL)
              <input aria-label="Valor (BRL)" inputMode="decimal" placeholder="100,00"
                value={amount} onChange={event => changeInput(event.target.value)} />
            </label>
            <button disabled={busy || !validMerchant(merchant) || !validBrl(amount)} type="submit">
              {busy ? 'Processando...' : 'Registrar lançamento'}
            </button>
          </form>
          <p className="muted">Informe em reais: 100, 100,00 ou 1.234,56.</p>
        </section>;
}
