# ADR-001: C# e ASP.NET Core para os serviços financeiros

> **Status:** aceita  
> **Data:** 2026-11-14  
> **Issue:** [#7, Documentar ADRs e trade-offs](https://github.com/wolwerr/CashFlow-Architecture/issues/7)  
> **Escopo da decisão:** linguagem e framework dos serviços de Lançamentos e Consolidado, não dos clientes Web/Mobile.

## Contexto

O desafio requer serviços de **registro de créditos e débitos** e **consulta ao consolidado diário**. O registro deve continuar disponível quando o consolidado estiver indisponível. O consolidado deve suportar **50 requisições por segundo**, com **até 5% de perda de requisições** segundo a interpretação ainda a validar.

A empresa utiliza predominantemente C#; o responsável pela implementação tem experiência maior com Java e Spring Boot. A decisão deve equilibrar aderência ao ambiente da empresa, produtividade e evidências de qualidade. **Nenhuma linguagem, isoladamente, garante resiliência ou a meta de carga.**

## Forças e requisitos afetados

- **RF-01 / RF-02:** registrar créditos e débitos com integridade.
- **RF-03:** consultar o consolidado diário.
- **RNF-01:** registro independente da disponibilidade do consolidado.
- **RNF-02 / RNF-03:** 50 RPS e no máximo 5% de perda nas consultas.
- **RNF-04 / RNF-05 (propostos):** preservar registros confirmados e impedir contagem duplicada.
- **RNF-07 / RNF-08 (propostos):** observabilidade e segurança.
- Manutenibilidade pela equipe, facilidade de execução local e prazo do desafio.

## Alternativas avaliadas

| Critério | C# / ASP.NET Core | Java / Spring Boot |
| --- | --- | --- |
| Alinhamento com a stack da empresa | **Alto** | Menor |
| Familiaridade do implementador | Adaptação necessária | **Alta** |
| API HTTP e validação | ASP.NET Core, middleware, DI | Spring Web, Validation, DI |
| Worker de publicação Outbox | `BackgroundService` / `IHostedService` | `@Scheduled` ou serviço agendado |
| Persistência transacional | EF Core ou Dapper/ADO.NET, a definir na ADR-003 | Spring Data / JDBC, a definir na ADR-003 |
| Mensageria | Cliente RabbitMQ ou biblioteca compatível, a definir na ADR-004 | Cliente RabbitMQ / Spring AMQP, a definir na ADR-004 |
| Testes | xUnit/NUnit, testes de integração e carga | JUnit e testes de integração e carga |
| Containerização | Linux e Docker | Linux e Docker |

Ambas as alternativas são capazes de atender aos requisitos. Não há evidência neste projeto para afirmar superioridade de performance ou custo de uma delas sem benchmark reproduzível.

### Versões avaliadas em 14/11/2026

- **.NET 10:** LTS, suporte oficial previsto até **14/11/2028**. **Preferido.**
- **.NET 11:** STS, lançado em **10/11/2026**, suporte oficial também previsto até **14/11/2028**; não há benefício demonstrado que justifique adotar a versão recém-lançada neste desafio.
- **.NET 8 / .NET 9:** suporte encerrado em **10/11/2026**; não selecionar para novo MVP.

**Referência:** [Política oficial de suporte do .NET](https://dotnet.microsoft.com/pt-br/platform/support/policy), consultada em 14/11/2026.

## Decisão aceita

Adotar **C# com .NET 10 LTS e ASP.NET Core** nos dois serviços financeiros do MVP:

1. **Lançamentos:** API HTTP com regras de validação e persistência transacional. O publicador Outbox utiliza um **worker interno** (possivelmente `BackgroundService`), que publica eventos sem bloquear a confirmação do lançamento.
2. **Consolidado:** API HTTP para consulta diária e consumidor assíncrono, que atualiza a projeção e registra `event_id` processado na **mesma transação**, impedindo contabilização duplicada.

### Diretrizes iniciais de implementação

- Utilizar injeção de dependência e configuração nativas de ASP.NET Core.
- Separar regras financeiras de endpoints, infraestrutura e integrações, sem impor excesso de projetos/camadas.
- Utilizar `decimal` para valores monetários, conforme as regras de moeda e arredondamento que ainda serão refinadas.
- Usar operações assíncronas para I/O quando apropriado; não confundir `async/await` com processamento durável de mensagens.
- Garantir que gravação de lançamento + Outbox ocorra na mesma transação.
- Tratar falha de publicação, confirmação do broker e reprocessamento explicitamente.
- Validar tokens JWT e autorização por comerciante conforme ADR-006 e Issue #9.
- Implementar testes unitários, integração, idempotência, falha do consolidado e carga de 50 RPS.
- Considerar xUnit como candidato de testes e OpenTelemetry para instrumentação; escolhas de bibliotecas serão confirmadas na implementação e ADR-008.

**Não decidido nesta ADR:** EF Core vs. Dapper, PostgreSQL vs. outro banco, RabbitMQ vs. Kafka, provedor IAM, frontend Web/Mobile, API Gateway e cloud. Essas escolhas pertencem às ADRs correspondentes.

## Consequências positivas

- Maior aderência ao ecossistema predominante da empresa avaliadora.
- Uso de plataforma LTS suportada e adequada a containers Linux.
- Implementação dos serviços HTTP e workers na mesma plataforma.
- Familiaridade potencial da equipe com ferramentas, operações e manutenção do backend.

## Consequências negativas, riscos e mitigação

| Risco / trade-off | Impacto | Mitigação |
| --- | --- | --- |
| Menor experiência prática com .NET que com Spring Boot | Implementação e depuração podem demorar mais | MVP pequeno, exemplos oficiais, testes desde o início e revisão de código |
| Abstrações excessivas para demonstrar arquitetura | Complexidade sem benefício comprovado | Priorizar duas APIs, Outbox e consumidor; justificar cada componente |
| Worker Outbox falhar ou publicar duplicado | Acúmulo de eventos ou reentrega | Publisher confirms, tentativas, monitoramento e consumidor idempotente; detalhar ADR-005 e Issue #8 |
| Meta de 50 RPS não ser alcançada | Falha no requisito não funcional | Medir latência, erros e recursos sob carga; ajustar pool, índices e instâncias a partir de evidências |
| Atualizações de versão e dependências | Manutenção contínua | Fixar .NET 10 LTS, aplicar patches e automatizar verificações de vulnerabilidade |
| Custo extra da arquitetura completa | Entrega do desafio pode ficar inviável | Separar arquitetura-alvo (Web/Mobile, Gateway, IAM) do escopo demonstrável do MVP |

## Impactos operacionais

- Disponibilizar SDK do .NET 10, Docker e instruções de execução local no README.
- Expor health checks e métricas das APIs; tratar readiness do consolidado e do worker separadamente quando necessário.
- Exportar logs estruturados e traces sem dados financeiros sensíveis ou segredos.
- Observar duração dos workers, backlog Outbox, erros de publicação, latência de consulta e falhas de autenticação.
- Manter a disponibilidade do registro independente do **serviço de consolidado**; não confundir com independência de banco de lançamentos, Gateway ou IAM em todas as condições.

## Critérios de validação

- `dotnet build` e `dotnet test` executam localmente e em CI.
- Um crédito e um débito válidos são persistidos com seus eventos Outbox.
- O lançamento permanece disponível durante falha do consolidado.
- Reentregas não geram dupla contabilização.
- Teste de consulta com **50 RPS** demonstra sucesso de ao menos **95% das requisições válidas**, conforme interpretação documentada do requisito.
- README permite reproduzir o cenário sem serviços pagos.

## Quando revisitar esta decisão

- Se o time responsável pela manutenção não possuir competência nem suporte para .NET.
- Se a implementação do MVP mostrar risco concreto de prazo frente à opção Java.
- Se políticas corporativas exigirem outra versão suportada.
- Se os testes demonstrarem limitações que não possam ser resolvidas com ajuste de código e infraestrutura.

## Referências internas

- [Requisitos funcionais](../../requirements/functional-requirements.md)
- [Requisitos não funcionais](../../requirements/non-functional-requirements.md)
- [Building Block View](../building-block-view.md)
- [Registro das ADRs](README.md)
