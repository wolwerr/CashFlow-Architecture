# Desafio de Arquitetura de Soluções | Matriz de atendimento

**Fonte:** documento `Desafio - Arq. Soluções` fornecido para revisão, três páginas. Esta matriz separa **artefato existente** de **evidência de funcionamento real**. As especificações adicionais do projeto não devem ser apresentadas como exigências expressas da banca.

Legenda: **Documentado** = há decisão/artefato rastreável; **Implementado, não homologado** = código criado mas teste real local não demonstrado nesta revisão; **Pendente de comprovação** = exige medição ou execução real; **Não aplicável** = contexto não descrito no desafio.

| Página e exigência | Atendimento técnico / referência | Estado e comprovação necessária |
| --- | --- | --- |
| p. 3: serviço de controle de lançamentos | [RF-01/RF-02](functional-requirements.md), Transactions API, PostgreSQL, Outbox | Implementado, não homologado; confirmar POST de crédito e débito |
| p. 3: serviço de consolidado diário | [RF-03](functional-requirements.md), Consolidation API, projeção por RabbitMQ | Implementado, não homologado; confirmar totais e saldo |
| p. 2: domínios funcionais e capacidades de negócio | [Domínios e contexto](../domain/business-capabilities-and-context-map.md), [casos de uso](../architecture/use-cases.md) | Documentado |
| p. 2: levantamento refinado de requisitos funcionais e não funcionais | [Requisitos funcionais](functional-requirements.md), [não funcionais](non-functional-requirements.md), [premissas e riscos](priorities-assumptions-risks.md) | Documentado; premissas externas não validadas indicadas |
| p. 2: desenho completo da arquitetura alvo | [C4 e componentes](../architecture/building-block-view.md), [Deployment View](../architecture/deployment-view.md), [sequências](../architecture/sequence-diagrams.md), [modelo de dados](../architecture/data-model.md) | Documentado; revisar PNGs versus fontes PlantUML antes da apresentação |
| p. 2: justificativa de arquitetura e tecnologias | [ADRs 001-010](../architecture/adr/README.md), [decisões do MVP](../architecture/mvp-decisions.md) | Documentado, com trade-offs |
| p. 2: linguagem escolhida pelo candidato | C#/.NET 10 backend, React/TypeScript frontend, [ADR-001](../architecture/adr/ADR-001-language-framework.md) | Implementado |
| p. 2: testes | Testes unitários, API, integração e pipeline; [Trilha 08](../implementation/trilha-08-local-acceptance.md) | CI rastreável; E2E e testes de falhas reais pendentes |
| p. 2: README claro e instruções locais | [README raiz](../../README.md), `compose.yaml`, import Keycloak e bootstrap manual | Documentado; executar do zero em máquina local para confirmar reprodutibilidade |
| p. 2: repositório público GitHub | [CashFlow-Architecture](https://github.com/wolwerr/CashFlow-Architecture) | Repositório identificado; revisar visibilidade pública antes de entregar |
| p. 2: documentações no repositório | `docs/requirements`, `docs/domain`, `docs/architecture`, `docs/implementation` | Documentado |
| p. 2: arquitetura de transição, diferencial se necessária | Nenhum sistema legado, origem de migração ou transferência de dados foi informado | **Não aplicável até existir cenário legado**; não inventar transição |
| p. 2: estimativa de infraestrutura/licenças, diferencial | [Custo Azure estimado](../architecture/four-environment-total-cost-plan.md), [ADR-009](../architecture/adr/ADR-009-deployment-cloud.md) | Documentado; preços estimados, sem cloud provisionada |
| p. 2: monitoramento e observabilidade, diferencial | [ADR-008](../architecture/adr/ADR-008-observability.md), [Trilha 07](../implementation/trilha-07-resilience-observability.md), métricas e logging | Desenho documentado e probes locais; stack Prometheus/Loki/Tempo/Grafana não consta dos seis serviços do Compose principal, não afirmar deployment completo |
| p. 2: segurança para integração de serviços, diferencial | [ADR-006](../architecture/adr/ADR-006-identity-and-access.md), Keycloak/JWT, autorização por comerciante, [Trilha 06](../implementation/trilha-06-keycloak-consistency.md) | Implementado com testes automatizados; revisar privilégios e revogação no E2E real |
| p. 3: Transactions não pode cair se Consolidation cair | [RNF-01](non-functional-requirements.md), Outbox + RabbitMQ, [drill de recuperação](../implementation/local-recovery-drills.md) | **Pendente de comprovação**: desligar apenas Consolidation e confirmar novos POST 201 em Transactions; religar e conferir saldo único |
| p. 3: Consolidation em pico de 50 RPS | [RNF-02](non-functional-requirements.md), [teste de carga local](../implementation/local-hardening-load.md) | **Pendente de comprovação**: 50 RPS sustentados durante 300 segundos, com recursos do host documentados |
| p. 3: até 5% de perda de consultas no pico | [RNF-03](non-functional-requirements.md), mesma campanha de carga | **Pendente de comprovação**: no mínimo 95% de consultas válidas enviadas obtêm resposta útil, conforme interpretação aprovada internamente |
| p. 1-2: escalabilidade, reutilização, flexibilidade, resiliência, segurança e integração | [ADRs](../architecture/adr/README.md), contratos de serviço, [non-functional](non-functional-requirements.md) | Decisões documentadas; escala horizontal/HA real só após implantação e ensaios |

## Critérios de aceite para considerar o desafio demonstrado

1. Realizar bootstrap do zero conforme [README](../../README.md), sem atalhos ou valores secretos versionados.
2. Cadastrar comerciantes distintos; executar crédito, débito, consulta e isolamento de dados com acesso autorizado.
3. Com **Consolidation parado** e **Transactions disponível**, criar lançamentos confirmados, então restabelecer e conferir processamento correto, inclusive reentrega sem contabilização duplicada.
4. Executar 50 RPS por cinco minutos no endpoint consolidado e medir **até 5% de consultas válidas sem resposta útil**. Manter o objetivo de projeto de **p95 abaixo de 500 ms** claramente separado do enunciado.
5. Registrar resultado de cada cenário, SHA do código e evidências sanitizadas no [manifesto de homologação](../implementation/trilha-08-local-acceptance.md); não informar que passou sem executar.
6. Revisar referências e PNGs renderizados dos diagramas antes da apresentação. As fontes PlantUML são editáveis; imagens desatualizadas devem ser regeneradas.

O enunciado expressamente permite que parte dos critérios seja apresentada **nas decisões e representações arquiteturais**, e não necessariamente toda na codificação. Por isso esta matriz reconhece documentação válida, mas não transforma proposta de arquitetura em resultado de teste.
