# Registro de decisões arquiteturais (ADRs)

> **Issue #7 · Milestone M2** | **Status:** em elaboração  
> **Arquitetura-alvo:** [Building Block View](../building-block-view.md)  
> **Requisitos:** [funcionais](../../requirements/functional-requirements.md) e [não funcionais](../../requirements/non-functional-requirements.md)

## Objetivo

Justificar decisões do CashFlow com **contexto, alternativas, benefícios, riscos e consequências operacionais**. O desenho-alvo contempla cliente Web, Keycloak e proxy NGINX incluído no MVP; Gateway dedicado e BFF são evoluções futuras, mas o desafio exige principalmente dois serviços financeiros, registro independente do consolidado e processamento sob carga. Não confundir capacidades propostas com funcionalidades obrigatórias para o MVP.

## Registro das decisões

| ADR | Tema | Direção inicial | Estado |
| --- | --- | --- | --- |
| [ADR-001](ADR-001-language-framework.md) | Linguagem e framework | **C# / ASP.NET Core / .NET 10 LTS** | **Aceita** |
| [ADR-002](ADR-002-architecture-style-clean-architecture.md) | Estilo arquitetural e Clean Architecture | Dois microsserviços independentes no monorepo, com Clean Architecture | **Aceita** |
| [ADR-003](ADR-003-persistence.md) | Persistência | PostgreSQL + EF Core 10, banco lógico por serviço e Dapper somente se justificado | **Aceita** |
| [ADR-004](ADR-004-async-messaging.md) | Comunicação assíncrona | RabbitMQ / AMQP 0-9-1, publisher confirms e ACK manual | **Aceita** |
| [ADR-005](ADR-005-outbox-consistency-idempotency.md) | Consistência e publicação | Transactional Outbox, consistência eventual e consumidor idempotente; sem Event Sourcing no MVP | **Aceita** |
| [ADR-006](ADR-006-identity-and-access.md) | Autenticação e autorização | Keycloak / OIDC / OAuth 2.0, JWT validado localmente e autorização por comerciante no CashFlow | **Aceita** |
| [ADR-007](ADR-007-clients-api-gateway.md) | Clientes e borda | React + TypeScript; sem Gateway dedicado no MVP; NGINX incluído no MVP e Gateway/BFF futuros | **Aceita** |
| [ADR-008](ADR-008-observability.md) | Observabilidade | OpenTelemetry .NET + OTLP, Collector opcional no MVP; backends a escolher | **Proposta para aprovação** |
| ADR-009 | Implantação e cloud | Docker Compose para avaliação local; cloud de referência a comparar | **Pendente** |
| ADR-010 | Estratégia de testes e cobertura | Mínimo de **80% de cobertura de linhas**, além de testes de integração, carga e resiliência | **Proposto** |
| ADR-011 | CI/CD | Comparar GitHub Actions, Azure DevOps Pipelines e Jenkins; automatizar quality gates | **Pendente** |

**Status proposto** significa que o padrão está desenhado, mas ainda não é uma escolha aprovada. Cada ADR será detalhada e terá seu status atualizado após análise dos trade-offs.

## Prioridades de decisão

1. **ADR-001:** linguagem/framework do MVP, importante para a avaliação e para a equipe C# da empresa.
2. **ADR-002 e ADR-005:** isolamento entre os serviços financeiros e confiabilidade dos eventos, requisitos centrais do desafio.
3. **ADR-003 e ADR-004:** tecnologias necessárias à implementação da persistência e da mensageria.
4. **ADR-006:** segurança e segregação por comerciante, com atenção aos custos operacionais de manter um IAM próprio.
5. **ADR-007 a ADR-009:** custo e complexidade da solução completa, incluindo cenários local e cloud.

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

A [ADR-001](ADR-001-language-framework.md) foi **aceita**: C# / ASP.NET Core sobre .NET 10 LTS. A [ADR-002](ADR-002-architecture-style-clean-architecture.md) foi **aceita**: dois microsserviços independentes desde o MVP dentro de uma única solution e monorepo. A [ADR-003](ADR-003-persistence.md) foi **aceita**, com PostgreSQL e EF Core 10, após avaliação do modelo relacional versus NoSQL. A [ADR-004](ADR-004-async-messaging.md) foi **aceita**, com RabbitMQ para o MVP e esclarecimentos de ACK e alta disponibilidade. A [ADR-005](ADR-005-outbox-consistency-idempotency.md) foi **aceita**, mantendo Transactional Outbox e não adotando Event Sourcing no MVP. A [ADR-006](ADR-006-identity-and-access.md) foi **aceita**, adotando Keycloak para autenticação, mantendo autorização por comerciante nos serviços de negócio. A [ADR-007](ADR-007-clients-api-gateway.md) foi **aceita**: React + TypeScript, sem Gateway dedicado no MVP; NGINX como reverse proxy implementado no MVP e Gateway/BFF como evolução futura. A [ADR-008](ADR-008-observability.md) está proposta para revisão com OpenTelemetry, sinais prioritários e alternativas de coleta/visualização. A ADR-010 formalizará a meta de cobertura mínima de 80%, e a ADR-011 comparará plataformas CI/CD antes da escolha.
