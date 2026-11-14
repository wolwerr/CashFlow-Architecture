# ADR-004: RabbitMQ para comunicação assíncrona entre serviços financeiros

> **Status:** proposta para aprovação  
> **Data:** 2026-11-14  
> **Issue:** [#7, Documentar ADRs e trade-offs](https://github.com/wolwerr/CashFlow-Architecture/issues/7)  
> **Relacionadas:** [ADR-002](ADR-002-architecture-style-clean-architecture.md), [ADR-003](ADR-003-persistence.md), ADR-005 (Outbox, consistência e idempotência)

## Contexto e requisitos

O CashFlow registra débitos/créditos no serviço Transactions e mantém o saldo diário no Consolidation. A indisponibilidade do consolidado não deve bloquear o registro (**RNF-01**). O consolidado precisa suportar **50 RPS de consultas**, com limite de até 5% de requisições sem resposta útil segundo interpretação pendente (**RNF-02/RNF-03**); **50 RPS de consultas não é uma medida de vazão de mensagens nem prova necessidade de Kafka**.

Os eventos financeiros devem ser entregues de forma durável e reprocessável após falhas. A ADR-003 estabelece PostgreSQL com Outbox transacional no domínio Transactions e projeções independentes no Consolidation. A ADR-005 detalhará a semântica de publicação, consistência eventual, repetição e ordenação.

## Alternativas

| Alternativa | Vantagens | Desvantagens e adequação |
| --- | --- | --- |
| **RabbitMQ (AMQP 0-9-1)** | Modelo tradicional de fila, exchange e roteamento; publisher confirms, consumer acknowledgements, DLQ, integração .NET e Docker local simples | Operação do broker, dimensionamento, configuração de durabilidade e governança; replay histórico ilimitado não é o foco da fila comum. **Preferida para o MVP** |
| **Apache Kafka** | Log distribuído particionado, retenção de eventos, replay, múltiplos consumidores e alto potencial de throughput | Complexidade de operação, particionamento e retenção mais alta para o cenário atual; ainda exige idempotência ao atualizar PostgreSQL. **Não escolhido neste momento** |
| **Azure Service Bus** | Filas/tópicos gerenciados, DLQ e recursos de confiabilidade; reduz operação de infraestrutura no Azure | Dependência da cloud e custos variáveis; menor conveniência para desafio reproduzível localmente. **Alternativa para implantação Azure** |
| **HTTP síncrono direto Transactions → Consolidation** | Implementação simples, sem broker | A disponibilidade de Consolidation passaria a afetar o registro, contrariando RNF-01. **Descartado** |

Nenhuma alternativa dispensa desenho de consistência e idempotência. A escolha não afirma que RabbitMQ seja intrinsecamente superior a Kafka em toda carga ou integração.

## Decisão proposta

Utilizar **RabbitMQ com AMQP 0-9-1** no MVP como broker entre Transactions e Consolidation, preservando a propriedade dos eventos em Transactions por meio de **Transactional Outbox**. Executar RabbitMQ no Docker Compose para avaliação. Não fixar uma tecnologia cloud gerenciada nesta ADR.

Fluxo:

```text
Transactions API
  -> PostgreSQL Transactions: movimento + Outbox (mesma transação)
  -> resposta HTTP após commit, sem consultar Consolidation

Transactions Outbox worker
  -> RabbitMQ exchange (evento publicado com publisher confirm)
  -> fila persistente de Consolidation
  -> Consolidation consumer
  -> PostgreSQL Consolidation: saldo + event_id processado (mesma transação)
  -> ACK manual após commit
```

### Regras de confiabilidade

1. **Outbox antes da publicação:** a operação aceita não depende de disponibilidade imediata do RabbitMQ. O worker reenvia pendências quando o broker voltar.
2. **Publisher confirms e roteamento:** marcar Outbox como publicada somente após confirmação positiva **e garantia de roteamento à fila prevista**, por exemplo com publicação `mandatory`, tratamento de retornos e topologia validada. Um confirm isolado não comprova que uma mensagem não roteável chegou à fila.
3. **Durabilidade:** exchanges/filas duráveis, mensagens persistentes e política de armazenamento adequada. No ambiente de produção, avaliar quorum queues e seu custo operacional; Docker com instância única **não** oferece alta disponibilidade.
4. **ACK manual:** consumir com confirmação somente depois que a projeção e o identificador do evento forem persistidos em transação no Consolidation.
5. **At-least-once:** aceitar possíveis duplicidades em falhas de confirmação, reconexão e retry. Aplicar `event_id` único e idempotência transacional no Consolidation. Não prometer exatamente uma entrega.
6. **Retry e DLQ:** distinguir falhas transitórias de mensagens inválidas; limitar tentativas com backoff e encaminhar mensagens irrecuperáveis à dead-letter queue. Definir procedimento seguro de inspeção/reprocessamento.
7. **Ordenação:** não presumir ordenação global. Avaliar necessidade de ordenação por comerciante/conta e concorrência nas atualizações; a solução precisa produzir saldos corretos sob redelivery e paralelismo.
8. **Contratos:** eventos versionáveis, por exemplo `event_id`, `event_type`, `schema_version`, `merchant_id`, `transaction_id`, `occurred_at`, `amount`, `direction`, e correlação. Dados sensíveis devem ser minimizados. Os detalhes definitivos ficam na ADR-005.

### Organização com Clean Architecture

- `Transactions.Domain`: regras financeiras, sem tipos AMQP.
- `Transactions.Application`: caso de uso e contratos necessários, sem cliente RabbitMQ.
- `Transactions.Infrastructure`: implementação do publicador, leitura transacional da Outbox e tratamento das confirmações.
- `Transactions.Api`: host/DI e registro do worker interno, sem criar terceiro microsserviço obrigatório.
- `Consolidation.Infrastructure`: adapter/consumer RabbitMQ e persistência das projeções.
- `Consolidation.Application/Domain`: processamento idempotente e regras de saldo, sem dependência da biblioteca do broker.

## Consequências positivas

- Registro financeiro desacoplado da disponibilidade do Consolidation e, com Outbox, da disponibilidade momentânea do broker.
- Recuperação de backlog após retorno do broker/consumidor, sujeita à capacidade e retenção.
- Execução local reprodutível em Docker Compose sem serviço pago.
- Modelo de filas suficientemente simples para demonstrar os requisitos explícitos.

## Riscos e mitigação

| Risco | Impacto | Mitigação |
| --- | --- | --- |
| Broker indisponível | Eventos não chegam imediatamente; saldo fica defasado | Outbox persistente, retries, alerta de backlog e teste de recuperação |
| Publicação duplicada | Saldo duplicado | Deduplicação transacional por `event_id` |
| ACK antes de commit | Perda lógica na projeção | ACK apenas depois de gravar saldo e evento processado |
| Mensagem não roteável | Confirmação do publisher sem entrega à fila | `mandatory`, tratamento de retornos e topologia declarada/testada |
| Poison messages | Consumo travado ou retry infinito | Tentativas limitadas, DLQ e reprocessamento controlado |
| Ordem/concorrência | Saldo incorreto em execução paralela | Estratégia de concorrência e testes por comerciante/conta |
| RabbitMQ único no Docker | Ponto único de falha | Delimitar o escopo de demonstração; avaliar quorum queues e HA no ambiente real |
| Crescimento do backlog | Atraso para atualizar o consolidado | Métricas de profundidade da fila, idade do evento e capacidade de consumo |

## Critérios de validação

- Um lançamento confirmado permanece no PostgreSQL e na Outbox quando RabbitMQ ou Consolidation caem.
- Após restabelecer o broker, o worker publica pendências com confirmação e o Consolidation atualiza saldos.
- Reentrega do mesmo `event_id` não altera o saldo duas vezes.
- Um crash antes do ACK gera reentrega e processamento seguro.
- Mensagem inválida chega à DLQ após política de tentativas, com causa rastreável.
- Publicação não roteável é detectada e não marcada como sucesso.
- Observabilidade registra fila, backlog da Outbox, falhas, idade dos eventos e DLQ.
- Os testes de **50 RPS de consultas** são executados separadamente sobre a API do Consolidation; não se deve confundir a taxa de consultas com a taxa de publicação.

## Revisitar a escolha

Considerar **Kafka** se surgirem múltiplos consumidores independentes, necessidade explícita de replay histórico, retenção longa de eventos ou evidências de vazão incompatíveis com o desenho atual. Considerar **Azure Service Bus** se a implantação for padronizada em Azure e custo/operacionalização favoreçam um broker gerenciado.

## Referências externas

- [RabbitMQ: Publisher Confirms e Consumer Acknowledgements](https://www.rabbitmq.com/docs/confirms)
- [RabbitMQ: Reliability Guide](https://www.rabbitmq.com/docs/reliability)
- [RabbitMQ: Quorum Queues](https://www.rabbitmq.com/docs/quorum-queues)
- [Apache Kafka: documentação](https://kafka.apache.org/documentation/)
- [Azure Service Bus: Dead-Letter Queues](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dead-letter-queues)
- [Azure Service Bus: Duplicate Detection](https://learn.microsoft.com/en-us/azure/service-bus-messaging/duplicate-detection)

## Referências internas

- [Requisitos não funcionais](../../requirements/non-functional-requirements.md)
- [Building Block View](../building-block-view.md)
- [Diagramas de sequência](../sequence-diagrams.md)
