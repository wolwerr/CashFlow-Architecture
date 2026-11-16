import { afterEach, describe, expect, it, vi } from 'vitest';
import { beginLogin, decodeJwtPayload, expiresInSeconds, finishLogin, getSubject, getUsername, makeChallenge, makeVerifier, logout, renewSession } from './oidc';
afterEach(() => vi.unstubAllGlobals());

const jwt = (payload: object) => ['eyJhbGciOiJSUzI1NiJ9', btoa(JSON.stringify(payload)), 'sig'].join('.');
function storage() {
  const records = new Map<string, string>();
  return {
    getItem: (key: string) => records.get(key) ?? null,
    setItem: (key: string, val: string) => { records.set(key, val); },
    removeItem: (key: string) => { records.delete(key); },
    size: () => records.size
  };
}

describe('OIDC Authorization Code and PKCE', () => {
  it('requires >= 32 random bytes', () => {
    expect(() => makeVerifier(new Uint8Array(10))).toThrow();
    expect(makeVerifier(new Uint8Array(32))).toHaveLength(43);
  });
  it('derives RFC7636 S256 challenge', async () => {
    expect(await makeChallenge('dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk'))
      .toBe('E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM');
  });
  it('extracts subject and expiry', () => {
    const token = jwt({ sub: 'user-1', exp: Math.floor(Date.now() / 1000) + 240 });
    expect(getSubject(token)).toBe('user-1');
    expect(expiresInSeconds(decodeJwtPayload(token))).toBeGreaterThan(220);
    expect(expiresInSeconds({})).toBe(0);
    expect(expiresInSeconds({ exp: 0 })).toBe(0);
    expect(getSubject(jwt({ sub: 10 }))).toBe('');
    expect(() => decodeJwtPayload('bad')).toThrow();
  });
  it('uses preferred_username for display but falls back to the subject', () => {
    expect(getUsername(jwt({ sub: 'user-1', preferred_username: 'cashflow.admin' })))
      .toBe('cashflow.admin');
    expect(getUsername(jwt({ sub: 'user-1', preferred_username: '' }))).toBe('user-1');
    expect(getUsername(jwt({ sub: 'user-1' }))).toBe('user-1');
  });
  it('starts login with challenge, state and redirect and never persists tokens', async () => {
    const session = storage();
    const assign = vi.fn();
    vi.stubGlobal('sessionStorage', session);
    vi.stubGlobal('location', { origin: 'http://localhost:8080', assign });
    await beginLogin();
    expect(assign).toHaveBeenCalledOnce();
    const url = new URL(assign.mock.calls[0][0] as string);
    expect(url.searchParams.get('response_type')).toBe('code');
    expect(url.searchParams.get('code_challenge_method')).toBe('S256');
    expect(url.searchParams.get('state')).toBeTruthy();
    expect(session.size()).toBe(2);
  });
  it('exchanges callback code using transient PKCE verifier and clears storage', async () => {
    const session = storage();
    session.setItem('cashflow.pkce.state', 'expected');
    session.setItem('cashflow.pkce.verifier', 'verifier');
    vi.stubGlobal('sessionStorage', session);
    vi.stubGlobal('location', { origin: 'http://localhost:8080' });
    const token = jwt({ sub: 'user-1', exp: Math.floor(Date.now()/1000) + 260 });
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true, json: async () => ({ access_token: token, expires_in: 300 })
    });
    vi.stubGlobal('fetch', fetchMock);
    const result = await finishLogin('http://localhost:8080/auth/callback?code=auth-code&state=expected');
    expect(result.accessToken).toBe(token);
    expect(result.expiresAt).toBeGreaterThan(Date.now());
    expect(session.size()).toBe(0);
    const body = fetchMock.mock.calls[0][1].body as URLSearchParams;
    expect(body.get('code_verifier')).toBe('verifier');
    expect(body.get('grant_type')).toBe('authorization_code');
  });
  it('rejects wrong state without calling the token endpoint', async () => {
    const session = storage();
    session.setItem('cashflow.pkce.state', 'expected');
    session.setItem('cashflow.pkce.verifier', 'verifier');
    vi.stubGlobal('sessionStorage', session);
    vi.stubGlobal('fetch', vi.fn());
    await expect(finishLogin('http://localhost:8080/auth/callback?code=test&state=wrong')).rejects.toThrow();
    expect(session.size()).toBe(0);
  });
  it('fails closed on provider error, failed exchange and invalid token', async () => {
    vi.stubGlobal('sessionStorage', storage());
    await expect(finishLogin('http://localhost/auth/callback?error=access_denied')).rejects.toThrow();
    const session = storage();
    session.setItem('cashflow.pkce.state', 'yes');
    session.setItem('cashflow.pkce.verifier', 'v');
    vi.stubGlobal('sessionStorage', session);
    vi.stubGlobal('location', { origin: 'http://localhost:8080' });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false }));
    await expect(finishLogin('http://localhost/auth/callback?code=c&state=yes')).rejects.toThrow();
  });
  it('renews access token with refresh token only from memory', async () => {
    const refreshed = jwt({ sub: 'u', exp: Math.floor(Date.now()/1000) + 240 });
    const mocked = vi.fn().mockResolvedValue({
      ok: true, json: async () => ({
        access_token: refreshed, refresh_token: 'rotated-refresh', expires_in: 240
      })
    });
    vi.stubGlobal('fetch', mocked);
    const next = await renewSession({
      accessToken: 'old', refreshToken: 'old-refresh', expiresAt: Date.now() - 1
    });
    expect(next.accessToken).toBe(refreshed);
    expect(next.refreshToken).toBe('rotated-refresh');
    const params = mocked.mock.calls[0][1].body as URLSearchParams;
    expect(params.get('grant_type')).toBe('refresh_token');
    expect(params.get('refresh_token')).toBe('old-refresh');
  });
  it('fails closed when no refresh token or renewal fails', async () => {
    await expect(renewSession({ accessToken: 'a', expiresAt: 0 })).rejects.toThrow();
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false }));
    await expect(renewSession({ accessToken: 'a', refreshToken: 'r', expiresAt: 0 })).rejects.toThrow();
  });
  it('logs out without persisting credentials', () => {
    const session = storage();
    session.setItem('cashflow.pkce.state', 'pending');
    const replace = vi.fn();
    vi.stubGlobal('sessionStorage', session);
    vi.stubGlobal('location', { origin: 'http://localhost:8080', replace });
    logout();
    expect(session.size()).toBe(0);
    expect(replace).toHaveBeenCalledOnce();
  });
});
