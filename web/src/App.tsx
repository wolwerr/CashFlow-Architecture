import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError, createTransaction, dailyBalance, transactionHistory, validBrl, validMerchant } from './api/cashflow';
import type { Balance, TransactionPage, TransactionRecord } from './api/cashflow';
import { beginLogin, finishLogin, getSubject, logout, renewSession } from './auth/oidc';
import type { TokenSet } from './auth/oidc';
import { businessDate } from './time';

type Notice = { text: string; isError: boolean };
const formatMoney = (value: string) => new Intl.NumberFormat('pt-BR', {
  style: 'currency', currency: 'BRL'
}).format(Number(value));
const displayDate = (date: string) => date.split('-').reverse().join('/');
const activeAccessToken = (session: TokenSet | null): string | null =>
  session && Date.now() < session.expiresAt - 5000 ? session.accessToken : null;

export default function App() {
  const [session, setSession] = useState<TokenSet | null>(null);
  const [identity, setIdentity] = useState('');
  const [authPending, setAuthPending] = useState(false);
  const [merchant, setMerchant] = useState('');
  const [date, setDate] = useState(() => businessDate());
  const [type, setType] = useState<'CREDIT' | 'DEBIT'>('CREDIT');
  const [amount, setAmount] = useState('');
  const [balance, setBalance] = useState<Balance | null>(null);
  const [history, setHistory] = useState<TransactionPage | null>(null);
  const [page, setPage] = useState(1);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [busy, setBusy] = useState(false);
  const pendingKey = useRef<string | null>(null);
  const authStarted = useRef(false);
  const currentToken = activeAccessToken(session);

  useEffect(() => {
    if (location.pathname !== '/auth/callback' || authStarted.current) return;
    authStarted.current = true;
    setAuthPending(true);
    finishLogin().then(tokens => {
      setSession(tokens);
      setIdentity(getSubject(tokens.accessToken));
      window.history.replaceState({}, '', '/');
      setNotice({ text: 'Login realizado. Informe o ID de um comerciante ao qual você tenha acesso.', isError: false });
    }).catch((error: unknown) => {
      setNotice({ text: (error as Error).message, isError: true });
      window.history.replaceState({}, '', '/');
    }).finally(() => setAuthPending(false));
  }, []);

  useEffect(() => {
    if (!session?.refreshToken) return;
    let active = true;
    const delay = Math.max(0, session.expiresAt - Date.now() - 30_000);
    const timer = window.setTimeout(() => {
      renewSession(session).then(tokens => {
        if (active) setSession(tokens);
      }).catch(() => {
        if (active) {
          setSession(null);
          setBalance(null);
          setHistory(null);
          setNotice({ text: 'Sessão expirada. Entre novamente.', isError: true });
        }
      });
    }, delay);
    return () => { active = false; window.clearTimeout(timer); };
  }, [session]);

  const clearFinancialData = useCallback(() => {
    setBalance(null);
    setHistory(null);
    setPage(1);
    pendingKey.current = null;
    setNotice(null);
  }, []);

  const load = useCallback(async (token: string, merchantId: string, day: string, requestedPage: number) => {
    // Both APIs validate the merchant's current Keycloak membership independently.
    const [b, h] = await Promise.all([
      dailyBalance(token, merchantId, day),
      transactionHistory(token, merchantId, requestedPage)
    ]);
    setBalance(b);
    setHistory(h);
    setPage(requestedPage);
  }, []);

  async function showBalance(requestedPage = 1) {
    if (!currentToken) { setNotice({ text: 'Entre novamente para continuar.', isError: true }); return; }
    if (!validMerchant(merchant)) { setNotice({ text: 'Informe um UUID de comerciante válido.', isError: true }); return; }
    setBusy(true);
    setNotice(null);
    try {
      await load(currentToken, merchant, date, requestedPage);
    } catch (error) {
      setBalance(null);
      setHistory(null);
      setNotice({ text: (error as Error).message, isError: true });
    } finally { setBusy(false); }
  }

  async function submitTransaction(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!currentToken) { setNotice({ text: 'Sessão expirada. Faça login novamente.', isError: true }); return; }
    if (!validMerchant(merchant) || !validBrl(amount)) {
      setNotice({ text: 'Comerciante ou valor inválido. Use valor como 10.25.', isError: true }); return;
    }
    setBusy(true);
    setNotice(null);
    // Keep the same idempotency key if the network response is uncertain.
    pendingKey.current ??= crypto.randomUUID();
    try {
      const created = await createTransaction(currentToken, {
        merchant_id: merchant, reference_date: date, type, amount
      }, pendingKey.current);
      pendingKey.current = null;
      setAmount('');
      setNotice({ text: 'Lançamento ' + created.id + ' registrado. A consolidação é assíncrona.', isError: false });
      await load(currentToken, merchant, date, 1).catch(() => {
        setBalance(null);
        setHistory(null);
        setNotice({ text: 'Lançamento registrado; saldo ainda não disponível. Atualize a consulta.', isError: false });
      });
    } catch (error) {
      const apiError = error as ApiError;
      if (apiError.status && apiError.status !== 503) pendingKey.current = null;
      setNotice({ text: apiError.message || 'Falha ao registrar.', isError: true });
    } finally { setBusy(false); }
  }

  function changeMerchant(value: string) {
    setMerchant(value.trim());
    clearFinancialData();
  }

  function changeInput(value: string) {
    setAmount(value);
    pendingKey.current = null;
  }

  return <main className="app">
    <header className="topbar">
      <div><div className="eyebrow">GESTÃO FINANCEIRA</div><h1>CashFlow</h1></div>
      <div className="auth-controls">
        {currentToken ? <>
          <span className="user" title={identity}>Conectado</span>
          <button type="button" className="outline" onClick={logout}>Sair</button>
        </> : <button type="button" onClick={() => void beginLogin()} disabled={authPending}>
          {authPending ? 'Autenticando...' : 'Entrar com Keycloak'}
        </button>}
      </div>
    </header>
    <p className="intro">Lançamentos, histórico e saldos diários, com autorização por comerciante.</p>
    {notice && <div role="status" className={notice.isError ? 'notice error' : 'notice'}>{notice.text}</div>}
    {!currentToken ? <section className="panel welcome">
      <h2>Acesse sua conta</h2>
      <p>Use o login seguro do Keycloak para consultar e registrar movimentações financeiras.</p>
      <p className="muted">Os tokens são mantidos somente na memória do navegador. Se recarregar, entre novamente.</p>
    </section> : <>
      <section className="panel">
        <h2>Comerciante e competência</h2>
        <div className="fields">
          <label>ID do comerciante
            <input aria-label="ID do comerciante" placeholder="UUID do comerciante autorizado"
              value={merchant} onChange={event => changeMerchant(event.target.value)} />
          </label>
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
      <div className="columns">
        <section className="panel">
          <h2>Novo lançamento</h2>
          <form onSubmit={submitTransaction}>
            <label>Tipo
              <select value={type} onChange={event => { setType(event.target.value as 'CREDIT' | 'DEBIT'); pendingKey.current = null; }}>
                <option value="CREDIT">Crédito</option>
                <option value="DEBIT">Débito</option>
              </select>
            </label>
            <label>Valor (BRL)
              <input aria-label="Valor (BRL)" inputMode="decimal" placeholder="10.25"
                value={amount} onChange={event => changeInput(event.target.value)} />
            </label>
            <button disabled={busy || !validMerchant(merchant) || !validBrl(amount)} type="submit">
              {busy ? 'Processando...' : 'Registrar lançamento'}
            </button>
          </form>
          <p className="muted">Use ponto decimal e duas casas, por exemplo, 125.50.</p>
        </section>
        <section className="panel">
          <h2>Saldo diário</h2>
          {balance ? <div className="metrics">
            <div><span>Créditos</span><strong>{formatMoney(balance.credit_total)}</strong></div>
            <div><span>Débitos</span><strong>{formatMoney(balance.debit_total)}</strong></div>
            <div><span>Líquido do dia</span><strong>{formatMoney(balance.net_amount)}</strong></div>
            <div className="total"><span>Saldo acumulado</span><strong>{formatMoney(balance.closing_balance)}</strong></div>
          </div> : <p className="muted">Consulte um comerciante e uma data para visualizar o saldo.</p>}
          <p className="muted">O Consolidation é atualizado de forma assíncrona após o registro.</p>
        </section>
      </div>
      <section className="panel">
        <h2>Histórico de lançamentos</h2>
        {history ? <>
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
        </> : <p className="muted">O histórico aparece após a consulta.</p>}
      </section>
    </>}
    <footer>Ambiente local · Keycloak · PostgreSQL · RabbitMQ</footer>
  </main>;
}
