# Validação 01 | Rastreabilidade de requisitos e arquitetura

> **Status:** análise documental concluída; validação de implementação e aceite de negócio pendentes  
> **Data:** 2026-11-15  
> **Escopo:** requisitos funcionais, requisitos não funcionais, riscos, ADRs e diagramas/modelo de dados do CashFlow  
> **Natureza:** revisão de documentação existente, **não** teste executado e **não** aceite formal da empresa

## Objetivo e critérios de classificação

Conferir se as exigências explicitadas nos documentos de requisitos têm uma solução arquitetural identificável e um cenário de verificação, sem confundir **exigências do desafio**, **propostas internas** ou **decisões de negócio ainda abertas**.

- **Conforme no desenho:** há componente e decisão arquitetural alinhados ao requisito, mas ainda não se comprovou execução.
- **Parcial:** existe solução arquitetural, mas falta definir a regra de negócio, um contrato ou um critério de aceitação.
- **Pendente de teste:** critério está documentado, mas falta execução e relatório.
- **Pendente de decisão:** documento de origem ainda apresenta alternativas e perguntas não respondidas.

Não transformar propostas em requisitos obrigatórios nem caracterizar o percentual de cobertura ou SLOs propostos como exigências do desafio sem fonte.

## Matriz de rastreabilidade funcional

| ID | Origem e prioridade segundo requisitos | Evidência de arquitetura / domínio | Situação documental | Validação de execução a produzir |
| --- | --- | --- | --- | --- |
| **RF-01** Registro de crédito | Exigido, essencial | [ADR-002](adr/ADR-002-architecture-style-clean-architecture.md), [ADR-005](adr/ADR-005-outbox-consistency-idempotency.md), C4 Transactions e `Transaction` | **Conforme no desenho** | Crédito válido persiste com Outbox no mesmo commit, resposta coerente e sem aguardar Consolidation |
| **RF-02** Registro de débito | Exigido, essencial | Mesmas decisões de Transactions e modelo `Transaction` | **Conforme no desenho** | Débito válido persiste com Outbox e altera a projeção segundo regra de negócio definida |
| **RF-03** Consultar consolidado diário | Exigido, essencial | C4 Consolidation, `DailyBalance`, ADR-002 e ADR-005 | **Parcial**: movimento líquido e saldo acumulado decididos; saldo inicial e dia sem lançamentos indefinidos | Consulta por data e comerciante com valores esperados derivados da regra aprovada |
| **RF-04** Validar dados de lançamento | **Proposto** como essencial ao MVP | Domain e Application de Transactions, endpoints e critérios da HU-04 | **Parcial**: campos, limites, valor e data ainda a definir | Rejeições não persistem lançamentos confirmados |
| **RF-05** Consultar histórico | **Proposto**, desejável | `Transaction` como registro de referência; fluxo opcional nos casos de uso | **Pendente de escopo** | Se incluído, consulta paginada/filtrada e autorização por comerciante |
| **RF-06** Corrigir por estorno | **Proposto**, futuro/a validar | `Transaction`, `AuditEvent` proposto e referência potencial `reversal_of_id` no modelo de dados | **Pendente de escopo e regra** | Se incluído, efeito contábil correto, preservação de histórico e idempotência |
| **RF-07** Manter auditoria financeira | **Proposto**, importante à integridade | `AuditEvent`, [modelo de dados](data-model.md), ADR-005 e distinção de logs da ADR-008 | **Parcial**: escopo de auditoria, retenção e acesso indefinidos | Auditoria transacional quando aplicável; trilha de autor/operação/correlação sem credenciais |

Os requisitos RF-01 a RF-03 vêm de [Requisitos Funcionais](../requirements/functional-requirements.md); RF-04 a RF-07 estão explicitamente indicados como **propostas** no mesmo arquivo.

## Matriz de rastreabilidade não funcional

| ID | Origem segundo requisitos | Decisão / mecanismo documentado | Classificação | Evidência exigida |
| --- | --- | --- | --- | --- |
| **RNF-01** | **Exigido**: falha de Consolidation não impede Transactions | [ADR-002](adr/ADR-002-architecture-style-clean-architecture.md), [ADR-005](adr/ADR-005-outbox-consistency-idempotency.md), serviços e bancos independentes | **Conforme no desenho; teste pendente** | Parar apenas Consolidation, registrar e conferir persistência e recuperação |
| **RNF-02** | **Exigido**: 50 consultas/s no pico | Consolidation com deploy independente, [ADR-009](adr/ADR-009-deployment-cloud.md), cenário de carga no documento de RNFs | **Pendente de teste** | Carga real de 50 RPS, registrar ambiente, massa, tempo, respostas e latências |
| **RNF-03** | **Exigido, interpretação pendente**: no máximo 5% de perda | Fórmula provisória e cenário de 5 minutos nos RNFs | **Pendente de decisão e teste** | Confirmar denominador e definição de perda; medir erros e timeouts em consultas válidas |
| **RNF-04** | **Proposto**: persistência de lançamentos confirmados | PostgreSQL, transação lançamento + Outbox, [ADR-003](adr/ADR-003-persistence.md), ADR-005 | **Conforme no desenho; teste pendente** | Persistência em falha e recuperação; 0 perda de registros confirmados no cenário |
| **RNF-05** | **Proposto**: processamento sem duplicidade | `ProcessedEvent` + `DailyBalance` no mesmo commit, chave única `event_id`, ADR-005 | **Conforme no desenho; teste pendente** | Reentrega, crash entre commit/ACK e concorrência sem contabilização duplicada |
| **RNF-06** | **Proposto**: latência previsível | Consulta em projeção própria, métricas e teste de carga | **Pendente de teste / SLO proposto** | Medir p95; 500 ms em 50 RPS é proposta, não exigência expressa |
| **RNF-07** | **Proposto**: diagnóstico de falhas | [ADR-008](adr/ADR-008-observability.md), OTel/Prometheus/Loki/Tempo/Grafana, ADR-005 | **Conforme no desenho; implementação pendente** | Logs, métricas, traces, atraso de Outbox e consolidado; sem bloquear Transactions |
| **RNF-08** | **Recomendação arquitetural**: segurança e dados sensíveis | [ADR-006](adr/ADR-006-identity-and-access.md), [ADR-007](adr/ADR-007-clients-api-gateway.md), JWT local, `merchant_id`, Key Vault | **Parcial**: vínculo usuário/comerciante e políticas pendentes | Testes 401/403, isolamento por comerciante, proteção de segredos, controles TLS |
| **RNF-09** | **Recomendação arquitetural**: escalabilidade | [ADR-002](adr/ADR-002-architecture-style-clean-architecture.md), [ADR-009](adr/ADR-009-deployment-cloud.md), réplicas/HPA previstos | **Parcial; capacidade pendente** | Escalonar consulta sem acoplar deploys; medir antes/depois e capacidade real |

**RNF-02 e RNF-03:** o documento [Requisitos Não Funcionais](../requirements/non-functional-requirements.md) propõe **50 RPS por 5 minutos após aquecimento**, **15.000 consultas planejadas** e **≥95% de respostas úteis**. São parâmetros para o ensaio, ainda sujeitos a confirmação da definição de *perda* e da janela. O denominador deve ser as consultas **válidas realmente enviadas**, não apenas as planejadas.

Outros SLOs, como p95 inferior a 500 ms, 95% das atualizações em 30 s, continuidade durante 2 minutos de falha e backlog recuperado em 2 minutos, são **metas propostas**, não promessas de produção.

## Inconsistências e decisões abertas por prioridade

| Prioridade | Tema | Fontes e impacto | Ação de validação |
| --- | --- | --- | --- |
| **Decidido em 15/11/2026** | **RN-02**: exibir movimento líquido diário e saldo acumulado | RF-03, UC-03 e `DailyBalance` devem refletir ambos; saldo inicial e retroatividade seguem abertos | Registrar regra e conferir documentos/contratos |
| **Alta** | **RN-03 e RN-09**: saldo inicial e dia sem movimentação | RF-03, cálculo, consultas sem eventos | Definir semântica da resposta e efeito do saldo anterior |
| **Alta** | **RN-04**: data de referência/fuso e competência | RF-01 a RF-03 e agregação por dia | Definir fuso financeiro, entrada retroativa e fronteira diária |
| **Alta** | **RN-07**: moeda, precisão e arredondamento | ADR-005 recomenda `decimal`/`numeric(p,s)`, mas escala de negócio continua aberta | Definir moeda, escala e convenção |
| **Alta** | **RNF-03**: significado dos 5% de perda e janela | Sem definição não há aceite inequívoco de desempenho | Confirmar erro/timeout/resposta útil, carga e janela |
| **Alta** | **RN-06 / Issue #9**: vínculo sujeito-comerciante | Segurança multi-comerciante proposta; Keycloak não autoriza `merchant_id` automaticamente | Definir fonte e mecanismo de autorização; testar isolamento |
| **Média** | **RN-08**: atraso aceitável de consolidação | Consistência eventual decidida, mas experiência e SLO de frescor ainda propostos | Definir atraso tolerável e comunicação ao usuário |
| **Média** | **RN-05**: edição, exclusão e estorno | RF-06 e integridade do livro financeiro | Decidir se pertence ao MVP; se sim, definir data de efeito |
| **Média** | Escopo de RF-05, RF-07 e administração de acessos | Casos de uso e auditoria são propostos, sem escopo final fechado | Delimitar MVP versus evolução futura |
| **Média** | Recuperação, retenção, DLQ e reconciliação | ADR-005, Issue #8 e deployment cloud | Especificar operação e provas de retomada sem supor HA validada |

### Pontos já coerentes na documentação

- **Separação dos serviços:** Transactions não chama Consolidation sincronicamente para confirmar crédito/débito.
- **Atomicidade local:** `Transaction` e `OutboxEvent` confirmados na mesma transação de Transactions.
- **Processamento seguro:** projeção `DailyBalance` e controle `ProcessedEvent` atualizados atomicamente; ACK somente depois do commit.
- **Autenticação e autorização:** Keycloak emite tokens, APIs validam JWT com JWKS local/cache e verificam `merchant_id` segundo regra CashFlow; NGINX é proxy e não substitui essas verificações.
- **Escalabilidade prevista:** C4 e deployment separam serviços e bancos lógicos; AKS PROD com três nós não comprova automaticamente 50 RPS, quorum seguro ou HA.
- **Custos versus qualidade:** orçamento-base de US$ 1.305,02/mês da ADR-009 é financeiro, não evidência de teste, disponibilidade ou capacidade.

## Critérios de conclusão da etapa 01

- [x] Todos os **RF-01 a RF-07** localizados e classificados segundo sua origem.
- [x] Todos os **RNF-01 a RNF-09** relacionados a mecanismos arquiteturais e provas previstas.
- [x] Identificadas as regras financeiras que impedem fixar o resultado esperado do consolidado.
- [x] Confirmado que os critérios de 50 RPS e 5% ainda necessitam execução e interpretação oficial.
- [x] Confirmada a diferenciação entre decisão arquitetural, critério proposto e teste executado.
- [ ] Obter decisão/premissas explícitas para saldo, data, moeda, escopo e limiar de perda.
- [ ] Implementar e executar cenários automatizados; registrar relatórios.
- [ ] Aceite funcional e não funcional após revisão dos resultados.

**Resultado:** etapa de **revisão documental** executada, com lacunas registradas. Não há evidência suficiente para classificar o CashFlow como validado em execução.

## Próxima etapa

**Validação 02, diagramas de sequência**: comparar os fluxos de registro, publicação confiável Outbox, consumer ACK após commit, indisponibilidade de Consolidation e consultas com as ADR-002/004/005/006. Corrigir discrepâncias de setas, portas, responsabilidade e semântica da resposta sem fixar regras financeiras ainda abertas.

## Fontes consultadas

- [Requisitos Funcionais](../requirements/functional-requirements.md)
- [Requisitos Não Funcionais](../requirements/non-functional-requirements.md)
- [Priorização, premissas e riscos](../requirements/priorities-assumptions-risks.md)
- [Casos de Uso](use-cases.md)
- [Modelo de Dados](data-model.md)
- [Building Block View](building-block-view.md)
- [ADR-002](adr/ADR-002-architecture-style-clean-architecture.md)
- [ADR-003](adr/ADR-003-persistence.md)
- [ADR-005](adr/ADR-005-outbox-consistency-idempotency.md)
- [ADR-006](adr/ADR-006-identity-and-access.md)
- [ADR-008](adr/ADR-008-observability.md)
- [ADR-009](adr/ADR-009-deployment-cloud.md)
