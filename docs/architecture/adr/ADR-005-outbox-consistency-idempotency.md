# ADR-005: Transactional Outbox, consistência eventual e consumidor idempotente

> **Status:** proposta para aprovação  
> **Data:** 2026-11-14  
> **Issue:** [#7, Documentar ADRs e trade-offs](https://github.com/wolwerr/CashFlow-Architecture/issues/7)  
> **Relacionadas:** [ADR-002](ADR-002-architecture-style-clean-architecture.md), [ADR-003](ADR-003-persistence.md), [ADR-004](ADR-004-async-messaging.md)

## Contexto e problema

O CashFlow deve aceitar lançamentos financeiros mesmo quando o Consolidation estiver indisponível (RNF-01). Um registro confirmado deve permanecer durável; o consolidado diário pode ficar temporariamente desatualizado e recuperar os eventos pendentes. O broker escolhido na ADR-004 é RabbitMQ, e a persistência na ADR-003 é PostgreSQL com EF Core 10.

Precisamos evitar o problema de **dual write**, quando o serviço grava no banco e publica no broker em operações independentes. Falhas entre essas operações podem deixar lançamentos sem evento ou eventos sem lançamento. Também precisamos impedir que reentrega de mensagens contabilize um valor mais de uma vez.

## Alternativas consideradas

| Alternativa | Benefícios | Limitações |
| --- | --- | --- |
| Chamada HTTP síncrona Transactions → Consolidation | Simplicidade de fluxo e atualização imediata | Falha de Consolidation impactaria o registro, contrariando RNF-01 |
| Persistência + publicação diretamente no broker (dual write) | Poucas peças no código inicial | Não há atomicidade entre PostgreSQL e RabbitMQ; falha entre as operações cria inconsistências |
| Transação distribuída / 2PC | Busca atomicidade distribuída em ambientes compatíveis | Coordenação e suporte dos recursos; complexidade e acoplamento desproporcionais ao MVP |
| **Transactional Outbox + mensagens at-least-once + consumidor idempotente** | Transação local única; desacoplamento; recuperação de falhas | Consistência eventual, worker adicional e necessidade de deduplicação |
| Event Sourcing como registro de verdade | Log completo de mudanças, replay e reconstrução de projeções | Remodelagem do domínio, versionamento e replay mais complexos; não é requisito do desafio |

## Decisão proposta

Adotar **Transactional Outbox** em Transactions e **consumo idempotente** em Consolidation, com consistência eventual entre os bancos. O registro de lançamentos é a fonte de verdade das movimentações financeiras; a projeção diária é derivada e pode ser reconstruída mediante uma estratégia de reconciliação/reprocessamento que será detalhada na implementação. **Não adotar Event Sourcing como modelo primário no MVP.**

### Registro do lançamento (Transactions)

1. Validar entrada e autorização do comerciante.
2. Criar `transaction_id` e `event_id` únicos e estáveis, além de chave de idempotência HTTP quando definida para o endpoint.
3. Em **uma transação PostgreSQL**, persistir lançamento e registro `OutboxEvent` contendo payload, metadados e status pendente.
4. Fazer commit; **somente depois** responder sucesso ao cliente.
5. Um `BackgroundService` interno ao Transactions busca eventos pendentes em lotes e publica no RabbitMQ. Concorrência entre workers precisa de estratégia segura de claim/lock, evitando publicação excessiva.
6. Só marcar publicação concluída depois de publisher confirm positivo **e confirmação de roteamento ao destino esperado**, conforme a ADR-004. Se a confirmação for perdida ou o processo cair, publicar novamente é permitido.

A resposta de sucesso significa **lançamento registrado**, não **saldo consolidado atualizado**. Status HTTP (201 ou 202) e contrato de consulta de acompanhamento serão definidos na API.

### Atualização da projeção (Consolidation)

1. Receber evento e validar esquema, versão, identificador, comerciante e campos financeiros.
2. Iniciar transação local no PostgreSQL do Consolidation.
3. Tentar inserir `event_id` na tabela de eventos processados com **restrição única**.
4. Se já existir, considerar o evento previamente aplicado; não alterar saldo novamente. Se não existir, aplicar a operação à projeção diária sob estratégia segura de concorrência.
5. Confirmar a transação contendo **identificador processado + saldo atualizado**.
6. **Somente após commit**, enviar ACK manual ao RabbitMQ.

Falha antes do commit implica rollback e reentrega possível. Falha após commit e antes do ACK pode resultar em entrega duplicada, que deve ser inofensiva à projeção.

### Ordenação, valores e concorrência

- A soma de créditos e débitos de uma mesma data/comerciante pode ser comutativa para movimentos independentes, mas isso precisa ser conferido contra regras de negócio como estorno, cancelamento e ajustes posteriores. Não presumir ordem global do RabbitMQ.
- Atualizações concorrentes para o mesmo saldo devem ser **atômicas** (por exemplo, UPSERT incremental ou bloqueio/transação compatível), evitando *lost updates*. Unicidade do `event_id` sozinha não resolve concorrência entre eventos distintos.
- Valores são `decimal` no código e `numeric(p,s)` no banco; moeda, escala e data de competência serão definidas nos requisitos e modelo físico.
- Não criar uma segunda fonte de verdade no consumidor: o consolidado é uma projeção, não o livro de lançamentos.

## Semântica de garantias

| Propriedade | O que é prometido |
| --- | --- |
| Gravação de lançamento e Outbox | **Atômica dentro do banco de Transactions** |
| Publicação em RabbitMQ | **Pelo menos uma vez**, não exatamente uma vez |
| Processamento no Consolidation | **Efeito idempotente** na projeção por `event_id`, se transações e constraints forem implementadas corretamente |
| Visibilidade do saldo | **Eventualmente consistente**: pode atrasar durante falhas e backlog |
| Disponibilidade do registro | Não depende da saúde de Consolidation nem da disponibilidade momentânea do RabbitMQ após commit local |
| Entrega infalível sob todas as falhas | **Não garantida**: depende de infraestrutura durável, retenção, políticas, backups, recuperação e testes |

## Tratamento de falhas

| Falha | Resposta esperada |
| --- | --- |
| Transactions falha antes do commit | Não confirmar lançamento; rollback de transação/outbox |
| RabbitMQ indisponível | Lançamentos persistem e acumulam Outbox pendente; worker faz retry |
| Transactions falha após publish antes de marcar Outbox | Evento pode ser republicado; consumidor deve deduplicar |
| Consolidation indisponível | Broker mantém mensagens segundo configuração de durabilidade/retenção; registro permanece disponível |
| Consolidation faz commit mas perde ACK | RabbitMQ pode redeliver; `event_id` impede contagem dupla |
| Mensagem inválida | Retry limitado e DLQ, com alerta e investigação; não dar ACK indiscriminado sem política |
| Backlog grande | Monitorar idade, capacidade de consumo e progressão; ajustar batch/concurrency de modo seguro |

## Observabilidade e reconciliação

- Métricas: quantidade e idade da Outbox pendente, publicações confirmadas, retries, retornos não roteáveis, mensagens em fila, DLQ, erros de consumo, atrasos de atualização de saldo e eventos duplicados.
- Traces correlacionam `transaction_id`, `event_id` e `correlation_id`, sem divulgar valores sensíveis em logs.
- Uma rotina de reconciliação deve comparar lançamentos de referência e projeções por comerciante/data, identificar divergências e permitir reconstrução controlada do consolidado. **Não pressupor replay histórico ilimitado a partir de filas RabbitMQ comuns**; lançamentos confirmados no PostgreSQL são a base de recuperação.
- Auditoria financeira persistida não se confunde com logs operacionais.

## Testes de aceitação

1. Testar rollback: falha de banco antes do commit não deixa lançamento sem Outbox nem Outbox sem lançamento.
2. Desligar Consolidation e registrar créditos/débitos com sucesso; confirmar pendências e posterior atualização.
3. Desligar RabbitMQ e repetir; após retorno, confirmar escoamento do backlog.
4. Simular confirmação de publicação perdida e reentrega do mesmo `event_id`; conferir saldo único.
5. Simular crash do consumidor após commit, antes do ACK; conferir saldo único.
6. Processar eventos distintos concorrentes para o mesmo comerciante/data; conferir ausência de *lost updates*.
7. Testar mensagens inválidas, DLQ, métricas e reprocessamento controlado.
8. Executar teste de 50 RPS na **API de consulta do Consolidation** como requisito separado, não como teste de throughput do broker.

## Consequências e trade-offs

**Positivos:** isolamento entre gravação e consolidação, integridade transacional local, reentrega segura e testes reproduzíveis. **Negativos:** consistência eventual, fila/outbox, necessidade de limpeza/retenção de registros e reconciliação, e atenção especial a concorrência, ordenação, monitoramento e operação.

### Por que não Event Sourcing agora?

O desafio exige auditabilidade e recuperação, **não exige que o estado seja reconstruído exclusivamente de um log de eventos imutável**. O registro tradicional de lançamentos mais Outbox oferece trilha de movimentações e projeções sem assumir o custo de um sistema Event Sourced, com versionamento histórico do domínio e regras de replay complexas. Event Sourcing pode ser reavaliado se surgirem requisitos de replay integral, histórico temporal detalhado ou múltiplas projeções.

## Limites e pontos pendentes

- Especificar contrato JSON, schema versioning, estratégia de chaves de idempotência HTTP, ordenação/competência de eventos e correções/estornos.
- Definir comportamento de reconciliação e ferramenta operacional para reprocessamento.
- Definir SLO realista de atraso do consolidado e retenção conforme requisitos de negócio; as metas existentes são propostas e precisam de validação.
- Detalhar índices, concorrência de workers e política DLQ/retry na implementação.

## Referências

- [Requisitos não funcionais](../../requirements/non-functional-requirements.md)
- [Modelo de dados](../data-model.md)
- [Diagramas de sequência](../sequence-diagrams.md)
- [RabbitMQ: confirmações de publicação e ACK](https://www.rabbitmq.com/docs/confirms)
- [Microsoft: padrão Transactional Outbox](https://learn.microsoft.com/en-us/azure/architecture/databases/guide/transactional-outbox-cosmos)
