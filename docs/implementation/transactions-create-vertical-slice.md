# Trilha vertical 01 | Registrar lançamento financeiro (LOCAL)

Esta trilha reúne autenticação, autorização por comerciante, HTTP, regras de negócio, banco PostgreSQL e auditoria/Outbox no mesmo Pull Request, conforme ADR-006.

## Entrega técnica

- `POST /api/v1/transactions`, `Bearer` JWT Keycloak (iss/aud/JWKS/RS256 e exp); actor `Guid` extraído **exclusivamente de `sub`** do token, nunca do body.
- Request `{ "merchant_id": "UUID", "type": "CREDIT", "amount": "10.25", "reference_date": "2020-01-01" }`, com `Idempotency-Key` obrigatório (1 a 128 caracteres) e `X-Correlation-ID` opcional (se ausente, gerado pelo servidor).
- Autorização antes da gravação: `ICreateTransactionAuthorizationGateway` implementado por `KeycloakMerchantAuthorizationGateway`, que consulta **diretamente a fonte atual** via Admin API `GET /admin/realms/cashflow/users/{sub}/groups`, usando cliente confidencial `client_credentials`. Checa exclusivamente caminhos exatos `/merchants/{merchant_uuid}/ADMIN` e `/merchants/{merchant_uuid}/OPERATOR`. Pagina grupos e rejeita caso não seja possível examinar tudo.
- Token **do serviço** é cacheado até expirar; vínculos/permissões **não são cacheados**. Se não há vínculo, acesso negado; se Keycloak falha, acesso bloqueado. Não interpretar claims globais como autorização por merchant.
- `CreateTransactionUseCase` valida autorização e regras de domínio; `TransactionRegistrationGateway` mantém um commit para Transaction, Audit, Outbox e IdempotencyRecord.
- Em sucesso: `201 Created`, `Location: /api/v1/transactions/{id}`, `X-Correlation-ID` e corpo com BRL `amount` como string decimal e `created_at` UTC. A resposta **não** aguarda RabbitMQ nem Consolidation.
- Erros previstos: `400` formato/cabeçalhos, `401` token inválido, `403` sem vínculo, `409` chave já utilizada com payload diferente, `422` regra de negócio/valor, `503` Keycloak ou banco indisponível.
- Docker Compose inicia migrations **apenas em Development**, via `Transactions__ApplyMigrationsOnStartup=true`. Fora disso a aplicação não altera schema automaticamente.

## Preparar Keycloak 26.2 no Docker Compose

**O endpoint de autorização NÃO deve ser chamado com o token do navegador nem com o administrador master.** É uma integração interna exclusiva de Transactions.

1. `docker compose up -d postgres keycloak`. No console em `http://localhost:8081`, realm `cashflow`, valide se o import inicial de `infra/local/keycloak/cashflow-realm.json` ocorreu (import não sobrescreve realms existentes). Se existente, configure manualmente o cliente descrito abaixo.
2. Em **Clients**, abra `cashflow-authorization-service`. Ele deve ser **confidential** (Client Authentication ON), Service Accounts ON, Standard Flow OFF e Direct Access Grants OFF.
3. Em **Service account roles**, atribua apenas a role `view-users` do client `realm-management` (incluindo leitura dos grupos do usuário). Não conceder `realm-admin`, `manage-users` ou permissão de modificar grupos.
4. Copie o segredo do cliente em **Credentials**, armazene-o localmente no arquivo `.env` do projeto como `KEYCLOAK_AUTHZ_CLIENT_SECRET=<segredo>`. **Nunca comite `.env` nem coloque o segredo no React.** Caso o realm já exista, a importação pode não incluir o cliente novo; crie/configure-o via console.
5. Em **Groups**, crie grupo pai `merchants`. Para cada comerciante crie o subgrupo de nome `{merchant_uuid}`; dentro dele crie subgrupos `ADMIN` e `OPERATOR`. Atribua os usuários aos grupos específicos. Um usuário pode pertencer a merchants diferentes com papéis diferentes. **Somente o administrador local provisiona o primeiro ADMIN.**
6. Gere um usuário de teste com cadastro controlado; o UUID do Keycloak será usado como `sub`. O fluxo de login React/PKCE ainda requer integração UI; para a trilha da API, obtenha **um access token válido via OIDC PKCE** (não usar password grant). O token precisa ter audiência `cashflow-transactions-api`.
7. Inicie `docker compose up -d --build transactions web`; confirme `/health` e verifique logs. O registro deverá retornar `403` caso o usuário não pertença ao grupo do merchant indicado.

**Atenção:** para desenvolvimento local, o serviço Transactions exige uma credencial de serviço configurada e não inicia com segredo vazio. Se o token de serviço não conseguir ler grupos (`401/403` do Keycloak), a operação financeira retorna `503`, nunca libera acesso.

## Testes

- CI: testes JWT de API, testes da API POST com gateways controlados, testes do Gateway Keycloak via respostas HTTP simuladas, e testes reais de PostgreSQL para idempotência, concorrência, restrições e rollback.
- Ainda é necessário executar smoke/E2E manual com Keycloak real via Docker Compose, incluindo criação de grupos, revogação imediatamente antes da próxima requisição e contas de teste com dois merchants.
- A implementação atual **não** inclui interface React de lançamento/login, consultas de histórico, worker RabbitMQ, Consolidation completo ou deploy Azure. Esses itens serão trilhas seguintes.
- Sujeito `sub` do Keycloak é UUID na configuração padrão; se o provedor de identidade mudar, rever o contrato de identidade do Domain, que hoje usa `Guid` para ActorId.
