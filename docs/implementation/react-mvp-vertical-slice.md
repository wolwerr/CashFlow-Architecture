# Trilha vertical 03 | Interface React do CashFlow (LOCAL)

## Implementado

- SPA React/TypeScript com dashboard responsivo: login, seleção de merchant, registro CREDIT/DEBIT com valor BRL em string `10.25`, data de competência São Paulo, saldo e histórico com paginação.
- Autenticação OIDC do Keycloak `cashflow-spa`: Authorization Code + PKCE S256, `state` aleatório verificado no retorno, token access e refresh **somente em memória**, renovação via `refresh_token` em memória; após reload é necessário novo login. O único dado gravado temporariamente em `sessionStorage` é a dupla `state` e `code_verifier` necessária para o redirecionamento, removida na troca de código. **Nunca salvar token em storage.**
- SPA usa `http://localhost:8081/realms/cashflow` para login quando executada no Compose local (ou `VITE_KEYCLOAK_ISSUER` durante o build). Redirect URI `http://localhost:8080/auth/callback`. É necessário que o realm Keycloak existente inclua o client com PKCE obrigatório e ambos audiences `cashflow-transactions-api` e `cashflow-consolidation-api`.
- Todas as chamadas às APIs passam pelo NGINX na mesma origem `http://localhost:8080`; bearer JWT enviado somente às APIs. HTTP 401/403/409/422/503 são apresentados como erro seguro na interface.
- O campo `merchant_id` é **manual no MVP**, porque ainda não há API segura de descoberta de comerciantes autorizados. O usuário só poderá consultar/gravar em UUIDs ligados ao seu `sub`; o backend consulta a associação atual no Keycloak, nunca confia no ID enviado pelo formulário.
- `POST /api/v1/transactions`: `Idempotency-Key` gerado por ação, reutilizado quando houve erro ambíguo de rede/503 e regenerado quando os dados mudam. Resposta `201` reflete a gravação, **não** significa que o saldo já foi projetado.
- `GET /api/v1/transactions?merchant_id={UUID}&page=1&page_size=20`: endpoint novo de histórico, paginado e ordenado por UTC descendente, obrigatório JWT e autorização atual por comerciante, filtro SQL por `merchant_id`, `has_more` sem contagem global.
- `GET /api/v1/consolidation/daily?merchant_id={UUID}&date={YYYY-MM-DD}`: contrato da trilha 02 já existente; retorna dia + acumulado.
- Os testes automatizados cobrem PKCE, `state`, erros OIDC, renovação de token, contratos HTTP, campos monetários, competência São Paulo, dashboard e acesso negado ao histórico.

## Como executar localmente

1. Confirmar cliente `cashflow-spa` no realm Keycloak, Authorization Code e PKCE S256, URLs de redirect e audiences.
2. Configurar `KEYCLOAK_AUTHZ_CLIENT_SECRET` no `.env` local para o cliente confidencial `cashflow-authorization-service`; não colocar segredos no frontend.
3. `docker compose up --build -d` e acessar `http://localhost:8080`.
4. Fazer login, obter o UUID de um merchant previamente provisionado em `/merchants/{uuid}/ADMIN` ou `/merchants/{uuid}/OPERATOR`, informar o ID e realizar um lançamento.
5. Consultar o saldo e o histórico. O processamento RabbitMQ é assíncrono e o saldo pode demorar até o evento ser consumido.

## Pendências para homologação final

- Smoke E2E com Keycloak real, vínculo revogado, múltiplos comerciantes, login/logout/refresh, reinício de container e indisponibilidade de broker.
- Listagem amigável de merchants autorizados sem exigir UUID manual depende de endpoint próprio de descoberta autenticada e verificação de política.
- Ainda não há telas administrativas para criação de merchants ou gerência de membros.
- Os testes de carga, revogação, falhas e SLO exigem evidências na Trilha 04.
- Azure futuro, sem deployment cloud nesta trilha.
