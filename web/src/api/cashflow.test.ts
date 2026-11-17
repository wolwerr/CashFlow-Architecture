import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, createTransaction, dailyBalance, transactionHistory, validBrl, validMerchant, listMerchants, createMerchant, addMerchantAdmin, merchantPermissions, listMerchantMembers, addMerchantOperator, updateMerchantMember, removeMerchantMember, renameMerchant } from './cashflow';

afterEach(() => vi.unstubAllGlobals());
describe('CashFlow API contracts', () => {
  it.each(['10.25', '0.01', '100000.00', '100', '100,00', '1.234,56'])('accepts positive BRL %s', value => {
    expect(validBrl(value)).toBe(true);
  });
  it.each(['0.00', '0', '-1.00', '10.251', '10,251', 'abc', 'Infinity', '1,000.00'])('rejects BRL %s', value => {
    expect(validBrl(value)).toBe(false);
  });
  it('validates UUID merchant', () => {
    expect(validMerchant('68a5baac-d97f-45b4-9119-b4c5b9de7c9b')).toBe(true);
    expect(validMerchant('not-a-merchant')).toBe(false);
  });
  it('posts the expected contract with idempotency and bearer headers', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ id: 'ok' }) });
    vi.stubGlobal('fetch', fetchMock);
    vi.stubGlobal('crypto', { randomUUID: () => 'trace-id' });
    const payload = { merchant_id: 'merchant', type: 'CREDIT' as const, amount: '10.25', reference_date: '2026-11-16' };
    expect(await createTransaction('access', payload, 'idem-1')).toEqual({ id: 'ok' });
    const [url, request] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('/api/v1/transactions');
    expect(request.method).toBe('POST');
    expect(request.headers).toMatchObject({
      Authorization: 'Bearer access', 'Idempotency-Key': 'idem-1',
      'X-Correlation-ID': 'trace-id', 'Content-Type': 'application/json'
    });
    expect(JSON.parse(request.body as string)).toEqual(payload);
  });
  it('constructs merchant-scoped URLs with auth headers', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ items: [] }) });
    vi.stubGlobal('fetch', fetchMock);
    await dailyBalance('access', 'merchant-id', '2026-11-16');
    await transactionHistory('access', 'merchant-id', 3);
    expect(fetchMock.mock.calls[0][0]).toContain('merchant_id=merchant-id&date=2026-11-16');
    expect(fetchMock.mock.calls[1][0]).toContain('page=3&page_size=20');
    expect(fetchMock.mock.calls[1][1].headers.Authorization).toBe('Bearer access');
    expect(fetchMock.mock.calls[1][1].cache).toBe('no-store');
  });
  it.each([400, 401, 403, 409, 422, 503, 500])('maps HTTP %s into typed errors', async status => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: false, status, json: async () => ({})
    }));
    await expect(dailyBalance('token', 'merchant', '2026-11-16')).rejects.toMatchObject({ status });
  });
  it('preserves a safe problem title from API', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: false, status: 403, json: async () => ({ title: 'Merchant denied' })
    }));
    await expect(transactionHistory('token', 'm')).rejects.toEqual(new ApiError(403, 'Merchant denied'));
  });
  it('lists only merchant records returned from the protected management endpoint', async () => {
    const items = [{ id: 'merchant', name: 'Loja Campinas', role: 'ADMIN' }];
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => items });
    vi.stubGlobal('fetch', fetchMock);
    await expect(listMerchants('access')).resolves.toEqual(items);
    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/merchants/');
    expect(fetchMock.mock.calls[0][1].headers.Authorization).toBe('Bearer access');
  });
  it('fetches real merchant usernames and roles with an authorized bearer token', async () => {
    const members = [
      { username: 'cashflow.admin', role: 'ADMIN' },
      { username: 'loja.teste', role: 'OPERATOR' }
    ];
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => members });
    vi.stubGlobal('fetch', fetchMock);
    expect(await listMerchantMembers('admin-token', '68a5baac-d97f-45b4-9119-b4c5b9de7c9b')).toEqual(members);
    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/merchants/members');
    expect(fetchMock.mock.calls[0][1].headers['X-Merchant-ID'])
      .toBe('68a5baac-d97f-45b4-9119-b4c5b9de7c9b');
    expect(fetchMock.mock.calls[0][1].headers.Authorization).toBe('Bearer admin-token');
    expect(fetchMock.mock.calls[0][1].cache).toBe('no-store');
  });
  it.each(['../admin', '/api/v1/users', 'not-a-uuid', '68a5baac-d97f-45b4-9119-b4c5b9de7c9b?redirect=https://example.com'])(
    'rejects malicious or invalid merchant route id %s without making any request', async id => {
      const fetchMock = vi.fn();
      vi.stubGlobal('fetch', fetchMock);
      await expect(listMerchantMembers('access', id)).rejects.toMatchObject({ status: 400 });
      expect(fetchMock).not.toHaveBeenCalled();
    });
  it('denies merchant member listings to operators', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: false, status: 403, json: async () => ({ title: 'Forbidden' })
    }));
    await expect(listMerchantMembers('operator', '68a5baac-d97f-45b4-9119-b4c5b9de7c9b'))
      .rejects.toMatchObject({ status: 403 });
  });
  it.each([
    ['add operator', 'POST', '/api/v1/merchants/members'],
    ['update role', 'PUT', '/api/v1/merchants/members'],
    ['remove access', 'DELETE', '/api/v1/merchants/members'],
    ['rename shop', 'PUT', '/api/v1/merchants/details']
  ])('uses a fixed URL for %s', async (_label, method, url) => {
    const id = '68a5baac-d97f-45b4-9119-b4c5b9de7c9b';
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 204 });
    vi.stubGlobal('fetch', fetchMock);
    if (_label === 'add operator')
      await addMerchantOperator('token', id, 'user1', 'password-long');
    if (_label === 'update role')
      await updateMerchantMember('token', id, 'user1', 'ADMIN');
    if (_label === 'remove access')
      await removeMerchantMember('token', id, 'user1');
    if (_label === 'rename shop')
      await renameMerchant('token', id, 'Loja Nova');
    const [path, req] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(path).toBe(url);
    expect(req.method).toBe(method);
    expect((req.headers as Record<string, string>)['X-Merchant-ID']).toBe(id);
    expect((req.headers as Record<string, string>).Authorization).toBe('Bearer token');
  });

  it.each(['../admin', 'not-a-uuid', '68a5baac-d97f-45b4-9119-b4c5b9de7c9b?foo=bar'])(
    'refuses dangerous merchant IDs in every mutation: %s', async invalid => {
      const fetchMock = vi.fn();
      vi.stubGlobal('fetch', fetchMock);
      await expect(addMerchantOperator('token', invalid, 'user1', 'valid-password')).rejects
        .toMatchObject({ status: 400 });
      await expect(updateMerchantMember('token', invalid, 'user1', 'ADMIN')).rejects
        .toMatchObject({ status: 400 });
      await expect(removeMerchantMember('token', invalid, 'user1')).rejects
        .toMatchObject({ status: 400 });
      await expect(renameMerchant('token', invalid, 'Loja')).rejects
        .toMatchObject({ status: 400 });
      expect(fetchMock).not.toHaveBeenCalled();
    });

  it('reports a rejected role update without pretending it succeeded', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: false, status: 403, json: async () => ({ title: 'Forbidden' })
    }));
    await expect(updateMerchantMember('operator', '68a5baac-d97f-45b4-9119-b4c5b9de7c9b',
      'user1', 'ADMIN')).rejects.toMatchObject({ status: 403 });
  });

  it('queries platform administration permission from the protected API', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true, json: async () => ({ can_manage_merchants: false })
    });
    vi.stubGlobal('fetch', fetchMock);
    expect(await merchantPermissions('operator-access')).toEqual({ can_manage_merchants: false });
    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/merchants/permissions');
    expect(fetchMock.mock.calls[0][1].headers.Authorization).toBe('Bearer operator-access');
  });
  it('creates a merchant by name and returns the generated ID', async () => {
    const created = { id: 'merchant-id', name: 'Loja Campinas', role: 'ADMIN' };
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => created });
    vi.stubGlobal('fetch', fetchMock);
    expect(await createMerchant('access', 'Loja Campinas', 'loja.campinas', 'senha-local-segura')).toEqual(created);
    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({ name: 'Loja Campinas', username: 'loja.campinas', password: 'senha-local-segura' });
    expect(fetchMock.mock.calls[0][1].method).toBe('POST');
  });
  it('adds a merchant administrator without storing its password', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 204 });
    vi.stubGlobal('fetch', fetchMock);
    await addMerchantAdmin('access', 'merchant-id', 'manager', 'temporary-password');
    const [url, options] = fetchMock.mock.calls[0];
    expect(url).toBe('/api/v1/merchants/merchant-id/admins');
    expect(options.method).toBe('POST');
    expect(options.headers.Authorization).toBe('Bearer access');
    expect(JSON.parse(options.body)).toEqual({
      username: 'manager', temporaryPassword: 'temporary-password'
    });
  });
  it('rejects an unauthorized administrator mutation', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: false, status: 403, json: async () => ({ title: 'Forbidden' })
    }));
    await expect(addMerchantAdmin('access', 'merchant-id', 'manager'))
      .rejects.toEqual(new ApiError(403, 'Forbidden'));
  });
  it('handles invalid JSON problem responses without exposing tokens', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: false, status: 503, json: async () => { throw Error('invalid'); }
    }));
    await expect(transactionHistory('token', 'm')).rejects.toThrow('Serviço indisponível');
  });
});
