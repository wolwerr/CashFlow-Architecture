import { useCallback, useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { ApiError, createTransaction, dailyBalance, transactionHistory, validMerchant,
  listMerchants, createMerchant, addMerchantAdmin, merchantPermissions } from '../../api/cashflow';
import type { Balance, TransactionPage, Merchant } from '../../api/cashflow';
import { finishLogin, getSubject, renewSession } from '../../auth/oidc';
import type { TokenSet } from '../../auth/oidc';
import { businessDate } from '../../time';
import { normalizeBrl } from '../../api/brl';
import { pollProjection } from '../../api/projection';
import type { Projection } from '../../api/projection';

export type Notice = { text: string; isError: boolean };
const activeAccessToken = (session: TokenSet | null): string | null =>
  session && Date.now() < session.expiresAt - 5000 ? session.accessToken : null;

// Centralizes the frontend orchestration. Presentation lives in feature components.
export function useCashFlowController() {
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
  const [merchants, setMerchants] = useState<Merchant[]>([]);
  const [newMerchantName, setNewMerchantName] = useState('');
  const [adminUsername, setAdminUsername] = useState('');
  const [adminPassword, setAdminPassword] = useState('');
  const [pendingProjection, setPendingProjection] = useState<Projection | null>(null);
  const [merchantUsername, setMerchantUsername] = useState('');
  const [merchantPassword, setMerchantPassword] = useState('');
  const [canManageMerchants, setCanManageMerchants] = useState(false);
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
      setNotice({ text: 'Login realizado. Selecione ou cadastre um comerciante.', isError: false });
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

  useEffect(() => {
    if (!currentToken) { setMerchants([]); setCanManageMerchants(false); return; }
    let active = true;
    merchantPermissions(currentToken).then(permission => {
      if (active) setCanManageMerchants(permission.can_manage_merchants);
    }).catch(() => {
      if (active) setCanManageMerchants(false);
    });
    listMerchants(currentToken).then(items => {
      if (active) {
        setMerchants(items);
        setMerchant(current => current || items[0]?.id || '');
      }
    }).catch(() => {
      if (active) setNotice({ text: 'Não foi possível listar comerciantes. Verifique o serviço administrativo do Keycloak.', isError: true });
    });
    return () => { active = false; };
  }, [currentToken]);

  useEffect(() => {
    if (!pendingProjection || !currentToken) return;
    let active = true;
    const projection = pendingProjection;
    const token = currentToken;
    const read = () => dailyBalance(token, projection.merchantId, projection.referenceDate);
    const delay = (ms: number) => new Promise<void>(resolve => { window.setTimeout(resolve, ms); });
    void pollProjection(projection, read, setBalance, () => active, delay).then(updated => {
      if (!active) return;
      setPendingProjection(null);
      setNotice(updated
        ? { text: 'Lançamento consolidado. Saldo atualizado.', isError: false }
        : { text: 'Lançamento registrado, mas o saldo ainda está pendente. Consulte novamente e verifique o Consolidation se persistir.', isError: false });
    });
    return () => { active = false; };
  }, [pendingProjection, currentToken]);

  async function registerMerchant(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!currentToken || !canManageMerchants || !newMerchantName.trim() ||
        !merchantUsername.trim() || merchantPassword.length < 12) return;
    setBusy(true);
    try {
      const created = await createMerchant(currentToken, newMerchantName.trim(),
        merchantUsername.trim(), merchantPassword);
      setMerchants(previous => [...previous, created]);
      changeMerchant(created.id);
      setNewMerchantName('');
      setMerchantUsername('');
      setMerchantPassword('');
      setNotice({ text: 'Comerciante e acesso do operador cadastrados. A conta da loja pode entrar no CashFlow.', isError: false });
    } catch (error) {
      setNotice({ text: (error as Error).message, isError: true });
    } finally { setBusy(false); }
  }

  async function registerAdmin(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!currentToken || !validMerchant(merchant) || !adminUsername.trim()) return;
    setBusy(true);
    try {
      await addMerchantAdmin(currentToken, merchant, adminUsername.trim(), adminPassword || undefined);
      setAdminUsername('');
      setAdminPassword('');
      setNotice({ text: 'Administrador vinculado ao comerciante.', isError: false });
    } catch (error) {
      setNotice({ text: (error as Error).message, isError: true });
    } finally { setBusy(false); }
  }

  const clearFinancialData = useCallback(() => {
    setBalance(null);
    setHistory(null);
    setPage(1);
    pendingKey.current = null;
    setNotice(null);
    setPendingProjection(null);
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

  async function submitTransaction(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!currentToken) { setNotice({ text: 'Sessão expirada. Faça login novamente.', isError: true }); return; }
    const normalized = normalizeBrl(amount);
    if (!validMerchant(merchant) || !normalized) {
      setNotice({ text: 'Comerciante ou valor inválido. Use 100, 100,00 ou 100.00.', isError: true }); return;
    }
    setBusy(true);
    setNotice(null);
    // Keep the same idempotency key if the network response is uncertain.
    pendingKey.current ??= crypto.randomUUID();
    try {
      const created = await createTransaction(currentToken, {
        merchant_id: merchant, reference_date: date, type, amount: normalized
      }, pendingKey.current);
      pendingKey.current = null;
      setAmount('');
      setNotice({ text: 'Lançamento ' + created.id + ' registrado. Aguardando atualização do saldo.', isError: false });
      setPendingProjection({
        merchantId: merchant, referenceDate: date, type, amount: normalized, before: balance
      });
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

  return {
    session, identity, authPending, merchant, date, type, amount,
    balance, history, page, notice, busy, merchants, newMerchantName,
    adminUsername, adminPassword, pendingProjection, merchantUsername,
    merchantPassword, canManageMerchants, currentToken,
    setNewMerchantName, setAdminUsername, setAdminPassword,
    setMerchantUsername, setMerchantPassword, setDate, setType,
    changeMerchant, changeInput, clearFinancialData,
    registerMerchant, registerAdmin, showBalance, submitTransaction
  };
}

export type CashFlowController = ReturnType<typeof useCashFlowController>;
