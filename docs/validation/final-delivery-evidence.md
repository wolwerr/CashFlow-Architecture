# CashFlow | Auditoria final da entrega e evidências

> **Base:** documento de 3 páginas `Desafio - Arq. Soluções`, revisado em 17/11/2026.  
> **Escopo executado:** aplicação em Docker Compose LOCAL. A infraestrutura Azure é desenho e estimativa, **não foi provisionada**.  
> **Repositório:** [CashFlow-Architecture](https://github.com/wolwerr/CashFlow-Architecture), visibilidade **public** verificada pelo metadado do GitHub na revisão.  
> **Qualidade:** [GitHub Actions #577](https://github.com/wolwerr/CashFlow-Architecture/actions/runs/44163593607), os quatro jobs Backend, Frontend, Validation e Sonar concluíram com sucesso. Essa execução pertence ao código da PR #82 e não substitui uma verificação da `main` após alterações documentais.

## Conferência dos requisitos do enunciado

| Exigência do PDF | Evidência verificável / artefato | Situação |
| --- | --- | --- |
| Controle de lançamentos (débitos e créditos) | [RF-01/RF-02](../requirements/functional-requirements.md), Transactions API, [ensaio de escrita](local-write-load-test.md) | Implementado, teste LOCAL informado PASS |
| Saldo diário consolidado | [RF-03](../requirements/functional-requirements.md), Consolidation API, [projeção](local-write-load-test.md) | Implementado, teste LOCAL informado PASS |
| Domínios funcionais e capacidades | [Business capabilities e context map](../domain/business-capabilities-and-context-map.md) | Documentado |
| Requisitos funcionais e não funcionais refinados | [RF](../requirements/functional-requirements.md), [RNF](../requirements/non-functional-requirements.md) | Documentado |
| Arquitetura alvo completa | [Building Block View/C4](../architecture/building-block-view.md), [Deployment View](../architecture/deployment-view.md), [sequências](../architecture/sequence-diagrams.md), [modelo de dados](../architecture/data-model.md) | Fontes e PNGs versionados; correspondência visual entre imagens e fontes **não auditada nesta revisão** |
| Decisões e trade-offs | [ADRs](../architecture/adr/README.md), escolhas .NET, React, Keycloak, PostgreSQL, RabbitMQ/Outbox e Azure futura | Documentado |
| Linguagem à escolha do candidato | .NET 10, React/TypeScript | Implementado |
| Testes | [GitHub Actions #577](https://github.com/wolwerr/CashFlow-Architecture/actions/runs/44163593607); [roteiro de validação](../requirements/challenge-compliance-matrix.md) | CI PASS + cenários locais declarados |
| README com execução local | [README](../../README.md), [`.env.example`](../../.env.example), [Compose](../../compose.yaml) | Publicado, utilizado no ambiente local; **clone limpo independente não foi executado nesta revisão** |
| Repositório público e documentação versionada | [GitHub](https://github.com/wolwerr/CashFlow-Architecture), `docs/`, `README.md` | Verificado público |
| Resiliência: registro não cai quando Consolidation cai | [Ensaio de recuperação](local-integrated-recovery-test.md), [drill](local-recovery-drill-test.md) | PASS LOCAL informado com POST 201 durante indisponibilidade e projeção após recuperação |
| Consolidation: 50 requisições por segundo | [Medição local de carga](local-load-test.md) | **50,00 RPS por 300,001 s**, 15.000 respostas HTTP 200, p95 27,91 ms (resultado histórico informado em 17/11/2026) |
| No máximo 5% de perda | [Mesmo ensaio de carga](local-load-test.md) | **0% de falhas** observadas no ensaio local, sem extrapolação para produção |
| Diferencial: transição | Não foi apresentado cenário de legado no PDF | Não aplicável; não inventar migração |
| Diferencial: estimativa de custos | [Plano Azure de 4 grupos](../architecture/four-environment-total-cost-plan.md) | Estimativa publicada; sem cobrança real |
| Diferencial: observabilidade | [ADR-008](../architecture/adr/ADR-008-observability.md), [runbook](local-observability-stack.md) | Stack implementada e validação funcional relatada |
| Diferencial: segurança da integração | [ADR-006](../architecture/adr/ADR-006-identity-and-access.md), [teste de isolamento](local-merchant-isolation-test.md), testes JWT | JWT/OIDC e autorização por comerciante implementados; controles para produção são evoluções futuras |

O PDF explicita que nem todas as premissas de arquitetura precisam estar codificadas: decisões e representações arquiteturais são válidas. Por isso o escopo Azure, transição e recomendações de HA são documentados como **desenho**, não implantação real.

## Evidências de cobertura

A [seção 9 do README](../../README.md#9-testes-automatizados-e-evidências-do-desafio) descreve os limites por camada do backend e os percentuais históricos do frontend. A cobertura atual é calculada **a cada execução do GitHub Actions** por [`.github/scripts/ci_coverage_summary.py`](../../.github/scripts/ci_coverage_summary.py) usando os XMLs OpenCover e o LCOV gerados pelos testes.

Os job summaries **Backend .NET Coverage Report** e **Frontend Coverage Report** exibem os números observados nessa execução, além de links para os artifacts com os arquivos originais. Os relatórios do backend podem ter sobreposição e não são somados para declarar uma cobertura global. Os percentuais históricos do README não substituem os resultados medidos na CI mais recente.

## Evidências de execução e rastreabilidade

**CI auditável, executada no GitHub:** [run #577](https://github.com/wolwerr/CashFlow-Architecture/actions/runs/44163593607) aprovou os quatro jobs; os logs e artefatos dessa execução são a fonte dos resultados automatizados.

**Aceite LOCAL declarado pelo operador:** saída de `python .github/scripts/local_mvp_acceptance.py --manifest local-acceptance.json` informada em 17/11/2026 20:01:25 UTC:
- `PASS: 14`, `FAIL: 0`, `BLOCKED: 0`, `NOT_RUN: 0`.
- `commit: NOT_RECORDED`, `ready_for_mvp_acceptance: false`, `outcome: NOT_ACCEPTED`.
- O manifesto e os arquivos locais referenciados permanecem fora do Git. Essa saída **não é um artefato de CI** e não autentica sozinha as execuções históricas.

**Observabilidade, inspeção manual relatada em 17/11/2026:** o operador forneceu capturas da coleta Prometheus (métricas HTTP de ambos os serviços), logs no Loki, trace HTTP 201 com spans de publicação/consumo RabbitMQ no Tempo, e `up=1` do Collector após sua restauração. Com o Collector parado, informou que um depósito foi aceito, registrado no histórico e refletido no saldo consolidado. Esses resultados são **declaração do operador apoiada nas capturas da sessão**, sem anexar tokens/segredos. Ver [runbook](local-observability-stack.md).

**Rastreabilidade do SHA:** o código da [PR #82](https://github.com/wolwerr/CashFlow-Architecture/pull/82) foi integrado pelo merge `71237fa385fd9e0a5c6d9267ecf86997e71aec98`. Não atribuir esse SHA retroativamente aos 14 ensaios locais, pois o commit não foi registrado na execução de todos eles.

## Limitações explicitadas para a banca

1. Não foi executado **clone completamente limpo por um segundo operador** nesta auditoria. O README contém bootstrap do Keycloak, credenciais obrigatórias e execução por Compose; a validação de instalação completa ocorreu em ambiente de desenvolvimento existente.
2. A correspondência **PNG versus fontes PlantUML** requer revisão visual adicional. Links para imagens e fontes existem no repositório; possíveis notas de atualização em páginas antigas não devem ser tratadas como prova de que foram regeradas.
3. Os números de 50 RPS, recuperação e projeção são **resultados locais históricos** com medições informadas pelo executor, sem SHA contemporâneo nem publicação do log bruto. Não implicam testes de produção, alta disponibilidade geográfica ou SLA real.
4. Não há Azure provisionada. Preços e disponibilidade são estimativas e propostas; não houve teste de custo, HA ou escalabilidade horizontal em AKS.
5. O GitHub Actions valida build, suites de teste e regras do Sonar, mas **não executa os cenários E2E destrutivos** de queda de serviços ou lançamento financeiro do operador.
6. Em observabilidade, métricas específicas de workers e alertas automáticos avançados permanecem evolução futura; isso não impede a demonstração dos sinais Prometheus/Loki/Tempo do MVP.

## Apresentação

Use [roteiro de defesa e demonstração](demo-presentation.md). Compartilhe o **link do repositório**, o [README](../../README.md), esta página de evidências e o [run #577](https://github.com/wolwerr/CashFlow-Architecture/actions/runs/44163593607), sem publicar `.env`, JWT, logs contendo dados sensíveis ou o manifesto de comerciantes.

## Conclusão da auditoria

**Critérios técnicos obrigatórios cobertos por código/documentação e testes disponíveis.** A entrega está **apta a compartilhamento para avaliação de arquitetura**, ressalvadas as limitações de proveniência das evidências locais e verificações independentes não realizadas. Não declarar aceite formal `ACCEPTED` enquanto o manifesto registrar `NOT_ACCEPTED`.
