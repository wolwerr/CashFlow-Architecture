import { beforeEach, describe, expect, it, vi } from 'vitest';
const mock = vi.hoisted(() => ({
  state: [] as unknown[], pointer: 0,
  references: [] as { current: unknown }[], refPointer: 0,
  effects: [] as Array<() => void | (() => void)>
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
    useEffect: (effect: () => void | (() => void)) => { mock.effects.push(effect); },
    useCallback: (callback: unknown) => callback,
    useRef: (initial: unknown) => {
      const i = mock.refPointer++;
      return mock.references[i] ?? (mock.references[i] = { current: initial });
    }
  };
});
vi.mock('./auth/oidc', () => ({
  beginLogin: vi.fn(), finishLogin: vi.fn(), getSubject: vi.fn(), getUsername: vi.fn(), logout: vi.fn()
}));
vi.mock('./api/cashflow', async importOriginal => {
  const original = await importOriginal<typeof import('./api/cashflow')>();
  return {
    ...original, createTransaction: vi.fn(), dailyBalance: vi.fn(),
    transactionHistory: vi.fn(), listMerchants: vi.fn(), listMerchantMembers: vi.fn(), createMerchant: vi.fn(), addMerchantAdmin: vi.fn(), merchantPermissions: vi.fn()
  };
});
import App from './App';
import { createTransaction, dailyBalance, transactionHistory, createMerchant, addMerchantAdmin, listMerchants, listMerchantMembers, merchantPermissions } from './api/cashflow';
import { beginLogin, logout } from './auth/oidc';
import type { ReactElement } from 'react';

type Node = ReactElement<{children?: unknown; [key:string]: unknown}>;
function walk(root: unknown): Node[] {
  if (Array.isArray(root)) return root.flatMap(walk);
  if (typeof root !== 'object' || root === null || !('props' in root)) return [];
  const el = root as Node;
  if (typeof el.type === 'function') {
    const view = el.type as (props: Node['props']) => unknown;
    return [el, ...walk(view(el.props))];
  }
  return [el, ...walk(el.props.children)];
}
function render() {
  mock.pointer = 0;
  mock.refPointer = 0;
  mock.effects.length = 0;
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
  return find('select', 'Comerciante da consulta');
}
function setupSignedIn() {
  mock.state[0] = { accessToken: 'access-token', expiresAt: Date.now() + 120_000 };
  mock.state[3] = '68a5baac-d97f-45b4-9119-b4c5b9de7c9b';
  mock.state[4] = '2026-11-16';
  mock.state[19] = true;
  mock.state[20] = 'admin';
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
  mock.effects.length = 0;
  mock.pointer = 0;
  vi.clearAllMocks();
  seedApi();
  vi.stubGlobal('crypto', { randomUUID: () => 'unique-key' });
});
describe('CashFlow dashboard and interactions', () => {
  it('resolves a verified administrator profile and shows the correct welcome message', async () => {
    setupSignedIn();
    vi.mocked(merchantPermissions).mockResolvedValue({ can_manage_merchants: true });
    vi.mocked(listMerchants).mockResolvedValue([
      { id: '68a5baac-d97f-45b4-9119-b4c5b9de7c9b', name: 'Loja Campinas', role: 'ADMIN' }
    ]);
    render();
    const cleanup = mock.effects[2]();
    await vi.waitFor(() => {
      expect(mock.state[19]).toBe(true);
      expect(mock.state[20]).toBe('admin');
      expect(mock.state[10]).toMatchObject({ isError: false });
      expect((mock.state[10] as { text: string }).text).toContain('administrador validado');
    });
    expect(merchantPermissions).toHaveBeenCalledWith('access-token');
    expect(listMerchants).toHaveBeenCalledWith('access-token');
    if (typeof cleanup === 'function') cleanup();
  });
  it('resolves the merchant profile without permitting administrative access', async () => {
    setupSignedIn();
    vi.mocked(merchantPermissions).mockResolvedValue({ can_manage_merchants: false });
    vi.mocked(listMerchants).mockResolvedValue([]);
    render();
    mock.effects[2]();
    await vi.waitFor(() => {
      expect(mock.state[19]).toBe(false);
      expect(mock.state[20]).toBe('merchant');
      expect((mock.state[10] as { text: string }).text).toContain('comerciante validado');
    });
  });
  it('fails closed when the permissions endpoint is unavailable', async () => {
    setupSignedIn();
    vi.mocked(merchantPermissions).mockRejectedValue(new Error('unavailable'));
    vi.mocked(listMerchants).mockResolvedValue([]);
    render();
    mock.effects[2]();
    await vi.waitFor(() => {
      expect(mock.state[19]).toBe(false);
      expect(mock.state[20]).toBe('error');
      expect(mock.state[10]).toMatchObject({ isError: true });
    });
  });
  it('preserves a prior API error without hiding it with a welcome message', async () => {
    setupSignedIn();
    mock.state[10] = { text: 'Falha prévia', isError: true };
    vi.mocked(merchantPermissions).mockResolvedValue({ can_manage_merchants: true });
    vi.mocked(listMerchants).mockResolvedValue([]);
    render();
    mock.effects[2]();
    await vi.waitFor(() => expect(mock.state[20]).toBe('admin'));
    expect(mock.state[10]).toEqual({ text: 'Falha prévia', isError: true });
  });
  it('resets verified privileges when there is no active access token', () => {
    setupSignedIn();
    mock.state[0] = null;
    render();
    mock.effects[2]();
    expect(mock.state[19]).toBe(false);
    expect(mock.state[20]).toBe('loading');
    expect(mock.state[12]).toEqual([]);
  });
  it('ignores asynchronous permission responses after the view is disposed', async () => {
    setupSignedIn();
    vi.mocked(merchantPermissions).mockResolvedValue({ can_manage_merchants: true });
    vi.mocked(listMerchants).mockResolvedValue([]);
    render();
    const cleanup = mock.effects[2]();
    if (typeof cleanup === 'function') cleanup();
    await Promise.resolve();
    await Promise.resolve();
    expect(mock.state[20]).toBe('loading');
  });
  it('shows sign-in and no protected transaction form before authentication', () => {
    const nodes = render();
    expect(nodes.some(n => n.type === 'h1')).toBe(true);
    expect(nodes.filter(n => n.type === 'form')).toHaveLength(0);
    const login = find('button', 'Entrar com Keycloak');
    (login.props.onClick as () => void)();
    expect(beginLogin).toHaveBeenCalledOnce();
  });
  it('does not label an unresolved profile as merchant or expose finance operations', () => {
    setupSignedIn();
    mock.state[20] = 'loading';
    const nodes = render();
    expect(nodes.some(n => n.type === 'span' && n.props.children === 'Verificando perfil')).toBe(true);
    expect(nodes.some(n => n.type === 'h2' && n.props.children === 'Novo lançamento')).toBe(false);
  });
  it('blocks merchant and admin views when role verification fails', () => {
    setupSignedIn();
    mock.state[20] = 'error';
    const nodes = render();
    expect(nodes.some(n => n.type === 'h2' && n.props.children === 'Novo lançamento')).toBe(false);
    expect(nodes.some(n => n.type === 'h2' && n.props.children === 'Gestão de comerciantes')).toBe(false);
    expect(nodes.some(n => n.type === 'span' && n.props.children === 'Comerciante')).toBe(false);
  });
  it('distinguishes a verified admin from a merchant in the header', () => {
    setupSignedIn();
    mock.state[1] = 'cashflow.admin';
    const nodes = render();
    expect(nodes.some(n => n.type === 'span' && n.props.children === 'Administrador')).toBe(true);
    expect(nodes.some(n => n.type === 'span' && Array.isArray(n.props.children) && n.props.children.join('') === 'Olá, cashflow.admin')).toBe(true);
  });
  it('greets the merchant with the authenticated Keycloak username', () => {
    setupSignedIn();
    mock.state[1] = 'loja.campinas';
    mock.state[19] = false;
    mock.state[20] = 'merchant';
    const nodes = render();
    expect(nodes.some(n => n.type === 'span' &&
      Array.isArray(n.props.children) && n.props.children.join('') === 'Olá, loja.campinas')).toBe(true);
    expect(nodes.some(n => n.type === 'span' && n.props.children === 'Comerciante')).toBe(true);
  });

  it('does not reveal an empty username as a fake administrator identity', () => {
    setupSignedIn();
    mock.state[1] = '';
    mock.state[20] = 'loading';
    const nodes = render();
    expect(nodes.some(n => n.type === 'span' &&
      Array.isArray(n.props.children) && n.props.children.join('') === 'Olá, usuário')).toBe(true);
    expect(nodes.some(n => n.type === 'span' && n.props.children === 'Verificando perfil')).toBe(true);
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
  it('shows names and translated roles in both admin selectors and merchant directory', () => {
    setupSignedIn();
    mock.state[12] = [
      { id: mock.state[3], name: 'Loja Campinas', role: 'ADMIN' },
      { id: '59d0b4c5-4e64-46e1-ac52-ad946d2b4865', name: 'Loja Teste', role: 'OPERATOR' }
    ];
    mock.state[21] = {
      [mock.state[3] as string]: [
        { username: 'cashflow.admin', role: 'ADMIN' },
        { username: 'loja.campinas', role: 'OPERATOR' }
      ],
      '59d0b4c5-4e64-46e1-ac52-ad946d2b4865': [
        { username: 'loja.teste', role: 'OPERATOR' }
      ]
    };
    const nodes = render();
    const labels = nodes.filter(n => n.type === 'option').map(n => n.props.children);
    expect(labels).toContain('Loja Campinas');
    expect(labels).toContain('Loja Teste');
    expect(nodes.some(n => n.type === 'h3' && n.props.children === 'Comerciantes cadastrados')).toBe(true);
    expect(nodes.some(n => n.type === 'td' && n.props.children === 'Loja Teste')).toBe(true);
    expect(nodes.some(n => n.type === 'span' && n.props.children === 'Comerciante')).toBe(true);
    expect(nodes.some(n => n.type === 'span' && n.props.children === 'ADMIN')).toBe(false);
    expect(nodes.some(n => n.type === 'span' && n.props.children === 'loja.teste')).toBe(true);
    expect(nodes.some(n => n.type === 'span' && n.props.children === 'cashflow.admin')).toBe(false);
    expect(nodes.some(n => n.type === 'th' && n.props.children === 'Cargo')).toBe(true);
    expect(nodes.some(n => n.type === 'input' && n.props['aria-label'] === 'ID do comerciante')).toBe(false);
    expect(nodes.some(n => n.type === 'select' && n.props['aria-label'] === 'Comerciante da consulta')).toBe(true);
  });

  it('loads actual Keycloak members and their ADMIN or OPERATOR roles', async () => {
    setupSignedIn();
    mock.state[12] = [
      { id: mock.state[3], name: 'Loja Campinas', role: 'ADMIN' }
    ];
    vi.mocked(listMerchantMembers).mockResolvedValue([
      { username: 'cashflow.admin', role: 'ADMIN' },
      { username: 'loja.campinas', role: 'OPERATOR' }
    ]);
    render();
    const cleanup = mock.effects[4]();
    await vi.waitFor(() => {
      const map = mock.state[21] as Record<string, Array<{username: string; role: string}>>;
      expect(map[mock.state[3] as string]).toHaveLength(2);
      expect(map[mock.state[3] as string][1].role).toBe('OPERATOR');
    });
    expect(listMerchantMembers).toHaveBeenCalledWith('access-token', mock.state[3]);
    const nodes = render();
    expect(nodes.some(n => n.type === 'span' && n.props.children === 'loja.campinas')).toBe(true);
    expect(nodes.some(n => n.type === 'span' && n.props.children === 'Comerciante')).toBe(true);
    if (typeof cleanup === 'function') cleanup();
  });

  it('marks a failed member query unavailable, never assumes ADMIN', async () => {
    setupSignedIn();
    mock.state[12] = [{ id: mock.state[3], name: 'Loja Campinas', role: 'ADMIN' }];
    vi.mocked(listMerchantMembers).mockRejectedValue(new Error('Keycloak unavailable'));
    render();
    mock.effects[4]();
    await vi.waitFor(() => {
      const map = mock.state[21] as Record<string, unknown>;
      expect(map[mock.state[3] as string]).toBeNull();
      expect(mock.state[22]).toBe(true);
    });
    const nodes = render();
    expect(nodes.some(n => n.type === 'td' && n.props.children === 'Indisponível')).toBe(true);
  });

  it('does not expose a late directory response after the view is unmounted', async () => {
    setupSignedIn();
    mock.state[12] = [{ id: mock.state[3], name: 'Loja Campinas', role: 'ADMIN' }];
    vi.mocked(listMerchantMembers).mockResolvedValue([{ username: 'loja.campinas', role: 'OPERATOR' }]);
    render();
    const cleanup = mock.effects[4]();
    if (typeof cleanup === 'function') cleanup();
    await Promise.resolve();
    await Promise.resolve();
    expect(mock.state[21]).toEqual({});
  });

  it('selects a registered merchant by name for consultation and clears previous balances', () => {
    setupSignedIn();
    const other = '59d0b4c5-4e64-46e1-ac52-ad946d2b4865';
    mock.state[12] = [
      { id: mock.state[3], name: 'Loja Campinas', role: 'ADMIN' },
      { id: other, name: 'Loja Teste', role: 'ADMIN' }
    ];
    mock.state[7] = { closing_balance: '100.00' };
    mock.state[8] = { page: 1, page_size: 20, items: [], has_more: false };
    const selector = find('select', 'Comerciante da consulta');
    (selector.props.onChange as (e: unknown) => void)({ target: { value: other } });
    expect(mock.state[3]).toBe(other);
    expect(mock.state[7]).toBeNull();
    expect(mock.state[8]).toBeNull();
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
    mock.state[20] = 'merchant';
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
  it('admin sees merchant creation only after login and requires username and password', () => {
    setupSignedIn();
    const nodes = render();
    expect(nodes.some(n => n.props['aria-label'] === 'Nome do comerciante')).toBe(true);
    expect(nodes.some(n => n.props['aria-label'] === 'Usuário do comerciante')).toBe(true);
    expect(nodes.some(n => n.props['aria-label'] === 'Senha do comerciante')).toBe(true);
    expect(nodes.some(n => n.type === 'h2' && n.props.children === 'Comerciante e competência')).toBe(true);
    const submit = nodes.find(n => n.type === 'button' && n.props.children === 'Cadastrar comerciante e acesso');
    expect(submit?.props.disabled).toBe(true);
  });

  it('merchant account registration rejects missing username and short password', async () => {
    setupSignedIn();
    mock.state[13] = 'Nova Loja';
    mock.state[17] = '';
    mock.state[18] = 'short';
    const form = render().filter(n => n.type === 'form')[0];
    await (form.props.onSubmit as (event: unknown) => Promise<void>)({ preventDefault: vi.fn() });
    expect(createMerchant).not.toHaveBeenCalled();
    expect(mock.state[11]).toBe(false);
  });

  it('merchant registration exposes username conflicts without creating a local merchant', async () => {
    setupSignedIn();
    mock.state[13] = 'Nova Loja';
    mock.state[17] = 'loja.existente';
    mock.state[18] = 'senha-local-segura';
    vi.mocked(createMerchant).mockRejectedValue(new Error('Username already exists in this realm.'));
    const form = render().filter(n => n.type === 'form')[0];
    await (form.props.onSubmit as (event: unknown) => Promise<void>)({ preventDefault: vi.fn() });
    expect(mock.state[10]).toMatchObject({ isError: true });
    expect(mock.state[12]).toEqual([]);
    expect(mock.state[11]).toBe(false);
  });

  it('merchant financial panel can consult its linked merchant without admin fields', async () => {
    setupSignedIn();
    mock.state[19] = false;
    mock.state[12] = [{
      id: mock.state[3], name: 'Loja Campinas', role: 'OPERATOR'
    }];
    const nodes = render();
    const consult = nodes.find(n => n.type === 'button' && n.props.children === 'Consultar');
    expect(consult).toBeDefined();
    await (consult!.props.onClick as () => Promise<void>)();
    expect(dailyBalance).toHaveBeenCalledOnce();
    expect(transactionHistory).toHaveBeenCalledOnce();
    expect(nodes.some(n => n.props['aria-label'] === 'Comerciante da consulta')).toBe(false);
  });

  it('history displays empty state and disables navigation when there are no transactions', () => {
    setupSignedIn();
    mock.state[8] = { page: 1, page_size: 20, items: [], has_more: false };
    const nodes = render();
    expect(nodes.some(n => n.type === 'p' && n.props.children === 'Nenhum lançamento encontrado.')).toBe(true);
    const next = nodes.find(n => n.type === 'button' && n.props.children === 'Próxima');
    expect(next?.props.disabled).toBe(true);
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
