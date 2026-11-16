# Convenções Clean Architecture: CashFlow

- **Domain/Models:** modelos de negócio e suas invariantes, sem frameworks ou dependências de persistência. Inclui `Transaction` e `BusinessCalendar`.
- **Domain/Enums:** enumerações do domínio.
- **Domain/Exceptions:** exceções específicas derivadas de `DomainException`.
- **Sem Domain/Entities e sem ValueObjects:** o modelo financeiro fica em `Models`; não criar estas pastas no Domain.
- **Application/UseCases/<CasoDeUso>/Commands:** entrada de casos de escrita como `sealed record`; nunca soltar Commands diretamente na raiz do UseCase.
- **Application/UseCases/<CasoDeUso>/Queries:** entrada de casos de consulta como `sealed record`, quando aplicável.
- **Application/UseCases/<CasoDeUso>/Results:** saídas como `sealed record`, quando aplicável.
- **Application/UseCases:** casos de uso organizados por intenção. Não criar camada de negócio chamada `Services`.
- **Application/Exceptions:** exceções personalizadas de execução de UseCases, derivadas de `ApplicationLayerException`. Não duplicar violações já tratadas pelo Domain.
- **Application/Gateways:** contratos de dependências externas, sem diretório `Interfaces`.
- **Infrastructure/Exceptions:** exceções técnicas personalizadas, derivadas de `InfrastructureLayerException`, preservando a causa em `InnerException` sem expor segredos.
- **Infrastructure/DTOs:** `sealed record` de transporte para integrações externas, podendo separar por integração. Não colocar DTOs diretamente na raiz de Infrastructure.
- **Api/DTOs:** `sealed record` de request/response HTTP; não confundir com contratos de mensageria.
- **Infrastructure/Entities:** entidades do EF Core e modelos físicos de persistência quando implementados. Elas serão mapeadas para os modelos de domínio, sem anotações do ORM no Domain.
- **Infrastructure:** persistência, implementações de portas e integrações.
- **Api:** transporte HTTP, autenticação e composição.

A pasta superior `src/Services` identifica microsserviços independentes (Transactions e Consolidation), não uma camada de business Services.

Preferir `sealed class` para Models de domínio com identidade e invariantes, `sealed record` para Commands, Queries, Results e DTOs, e `class` para Entities do EF Core. `record` oferece imutabilidade superficial. Não introduzir MediatR automaticamente.

As exceções do domínio não conhecem HTTP. `ProblemDetails` e status HTTP pertencem à API.

Os testes arquiteturais e o gate de 80% do Domain continuam válidos. As validações precisam ser reexecutadas após esta reorganização.

## Política de erros entre camadas

- Domain lança exceções de negócio; não conhece EF Core, mensageria ou HTTP.
- Application expõe erros de orquestração por classes próprias e não referencia exceções da Infrastructure; exceções Domain podem propagar sem conversão genérica.
- Infrastructure captura erros técnicos nos adaptadores somente quando necessário para dar contexto e garantir o contrato do Gateway; mantém a causa original para diagnóstico.
- API centraliza o tratamento, converte exceções esperadas em `ProblemDetails` e evita divulgar detalhes internos. Nenhum código de status HTTP nas classes de exceção internas.
- Classes-base existem nos dois microsserviços; exceções concretas adicionais devem nascer de casos reais e receber testes específicos.
