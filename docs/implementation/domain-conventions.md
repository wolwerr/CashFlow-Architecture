# Convenções Clean Architecture: CashFlow

- **Domain/Entities:** entidades e invariantes, sem frameworks.
- **Domain/Enums:** enumerações do domínio.
- **Domain/Exceptions:** exceções específicas de domínio derivadas de `DomainException`.
- **Domain/Rules:** regras de domínio puras e auxiliares de calendário, sem infraestrutura.
- **Domain/ValueObjects:** reservado para value objects reais, quando houver, sem placeholders.
- **Application/UseCases:** casos de uso, organizados por intenção/funcionalidade. **Não criar `Services` para regras de negócio.**
- **Application:** contratos/portas das integrações, quando forem definidos.
- **Infrastructure:** implementações de portas (EF Core, mensageria e outras integrações).
- **Api:** transporte HTTP, autenticação e composição.

A pasta `src/Services` no nível superior nomeia os **microsserviços implantáveis** Transactions e Consolidation, não uma camada de `Services` na Clean Architecture. A nomenclatura poderá ser revista separadamente sem alterar a regra de dependências.

Exceções do domínio não carregam HTTP status. O mapeamento para `ProblemDetails` e códigos HTTP pertence à API quando os endpoints forem implementados.

Nenhuma dependência externa foi adicionada ao Domain ou Application. O quality gate de 80% permanece definido em `docs/implementation/quality-gates.md`.
