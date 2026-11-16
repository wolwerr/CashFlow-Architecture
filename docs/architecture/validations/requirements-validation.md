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
| **RF-01** Registro de crédito | Exigido, essencial | [ADR-002](../adr/ADR-002-architecture-style-clean-architecture.md), [ADR-005](../adr/ADR-005-outbox-consistency-idempotency.md), C4 Transactions e `Transaction` | **Conforme no desenho** | Crédito válido persiste com Outbox no mesmo commit, resposta coerente e sem aguardar Consolidation |
| **RF-02** Registro de débito | Exigido, essencial | Mesmas decisões de Transactions e modelo `Transaction` | **Conforme no desenho** | Débito válido persiste com Outbox e altera a projeção segundo regra de negócio definida |
| **RF-03** Consultar consolidado diário | Exigido, essencial | Consolidation e `DailyBalance` | **Decisão funcional fechada; implementação pendente**: totais diários persistidos e acumulado calculado na consulta por comerciante/competência, inclusive dias vazios e retroativos | Testar soma histórica, concorrência e 50 RPS |
| **RF-04** Validar lançamento | Proposto, aprovado para MVP | Domain/Application Transactions | **Regras decididas**: BRL positivo duas casas, competência passada/atual, data atual em America/Sao_Paulo, Idempotency-Key obrigatório | Testar 400/409, valor inválido, futura e repetição |
| **RF-05** Consultar histórico | Proposto, aprovado para MVP | Transactions e `Transaction` | **Escopo fechado**: filtro de período, paginação e autorização por comerciante | Testar filtro, paginação e isolamento |
| **RF-06** Estorno | Proposto, **fora do MVP** | Próxima versão | Sem endpoint de estorno/edição/exclusão para fato confirmado no MVP | Revisitar para versão futura |
| **RF-07** Auditoria financeira | Proposto, aprovado para MVP | `AuditEvent` em Transactions | Registros confirmados com ator, comerciante, instante UTC, operação e correlação; retenção inicial 365 dias | Testar gravação atômica e retenção |

Os requisitos RF-01 a RF-03 vêm de [Requisitos Funcionais](../../requirements/functional-requirements.md); RF-04 a RF-07 estão explicitamente indicados como **propostas** no mesmo arquivo.

## Matriz de rastreabilidade não funcional

| ID | Origem segundo requisitos | Decisão / mecanismo documentado | Classificação | Evidência exigida |
| --- | --- | --- | --- | --- |
| **RNF-01** | **Exigido**: falha de Consolidation não impede Transactions | [ADR-002](../adr/ADR-002-architecture-style-clean-architecture.md), [ADR-005](../adr/ADR-005-outbox-consistency-idempotency.md), serviços e bancos independentes | **Conforme no desenho; teste pendente** | Parar apenas Consolidation, registrar e conferir persistência e recuperação |
| **RNF-02** | **Exigido**: 50 consultas/s no pico | Consolidation com deploy independente, [ADR-009](../adr/ADR-009-deployment-cloud.md), cenário de carga no documento de RNFs | **Pendente de teste** | Carga real de 50 RPS, registrar ambiente, massa, tempo, respostas e latências |
| **RNF-03** | Exigido, interpretação interna aprovada | 50 RPS por 5 min após aquecimento, até 5% de consultas válidas com erro/timeout/sem resposta útil | **Critério decidido, teste pendente** | Gerador de carga e relatório de resposta útil |
| **RNF-04** | **Proposto**: persistência de lançamentos confirmados | PostgreSQL, transação lançamento + Outbox, [ADR-003](../adr/ADR-003-persistence.md), ADR-005 | **Conforme no desenho; teste pendente** | Persistência em falha e recuperação; 0 perda de registros confirmados no cenário |
| **RNF-05** | **Proposto**: processamento sem duplicidade | `ProcessedEvent` + `DailyBalance` no mesmo commit, chave única `event_id`, ADR-005 | **Conforme no desenho; teste pendente** | Reentrega, crash entre commit/ACK e concorrência sem contabilização duplicada |
| **RNF-06** | Proposto, **SLO aprovado no MVP** | p95 < 500 ms em 50 RPS | **Teste pendente** | Relatório de percentis e ambiente |
| **RNF-07** | **Proposto**: diagnóstico de falhas | [ADR-008](../adr/ADR-008-observability.md), OTel/Prometheus/Loki/Tempo/Grafana, ADR-005 | **Conforme no desenho; implementação pendente** | Logs, métricas, traces, atraso de Outbox e consolidado; sem bloquear Transactions |
| **RNF-08** | Recomendação arquitetural; política interna aprovada | Keycloak + JWT, banco lógico de autorização com cache curto/invalidação e ADMIN/OPERATOR por comerciante | **Decisão registrada, implementação pendente** | Testes 401/403, troca/revogação de vínculo e isolamento |
| **RNF-09** | **Recomendação arquitetural**: escalabilidade | [ADR-002](../adr/ADR-002-architecture-style-clean-architecture.md), [ADR-009](../adr/ADR-009-deployment-cloud.md), réplicas/HPA previstos | **Parcial; capacidade pendente** | Escalonar consulta sem acoplar deploys; medir antes/depois e capacidade real |

**RNF-02 e RNF-03:** o documento [Requisitos Não Funcionais](../../requirements/non-functional-requirements.md) propõe **50 RPS por 5 minutos após aquecimento**, **15.000 consultas planejadas** e **≥95% de respostas úteis**. São parâmetros para o ensaio, ainda sujeitos a confirmação da definição de *perda* e da janela. O denominador deve ser as consultas **válidas realmente enviadas**, não apenas as planejadas.

Outros SLOs, como p95 inferior a 500 ms, 95% das atualizações em 30 s, continuidade durante 2 minutos de falha e backlog recuperado em 2 minutos, são **metas propostas**, não promessas de produção.

## Inconsistências e decisões abertas por prioridade

| Prioridade | Tema | Fontes e impacto | Ação de validação |
| --- | --- | --- | --- |
| **Decidido em 15/11/2026** | **RN-02**: exibir movimento líquido diário e saldo acumulado | RF-03, UC-03 e `DailyBalance` devem refletir ambos; saldo inicial de R$ 0,00 decidido; retroatividade decidida; implementação e testes de recomposição pendentes | Registrar regra e conferir documentos/contratos |
| **Decidido em 15/11/2026** | **RN-03**: saldo inicial de R$ 0,00 por comerciante | O acumulado parte de zero, sem saldo manual de abertura no MVP | Refletir em cálculos e testes |
| **Decidido em 15/11/2026** | **RN-09**: dia sem movimentação | Totais e líquido zero, saldo acumulado anterior mantido; antes da primeira transação, saldo zero | Cobrir com testes de consulta em dia vazio |
| **Decidido em 15/11/2026** | **RN-04**: competência informada pelo comerciante, criação UTC | `reference_date` define dia financeiro e `created_at` registra instante UTC | Competências passadas/atuais aceitas, futuras rejeitadas; recompor acumulados afetados. Fuso de referência/exibição pendente |
| **Decidido em 15/11/2026** | **RN-07**: BRL com duas casas, `decimal` no .NET, `numeric(19,2)` no PostgreSQL | Montantes de lançamento > 0; rejeitar precisão excessiva; `CREDIT`/`DEBIT` determina o efeito; validar em contratos/constraints/testes |
| **Decidido em 15/11/2026** | **RNF-03**: perda e janela | Até 5% das consultas válidas enviadas sem resposta útil em 50 RPS/5 min | Executar teste e verificar interpretação externa separadamente |
| **Decidido em 15/11/2026** | **RN-06**: multi-comerciante e vínculo | Banco lógico próprio; ADMIN/OPERATOR; cache curto e invalidação | Projetar interface, TTL e revogação; testar isolamento |
| **Decidido em 15/11/2026** | **RN-08**: frescor | 95% em até 30 segundos em operação saudável | Instrumentar e medir |
| **Decidido em 15/11/2026** | **RN-05**: estorno | Fora do MVP; próxima versão | Não implementar estorno agora |
| **Decidido em 15/11/2026** | RF-05, RF-07 e acesso | Histórico paginado e auditoria básica no MVP; administração de vínculos precisa especificação | Implementar/testar |
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
- [ ] Concluir fuso de referência para a data atual, escopo, autorização por comerciante, estratégia de recomposição e interpretação de perda de requisições; verificar RN-07 em testes.
- [ ] Implementar e executar cenários automatizados; registrar relatórios.
- [ ] Aceite funcional e não funcional após revisão dos resultados.

**Resultado:** etapa de **revisão documental** executada, com lacunas registradas. Não há evidência suficiente para classificar o CashFlow como validado em execução.

## Próxima etapa

**Validação 02, diagramas de sequência**: comparar os fluxos de registro, publicação confiável Outbox, consumer ACK após commit, indisponibilidade de Consolidation e consultas com as ADR-002/004/005/006. Corrigir discrepâncias de setas, portas, responsabilidade e semântica da resposta sem fixar regras financeiras ainda abertas.

## Fontes consultadas

- [Requisitos Funcionais](../../requirements/functional-requirements.md)
- [Requisitos Não Funcionais](../../requirements/non-functional-requirements.md)
- [Priorização, premissas e riscos](../../requirements/priorities-assumptions-risks.md)
- [Casos de Uso](../use-cases.md)
- [Modelo de Dados](../data-model.md)
- [Building Block View](../building-block-view.md)
- [ADR-002](../adr/ADR-002-architecture-style-clean-architecture.md)
- [ADR-003](../adr/ADR-003-persistence.md)
- [ADR-005](../adr/ADR-005-outbox-consistency-idempotency.md)
- [ADR-006](../adr/ADR-006-identity-and-access.md)
- [ADR-008](../adr/ADR-008-observability.md)
- [ADR-009](../adr/ADR-009-deployment-cloud.md)

## Revalidação das 24 decisões aprovadas em 15/11/2026

As conclusões e pendências históricas anteriores devem ser lidas com o complemento de [Decisões consolidadas do MVP](../mvp-decisions.md). **Escopo e políticas foram aprovados, mas nenhuma prova em execução foi produzida apenas por esta atualização.** Histórico paginado, auditoria básica, multi-comerciante, Authorization CashFlow com banco próprio, REST v1, Idempotency-Key, 201 pós-commit, eventos versionados, retry/backoff/DLQ, SLOs de 50 RPS/5 min/até 5%, p95 < 500 ms e frescor de 95% em 30 s são a nova referência. O acumulado é calculado na leitura sobre totais diários persistidos. Restam contratos, código, migrations, testes, configuração de infraestrutura e PNGs atualizados.
