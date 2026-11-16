# Transactions | Modelo inicial de persistência EF Core

## Escopo implementado

Infraestrutura PostgreSQL/EF Core configurada para representar quatro unidades de persistência, separadas dos Models do Domain:

1. `TransactionEntity`: lançamento imutável por comerciante, valor `numeric(19,2)`, competência `date`, timestamp UTC, índice composto para consulta.
2. `OutboxMessageEntity`: evento com identificador próprio, envelope versionado, payload `jsonb`, correlação, publicação e tentativas.
3. `TransactionAuditEntity`: registro básico de operação confirmada, ator/comerciante/lançamento/horário/correlação.
4. `IdempotencyRecordEntity`: chave composta `(merchant_id, request_key)`, hash SHA-256 em hexadecimal (64 posições), referência ao lançamento e criação UTC.

`TransactionsDbContext` aplica as configurações Fluent API; nenhuma entidade de banco foi colocada no Domain. A versão do provedor Npgsql EF Core utilizada é 10.0.3, compatível com .NET 10.

## Ainda não implementado

- **Nenhuma migration gerada ou executada**, tabela efetiva ou conexão com PostgreSQL validada.
- `ITransactionRegistrationGateway` ainda não tem implementação concreta. A transação atômica envolvendo quatro tabelas **não foi implementada** nem testada.
- Não há geração de payload versionado, hash canônico, controle de concorrência/replay, retenção, expurgo ou worker de Outbox.
- Não há autorização real nem endpoint de registro. Não ativar registro sem estes controles.
- A cardinalidade/índices/constraints precisam ser validados com PostgreSQL real em testes locais, e as migrations precisam ser revisadas antes de utilização.
- A política de escopo e prazo de retenção de Idempotency-Key continua pendente de formalização; a chave composta é uma proposta de base para essa especificação.

## Testes executáveis

O projeto de testes unitários verifica o **modelo relacional EF Core sem acessar banco de dados**: tabelas, tipos SQL, constraints e índices. O pipeline tem um quality gate independente de cobertura >=80% para Infrastructure. O resultado deve ser confirmado na execução do GitHub Actions.

## Próxima etapa

Gerar migrations EF Core para Transactions e montar testes de integração reais com PostgreSQL local, inclusive rollback, idempotência concorrente e isolamento por comerciante; depois implementar o Gateway de registro atômico. Azure permanece futuro.
