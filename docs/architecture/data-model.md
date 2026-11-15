# Modelo de Dados (Data Model View)

> **Projeto:** CashFlow Architecture · **Issue #6 / Milestone M2**  
> **Estado:** modelo lógico preliminar, sujeito às regras financeiras ainda abertas na Issue #3

## 1. Objetivo

Mostrar **quais informações cada domínio precisa manter**, sem antecipar SQL de implementação ou escolher um fornecedor de banco. O desafio exige lançamentos e consolidado diário; **auditoria, Outbox e processamento idempotente** foram adotados para proteger os dados financeiros e garantir resiliência. O **Keycloak** foi escolhido para identidade na ADR-006.

**[Fonte editável do diagrama PlantUML](plantuml/data-model.puml)**

![Modelo lógico preliminar de dados do CashFlow](images/data-model.png)

> **PNG a regenerar após a Validação 03:** a fonte `plantuml/data-model.puml` foi ajustada para representar `actor_id` como identificador textual OIDC e explicitar a unicidade de `ProcessedEvent.event_id`. Substituir `images/data-model.png` após a renderização.

## 2. Dados por contexto

| Contexto | Entidade proposta | Para que serve |
| --- | --- | --- |
| Keycloak (provedor de identidade) | **User (referência)** | Conta, credenciais e identidade gerenciadas pelo Keycloak; identificada para o CashFlow pelo claim `sub`. |
| Autorização CashFlow (persistência a definir) | **Merchant** | Cadastro do comerciante (proposto). |
| Autorização CashFlow (persistência a definir) | **Membership** | Vínculo entre sujeito autenticado (`sub`) e comerciante. |
| Autorização CashFlow (persistência a definir) | **Role / Permission** | Papéis e permissões de negócio e suas associações aos vínculos. |
| Lançamentos | **Transaction** | Registro financeiro confirmado: tipo, valor, data e identificador. |
| Lançamentos | **OutboxEvent** | Evento persistido junto ao lançamento, aguardando publicação confiável. |
| Lançamentos | **AuditEvent** | Trilha de auditoria sobre ações relevantes, especialmente criação, tentativa de correção, estorno e operações administrativas. |
| Consolidado diário | **DailyBalance** | Projeção dos totais de crédito/débito e movimento líquido por dia, com saldo acumulado para consulta. A materialização do acumulado e seu recálculo após movimentos retroativos ainda devem ser definidos. |
| Consolidado diário | **ProcessedEvent** | Identificadores de eventos já aplicados, impedindo contagem dupla. |

O **Keycloak é a fonte de identidade e autenticação**, incluindo o identificador estável do sujeito (`sub`), credenciais e configuração OIDC. A **autorização de negócio e os vínculos com comerciantes pertencem ao CashFlow**, com persistência e estratégia de consulta a definir na Issue #9. O `merchant_id` e o `actor_id` em registros financeiros são referências lógicas, **sem FK física** entre domínios. O `actor_id` foi representado como **STRING** no modelo lógico para acomodar o claim `sub` OIDC sem presumir UUID; caso adotemos um identificador interno distinto, será necessário documentar o mapeamento e a escolha do namespace do emissor. O banco de lançamentos continua sendo a fonte de verdade dos registros financeiros. O consolidado guarda uma visão derivada que deve poder ser reconstruída, conforme estratégia a ser definida na Issue #8.

### Relações importantes

- A proposta de segregação por comerciante adiciona `merchant_id` em **Transaction**, **AuditEvent**, **DailyBalance** e **ProcessedEvent**. A chave lógica sugerida para **DailyBalance** passa a ser `(merchant_id, reference_date)`.
- **Membership** associa usuários a comerciantes; os papéis podem ser atribuídos por vínculo, permitindo que a mesma pessoa tenha permissões distintas em estabelecimentos diferentes.
- O serviço financeiro deve garantir que o `merchant_id` utilizado está **autorizado para o sujeito autenticado**, não apenas aceitar um identificador arbitrário no payload ou na URL.
- A emissão e validade dos tokens dependem do Keycloak conforme a ADR-006. A revogação e atualização dos vínculos de autorização de negócio dependem da Issue #9; não devemos copiar senhas ou tokens para os bancos financeiros.

- Um lançamento pode originar um evento da Outbox; no futuro pode originar outros eventos do ciclo de vida. A Outbox e o lançamento podem ser gravados **na mesma transação**.
- Um lançamento pode estar relacionado a registros de auditoria. A auditoria de sua criação, quando for requisito de consistência, deve ser persistida **na mesma transação** do lançamento, ou por um mecanismo com garantias equivalentes.
- **AuditEvent não substitui Transaction**, e não transforma a solução automaticamente em Event Sourcing.
- O **event_id** de `ProcessedEvent` deve ser **único** no banco de Consolidation, sem FK física para Outbox. O identificador do registro de Outbox que originou o evento deve ser estável durante retries, mas o mapeamento do envelope e do ID do evento será detalhado no contrato.\n- O **correlation_id** é um metadado técnico presente em **OutboxEvent** e **AuditEvent**, propagado também nos headers da mensagem. Ele associa registros, eventos e logs da mesma operação; não identifica o lançamento nem substitui o ID do evento.
- **Transaction** não precisa de correlation_id para representar o fato financeiro. **DailyBalance** não deve receber esse campo, pois agrega várias operações; em **ProcessedEvent** ele é opcional, já que o event_id permite rastrear a origem.
- **ProcessedEvent e DailyBalance** devem ser atualizados atomicamente ao consumir um evento; em caso de reentrega, o ID já processado impede a soma novamente.
- **Não existem chaves estrangeiras entre domínios** de Keycloak, autorização CashFlow, Lançamentos e Consolidado. A ligação entre identidades e regras de acesso ocorre por identificadores estáveis; entre os serviços financeiros, também por eventos.

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

- **Saldo diário: decidido exibir ambos.** `net_amount = credit_total - debit_total`; `closing_balance` corresponde ao saldo inicial somado aos movimentos líquidos até a data. **RN-03 decidida:** saldo inicial de **R$ 0,00 por comerciante no MVP**, sem lançamento de abertura ou campo configurável obrigatório. A estratégia física para calcular/materializar o acumulado, incluindo lançamentos retroativos, ainda depende de definição.
- **Dia financeiro:** definição de data de referência e fuso horário.
- **Identidade e autorização:** Keycloak é o provedor escolhido para autenticação. `actor_id` representa referência textual ao sujeito OIDC, não UUID obrigatório. Definir issuer/namespace e, se houver ID interno, o mapeamento. A segregação por comerciante é uma regra do CashFlow; armazenamento, cardinalidades e fluxos de cadastro ainda precisam ser validados.
- **Moeda e arredondamento:** precisão e convenção.
- **Correções:** se serão implementadas no MVP e qual regra de estorno.
- **Auditoria:** quais ações exigem registro transacional, prazo de retenção e quem pode consultar.
- **Tecnologia:** tipos SQL, índices, constraints e migrations ficarão para as Issues #7 e #11–#14.

**Nota:** `actor_id`, `reversal_of_id` e a chave de `DailyBalance` são indicativos. Poderão mudar conforme a definição de multiusuário, comerciantes e cálculo do saldo.

## 6. Validação do modelo lógico\n\nA [Validação 03](data-model-validation.md) confere a correspondência do modelo com as ADR-003/005 e os requisitos. Permanecem pendentes o modelo físico (tipos SQL, índices, constraints e migrations), o cálculo aprovado do saldo, política de autorização por comerciante e contratos de evento. A revisão do diagrama **não é evidência de migrations executadas ou integridade verificada em banco**.\n\n## 7. Rastreabilidade

| Requisito / risco | Elementos relacionados |
| --- | --- |
| RF-01 / RF-02: registrar movimentações | Transaction, OutboxEvent |
| RF-03: consultar consolidado | DailyBalance |
| RNF-01: independência em falhas | OutboxEvent |
| RNF-04: não perder lançamento confirmado | Transaction + OutboxEvent |
| RNF-05: idempotência | ProcessedEvent + DailyBalance |
| R-01 / R-03: perda ou duplicidade | Transações atômicas, Outbox e deduplicação |
| R-06: dados sensíveis | AuditEvent com campos mínimos, Keycloak e autorização de negócio com política de acesso |
| Segregação de comerciantes (proposta) | Merchant, Membership e merchant_id nas projeções e lançamentos |

**Próximos passos:** validar este modelo com as regras de negócio da Issue #3; formalizar decisões de armazenamento e auditoria nas ADRs (#7); detalhar segurança e retenção (#9).
