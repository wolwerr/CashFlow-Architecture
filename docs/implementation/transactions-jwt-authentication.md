# CashFlow Transactions | Autenticação JWT Keycloak (etapa 1)

## Implementado

- A API Transactions usa `JwtBearer` com verificação de **assinatura RS256** via OIDC/JWKS, emissor `iss` exato, audiência `aud` `cashflow-transactions-api`, expiração e janela de relógio de 30 s.
- Configuração exigida no startup: `Authentication:Authority`, `Authentication:Audience`; `Authentication:MetadataAddress` opcional para backchannel interno. No modo não Development, os endpoints OIDC/issuer devem usar HTTPS. Configuração ausente ou inválida faz o processo falhar fechado.
- `AddAuthorization` define **FallbackPolicy autenticada**; a rota de health `GET /health` é a única isenção explícita.
- `GET /internal/authentication/check` exige bearer JWT válido e retorna `204 No Content` após validação. É **apenas diagnóstico**, não lê nem escreve dados financeiros, não aceita `merchant_id` e não representa autorização de negócio.
- Realm local `infra/local/keycloak/cashflow-realm.json` importado com `--import-realm` pelo Compose: client público `cashflow-spa`, Authorization Code + PKCE S256, sem implicit/direct grants; scope de audiência para `cashflow-transactions-api`.
- `compose.yaml`: Keycloak anuncia issuer `http://localhost:8081/realms/cashflow`, enquanto a API acessa metadados por `http://keycloak:8080/realms/cashflow/.well-known/openid-configuration`; `KC_HOSTNAME_BACKCHANNEL_DYNAMIC=true` habilita os endpoints internos conforme o host de requisição, preservando o `iss` público fixo.
- Testes de API usam chaves RSA próprias de teste e verificam sucesso com token válido e `401` para ausência de token, assinatura inválida, emissor/audiência divergentes, expiração e token sem assinatura. Testes de configuração cobrem fail-closed para HTTP em produção, issuer/metadata inválidos e audience ausente.

## Como testar localmente

```powershell
docker compose up -d postgres keycloak
docker compose up -d --build transactions
curl.exe -i http://localhost:8080/api/v1/transactions
```

A rota financeira de criação **ainda não está disponível**. Para verificar autenticação, teste a rota do container a partir da rede Compose (a API não possui porta publicada diretamente no host):

```powershell
docker run --rm --network cashflow-local_default curlimages/curl:8.12.1 -i http://transactions:8080/internal/authentication/check
```

Sem token a resposta esperada é `401`. `GET /health` permanece público. O login manual do cliente público pode ser feito via `http://localhost:8081/realms/cashflow` e um navegador; o fluxo PKCE do frontend **não está implementado** nesta etapa.

**Importante:** Keycloak só importa o JSON automaticamente se o realm ainda não existir. Se o volume PostgreSQL já contém um realm `cashflow` configurado manualmente, a importação de startup não o sobrescreve. Revise o realm no console, sem remover volumes com dados pessoais/desenvolvimento. Testes de integração reais do Keycloak na rede Compose ainda são pendentes.

## Autenticação não é autorização por comerciante

**Decisão revisada em 16/11/2026 no [ADR-006](../architecture/adr/ADR-006-identity-and-access.md):** o Keycloak é a fonte de vínculos e papéis ADMIN/OPERATOR por comerciante via Groups. Não haverá banco lógico próprio nem módulo Authorization. A verificação por operação exige consulta atual do vínculo, inicialmente sem cache positivo, com falha fechada. O desenho da integração, provisionamento e testes de revogação ainda precisam ser implementados.

**Não foi implementado nenhum Gateway permissivo**. `ICreateTransactionAuthorizationGateway` continuará sem adaptador de produção até termos o adaptador Keycloak e seus testes. Nenhum endpoint financeiro (POST/GET de lançamentos) foi habilitado. JWT validado nunca substitui verificação de vínculo por `merchant_id`.

## Limitações e segurança

- Tokens usados nos testes são assinados localmente por chaves efêmeras, **não** são tokens reais emitidos pelo Keycloak; o end-to-end Keycloak + JWT/JWKS deve ser comprovado na próxima etapa.
- `http://` e `start-dev` do Compose são estritamente **LOCAL**. Sem Azure e sem configurações de produção nesta entrega.
- Antes de expor endpoints financeiros: implementar gateway de autorização real, provisionamento de comerciantes e vínculos, endpoints protegidos e mapeamento de erros 401/403/409/422 para ProblemDetails, com testes reais de isolamento.
