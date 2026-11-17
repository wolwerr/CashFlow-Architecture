# Trilha 05 | Integração ponta a ponta do MVP local

**Estado:** contratos implementados e testes de integração automatizados disponíveis. A execução E2E real, com dois usuários Keycloak e Docker Compose local, **ainda exige evidências no ambiente do operador**. Nenhum recurso Azure é criado.

## Escopo e componentes

O fluxo de ponta a ponta preserva o desenho acordado: navegador React com Authorization Code + PKCE, NGINX, APIs .NET 10, Keycloak como fonte dos vínculos, PostgreSQL Transactions/Consolidation e Outbox → RabbitMQ → consumer. O POST retorna 201 após commit de Transaction + Audit + Outbox + IdempotencyRecord, sem aguardar o saldo. O consumer confirma o evento após commit e deve tolerar reentrega. O histórico lê Transactions; o saldo lê Consolidation.

Esta trilha **não** adiciona serviços, bancos de autorização, deploy cloud ou estorno.

## 1. Preparação do ambiente descartável

- Usar somente Docker Compose **local** e dados fictícios. Não executar em ambientes com transações reais.
- Antes do primeiro uso, seguir [bootstrap do Keycloak](merchant-onboarding.md) para configurar a conta de plataforma, o cliente de gestão e o cliente de autorização. Um realm existente precisa de ajuste manual, pois importação não o sobrescreve.
- Verificar \`.env\` local com \`KEYCLOAK_AUTHZ_CLIENT_SECRET\`, \`KEYCLOAK_MGMT_CLIENT_SECRET\` e credenciais locais. **Nunca colar tokens ou segredos em issues/PRs**.
- Rodar na raiz do projeto:

\`\`\`bash
docker compose up --build -d
docker compose ps
docker compose logs --tail=100 transactions consolidation rabbitmq
\`\`\`

Na SPA \`http://localhost:8080\`, entrar com \`cashflow.admin\`. Criar **duas lojas descartáveis**, cada uma com credenciais diferentes de usuário \`OPERATOR\`. Confirmar a listagem e as permissões de cada loja, depois entrar em cada conta para verificar sua própria visualização e o bloqueio da administração.

Não executar \`docker compose down -v\`: isso destruiria bancos e configurações persistidas.

## 2. Verificação automatizada pelo NGINX

O teste \`.github/scripts/local_e2e_mvp.py\` é **opt-in** e usa apenas \`http://127.0.0.1:8080\`. Não lê senhas, não acessa o admin do Keycloak, não automatiza login e não guarda tokens. Obter os access tokens das duas sessões locais de teste por um método seguro e exportá-los no terminal; evite registrá-los em histórico de shell.

\`\`\`bash
export CASHFLOW_E2E_TOKEN='<token_da_loja_1>'
export CASHFLOW_E2E_OTHER_TOKEN='<token_da_loja_2>'
python3 .github/scripts/local_e2e_mvp.py \
  --merchant-id '<uuid_loja_1>' --other-merchant-id '<uuid_loja_2>' \
  --date '2026-11-16'
\`\`\`

Sem \`--execute\`, o comando é **preview e não efetua requisições**. Para executar somente sobre as duas lojas descartáveis:

\`\`\`bash
python3 .github/scripts/local_e2e_mvp.py \
  --merchant-id '<uuid_loja_1>' --other-merchant-id '<uuid_loja_2>' \
  --date '2026-11-16' --execute
\`\`\`

Escolher uma data passada ou atual em \`America/Sao_Paulo\` e deixar a loja 1 sem outros lançamentos concorrentes durante o teste. O teste observa o saldo anterior, cria um crédito de R$ 100 e um débito de R$ 25 com **chaves distintas**, repete o crédito com a mesma chave, tenta reutilizar a chave com payload diferente, confere os IDs no histórico e aguarda a projeção por até 30 segundos.

| Cenário | Aceite |
| --- | --- |
| Sem token | HTTP 401 em endpoint protegido |
| Dois usuários e lojas diferentes | Cada token visualiza sua própria loja |
| Token loja 2 acessa dados da loja 1 | HTTP 403 em histórico, saldo e POST |
| CREDIT | HTTP 201 e ID persistido |
| Replay da mesma chave + mesmo payload | HTTP 201 e **mesmo ID** |
| Mesma chave + payload diferente | HTTP 409 |
| DEBIT | HTTP 201 e ID distinto |
| Histórico | Dois IDs únicos, com tipos e valores corretos |
| RabbitMQ/Consolidation | Diferença de +100 em créditos, +25 em débitos e **+75 no saldo líquido e acumulado** em relação ao baseline |

Os checks usam diferenças em relação ao saldo anterior, não pressupõem saldo inicial zero. **Não há POST de compensação nem repetição automática de lançamentos quando o saldo atrasa.** Se houver falha de rede após uma gravação, inspecionar o histórico antes de qualquer nova tentativa.

O teste é intencionalmente conservador: caso não consiga provar isolamento, autorizações ou consistência, falha fechado. Um token que pertença às duas lojas não serve como prova de isolamento. Não imprimir response bodies, IDs ou tokens no relatório final.

## 3. Testes sem infraestrutura na CI

\`\`\`bash
python3 -m unittest discover -s .github/scripts -p 'test_local_e2e_mvp.py'
\`\`\`

A pipeline executa esses testes offline para verificar o contrato do próprio probe, incluindo preview sem escrita, idempotência e bloqueio de vazamento entre comerciantes. Isso **não substitui** a execução real no Docker Compose.

## 4. Roteiro manual complementar

- Verificar cadastro, alteração de cargo ADMIN/OPERATOR, desativação INACTIVE, revogação 403 e reativação, com Keycloak como fonte das permissões.
- Conferir no navegador logout, renovação de token e ausência de access token em localStorage/sessionStorage.
- Consultar uma competência vazia e uma competência retroativa; o acumulado deve refletir eventos anteriores e manter zero de movimento no dia vazio.
- Confirmar que o NGINX encaminha \`Authorization\`, \`Idempotency-Key\` e \`X-Correlation-ID\`, sem expor portas internas além das locais.
- Registrar o SHA do commit, horário, plataforma, comandos e evidências sem identificadores pessoais. O relatório deve distinguir **PASS**, **FAIL** e **NOT RUN**, nunca declarar sucesso sem execução observada.

## 5. Critério de conclusão da Trilha 05

**Código e CI:** build .NET, testes unitários, integração PostgreSQL/RabbitMQ, API JWT, frontend Vitest e policy Sonar aprovados na PR.

**Evidência local:** execução E2E real aprovada com dois tokens de contas diferentes, dois comerciantes isolados e saldo projetado; roteiro administrativo e navegador revisado. Registrar pendências na [validação de auditoria e homologação](local-audit-e2e-validation.md).

**Fora desta trilha:** ensaios de 50 RPS/5 min, p95, falhas injetadas, backup/restore, SLO estatístico de projeção, hardening de provisionamento e Azure. Esses pertencem às Trilhas 06 a 09.
