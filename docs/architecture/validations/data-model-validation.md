# Validação 03 | Modelo de dados e integridade entre domínios

> **Status:** revisão documental concluída, regras de negócio e execução de testes pendentes  
> **Data:** 2026-11-15  
> **Escopo:** modelo lógico PlantUML e documentação, requisitos funcionais, ADR-003 (PostgreSQL/EF Core) e ADR-005 (Outbox/idempotência)  
> **Natureza:** validação **de consistência do desenho**, sem criação de migrations, banco ou teste executado

## Objetivo

Verificar se a propriedade dos dados entre Transactions, Consolidation, Keycloak e autorização CashFlow é coerente com os requisitos de crédito/débito, consolidação diária, resiliência e segurança. Identificar constraints e testes obrigatórios que precisam ser implementados, sem transformar propostas ainda abertas em decisões de domínio.

## Matriz de validação

| Entidade / grupo | Função conforme modelo | Resultado documental | Próxima evidência necessária |
| --- | --- | --- | --- |
| `Transaction` | Registro de fato financeiro, tipo CREDIT/DEBIT, valor, comerciante e data de referência | **Conforme com ressalva**: BRL, escala de duas casas e valor positivo decididos; competência e instante UTC decididos | Validar `amount` com decimal/numeric, tipo válido e data definida; dados de teste e constraints |
| `OutboxEvent` | Evento durável vinculado ao lançamento, status, payload e `correlation_id` | **Conforme no desenho** | Mesmo commit de Transaction, ID de evento estável, retries e claim concorrente |
| `AuditEvent` | Trilha de criação financeira confirmada | **Aprovado no MVP**: ator, comerciante, instante UTC, correlação e retenção inicial 365 dias | Testar integridade transacional, acesso e expurgo |
| `DailyBalance` | Totais diários por `(merchant_id, reference_date)` | **Decidido**: manter totais e líquido persistidos, **calcular acumulado na leitura**; sem coluna `closing_balance` persistida | Medir 50 RPS, dias vazios, entradas retroativas e índices |
| `ProcessedEvent` | ID de evento aplicado, chave única para idempotência | **Conforme no desenho**, restrição física pendente | `UNIQUE(event_id)`, commit atômico com a projeção, reentrega sem duplicidade |
| `Merchant`, `Membership`, `Role`, `Permission` | Contexto de autorização CashFlow | **Aprovado no MVP**: banco lógico próprio, ADMIN/OPERATOR por comerciante, cache curto com invalidação | Definir contrato/ownership, migrations, TTL e testes de revogação |
| User/Keycloak | Identidade OIDC mantida fora dos domínios financeiros | **Conforme com ajuste** | Confirmar contrato de identificador `sub`, issuer e mecanismo de verificação; não presumir UUID |
| PostgreSQL por serviço | Bancos lógicos isolados para Transactions, Consolidation e identidade | **Conforme no desenho** | Credenciais, migrations e proibição de leitura/join cross-domain |

## Ajuste de consistência aplicado

No PlantUML original o identificador `subject_id` do Keycloak/Membership era **STRING**, mas o campo `actor_id` de Transaction e AuditEvent estava como **UUID**. **Não há garantia geral de que `sub` de OIDC seja UUID**. Por isso, no modelo lógico:

- `actor_id` passa a ser **STRING como referência ao sujeito OIDC**; se optarmos por uma identidade interna UUID, o mapeamento será uma decisão explícita de implementação;
- `ProcessedEvent.event_id` permanece chave lógica única e agora está anotado como **PK/UNIQUE** no PlantUML;
- não surgiram chaves estrangeiras entre domínios.

Alterações feitas em [PlantUML do modelo](../plantuml/data-model.puml) e [documentação principal](../data-model.md). O PNG ainda precisa ser regenerado para mostrar os novos rótulos.

## Integridade e concorrência

### Transactions

A [ADR-005](../adr/ADR-005-outbox-consistency-idempotency.md) exige commit **atômico** de `Transaction` e `OutboxEvent`. A existência de uma associação 1:N no modelo lógico **não prova** que serão gravados na mesma transação. Testar erro após persistência parcial, rollback e nenhuma resposta de sucesso antes do commit.

Um índice/estratégia para buscar eventos pendentes, o estado `status` e a reivindicação concorrente por múltiplos workers devem ser definidos no modelo físico. Publisher confirm perdido permite republicação, então o identificador do evento deve permanecer estável.

### Consolidation

Para `ProcessedEvent`, a chave lógica é `event_id`. A projeção `DailyBalance` utiliza como **chave candidata proposta** a combinação `merchant_id, reference_date`. A restrição única deve corresponder à semântica aprovada.

O consumer precisa:
1. iniciar transação local;
2. inserir/deduplicar `event_id`;
3. atualizar o saldo com controle de concorrência (UPSERT atômico, isolamento ou lock adequado), quando evento novo;
4. fazer commit de saldo + evento processado;
5. enviar ACK somente depois do commit seguro.

**`UNIQUE(event_id)` impede repetir o mesmo evento, mas não impede *lost update* entre dois eventos diferentes no mesmo dia.** Isso exige teste concorrente específico. Não há FK de `ProcessedEvent` ao `OutboxEvent` que pertence a outro banco/domínio.

### Identidade e autorização

Keycloak mantém usuários, credenciais e configurações OIDC. O CashFlow é responsável por regras de vínculo e permissão de comerciante. Uma string de `merchant_id` enviada no request não concede acesso por si só; o backend verifica o sujeito autenticado e seus privilégios. O sistema precisa definir como identifica o emissor/namespace de `sub` para evitar colisões caso haja vários emissores.

## Lacunas priorizadas

| Prioridade | Ponto aberto | Impacto |
| --- | --- | --- |
| **Decidido em 15/11/2026** | Exibir movimento líquido diário e saldo acumulado | Fórmulas conceituais definidas; saldo inicial de R$ 0,00 decidido; retroatividade aprovada com atualização dos acumulados afetados; materialização e estratégia física ainda abertas |
| **Decidido em 15/11/2026** | **RN-03:** saldo inicial de R$ 0,00 por comerciante no MVP | Cálculo acumulado parte de zero; não exige saldo de abertura |
| **Decidido em 15/11/2026** | **RN-09:** dia sem lançamentos retorna crédito, débito e líquido zero; saldo acumulado anterior é mantido | Não exige linha física por dia vazio; antes do primeiro lançamento, saldo acumulado zero |
| **Decidido em 15/11/2026** | **RN-04**: data de competência informada e instante de criação UTC | `reference_date` é `DATE`, enquanto `created_at` representa instante UTC; agrupamento pela competência | Competências passadas/atuais aceitas, futuras rejeitadas; saldos posteriores afetados devem ser corrigidos. Fuso de referência para a data atual e exibição a definir |
| **Decidido em 15/11/2026** | **RN-07**: BRL, `decimal`/.NET, `numeric(19,2)`/PostgreSQL, montantes > 0 e até duas casas | Rejeitar mais de duas casas sem arredondar; tipo `CREDIT`/`DEBIT` determina efeito. Totais e saldos calculados podem ser negativos |
| **Decidido em 15/11/2026** | Propriedade de dados de autorização | Banco lógico próprio aprovado; acesso via interface segura ainda a especificar | Implementar sem joins físicos cross-domain |
| **Alta** | Modelo físico de Outbox/ProcessedEvent e concorrência | Define garantias reais de idempotência e entrega |
| **Decidido em 15/11/2026** | Estorno e correção | Fora do MVP, agendado para a próxima versão | Não implantar operações de estorno agora |
| **Decidido em 15/11/2026** | Auditoria básica | Gravação de registros financeiros confirmados e retenção inicial 365 dias | Testar e definir política de expurgo/acesso |
| **Decidido em 15/11/2026** | Envelope de evento | JSON com event_id, event_type, schema_version e correlation_id | Especificar JSON Schema e versionamento compatível |
| **Média** | Migrations e isolamento de credenciais por banco lógico | Define deploy independente e integridade em runtime |

## Checklist da Validação 03

- [x] Conferidas as fronteiras de dados com ADR-003/005 e documentos de requisitos.
- [x] Identificada e corrigida a inconsistência entre `actor_id` UUID e `sub` textual OIDC no desenho.
- [x] Reafirmada a unicidade lógica de `ProcessedEvent.event_id`.
- [x] Explicitados os testes essenciais de atomicidade e concorrência.
- [x] Registradas decisões abertas sem alterar regras financeiras.
- [ ] Validar modelo físico e migrations com PostgreSQL real.
- [ ] Executar testes de rollback, duplicidade e concorrência.
- [ ] Definir fuso de referência para a data atual, recomposição física de acumulados retroativos, correção/estorno e autorização; verificar RN-07 em constraints, testes e contratos.
- [ ] Renderizar novamente `images/data-model.png` e verificar a apresentação.

**Conclusão:** estrutura lógica coerente com as ADRs após ajuste, mas **a integridade de execução e as regras contábeis não estão comprovadas**.

## Próxima etapa

**Validação 04: contratos de API e eventos**. Definir e revisar interfaces REST e mensagens RabbitMQ, incluindo `event_id`, versões, autorização, erros HTTP, criação idempotente e atualização de projeções. As decisões de negócio acima devem permanecer explicitamente abertas onde ainda não forem resolvidas.

## Referências

- [Modelo de Dados](../data-model.md)
- [Fonte do modelo PlantUML](../plantuml/data-model.puml)
- [Requisitos Funcionais](../../requirements/functional-requirements.md)
- [ADR-003: Persistência](../adr/ADR-003-persistence.md)
- [ADR-005: Outbox e Idempotência](../adr/ADR-005-outbox-consistency-idempotency.md)
- [ADR-006: Identidade e acesso](../adr/ADR-006-identity-and-access.md)
- [Validação 02: Diagramas de sequência](../sequence-validation.md)

## Revalidação das 24 decisões aprovadas em 15/11/2026

As conclusões e pendências históricas anteriores devem ser lidas com o complemento de [Decisões consolidadas do MVP](../mvp-decisions.md). **Escopo e políticas foram aprovados, mas nenhuma prova em execução foi produzida apenas por esta atualização.** Histórico paginado, auditoria básica, multi-comerciante, Authorization CashFlow com banco próprio, REST v1, Idempotency-Key, 201 pós-commit, eventos versionados, retry/backoff/DLQ, SLOs de 50 RPS/5 min/até 5%, p95 < 500 ms e frescor de 95% em 30 s são a nova referência. O acumulado é calculado na leitura sobre totais diários persistidos. Restam contratos, código, migrations, testes, configuração de infraestrutura e PNGs atualizados.
