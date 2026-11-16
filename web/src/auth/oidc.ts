/** Browser-only Authorization Code + PKCE. Access tokens are never persisted. */
export type TokenSet = { accessToken: string; expiresAt: number };
export const issuer = (import.meta.env.VITE_KEYCLOAK_ISSUER || 'http://localhost:8081/realms/cashflow').replace(/\/$/, '');
export const clientId = 'cashflow-spa';
const verifierKey = 'cashflow.pkce.verifier';
const stateKey = 'cashflow.pkce.state';
const redirectPath = '/auth/callback';

function base64url(bytes: Uint8Array): string {
  return btoa(Array.from(bytes, b => String.fromCharCode(b)).join(''))
    .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
}

export function makeVerifier(bytes: Uint8Array): string {
  if (bytes.length < 32) throw new Error('PKCE entropy insufficient');
  return base64url(bytes);
}

export async function makeChallenge(verifier: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier));
  return base64url(new Uint8Array(digest));
}

export function decodeJwtPayload(token: string): Record<string, unknown> {
  const middle = token.split('.')[1];
  if (!middle) throw new Error('Invalid access token');
  return JSON.parse(atob(middle.replace(/-/g, '+').replace(/_/g, '/'))) as Record<string, unknown>;
}

export function expiresInSeconds(payload: Record<string, unknown>, now = Date.now()): number {
  return typeof payload.exp === 'number' ? Math.max(0, payload.exp - Math.floor(now / 1000)) : 0;
}

export function getSubject(token: string): string {
  const sub = decodeJwtPayload(token).sub;
  return typeof sub === 'string' ? sub : '';
}

export async function beginLogin(): Promise<void> {
  const verifier = makeVerifier(crypto.getRandomValues(new Uint8Array(32)));
  const state = makeVerifier(crypto.getRandomValues(new Uint8Array(32)));
  sessionStorage.setItem(verifierKey, verifier);
  sessionStorage.setItem(stateKey, state);
  const challenge = await makeChallenge(verifier);
  const params = new URLSearchParams({
    client_id: clientId, response_type: 'code', scope: 'openid profile',
    code_challenge_method: 'S256', code_challenge: challenge,
    state, redirect_uri: location.origin + redirectPath
  });
  location.assign(issuer + '/protocol/openid-connect/auth?' + params);
}

export async function finishLogin(url = location.href): Promise<TokenSet> {
  const parsed = new URL(url);
  const code = parsed.searchParams.get('code');
  const state = parsed.searchParams.get('state');
  const expectedState = sessionStorage.getItem(stateKey);
  const verifier = sessionStorage.getItem(verifierKey);
  sessionStorage.removeItem(stateKey);
  sessionStorage.removeItem(verifierKey);
  if (parsed.searchParams.has('error')) throw new Error('Login não autorizado pelo Keycloak.');
  if (!code || !state || !expectedState || state !== expectedState || !verifier)
    throw new Error('Falha na validação de segurança do login.');
  const response = await fetch(issuer + '/protocol/openid-connect/token', {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'authorization_code', client_id: clientId, code,
      code_verifier: verifier, redirect_uri: location.origin + redirectPath
    })
  });
  if (!response.ok) throw new Error('Não foi possível concluir o login.');
  const tokens = await response.json() as { access_token?: string; expires_in?: number };
  if (!tokens.access_token || !tokens.expires_in || tokens.expires_in <= 0)
    throw new Error('Token inválido recebido.');
  const claims = decodeJwtPayload(tokens.access_token);
  const remaining = expiresInSeconds(claims);
  if (remaining <= 0) throw new Error('Token expirado recebido.');
  return { accessToken: tokens.access_token, expiresAt: Date.now() + Math.min(remaining, tokens.expires_in) * 1000 };
}

export function logout(): void {
  sessionStorage.removeItem(verifierKey);
  sessionStorage.removeItem(stateKey);
  location.replace(issuer + '/protocol/openid-connect/logout?client_id=' +
    encodeURIComponent(clientId) + '&post_logout_redirect_uri=' + encodeURIComponent(location.origin));
}
