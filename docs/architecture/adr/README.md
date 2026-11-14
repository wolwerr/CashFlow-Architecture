# Registro de decisões arquiteturais (ADRs)

> **Issue #7 · Milestone M2** | **Status:** em elaboração  
> **Arquitetura-alvo:** [Building Block View](../building-block-view.md)  
> **Requisitos:** [funcionais](../../requirements/functional-requirements.md) e [não funcionais](../../requirements/non-functional-requirements.md)

## Objetivo

Justificar decisões do CashFlow com **contexto, alternativas, benefícios, riscos e consequências operacionais**. O desenho-alvo contempla Web/Mobile, API Gateway e IAM, mas o desafio exige principalmente dois serviços financeiros, registro independente do consolidado e processamento sob carga. Não confundir capacidades propostas com funcionalidades obrigatórias para o MVP.

## Registro das decisões

| ADR | Tema | Direção inicial | Estado |
| --- | --- | --- | --- |
| ADR-001 | Linguagem e framework | Java 21/Spring Boot **ou** C#/.NET | **Pendente de decisão** |
| ADR-002 | Estilo de arquitetura | Dois serviços financeiros independentes, componentes auxiliares sob demanda | **Proposto** |
| ADR-003 | Persistência | Banco relacional transacional por domínio, PostgreSQL como candidato | **Proposto** |
| ADR-004 | Comunicação assíncrona | Broker de mensagens, RabbitMQ como candidato | **Proposto** |
| ADR-005 | Consistência e publicação | Transactional Outbox + consumidor idempotente; sem Event Sourcing no MVP | **Proposto** |
| ADR-006 | Autenticação e autorização | OIDC/OAuth 2.0, IAM consolidado, JWT validado localmente e escopo por comerciante | **Proposto** |
| ADR-007 | Clientes e borda | Web/Mobile e API Gateway na arquitetura-alvo, não necessariamente no MVP | **Proposto** |
| ADR-008 | Observabilidade | Logs estruturados, métricas e tracing com OpenTelemetry | **Proposto** |
| ADR-009 | Implantação e cloud | Docker Compose para avaliação local; cloud de referência a comparar | **Pendente** |

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
- Um API Gateway e uma interface Web/Mobile aumentam o escopo, os custos e os pontos de falha; são elementos propostos da arquitetura-alvo.
- Banco relacional por domínio não implica servidores físicos independentes para o MVP, mas deve preservar isolamento lógico e propriedade dos dados.
- Metas de volume, latência, perdas e consistência precisam ser verificadas por testes, não apenas representadas em diagramas.

## Próxima decisão

A primeira discussão detalhada será a **ADR-001: Linguagem e framework**, comparando Java/Spring Boot com C#/.NET para a implementação do MVP. A escolha deve considerar competência disponível, linguagem predominante na empresa, tempo de entrega, testes, operação e qualidade da demonstração.
