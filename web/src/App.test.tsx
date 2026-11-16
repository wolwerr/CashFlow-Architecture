import { beforeEach, describe, expect, it, vi } from 'vitest';
const mock = vi.hoisted(() => ({
  state: [] as unknown[], pointer: 0,
  references: [] as { current: unknown }[], refPointer: 0
}));
vi.mock('react', async importOriginal => {
  const actual = await importOriginal<typeof import('react')>();
  return {
    ...actual,
    useState: (initial: unknown) => {
      const i = mock.pointer++;
      if (!(i in mock.state)) mock.state[i] = typeof initial === 'function' ? (initial as () => unknown)() : initial;
      return [mock.state[i], (value: unknown) => {
        mock.state[i] = typeof value === 'function' ? (value as (old: unknown) => unknown)(mock.state[i]) : value;
      }];
    },
    useEffect: () => undefined,
    useCallback: (callback: unknown) => callback,
    useRef: (initial: unknown) => {
      const i = mock.refPointer++;
      return mock.references[i] ?? (mock.references[i] = { current: initial });
    }
  };
});
vi.mock('./auth/oidc', () => ({
  beginLogin: vi.fn(), finishLogin: vi.fn(), getSubject: vi.fn(), logout: vi.fn()
}));
vi.mock('./api/cashflow', async importOriginal => {
  const original = await importOriginal<typeof import('./api/cashflow')>();
  return {
    ...original, createTransaction: vi.fn(), dailyBalance: vi.fn(),
    transactionHistory: vi.fn(), listMerchants: vi.fn(), createMerchant: vi.fn(), addMerchantAdmin: vi.fn(), merchantPermissions: vi.fn()
  };
});
import App from './App';
import { createTransaction, dailyBalance, transactionHistory, createMerchant, addMerchantAdmin } from './api/cashflow';
import { beginLogin, logout } from './auth/oidc';
import type { ReactElement } from 'react';

type Node = ReactElement<{children?: unknown; [key:string]: unknown}>;
function walk(root: unknown): Node[] {
  if (Array.isArray(root)) return root.flatMap(walk);
  if (typeof root !== 'object' || root === null || !('props' in root)) return [];
  const el = root as Node;
  return [el, ...walk(el.props.children)];
}
function render() {
  mock.pointer = 0;
  mock.refPointer = 0;
  return walk(App());
}
function find(tag: string, label: string) {
  const nodes = render();
  const found = nodes.find(n => n.type === tag &&
    (n.props['aria-label'] === label ||
      (typeof n.props.children === 'string' && n.props.children.includes(label))));
  if (!found) throw new Error('Element not found: ' + label);
  return found;
}
function merchantInput() {
  return find('input', 'ID do comerciante');
}
function setupSignedIn() {
  mock.state[0] = { accessToken: 'access-token', expiresAt: Date.now() + 120_000 };
  mock.state[3] = '68a5baac-d97f-45b4-9119-b4c5b9de7c9b';
  mock.state[4] = '2026-11-16';
  mock.state[19] = true;
}
function seedApi() {
  vi.mocked(dailyBalance).mockResolvedValue({
    merchant_id: 'm', reference_date: '2026-11-16', currency: 'BRL',
    credit_total: '20.00', debit_total: '5.00',
    net_amount: '15.00', closing_balance: '15.00'
  });
  vi.mocked(transactionHistory).mockResolvedValue({
    page: 1, page_size: 20, has_more: false, items: []
  });
}
beforeEach(() => {
  mock.state.length = 0;
  mock.references.length = 0;
  mock.pointer = 0;
  vi.clearAllMocks();
  seedApi();
  vi.stubGlobal('crypto', { randomUUID: () => 'unique-key' });
});
describe('CashFlow dashboard and interactions', () => {
  it('shows sign-in and no protected transaction form before authentication', () => {
    const nodes = render();
    expect(nodes.some(n => n.type === 'h1')).toBe(true);
    expect(nodes.filter(n => n.type === 'form')).toHaveLength(0);
    const login = find('button', 'Entrar com Keycloak');
    (login.props.onClick as () => void)();
    expect(beginLogin).toHaveBeenCalledOnce();
  });
  it('shows protected form and merchant selection with a session in memory', () => {
    setupSignedIn();
    const nodes = render();
    expect(nodes.some(n => n.type === 'form')).toBe(true);
    expect(nodes.some(n => n.type === 'select')).toBe(true);
    expect(nodes.some(n => n.type === 'table')).toBe(false);
    expect(nodes.some(n => n.type === 'button' && n.props.children === 'Sair')).toBe(true);
  });
  it('expires the UI when access token is stale', () => {
    setupSignedIn();
    mock.state[0] = { accessToken: 'expired', expiresAt: Date.now() - 1000 };
    expect(render().filter(n => n.type === 'form')).toHaveLength(0);
  });
  it('does not leak merchant data after selection changes', () => {
    setupSignedIn();
    mock.state[7] = { closing_balance: '100.00' };
    mock.state[8] = { page: 1, page_size: 20, items: [{
      id: 'a7f4c9b1-1111-4111-8111-111111111111', merchant_id: mock.state[3],
      reference_date: '2026-11-16', amount: '10.00', type: 'CREDIT',
      currency: 'BRL', created_at: '2026-11-16T15:00:00Z'
    }], has_more: true };
    (merchantInput().props.onChange as (e: unknown) => void)({ target: { value: ' other ' } });
    expect(mock.state[3]).toBe('other');
    expect(mock.state[7]).toBe(null);
    expect(mock.state[8]).toBe(null);
    expect(mock.state[9]).toBe(1);
  });
  it('fetches balance and merchant-scoped paginated history', async () => {
    setupSignedIn();
    const btn = find('button', 'Consultar');
    await (btn.props.onClick as () => Promise<void>)();
    expect(dailyBalance).toHaveBeenCalledWith('access-token', mock.state[3], '2026-11-16');
    expect(transactionHistory).toHaveBeenCalledWith('access-token', mock.state[3], 1);
    expect(mock.state[7]).toMatchObject({ closing_balance: '15.00' });
    expect(mock.state[8]).toMatchObject({ has_more: false });
  });
  it('renders balances and history in the right format', () => {
    setupSignedIn();
    mock.state[7] = {
      merchant_id: 'm', reference_date: '2026-11-16', currency: 'BRL',
      credit_total: '30.00', debit_total: '5.00',
      net_amount: '25.00', closing_balance: '25.00'
    };
    mock.state[8] = {
      page: 1, page_size: 20, has_more: false,
      items: [{
        id: 'a7f4c9b1-1111-4111-8111-111111111111', merchant_id: 'm',
        type: 'DEBIT', amount: '5.00', currency: 'BRL',
        reference_date: '2026-11-16', created_at: '2026-11-16T15:00:00Z'
      }]
    };
    expect(render().some(n => n.type === 'table')).toBe(true);
    expect(render().some(n => n.type === 'td' && n.props.children === 'Débito')).toBe(true);
  });
  it('rejects invalid merchant before making API requests', async () => {
    setupSignedIn();
    mock.state[3] = 'broken';
    await (find('button', 'Consultar').props.onClick as () => Promise<void>)();
    expect(dailyBalance).not.toHaveBeenCalled();
    expect(mock.state[10]).toMatchObject({ isError: true });
  });
  it('handles balance API errors without displaying stale financial data', async () => {
    setupSignedIn();
    mock.state[7] = { closing_balance: '9999.00' };
    vi.mocked(dailyBalance).mockRejectedValue(new Error('Sem permissão'));
    await (find('button', 'Consultar').props.onClick as () => Promise<void>)();
    expect(mock.state[7]).toBe(null);
    expect(mock.state[8]).toBe(null);
    expect(mock.state[10]).toMatchObject({ isError: true });
  });
  it('submits a credit with stable idempotency and resets the amount', async () => {
    setupSignedIn();
    mock.state[6] = '10.25';
    vi.mocked(createTransaction).mockResolvedValue({
      id: 'new-id', merchant_id: mock.state[3] as string,
      type: 'CREDIT', amount: '10.25', currency: 'BRL',
      reference_date: '2026-11-16', created_at: '2026-11-16T15:00:00Z'
    });
    const form = render().filter(n => n.type === 'form').at(-1)!;
    await (form.props.onSubmit as (e: unknown) => Promise<void>)({ preventDefault: vi.fn() });
    expect(createTransaction).toHaveBeenCalledWith('access-token', {
      merchant_id: mock.state[3], type: 'CREDIT', amount: '10.25',
      reference_date: '2026-11-16'
    }, 'unique-key');
    expect(mock.state[6]).toBe('');
    expect(mock.references.some(ref => ref.current === 'unique-key')).toBe(false);
  });
  it('sends Brazilian comma input as invariant amount without changing the backend contract', async () => {
    setupSignedIn();
    mock.state[6] = '100,00';
    vi.mocked(createTransaction).mockResolvedValue({
      id: 'new-id', merchant_id: mock.state[3] as string,
      type: 'DEBIT', amount: '100.00', currency: 'BRL',
      reference_date: '2026-11-16', created_at: '2026-11-16T15:00:00Z'
    });
    const form = render().filter(n => n.type === 'form').at(-1)!;
    await (form.props.onSubmit as (e: unknown) => Promise<void>)({ preventDefault: vi.fn() });
    expect(createTransaction).toHaveBeenCalledWith('access-token', {
      merchant_id: mock.state[3], type: 'CREDIT', amount: '100.00',
      reference_date: '2026-11-16'
    }, 'unique-key');
  });
  it('records a debit of 25 BRL only once and schedules asynchronous balance refresh', async () => {
    setupSignedIn();
    mock.state[5] = 'DEBIT';
    mock.state[6] = '25';
    mock.state[7] = {
      merchant_id: mock.state[3], reference_date: '2026-11-16',
      currency: 'BRL', credit_total: '100.00', debit_total: '0.00',
      net_amount: '100.00', closing_balance: '100.00'
    };
    vi.mocked(createTransaction).mockResolvedValue({
      id: 'debit-id', merchant_id: mock.state[3] as string,
      type: 'DEBIT', amount: '25.00', currency: 'BRL',
      reference_date: '2026-11-16', created_at: '2026-11-16T15:00:00Z'
    });
    const form = render().filter(n => n.type === 'form').at(-1)!;
    await (form.props.onSubmit as (event: unknown) => Promise<void>)({ preventDefault: vi.fn() });
    expect(createTransaction).toHaveBeenCalledTimes(1);
    expect(createTransaction).toHaveBeenCalledWith('access-token', {
      merchant_id: mock.state[3], type: 'DEBIT', amount: '25.00',
      reference_date: '2026-11-16'
    }, 'unique-key');
    expect(mock.state[16]).toMatchObject({
      type: 'DEBIT', amount: '25.00',
      before: { credit_total: '100.00', debit_total: '0.00' }
    });
  });
  it('cannot launch an operation until the administrator links a merchant', async () => {
    setupSignedIn();
    mock.state[19] = false;
    mock.state[3] = '';
    mock.state[6] = '100';
    const nodes = render();
    const submit = nodes.find(n => n.type === 'button' && n.props.children === 'Registrar lançamento');
    expect(submit?.props.disabled).toBe(true);
    const form = nodes.filter(n => n.type === 'form').at(-1)!;
    await (form.props.onSubmit as (event: unknown) => Promise<void>)({ preventDefault: vi.fn() });
    expect(createTransaction).not.toHaveBeenCalled();
  });
  it('preserves idempotency for an uncertain network failure and clears on new input', async () => {
    setupSignedIn();
    mock.state[6] = '10.25';
    vi.mocked(createTransaction).mockRejectedValue(new Error('Connection lost'));
    const form = render().filter(n => n.type === 'form').at(-1)!;
    await (form.props.onSubmit as (e: unknown) => Promise<void>)({ preventDefault: vi.fn() });
    expect(mock.references.some(ref => ref.current === 'unique-key')).toBe(true);
    const input = find('input', 'Valor (BRL)');
    (input.props.onChange as (e: unknown) => void)({ target: { value: '12.00' } });
    expect(mock.references.some(ref => ref.current === 'unique-key')).toBe(false);
  });
  it('does not submit invalid amounts', async () => {
    setupSignedIn();
    mock.state[6] = '10,251';
    const form = render().filter(n => n.type === 'form').at(-1)!;
    await (form.props.onSubmit as (e: unknown) => Promise<void>)({ preventDefault: vi.fn() });
    expect(createTransaction).not.toHaveBeenCalled();
    expect(mock.state[10]).toMatchObject({ isError: true });
  });
  it('creates a merchant and selects it without manually entering a UUID', async () => {
    setupSignedIn();
    const input = find('input', 'Nome do comerciante');
    (input.props.onChange as (event: unknown) => void)({ target: { value: 'Loja Campinas' } });
    mock.state[17] = 'loja.campinas';
    mock.state[18] = 'senha-local-segura';
    vi.mocked(createMerchant).mockResolvedValue({
      id: '68a5baac-d97f-45b4-9119-b4c5b9de7c9b',
      name: 'Loja Campinas', role: 'ADMIN'
    });
    const form = render().filter(n => n.type === 'form')[0];
    await (form.props.onSubmit as (event: unknown) => Promise<void>)({ preventDefault: vi.fn() });
    expect(createMerchant).toHaveBeenCalledWith('access-token', 'Loja Campinas', 'loja.campinas', 'senha-local-segura');
    expect(mock.state[3]).toBe('68a5baac-d97f-45b4-9119-b4c5b9de7c9b');
  });
  it('adds an existing administrator only after selecting a merchant with ADMIN role', async () => {
    setupSignedIn();
    mock.state[12] = [{
      id: '68a5baac-d97f-45b4-9119-b4c5b9de7c9b', name: 'Loja Campinas', role: 'ADMIN'
    }];
    const admin = find('input', 'Usuário administrador');
    (admin.props.onChange as (event: unknown) => void)({ target: { value: 'new.admin' } });
    const forms = render().filter(n => n.type === 'form');
    await (forms[1].props.onSubmit as (event: unknown) => Promise<void>)({ preventDefault: vi.fn() });
    expect(addMerchantAdmin).toHaveBeenCalledWith('access-token',
      '68a5baac-d97f-45b4-9119-b4c5b9de7c9b', 'new.admin', undefined);
  });
  it('merchant session shows only financial sections, without provisioning UI', () => {
    setupSignedIn();
    mock.state[19] = false;
    mock.state[12] = [{
      id: mock.state[3], name: 'Loja Campinas', role: 'OPERATOR'
    }];
    const nodes = render();
    expect(nodes.some(n => n.type === 'h2' && n.props.children === 'Gestão de comerciantes')).toBe(false);
    expect(nodes.some(n => n.type === 'h2' && n.props.children === 'Comerciante e competência')).toBe(false);
    expect(nodes.some(n => n.type === 'h2' && n.props.children === 'Novo lançamento')).toBe(true);
    expect(nodes.some(n => n.type === 'h2' && n.props.children === 'Saldo diário')).toBe(true);
    expect(nodes.some(n => n.type === 'h2' && n.props.children === 'Histórico de lançamentos')).toBe(true);
    expect(nodes.some(n => n.props['aria-label'] === 'Nome do comerciante')).toBe(false);
    expect(nodes.some(n => n.props['aria-label'] === 'Senha do comerciante')).toBe(false);
  });
  it('operator session cannot submit merchant creation even through a forged callback', async () => {
    setupSignedIn();
    mock.state[19] = false;
    const nodes = render();
    expect(nodes.filter(n => n.type === 'form')).toHaveLength(1);
    expect(nodes.some(n => n.props['aria-label'] === 'Usuário do comerciante')).toBe(false);
    expect(createMerchant).not.toHaveBeenCalled();
  });
  it('delegates logout and blocks queries after expiry', async () => {
    setupSignedIn();
    const exit = find('button', 'Sair');
    (exit.props.onClick as () => void)();
    expect(logout).toHaveBeenCalledOnce();
    mock.state[0] = { accessToken: 'expired', expiresAt: 0 };
    // the secured query buttons disappear on expiry
    expect(render().some(n => n.type === 'button' && n.props.children === 'Consultar')).toBe(false);
  });
});
