# Registro de decisões arquiteturais (ADRs)

> **Issue #7 · Milestone M2** | **Status:** em elaboração  
> **Arquitetura-alvo:** [Building Block View](../building-block-view.md)  
> **Requisitos:** [funcionais](../../requirements/functional-requirements.md) e [não funcionais](../../requirements/non-functional-requirements.md)

## Objetivo

Justificar decisões do CashFlow com **contexto, alternativas, benefícios, riscos e consequências operacionais**. O desenho-alvo contempla cliente Web, Keycloak e proxy NGINX incluído no MVP; Gateway dedicado e BFF são evoluções futuras, mas o desafio exige principalmente dois serviços financeiros, registro independente do consolidado e processamento sob carga. Não confundir capacidades propostas com funcionalidades obrigatórias para o MVP.

## Registro das decisões

| ADR | Tema | Direção inicial | Estado |
| --- | --- | --- | --- |
| [ADR-001](ADR-001-language-framework.md) | Linguagem e framework | **C# / ASP.NET Core / .NET 10 LTS** | **Decidida** |
| [ADR-002](ADR-002-architecture-style-clean-architecture.md) | Estilo arquitetural e Clean Architecture | Dois microsserviços independentes no monorepo, com Clean Architecture | **Decidida** |
| [ADR-003](ADR-003-persistence.md) | Persistência | PostgreSQL + EF Core 10, banco lógico por serviço e Dapper somente se justificado | **Decidida** |
| [ADR-004](ADR-004-async-messaging.md) | Comunicação assíncrona | RabbitMQ / AMQP 0-9-1, publisher confirms e ACK manual | **Decidida** |
| [ADR-005](ADR-005-outbox-consistency-idempotency.md) | Consistência e publicação | Transactional Outbox, consistência eventual e consumidor idempotente; sem Event Sourcing no MVP | **Decidida** |
| [ADR-006](ADR-006-identity-and-access.md) | Autenticação e autorização | Keycloak / OIDC / OAuth 2.0, JWT validado nas APIs e vínculos/papéis por comerciante mantidos no Keycloak (revisão 16/11/2026) | **Decidida** |
| [ADR-007](ADR-007-clients-api-gateway.md) | Clientes e borda | React + TypeScript; sem Gateway dedicado no MVP; NGINX incluído no MVP e Gateway/BFF futuros | **Decidida** |
| [ADR-008](ADR-008-observability.md) | Observabilidade | OpenTelemetry .NET + Collector; Prometheus, Loki, Tempo e Grafana (stack de observabilidade decidida) | **Decidida** |
| [ADR-009](ADR-009-deployment-cloud.md) | Implantação e cloud | LOCAL Docker; DEV/HOM econômicos e isolados, PROD AKS Standard + PostgreSQL HA; base Azure US$ 1.305,02/mês | **Decidida** |
| [ADR-010](ADR-010-ci-cd-github-actions.md) | CI/CD | GitHub Actions integrado a GitHub Issues, PRs e código; pipelines independentes para Transactions e Consolidation, promoção nos ambientes AKS | **Decidida** |
| [ADR-011](ADR-011-frontend-feature-based-architecture.md) | Arquitetura interna do frontend | **Feature-Based Architecture em React + TypeScript**; UI separada por funcionalidade, hook de orquestração e camada de API, sem Clean Architecture completa no MVP | **Decidida** |

**Decidida** indica que a decisão arquitetural foi registrada após análise das alternativas e dos trade-offs. Não significa que já foi implementada ou validada por testes. **Proposta** e **Pendente** continuam estados distintos.

## Prioridades de decisão

1. **ADR-001:** linguagem/framework do MVP, importante para a avaliação e para a equipe C# da empresa.
2. **ADR-002 e ADR-005:** isolamento entre os serviços financeiros e confiabilidade dos eventos, requisitos centrais do desafio.
3. **ADR-003 e ADR-004:** tecnologias necessárias à implementação da persistência e da mensageria.
4. **ADR-006:** segurança e segregação por comerciante, com atenção aos custos operacionais de manter um IAM próprio.
5. **ADR-007 a ADR-009:** custo e complexidade da solução completa, incluindo cenários local e cloud.
6. **ADR-011:** modularização do frontend mantendo as regras de autorização e finanças nos backends.

## Modelo para cada ADR

- **ID e título**
- **Status e data**
- **Contexto e problema**
- **Forças e requisitos afetados**
- **Alternativas consideradas**
- **Decisão e justificativa**
- **Consequências positivas e negativas**
- **Riscos, mitigação e impactos operacionais**
- **Critérios para revisitar a decisão**

## Riscos que não podem ser omitidos

- A API financeira não deve depender da disponibilidade do consolidado para confirmar um lançamento.
- O Outbox não evita entrega duplicada; o consolidado precisa registrar a aplicação de cada `event_id` de forma atômica com a atualização do saldo.
- Em caso de falha do IAM, tokens e chaves em cache podem permitir continuidade temporária, mas revogação e alteração de permissões exigem política explícita.
- Interface Web, NGINX e componentes futuros como API Gateway dedicado ou BFF aumentam o escopo, custos e pontos de falha; sua implementação deve ser justificada separadamente.
- Banco relacional por domínio não implica servidores físicos independentes para o MVP, mas deve preservar isolamento lógico e propriedade dos dados.
- Metas de volume, latência, perdas e consistência precisam ser verificadas por testes, não apenas representadas em diagramas.

## Próxima decisão

A [ADR-001](ADR-001-language-framework.md) foi **decidida**: C# / ASP.NET Core sobre .NET 10 LTS. A [ADR-002](ADR-002-architecture-style-clean-architecture.md) foi **decidida**: dois microsserviços independentes desde o MVP dentro de uma única solution e monorepo. A [ADR-003](ADR-003-persistence.md) foi **decidida**, com PostgreSQL e EF Core 10, após avaliação do modelo relacional versus NoSQL. A [ADR-004](ADR-004-async-messaging.md) foi **decidida**, com RabbitMQ para o MVP e esclarecimentos de ACK e alta disponibilidade. A [ADR-005](ADR-005-outbox-consistency-idempotency.md) foi **decidida**, mantendo Transactional Outbox e não adotando Event Sourcing no MVP. A [ADR-006](ADR-006-identity-and-access.md) foi **decidida**, adotando Keycloak para autenticação e vínculos por comerciante; as APIs financeiras conferem a autorização por operação, sem banco ou serviço Authorization adicional. A [ADR-007](ADR-007-clients-api-gateway.md) foi **decidida**: React + TypeScript, sem Gateway dedicado no MVP; NGINX definido como reverse proxy do MVP (integração pendente) e Gateway/BFF como evolução futura. A [ADR-008](ADR-008-observability.md) foi **decidida** com OpenTelemetry Collector, Prometheus, Loki, Tempo e Grafana, preservando trade-offs operacionais e a independência do registro financeiro em relação à telemetria. A [ADR-009](ADR-009-deployment-cloud.md) foi **decidida**: **Azure AKS em East US**, PostgreSQL Flexible Server externo ao cluster, LOCAL Docker Compose, DEV e HOM isolados e econômicos, PROD com AKS Standard e alta disponibilidade do banco habilitada. O orçamento de referência da Azure Pricing Calculator é **US$ 1.305,02/mês** (DEV 204,34; HOM 204,34; PROD 890,44; SHARED 5,90), sem caracterizá-lo como custo real garantido ou HA validada. [Registro financeiro](../four-environment-total-cost-plan.md). A [ADR-010](ADR-010-ci-cd-github-actions.md) foi **decidida**: GitHub Actions para CI/CD, centralizado no ecossistema GitHub, preservando pipelines e deploys independentes. A [ADR-011](ADR-011-frontend-feature-based-architecture.md) foi **decidida** após comparar três alternativas para organização do React: componente centralizado, modularização por funcionalidades e Clean Architecture completa. A escolha foi **Feature-Based Architecture**; implementação apresentada no PR #45. A estratégia de testes será detalhada na documentação de desenvolvimento, sem ADR própria.
