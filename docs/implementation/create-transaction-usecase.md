# Transactions: CreateTransaction (Application)

## Implementado nesta etapa

- `CreateTransactionCommand` e `CreateTransactionResult` como `sealed record`, nas pastas `Commands` e `Results`.
- `CreateTransactionUseCase` como classe explícita, sem MediatR e sem dependências de Infrastructure.
- `ICreateTransactionAuthorizationGateway` e `ITransactionRegistrationGateway` como contratos em `Application/Gateways`.
- Verificação fail-closed quando autorização retorna `false` ou falha; exceções específicas de Application; validação financeira pelo Domain.
- Data atual de negócio calculada com `BusinessCalendar` em `America/Sao_Paulo`, a partir de `TimeProvider.GetUtcNow()`.
- Testes unitários com implementações falsas dos Gateways e gate independente de 80% de linhas para Application.

## Limites explícitos

**Ainda não existe implementação concreta do Gateway de registro**. O contrato exige persistir **Transaction, Outbox, auditoria confirmada e estado/resultado de idempotência HTTP em uma mesma transação** e só retornar após commit bem-sucedido. Replays do mesmo payload devem retornar resultado persistido e payload divergente deve gerar conflito. A implementação e seus testes de integração são a próxima etapa. O UseCase não constitui uma API funcional.

**Autorização**: o Gateway precisa validar identidade e vínculo de usuário/comerciante e política RBAC; a matriz detalhada ADMIN/OPERATOR e a propriedade do banco de autorização permanecem pendentes. Nunca ligar o UseCase a um stub de autorização permissivo em runtime. O ator no Command deve ser derivado de identidade autenticada, e não de JSON do cliente.

**Idempotência**: validade da chave (não vazia, <=128) é verificada no Application; semântica de hash, retenção e escopo por comerciante/rota devem ser implementadas na persistência atômica. **Correlação**: o UseCase recebe ID de correlação obrigatório do limite de transporte, que deverá gerá-lo quando não for enviado pelo cliente, sem confiar cegamente em cabeçalhos.

**Ainda não implementado**: endpoint HTTP, mapeamento ProblemDetails, EF Core/migrations, RabbitMQ, autorização real, observabilidade ou execução de teste de integração.

## Validação

Executar:

```powershell
dotnet build CashFlow.slnx -c Release
dotnet test tests/CashFlow.ArchitectureTests/CashFlow.ArchitectureTests.csproj -c Release
dotnet test tests/CashFlow.Transactions.UnitTests/CashFlow.Transactions.UnitTests.csproj -c Release /p:CollectCoverage=true /p:Threshold=80 /p:ThresholdType=line /p:ThresholdStat=Minimum '/p:Include=[CashFlow.Transactions.Domain]*'
dotnet test tests/CashFlow.Transactions.UnitTests/CashFlow.Transactions.UnitTests.csproj -c Release /p:CollectCoverage=true /p:Threshold=80 /p:ThresholdType=line /p:ThresholdStat=Minimum '/p:Include=[CashFlow.Transactions.Application]*'
```

Não registrar testes como aprovados sem execução confirmada. Nenhum recurso Azure envolvido.
