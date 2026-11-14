# Visão de Blocos de Construção (Building Block View)

> **Issue #6 · Milestone M2** | Arquitetura proposta

Esta visão mostra **de que partes o CashFlow é composto** e o papel de cada uma. Os diagramas utilizam o **C4 Model** com PlantUML.

## C4 Nível 1: Contexto

O comerciante utiliza o CashFlow para registrar movimentações e consultar o consolidado. Um provedor de identidade externo é proposto para autenticação.

![Contexto do CashFlow](images/c4-context.png)

[Fonte PlantUML](plantuml/c4-context.puml)

## C4 Nível 2: Contêineres

O sistema tem dois serviços de negócio independentes: **Lançamentos** e **Consolidado diário**. O publicador Outbox faz parte do serviço de lançamentos, não é um terceiro serviço de negócio.

![Contêineres do CashFlow](images/c4-containers.png)

[Fonte PlantUML](plantuml/c4-containers.puml)

| Bloco | Responsabilidade |
| --- | --- |
| Serviço de lançamentos (API + Outbox worker) | Receber e confirmar movimentações; publicar eventos pendentes em segundo plano. |
| Banco de lançamentos | Guardar lançamentos e eventos Outbox na mesma transação. |
| Mensageria | Entregar eventos ao serviço de consolidado. |
| Serviço de consolidado | Atualizar a visão diária e responder consultas de saldo. |
| Banco do consolidado | Guardar resultados calculados e eventos já processados. |
| Provedor de identidade (externo) | Autenticar usuários e fornecer chaves públicas para validação dos tokens. |
| Coleta de observabilidade | Receber informações de diagnóstico e desempenho dos serviços. |

## Protocolos propostos

| Comunicação | Protocolo |
| --- | --- |
| Comerciante → APIs | HTTPS / REST / JSON, com Bearer JWT |
| Login e identificação | OpenID Connect / OAuth 2.0 sobre HTTPS |
| Validação de JWT | JWKS / HTTPS, com validação local e cache das chaves |
| APIs e seus bancos | SQL / TCP, com proteção de conexão |
| Worker Outbox → broker → consumidor | AMQP 0-9-1 |
| Serviços → observabilidade | OTLP / gRPC e logs estruturados |

Os protocolos representam **propostas**, não decisões finais. Produtos e garantias serão formalizados nos ADRs da Issue #7. A autenticação não requer consultar o provedor de identidade a cada requisição.

## Evolução para C4 Nível 3

Se for útil à avaliação, uma futura visão de componentes pode detalhar **API, aplicação de negócio, persistência e Outbox worker** dentro do serviço de lançamentos, assim como consumidor e consulta no serviço de consolidado. Não devemos acrescentar esse nível apenas para multiplicar diagramas.

## Próximas leituras

- [Diagramas de sequência](sequence-diagrams.md), para ver as interações passo a passo.
- [Casos de Uso](use-cases.md), para entender as ações do comerciante e os fluxos de negócio.
