# Trilha vertical 02 | Outbox, RabbitMQ e Consolidation

## Escopo implementado

**Transactions** mantém a confirmação HTTP independente do broker. A partir da tabela `outbox_messages` escrita no mesmo commit do lançamento, `OutboxPublisherWorker` busca o primeiro evento com `published_at_utc IS NULL`, ordenado e travado com `FOR UPDATE SKIP LOCKED`, publica um JSON persistente (delivery mode) para `cashflow.transactions` com routing key `transaction.recorded`, `mandatory` e publisher confirmation tracking. O status é marcado publicado **apenas após o broker confirmar**. Em falha, a linha permanece pendente com contador de tentativas. Se houver confirmação positiva seguida de falha do commit do banco, o evento pode reaparecer: o protocolo é **at-least-once**.

**RabbitMQ** usa exchange durável `cashflow.transactions`, fila quorum durável `cashflow.consolidation.transaction-recorded`, exchange de mensagens descartadas `cashflow.deadletter` e fila `cashflow.consolidation.dlq`. Um evento inválido é rejeitado sem requeue; falhas transitórias são reencaminhadas pelo broker até o limite `x-delivery-limit=5`, depois vão para DLQ. Esta não é uma garantia de entrega exatamente uma vez. Verificar configurações de rede, quorum e dead-lettering em testes reais antes de produção.

**Consolidation** tem banco próprio `cashflow_consolidation` e migração inicial de `daily_balances(merchant_id, reference_date, credit_total, debit_total, updated_at_utc)` e `processed_events(event_id, transaction_id, merchant_id, processed_at_utc)`. O consumidor valida `transaction.recorded` v1 e aplica uma transação PostgreSQL que insere o `event_id` com `ON CONFLICT DO NOTHING` e, somente na primeira aplicação, incrementa as somas do dia por `UPSERT`. O ACK é enviado **depois** do commit; mensagens repetidas não somam novamente.

**GET /api/v1/consolidation/daily?merchant_id={UUID}&date={YYYY-MM-DD}** valida JWT do Keycloak com audience `cashflow-consolidation-api`, consulta o vínculo **atual** por comerciante via gateway de Infrastructure, sem cache de permissões, e retorna valores BRL em strings. `closing_balance` é derivado da soma de movimentos **até e incluindo** a competência solicitada. Datas sem lançamentos têm movimentação zero e mantêm o acumulado anterior. Consulta com IAM indisponível falha fechada.

## Docker Compose local

As flags `Transactions__EnableOutboxPublisher=true`, `Consolidation__EnableConsumer=true` e `Consolidation__ApplyMigrationsOnStartup=true` são habilitadas no Compose local. Os dois serviços usam o RabbitMQ do próprio Compose. As migrations automáticas são **permitidas apenas no ambiente Development**. Não usar esse mecanismo em produção.

A configuração de cliente confidencial Keycloak `cashflow-authorization-service` e o segredo local `KEYCLOAK_AUTHZ_CLIENT_SECRET` continuam necessários. O realm existente não é atualizado por `--import-realm` caso já esteja criado; é necessário adicionar ao client SPA o audience mapper de `cashflow-consolidation-api` quando o realm estiver persistido.

## Evidências automatizadas

- Testes unitários do Domain e de Application.
- Testes PostgreSQL de migrations, idempotência por `event_id`, concorrência com merchant e competência iguais, retroativos, dia vazio e rollback.
- Testes de contrato `transaction.recorded` v1: moeda, enum, versão e valores inválidos.
- GitHub Actions continua com build .NET 10, frontend React/Vitest, cobertura e Sonar. Logs de publicação/consumo e testes E2E com broker Keycloak reais são programados para homologação do MVP.

## Não concluído nesta trilha

- Tela React, login/seleção de merchant, histórico de lançamentos e experiência ponta a ponta no navegador.
- Homologação manual completa de Keycloak, Quorum queues e recuperação após reinício/partição de rede.
- Dimensionamento de RPS/SLO e tuning de polling e limites DLQ com cargas representativas.
- Infraestrutura Azure ou CI de implantação.

**Atenção:** estas implementações não garantem ainda que o requisito de projeção em até 30 segundos foi medido ou atingido sob carga. Esse requisito deve ser comprovado no hardening. 
