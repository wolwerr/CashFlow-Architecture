# Transactions | Gateway de registro atômico

## Escopo

`Infrastructure/Gateways/TransactionRegistrationGateway.cs` implementa `ITransactionRegistrationGateway`.

- O UseCase valida autorização e regras de domínio; o gateway **não é exposto pela API** e não fornece mecanismo de autenticação temporário.
- O gateway inicia uma transação explícita PostgreSQL, grava a chave de idempotência, a Transaction, a auditoria e o evento Outbox, depois confirma **um único commit**. Um erro no meio causa rollback.
- A chave `(merchant_id, request_key)` é única no banco. A tentativa concorrente aguarda a decisão da primeira transação; em conflito, o gateway relê o registro já commitado. Mesmo payload retorna o Result original, payload divergente lança `TransactionIdempotencyConflictException`.
- O fingerprint usa SHA-256 de uma representação canônica de merchant, actor, type, decimal BRL com duas casas e data de competência. Identificador de correlação **não entra** no fingerprint porque retries podem ter novo rastreamento.
- `Infrastructure/DTOs/TransactionRecordedDto.cs` define o evento `transaction.recorded` v1. O payload é JSON com chaves `snake_case`, valor monetário em string decimal invariável e instante UTC.
- Falhas técnicas de gravação são encapsuladas em `TransactionPersistenceException` com `InnerException`; erros de conflito de idempotência permanecem exceções de Application.
- **Outbox não é publicado pelo gateway**. A publicação resiliente pelo RabbitMQ virá em etapa posterior.

## Testes reais de PostgreSQL no CI

- As quatro tabelas possuem registros correlacionados depois do commit.
- O payload armazenado é inspecionado em JSON.
- Replay do mesmo merchant/ID de idempotência e payload retorna exatamente o Result anterior, sem duplicar linhas ou eventos.
- Mudança de payload para a mesma chave retorna conflito e não cria novos lançamentos.
- Requisições concorrentes para a mesma chave convergem para um único lançamento e um único evento.
- Chaves idênticas em merchants diferentes não colidem.
- Violação na gravação do Outbox provoca rollback integral, sem deixar a reserva de idempotência ou outros registros; depois é possível tentar novamente com a mesma chave.
- O gate de cobertura Infrastructure usa **testes de integração PostgreSQL**, evitando que um módulo com operações reais de banco tenha cobertura artificialmente reduzida por testar somente mapeamentos em memória. O Sonar importa relatórios OpenCover unitários e de integração.

## Não implementado

- Autorização real Keycloak + membership/RBAC, endpoint HTTP e DI de runtime, executores e retries do Outbox e emissão RabbitMQ.
- Retenção da chave de idempotência, observabilidade completa, esquema de política de autorização de merchants.
- Testes de carga de alto paralelismo e cenários de perda de conectividade durante COMMIT.
- Persistência no Consolidation e Azure.

A implementação exige PostgreSQL local e migrations aplicadas. O CI usa PostgreSQL 17 efêmero, mas a API **não aplica migrations automaticamente**. Consultar `docs/implementation/transactions-postgres-migrations.md` para setup local.
