# Modelo de Dados (Data Model View)

> **Projeto:** CashFlow Architecture · **Issue #6 / Milestone M2**  
> **Estado:** modelo lógico preliminar, sujeito às regras financeiras ainda abertas na Issue #3

## 1. Objetivo

Mostrar **quais informações cada domínio precisa manter**, sem antecipar SQL de implementação ou escolher um fornecedor de banco. O desafio exige lançamentos e consolidado diário; **auditoria, Outbox e processamento idempotente são decisões de desenho propostas**, motivadas pela proteção de dados financeiros e pela resiliência.

**[Fonte editável do diagrama PlantUML](plantuml/data-model.puml)**

> A imagem `images/data-model.png` será incluída no documento depois que o PlantUML for renderizado e o PNG for enviado ao repositório. O arquivo `.puml` é a fonte oficial.

## 2. Dados por contexto

| Contexto | Entidade proposta | Para que serve |
| --- | --- | --- |
| Lançamentos | **Transaction** | Registro financeiro confirmado: tipo, valor, data e identificador. |
| Lançamentos | **OutboxEvent** | Evento persistido junto ao lançamento, aguardando publicação confiável. |
| Lançamentos | **AuditEvent** | Trilha de auditoria sobre ações relevantes, especialmente criação, tentativa de correção, estorno e operações administrativas. |
| Consolidado diário | **DailyBalance** | Visão calculada para consulta dos valores de um dia. |
| Consolidado diário | **ProcessedEvent** | Identificadores de eventos já aplicados, impedindo contagem dupla. |

O **banco de lançamentos é a fonte de verdade dos registros financeiros**. O consolidado guarda uma visão derivada que deve poder ser reconstruída, conforme estratégia a ser definida na Issue #8.

### Relações importantes

- Um lançamento pode originar um evento da Outbox; no futuro pode originar outros eventos do ciclo de vida. A Outbox e o lançamento podem ser gravados **na mesma transação**.
- Um lançamento pode estar relacionado a registros de auditoria. A auditoria de sua criação, quando for requisito de consistência, deve ser persistida **na mesma transação** do lançamento, ou por um mecanismo com garantias equivalentes.
- **AuditEvent não substitui Transaction**, e não transforma a solução automaticamente em Event Sourcing.
- O **correlation_id** é um metadado técnico presente em **OutboxEvent** e **AuditEvent**, propagado também nos headers da mensagem. Ele associa registros, eventos e logs da mesma operação; não identifica o lançamento nem substitui o ID do evento.
- **Transaction** não precisa de correlation_id para representar o fato financeiro. **DailyBalance** não deve receber esse campo, pois agrega várias operações; em **ProcessedEvent** ele é opcional, já que o event_id permite rastrear a origem.
- **ProcessedEvent e DailyBalance** devem ser atualizados atomicamente ao consumir um evento; em caso de reentrega, o ID já processado impede a soma novamente.
- **Não existem chaves estrangeiras entre os bancos** de Lançamentos e Consolidado. A ligação ocorre por eventos e identificadores estáveis.

## 3. Auditoria financeira proposta

Para cada ação relevante, uma trilha de auditoria deve permitir responder:

| Informação | Exemplo ou uso |
| --- | --- |
| **Quem** | Identificador interno do usuário ou serviço (quando disponível) |
| **O quê** | Lançamento criado, estorno solicitado, operação rejeitada |
| **Quando** | Instante da ação, em UTC |
| **Sobre qual registro** | ID do lançamento, quando aplicável |
| **Resultado** | Sucesso, rejeição ou falha |
| **Rastreabilidade** | Correlation ID da solicitação, também persistido na Outbox e propagado à mensageria |

**Política proposta:** registros de auditoria são **append-only**: não devem ser alterados ou excluídos por usuários da aplicação. Isso **não equivale a armazenamento à prova de adulteração**; proteção contra alteração por administradores exigiria controles adicionais, como armazenamento imutável, trilhas externas, restrições de privilégio e monitoramento.

**Privacidade e segurança:** não copiar tokens, senhas ou dados pessoais desnecessários para `details`. Retenção, acesso à auditoria, criptografia e tratamento de dados pessoais serão definidos na Issue #9. Falhas de autenticação ou solicitações rejeitadas podem ser auditadas fora da transação financeira, sem transformar toda tentativa de acesso em lançamento.

### Metadados de correlação e tracing

- **transaction_id** identifica um lançamento financeiro.
- **event_id** identifica um evento individual e permite deduplicação no consumidor.
- **correlation_id** associa requisições, mensagens, auditoria e logs de um mesmo fluxo, inclusive quando há reprocessamentos.
- O worker publica o correlation_id como **header da mensagem** (proposta de contrato AMQP) e o consumidor preserva o valor em seus logs e telemetria.
- **trace_id** e **span_id**, quando houver OpenTelemetry, são identificadores de tracing distribuído e não substituem o correlation_id de negócio/operação.

## 4. Regras de integridade propostas

1. Valores financeiros devem usar precisão decimal adequada, **nunca números de ponto flutuante** para cálculos monetários.
2. Lançamentos confirmados devem preservar histórico. Correção por estorno é uma possibilidade de negócio ainda pendente.
3. `AuditEvent` deve ter identificador próprio e data/hora de ocorrência.
4. A confirmação do registro depende do armazenamento confiável do lançamento e da Outbox, **não** da disponibilidade do consolidado.
5. Eventos reprocessados não podem modificar o saldo mais de uma vez.
6. O consolidado pode ser eventualmente consistente, e qualquer diferença de atualização deve ser observável.

## 5. Campos ainda não fechados

- **Saldo diário:** movimento líquido do dia ou saldo acumulado, incluindo saldo inicial.
- **Dia financeiro:** definição de data de referência e fuso horário.
- **Identidade de negócio:** comerciante único ou vários, e necessidade de segregação por comerciante.
- **Moeda e arredondamento:** precisão e convenção.
- **Correções:** se serão implementadas no MVP e qual regra de estorno.
- **Auditoria:** quais ações exigem registro transacional, prazo de retenção e quem pode consultar.
- **Tecnologia:** tipos SQL, índices, constraints e migrations ficarão para as Issues #7 e #11–#14.

**Nota:** `actor_id`, `reversal_of_id` e a chave de `DailyBalance` são indicativos. Poderão mudar conforme a definição de multiusuário, comerciantes e cálculo do saldo.

## 6. Rastreabilidade

| Requisito / risco | Elementos relacionados |
| --- | --- |
| RF-01 / RF-02: registrar movimentações | Transaction, OutboxEvent |
| RF-03: consultar consolidado | DailyBalance |
| RNF-01: independência em falhas | OutboxEvent |
| RNF-04: não perder lançamento confirmado | Transaction + OutboxEvent |
| RNF-05: idempotência | ProcessedEvent + DailyBalance |
| R-01 / R-03: perda ou duplicidade | Transações atômicas, Outbox e deduplicação |
| R-06: dados sensíveis | AuditEvent com campos mínimos e política de acesso |

**Próximos passos:** validar este modelo com as regras de negócio da Issue #3; formalizar decisões de armazenamento e auditoria nas ADRs (#7); detalhar segurança e retenção (#9).
