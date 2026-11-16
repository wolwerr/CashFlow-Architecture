# Transactions | PostgreSQL migrations e testes de integração

## O que existe

- Migration inicial EF Core **202611160001_InitialTransactionsPersistence**, com tabelas `transactions`, `outbox_messages`, `transaction_audits` e `idempotency_records`.
- Model snapshot inicial de EF Core, com o esquema de colunas, checks e índices definido no início desta etapa.
- Projeto `tests/CashFlow.Transactions.IntegrationTests` separado dos testes unitários, que testa um **PostgreSQL 17 real**.
- Job Backend do CI disponibiliza um container PostgreSQL efêmero com credenciais **exclusivas do teste**, aplica migrations, repete `MigrateAsync`, valida inserts, isolamento por `merchant_id` na consulta, unicidade de chave de idempotência, check `amount > 0` e rollback.
- O CI mantém gates de >=80% para Domain/Application/Infrastructure, React e análise Sonar; migrations e snapshots, como código de schema gerado/manualmente mantido, possuem `ExcludeFromCodeCoverage` para não contaminar a cobertura das regras de negócio. A integração de migrações é validada por testes reais, não por essa exclusão.

## Execução local (somente Docker)

Use exclusivamente um banco **descartável** para esses testes. Não aponte a conexão para o banco de desenvolvimento `cashflow_transactions` ou outro banco com dados persistentes.

```powershell
docker compose up -d postgres
```

```powershell
docker compose exec postgres psql -U cashflow -d postgres -c "CREATE DATABASE cashflow_transactions_test;"
$env:CASHFLOW_TEST_POSTGRES="Host=localhost;Port=5432;Database=cashflow_transactions_test;Username=cashflow;Password=cashflow_local_only"
dotnet test tests/CashFlow.Transactions.IntegrationTests/CashFlow.Transactions.IntegrationTests.csproj -c Release
```

O arquivo `compose.yaml` mantém PostgreSQL apenas no loopback da máquina. Nenhum serviço Azure é necessário.

## Limitações e próximas etapas

- A migration não é executada automaticamente ao iniciar a API; sua execução é validada somente pelo projeto de integração e deve ser explicitamente controlada em inicialização/local setup. Não aplicar migrations silenciosamente em produção.
- Ainda falta implementar **Gateway transacional real** para inserção de Transaction, IdempotencyRecord, Audit e Outbox em um commit, o gerenciamento de conflitos/replays concorrentes, a publicação de Outbox, autenticação e RBAC.
- As verificações de leitura por comerciante no teste verificam a filtragem da consulta. **Não há ainda Row Level Security ou enforcement universal por tenant no EF**.
- Revisar modelo snapshot e sincronização com futuras migrations geradas via `dotnet ef migrations add`. Essa migration é a linha de base inicial; não reutilizar `EnsureCreated` em banco migrado.
- Manter tudo local. Sem provisionamento ou deploy Azure.
