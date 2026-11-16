# Convenções Clean Architecture: CashFlow

- **Domain/Models:** modelos de negócio e suas invariantes, sem frameworks ou dependências de persistência. Inclui `Transaction` e `BusinessCalendar`.
- **Domain/Enums:** enumerações do domínio.
- **Domain/Exceptions:** exceções específicas derivadas de `DomainException`.
- **Sem Domain/Entities e sem ValueObjects:** o modelo financeiro fica em `Models`; não criar estas pastas no Domain.
- **Application/UseCases:** casos de uso organizados por intenção. Não criar camada de negócio chamada `Services`.
- **Infrastructure/Entities:** entidades do EF Core e modelos físicos de persistência quando implementados. Elas serão mapeadas para os modelos de domínio, sem anotações do ORM no Domain.
- **Infrastructure:** persistência, implementações de portas e integrações.
- **Api:** transporte HTTP, autenticação e composição.

A pasta superior `src/Services` identifica microsserviços independentes (Transactions e Consolidation), não uma camada de business Services.

As exceções do domínio não conhecem HTTP. `ProblemDetails` e status HTTP pertencem à API.

Os testes arquiteturais e o gate de 80% do Domain continuam válidos. As validações precisam ser reexecutadas após esta reorganização.
