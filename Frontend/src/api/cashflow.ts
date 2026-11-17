export type TransactionRecord = {
  id: string; merchant_id: string; type: 'CREDIT' | 'DEBIT';
  amount: string; currency: 'BRL'; reference_date: string; created_at: string;
};
export type Balance = {
  merchant_id: string; reference_date: string; currency: 'BRL';
  credit_total: string; debit_total: string; net_amount: string; closing_balance: string;
};
export type TransactionPage = { items: TransactionRecord[]; page: number; page_size: number; has_more: boolean };
export type CreateInput = {
  merchant_id: string; type: 'CREDIT' | 'DEBIT'; amount: string; reference_date: string;
};

export class ApiError extends Error {
  constructor(public status: number, message: string) { super(message); }
}

export function validMerchant(merchant: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(merchant);
}
export { validBrl } from './brl';

export function validMerchantUsername(username: string): boolean {
  return /^[a-zA-Z][a-zA-Z0-9._-]{2,63}$/.test(username);
}

async function call<T>(path: string, token: string, options: RequestInit = {}): Promise<T> {
  const response = await fetch(path, {
    ...options,
    headers: { Authorization: 'Bearer ' + token, ...options.headers },
    cache: 'no-store'
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as { title?: string };
    const messages: Record<number, string> = {
      400: 'Dados inválidos.', 401: 'Sessão inválida ou expirada.',
      403: 'Você não tem permissão neste comerciante.',
      409: 'Este identificador já foi usado com outro lançamento.',
      422: 'O lançamento não atende às regras financeiras.',
      503: 'Serviço indisponível. Tente novamente.'
    };
    throw new ApiError(response.status, problem.title || messages[response.status] || 'Falha na comunicação com o serviço.');
  }
  return response.json() as Promise<T>;
}

export function createTransaction(token: string, input: CreateInput, idempotencyKey: string): Promise<TransactionRecord> {
  return call('/api/v1/transactions', token, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Idempotency-Key': idempotencyKey,
      'X-Correlation-ID': crypto.randomUUID()
    },
    body: JSON.stringify(input)
  });
}

export function dailyBalance(token: string, merchant: string, date: string): Promise<Balance> {
  return call('/api/v1/consolidation/daily?' +
    new URLSearchParams({ merchant_id: merchant, date }), token);
}

export function transactionHistory(token: string, merchant: string, page = 1): Promise<TransactionPage> {
  return call('/api/v1/transactions?' +
    new URLSearchParams({ merchant_id: merchant, page: String(page), page_size: '20' }), token);
}

export type Merchant = { id: string; name: string; role: 'ADMIN' | 'OPERATOR' };

export function listMerchants(token: string): Promise<Merchant[]> {
  return call('/api/v1/merchants/', token);
}

export type MerchantMember = { username: string; role: 'ADMIN' | 'OPERATOR' | 'INACTIVE' };

export function listMerchantMembers(token: string, merchantId: string): Promise<MerchantMember[]> {
  // The request target is a constant same-origin path. Never interpolate a
  // merchant identifier into a fetch URL: carry it as validated request data.
  if (!validMerchant(merchantId)) {
    return Promise.reject(new ApiError(400, 'Identificador de comerciante inválido.'));
  }
  return call('/api/v1/merchants/members', token, {
    headers: { 'X-Merchant-ID': merchantId }
  });
}

async function mutateMerchant(token: string, merchantId: string, method: 'POST' | 'PUT' | 'DELETE',
  path: '/api/v1/merchants/members' | '/api/v1/merchants/members/username' | '/api/v1/merchants/details', data: object): Promise<void> {
  if (!validMerchant(merchantId)) throw new ApiError(400, 'Identificador de comerciante inválido.');
  const response = await fetch(path, {
    method, cache: 'no-store',
    headers: {
      Authorization: 'Bearer ' + token, 'Content-Type': 'application/json',
      'X-Merchant-ID': merchantId
    },
    body: JSON.stringify(data)
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as { title?: string };
    throw new ApiError(response.status, problem.title || ({
      400: 'Dados inválidos. Confira o usuário e a senha.',
      401: 'Sessão expirada. Entre novamente.',
      403: 'Sem permissão para gerenciar usuários desta loja.',
      404: 'Loja não encontrada.',
      409: 'Esse usuário já está cadastrado no Keycloak.',
      503: 'Keycloak indisponível ou erro de provisionamento. Consulte os logs de Transactions.'
    } as Record<number, string>)[response.status] || 'Não foi possível atualizar o usuário da loja.');
  }
}

export function addMerchantOperator(token: string, merchantId: string,
  username: string, password: string): Promise<void> {
  if (!validMerchantUsername(username)) {
    return Promise.reject(new ApiError(400,
      'Usuário inválido: use de 3 a 64 caracteres, começando com uma letra, sem espaços. São permitidos letras, números, ponto, hífen e _ .'));
  }
  return mutateMerchant(token, merchantId, 'POST', '/api/v1/merchants/members',
    { username, password });
}

export function updateMerchantMember(token: string, merchantId: string,
  username: string, role: 'ADMIN' | 'OPERATOR'): Promise<void> {
  return mutateMerchant(token, merchantId, 'PUT', '/api/v1/merchants/members',
    { username, role });
}

export function renameMerchantMember(token: string, merchantId: string,
  username: string, newUsername: string): Promise<void> {
  return mutateMerchant(token, merchantId, 'PUT', '/api/v1/merchants/members/username',
    { username, newUsername });
}

export function removeMerchantMember(token: string, merchantId: string, username: string): Promise<void> {
  return mutateMerchant(token, merchantId, 'DELETE', '/api/v1/merchants/members',
    { username });
}

export function renameMerchant(token: string, merchantId: string, name: string): Promise<void> {
  return mutateMerchant(token, merchantId, 'PUT', '/api/v1/merchants/details',
    { name });
}

export function merchantPermissions(token: string): Promise<{can_manage_merchants: boolean}> {
  return call('/api/v1/merchants/permissions', token);
}

export function createMerchant(token: string, name: string, username: string, password: string): Promise<Merchant> {
  return call('/api/v1/merchants/', token, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, username, password })
  });
}

export async function addMerchantAdmin(token: string, merchantId: string,
  username: string, temporaryPassword?: string): Promise<void> {
  const response = await fetch('/api/v1/merchants/' + encodeURIComponent(merchantId) + '/admins', {
    method: 'POST', cache: 'no-store',
    headers: { Authorization: 'Bearer ' + token, 'Content-Type': 'application/json' },
    body: JSON.stringify({ username, temporaryPassword: temporaryPassword || null })
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as { title?: string };
    throw new ApiError(response.status, problem.title || 'Não foi possível cadastrar o administrador.');
  }
}
