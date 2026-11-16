import type { CashFlowController } from '../app/useCashFlowController';
import type { TransactionPage, TransactionRecord } from '../../api/cashflow';

const formatMoney = (value: string) => new Intl.NumberFormat('pt-BR', {
  style: 'currency', currency: 'BRL'
}).format(Number(value));
const displayDate = (date: string) => date.split('-').reverse().join('/');

function renderHistory(history: TransactionPage | null, page: number, busy: boolean,
  showBalance: (requestedPage?: number) => Promise<void>) {
  return history ? <>
          {history.items.length ? <div className="table-wrap"><table>
            <thead><tr><th>Data</th><th>Tipo</th><th>Valor</th><th>ID</th></tr></thead>
            <tbody>{history.items.map((entry: TransactionRecord) =>
              <tr key={entry.id}><td>{displayDate(entry.reference_date)}</td>
                <td>{entry.type === 'CREDIT' ? 'Crédito' : 'Débito'}</td>
                <td>{formatMoney(entry.amount)}</td><td className="short-id" title={entry.id}>{entry.id.slice(0, 8)}</td>
              </tr>)}</tbody>
          </table></div> : <p>Nenhum lançamento encontrado.</p>}
          <div className="pagination">
            <button type="button" className="outline" disabled={busy || page <= 1}
              onClick={() => showBalance(page - 1)}>Anterior</button>
            <span>Página {page}</span>
            <button type="button" className="outline" disabled={busy || !history.has_more}
              onClick={() => showBalance(page + 1)}>Próxima</button>
          </div>
        </> : <p className="muted">O histórico aparece após a consulta.</p>;
}

export function HistoryPanel({ controller }: { controller: CashFlowController }) {
  const { history, page, busy, showBalance } = controller;
  return <section className="panel">
    <h2>Histórico de lançamentos</h2>
    {renderHistory(history, page, busy, showBalance)}
  </section>;
}
