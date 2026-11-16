import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, createTransaction, dailyBalance, transactionHistory, validBrl, validMerchant, listMerchants, createMerchant, addMerchantAdmin } from './cashflow';

afterEach(() => vi.unstubAllGlobals());
describe('CashFlow API contracts', () => {
  it.each(['10.25', '0.01', '100000.00'])('accepts positive BRL %s', value => {
    expect(validBrl(value)).toBe(true);
  });
  it.each(['0.00', '1', '10,25', '-1.00', '10.251', 'abc', 'Infinity', '1.0'])('rejects BRL %s', value => {
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
  it('creates a merchant by name and returns the generated ID', async () => {
    const created = { id: 'merchant-id', name: 'Loja Campinas', role: 'ADMIN' };
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => created });
    vi.stubGlobal('fetch', fetchMock);
    expect(await createMerchant('access', 'Loja Campinas')).toEqual(created);
    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({ name: 'Loja Campinas' });
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
