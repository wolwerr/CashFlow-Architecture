# CashFlow | Decisões consolidadas do MVP

> **Status:** 24 decisões aprovadas pelo responsável pelo projeto em 15/11/2026  
> **Natureza:** premissas **adotadas pelo CashFlow**, não novas exigências do enunciado do desafio  
> **Escopo:** complementar RN-02/03/04/07/09 e as ADR-001 a ADR-010 sem substituir a arquitetura aprovada  
> **Evidência:** esta página é o registro de decisões; implementações e testes ainda precisam ser produzidos

## Baseline já aprovado

- **Tecnologia:** .NET 10 / C#, React SPA/TypeScript, PostgreSQL/EF Core, RabbitMQ, Keycloak/OIDC, NGINX como proxy, OpenTelemetry e GitHub Actions.
- **Serviços financeiros independentes:** Transactions e Consolidation, cada um com seu banco lógico e Clean Architecture. O registro não depende da saúde do Consolidation.
- **Consistência:** Transaction + Outbox no mesmo commit; publicação at-least-once; ProcessedEvent + DailyBalance no mesmo commit; ACK após commit, com idempotência.
- **Ordem de implementação atual:** **somente LOCAL com Docker Compose** para desenvolver e executar testes. A arquitetura Azure AKS para DEV/HOM/PROD e SHARED (ACR/DNS), assim como o orçamento, permanece **planejamento para fase futura**, sem provisionamento agora.
- **Financeiro já decidido:** BRL, `decimal`/.NET e `numeric(19,2)`/PostgreSQL; valores de entrada positivos, até duas casas; `CREDIT`/ `DEBIT` definem o efeito. Saldo inicial zero por comerciante. Exibir créditos, débitos, movimento líquido e acumulado; dia vazio apresenta movimento zero e saldo acumulado mantido. Data de competência informada pelo comerciante, instante de criação UTC. Datas passadas ou presentes permitidas, futuras rejeitadas, com ajuste dos saldos acumulados afetados.

## Decisões aprovadas em bloco

### 1. Regras financeiras

| # | Decisão | Regra aprovada |
| --- | --- | --- |
| 01 | Fuso do dia atual | **`America/Sao_Paulo`** determina a data atual do negócio, a validação de competência futura e a data inicial da interface; instantes persistidos em UTC, competência persistida como `DATE` sem conversão de fuso |
| 02 | Data inicial do formulário | Data atual em `America/Sao_Paulo`, **editável**; a API ainda valida a competência recebida |
| 03 | Limite retroativo | **Sem janela de dias** para datas anteriores, desde que o comerciante/usuário esteja autorizado; futuras continuam rejeitadas; impactos no consolidado são reprocessados |
| 04 | Estratégia de saldo acumulado | **Persistir totais diários por comerciante/data**, inclusive `credit_total`, `debit_total` e `net_amount`. **Calcular `closing_balance` na leitura**, somando movimentos até a data (saldo inicial zero); não persistir o saldo acumulado como coluna autoritativa no MVP. Eventos retroativos alteram o dia pertinente e afetam o cálculo de todas as datas seguintes, sem atualizar uma linha por dia |

### 2. Escopo do MVP

| # | Decisão | Regra aprovada |
| --- | --- | --- |
| 05 | Histórico | **RF-05 no MVP**: consulta de lançamentos de Transactions, filtrável por data/intervalo e com paginação, sempre restrita ao comerciante autorizado |
| 06 | Estorno | **RF-06 fora do MVP**; reservado para a próxima versão. Não oferecer edição/exclusão que burle histórico financeiro |
| 07 | Auditoria | **RF-07 no MVP**, nível básico para registros **confirmados**: operação, identificador financeiro, ator, comerciante, horário UTC e correlação; gravar com integridade transacional compatível com o lançamento. Tentativas rejeitadas e ações administrativas não entram no escopo básico decidido; logs de segurança podem continuar independentes |
| 08 | Multicomerciante | **Sim**, dados segregados por `merchant_id` em Transactions, Consolidation, auditoria e consultas; validar vínculo usuário/comerciante em cada operação |
| 09 | RBAC do MVP | **ADMIN e OPERATOR por comerciante**, sem papel VIEWER no MVP. Matriz detalhada de ações por papel e fluxo de administração de vínculos ainda serão especificados |

### 3. Identidade e segurança

| # | Decisão | Regra aprovada |
| --- | --- | --- |
| 10 | Persistência de autorização | **Banco lógico próprio de autorização CashFlow** para `Merchant`, `Membership` e papéis; distinto dos bancos Transactions, Consolidation e Keycloak. A propriedade da camada de acesso e sua forma de deploy devem ser documentadas antes de codificar; não introduzir implicitamente um terceiro microsserviço financeiro nem consultas cross-db |
| 11 | Verificação de vínculo | Consultar a fonte de autorização CashFlow com **cache curto e invalidação** para mudanças/revogações; **fail closed** quando a autorização não puder ser verificada de modo confiável. TTL exato, estratégia de invalidação e disponibilidade ainda precisam ser definidos/testados |
| 12 | Token no frontend | React SPA pública com OIDC Authorization Code + PKCE; **access token somente em memória**, sem `localStorage` nem persistência em `sessionStorage`. Reload/logout exigem reaquisição de sessão conforme fluxo OIDC. Não implica adoção de BFF no MVP |
| 13 | Retenção de auditoria | **365 dias como política inicial**, sujeita à revisão por obrigações legais, segurança e LGPD; expurgo, controle de acesso e evidências de retenção precisam de implementação |

### 4. API REST e eventos

| # | Decisão | Regra aprovada |
| --- | --- | --- |
| 14 | Rotas versionadas | Prefixos públicos **`/api/v1/transactions`** e **`/api/v1/consolidation`**; NGINX encaminha sem remover o prefixo. Caminhos específicos, query params e schemas serão formalizados no OpenAPI |
| 15 | Resposta de registro | **HTTP 201 Created após commit** de Transaction + Outbox, sem aguardar RabbitMQ ou atualização do Consolidation; resposta representa *registro confirmado*, não saldo atualizado. Definir `Location` e corpo no OpenAPI |
| 16 | Idempotência HTTP | **`Idempotency-Key` obrigatório em criação**. Mesmo comerciante, mesma chave e mesmo payload não criam duplicata; reutilização da chave com conteúdo diferente resulta em conflito (contrato e prazo de retenção da chave a detalhar). Persistir decisão/resultado atomicamente com o lançamento e Outbox |
| 17 | Envelope RabbitMQ | **JSON versionado** contendo pelo menos `event_id`, `event_type`, `schema_version` e `correlation_id`, além de `transaction_id`, `merchant_id`, tipo, valor BRL e `reference_date` conforme contrato futuro. `event_id` é estável em retries; payload precisa ser evolutivo e validado |

### 5. Resiliência e qualidade

| # | Decisão | Regra aprovada |
| --- | --- | --- |
| 18 | Frescor da projeção | SLO **95% dos lançamentos refletidos no consolidado em até 30 s**, sob operação saudável, medido fim a fim do commit à consulta/projeção. Resultado ainda não comprovado |
| 19 | Interpretação de perda | Para o **ensaio do MVP**, em **50 RPS por 5 min** (após aquecimento), até **5% de consultas válidas enviadas** podem terminar em erro, timeout ou sem resposta útil. Não incluir erros de validação de input no numerador. Esta é a interpretação aprovada para o projeto, não confirmação externa do enunciado |
| 20 | Latência | **p95 inferior a 500 ms** para consulta em 50 RPS durante a janela de teste; meta do MVP a comprovar, não garantia de produção |
| 21 | Eventos com falha | **Retentativas limitadas + backoff + DLQ + reprocessamento controlado**, com métricas e alertas; contagem e intervalos exatos, roteamento e replay administrativo ficam na especificação operacional. Nunca ACK de mensagem não persistida sem política explícita |

### 6. Operação e entrega

| # | Decisão | Regra aprovada |
| --- | --- | --- |
| 22 | Retenção Outbox/ProcessedEvent | **Configurável**, com expurgo seguro e reconciliação. Não definir TTL único arbitrário: expurgar só quando não comprometer replay/deduplicação, política operacional, idempotência ou auditoria |
| 23 | Deploy PROD | **Aprovação humana obrigatória via GitHub Environments**, com configurações de proteção compatíveis com a conta/plano e bloqueio efetivo de deploy sem aprovação; configuração ainda não validada |
| 24 | Backups/restore | **Backups automáticos e teste de restauração obrigatório antes da liberação em PROD**. Incluir dados de Transactions, Consolidation, autorização, Keycloak e persistências críticas; detalhes de periodicidade, RPO/RTO, dados em RabbitMQ e evidências dependem do desenho de operações |

## Consequências importantes para o modelo

### Saldo acumulado na leitura

Para o comerciante `m` na data de competência `d`:

```text
daily_net(m,d) = credit_total(m,d) - debit_total(m,d)
closing_balance(m,d) = SUM(daily_net(m,t) para todo t <= d)
opening_balance(m) = 0 no MVP
```

A consulta deve responder mesmo sem linha `DailyBalance` no dia solicitado: créditos, débitos e líquido do dia = zero; acumulado = soma de todos os dias anteriores (ou zero). O banco do Consolidation **não** consulta o banco de Transactions em tempo real. A projeção é eventualmente consistente; o histórico de lançamentos é lido pela API de Transactions, com autorização por comerciante.

**Alteração retroativa:** projetar o delta do evento na competência, usando transação idempotente. Como o acumulado é **derivado na leitura** dos totais diários, consultas posteriores passam a refletir o novo total após o processamento do evento. Não armazenar `closing_balance` como estado redundante, eliminando necessidade de atualizar em cascata cada dia; isso não elimina a obrigação de tratar concorrência, cache e consistência eventual. Índices, massa de dados, plano de execução e eventual otimização são decisões de implementação sujeitas a teste de 50 RPS.

### Limite entre autorização e disponibilidade

Uma camada de autorização com banco lógico próprio pode criar dependência para operações financeiras. Manter validação local do JWT/JWKS e projetar cache **curto e invalidável** para consultas de vínculo. Não presumir continuidade irrestrita em caso de falha do serviço/banco de autorização: a política **fail closed** é prioritária sobre disponibilidade para acessos cujo vínculo não pode ser comprovado. Detalhar responsabilidades, cache e revogação sem quebrar independência dos **dois serviços financeiros** e sem acessar fisicamente bancos de outros domínios.

### Índices e integridade a especificar

- `Transaction`: `merchant_id, reference_date` para histórico e consultas; `amount > 0`, BRL 2 casas; `created_at` UTC.
- `DailyBalance`: PK/UNIQUE `(merchant_id, reference_date)`, atualizações atômicas por evento; `net_amount` como derivação/coerência do dia; **sem coluna acumulada autoritativa**.
- `ProcessedEvent`: `UNIQUE(event_id)` e transação única com a atualização de `DailyBalance`.
- Idempotência HTTP: escopo da chave por comerciante/rota, hash do request e resultado estável, persistência transacional sem expor registros de outros comerciantes.
- `AuditEvent`: ação confirmada vinculada a transação e ator, append-only no aplicativo, retenção inicial de 365 dias.
- `Merchant`/`Membership`/`Role`: domínio de autorização com banco lógico independente; sem FK física aos bancos financeiros ou Keycloak.

## Estratégia de execução, definida após as decisões\n\n**Fase atual: LOCAL exclusivamente.** Construir e testar React, NGINX, APIs .NET, quatro bancos lógicos PostgreSQL, RabbitMQ, Keycloak e telemetria usando Docker Compose. A homologação inicial ocorrerá por testes locais com evidências registradas. **Não implantar Azure agora.** O cenário AKS DEV/HOM/PROD, custos, GitHub Environments para PROD e políticas cloud são planejamento futuro, não tarefas bloqueadoras do desenvolvimento local.\n\n## O que ainda requer especificação e implementação

Decisões aprovadas **não** significam contratos executáveis, schema físico, deploy, testes ou homologação. Ficam pendentes:

1. **Contratos OpenAPI e eventos:** paths específicos de histórico, criação/consulta, `Location`, payloads, HTTP 400/401/403/409/422, JSON Schema, `schema_version`, esquema de erro e `Idempotency-Key`/retenção.
2. **Autorização:** dono e interfaces do banco lógico próprio, matriz exata ADMIN/OPERATOR, provisionamento de comerciante, mudança de vínculo, cache TTL/invalidação, revogação, falhas e testes de isolamento.
3. **Dados:** migrations EF Core, modelos físicos, índices, constraints e consistência monetária; custo/índices para soma de totais na leitura sob 50 RPS; política de histórico e auditoria.
4. **Mensageria:** filas/exchanges, flags durable/mandatory, confirmações e retornos, retry/backoff com limites específicos, DLQ, regras de expurgo seguro, reconciliação, manutenção de `ProcessedEvent`.
5. **Qualidade:** executar teste de 50 RPS/5 min, sucesso ≥95%, p95 <500ms, 95% de atualização em 30 s; testes de falha do consolidado, rollback, duplicação, concorrência, replay e integração JWT.
6. **Infra/entrega:** GitHub Environments com aprovação real, TLS/Ingress, rede egress, AKS/HPA/PDB/quorum, PostgreSQL HA, armazenamento persistente, backups e **restore documentado antes de PROD**.
7. **Diagramas:** regenerar PNGs depois das alterações nas fontes PlantUML. Imagens antigas não demonstram a nova semântica até substituição.

**Escopo:** estorno é evolução futura. Auditoria de operações financeiras confirmadas, histórico paginado e multicomerciante entram no MVP por decisão do projeto.

## Referências e documentos impactados

- [Requisitos funcionais](../requirements/functional-requirements.md)
- [Requisitos não funcionais](../requirements/non-functional-requirements.md)
- [Modelo de dados](data-model.md)
- [Casos de uso](use-cases.md)
- [Sequências](sequence-diagrams.md)
- [Validação 01](validations/requirements-validation.md), [02](validations/sequence-validation.md) e [03](validations/data-model-validation.md)
- [ADR-003](adr/ADR-003-persistence.md), [ADR-005](adr/ADR-005-outbox-consistency-idempotency.md), [ADR-006](adr/ADR-006-identity-and-access.md), [ADR-007](adr/ADR-007-clients-api-gateway.md), [ADR-010](adr/ADR-010-ci-cd-github-actions.md)
