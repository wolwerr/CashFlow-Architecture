# ADR-010: Estratégia de testes, cobertura e validação de requisitos

> **Status:** proposta para discussão  
> **Data:** 2026-11-15  
> **Issue:** [#7, Documentar ADRs e trade-offs](https://github.com/wolwerr/CashFlow-Architecture/issues/7)  
> **Relacionadas:** [ADR-001](ADR-001-language-framework.md), [ADR-002](ADR-002-architecture-style-clean-architecture.md), [ADR-003](ADR-003-persistence.md), [ADR-004](ADR-004-async-messaging.md), [ADR-005](ADR-005-outbox-consistency-idempotency.md), [ADR-006](ADR-006-identity-and-access.md), [ADR-008](ADR-008-observability.md), [ADR-009](ADR-009-deployment-cloud.md)  
> **Requisitos:** [RNFs](../../requirements/non-functional-requirements.md)

## Contexto e problema

CashFlow possui dois serviços .NET 10 independentes: **Transactions** confirma créditos e débitos com Transactional Outbox, e **Consolidation** consome eventos via RabbitMQ e disponibiliza saldos diários. A aplicação também usa PostgreSQL, Keycloak, NGINX e a stack de observabilidade da ADR-008.

O desafio exige que o serviço de lançamentos continue disponível quando Consolidation cair (**RNF-01**) e que a consulta do consolidado suporte **50 RPS** com no máximo **5% de perda de requisições** (**RNF-02/RNF-03**). O significado exato de *perda* ainda depende de confirmação; os requisitos de [RNFs](../../requirements/non-functional-requirements.md) propõem medi-la como requisições válidas sem resposta útil.

Uma cobertura alta de código não comprova essas propriedades de arquitetura. Precisamos de testes automatizados reprodutíveis com evidências de persistência, reentrega, autorização, desempenho e recuperação. **A meta de 80% de cobertura de linhas é uma proposta do projeto**, não deve ser apresentada como imposição explícita do enunciado sem evidência adicional.

## Alternativas avaliadas

| Opção | Benefícios | Limitações e trade-offs |
| --- | --- | --- |
| **A. Principalmente testes unitários** | Rápidos e baratos; ajudam a manter regras de negócio testáveis | Não demonstram transações reais, integração RabbitMQ, garantias de Outbox ou comportamento sob falha |
| **B. Pirâmide de testes: unitários + integração/componentes + fluxos E2E e testes não funcionais específicos** | Equilibra execução rápida e validação das dependências/requisitos críticos; falhas diagnosticáveis | Requer PostgreSQL/RabbitMQ em containers, fixtures, dados determinísticos e manutenção da infraestrutura de testes |
| **C. Principalmente testes end-to-end** | Exercitam integração da solução completa | Lentos, mais frágeis, difícil isolamento de defeitos e alto custo de execução no CI |

**Proposta em análise: opção B.** Testes unitários como base, testes de integração nos limites de infraestrutura e E2E seletivos para fluxos de negócio, complementados por carga, resiliência e segurança. Nenhuma ferramenta ou percentual por camada foi formalmente decidido nesta ADR.

## Decisão proposta, sujeita à discussão

1. **Cobertura:** propor **mínimo de 80% de cobertura de linhas** do código de produção testável de **Transactions e Consolidation individualmente**, e não só média agregada. Excluir por política explícita código gerado e migrations; não excluir regras de domínio para elevar artificialmente o índice. Tratar *branch coverage* como indicador complementar, sem limiar inicialmente imposto.
2. **Testes unitários:** concentrar em entidades, regras de crédito/débito, cálculo do saldo, validações, invariantes, handlers de aplicação e condições de erro. Evitar mocks de regras de negócio; simular apenas dependências externas nos testes de unidade.
3. **Testes de integração:** usar PostgreSQL e RabbitMQ reais em containers de teste para validar migrations/constraints, transação atômica lançamento + Outbox, publicação/confirm do broker, consumo idempotente, concorrência e processamento de reentrega. Validar por efeito persistido, não apenas chamadas de mock.
4. **Testes de API/contrato:** cobrir status HTTP, payloads, autenticação JWT, `merchant_id` autorizado, 401/403, contratos de eventos versionados e compatibilidade do produtor com o consumidor. Executar o caminho NGINX → API nos testes integrados de fluxo.
5. **Testes end-to-end seletivos:** registro de crédito/débito → confirmação → Outbox → RabbitMQ → Consolidation → consulta diária. Exercitar fluxo de login por Keycloak/NGINX em suíte específica quando os componentes estiverem implementados.
6. **Testes de resiliência:** desligar somente Consolidation e comprovar que Transactions continua aceitando/persistindo operações válidas; retomar e verificar backlog, idempotência e saldos. Separadamente, derrubar RabbitMQ e recuperar a publicação do Outbox. Não confundir falha de Consolidation com falha simultânea do banco de Transactions.
7. **Teste de carga:** gerar **50 RPS de consultas válidas** por **5 minutos após aquecimento**, seguindo a proposta já registrada nos RNFs, medindo requisições efetivamente enviadas, respostas úteis, timeouts, códigos HTTP, p95, CPU, banco e recursos. A interpretação inicial exige **≥95% de sucesso** (máximo 5% sem resposta útil); o enunciado ainda precisa confirmar a definição de perda. **p95 < 500 ms é SLO proposto, não requisito explícito**.
8. **Segurança:** testes de validação JWT, acesso cruzado entre comerciantes, ausência de autorização, manipulação de identificadores e não exposição de segredos/dados sensíveis. Testes não substituem análise de configuração de Keycloak e políticas cloud.
9. **Execução e evidências:** separar uma suíte curta para pull requests e uma completa para integração, carga e falhas; publicar resultados, relatório de cobertura, versão da aplicação e ambiente. A seleção de GitHub Actions/Azure DevOps/Jenkins pertence à **ADR-011**.

### Ferramentas candidatas, sem aprovação implícita

| Finalidade | Opção inicial | Alternativa / observação |
| --- | --- | --- |
| Testes .NET | **xUnit** | NUnit/MSTest também atendem |
| Asserções | Assert nativo ou biblioteca compatível com licença do projeto | Evitar dependência cuja licença não tenha sido avaliada |
| Cobertura | **Coverlet + ReportGenerator** | Cobertura integrada do Visual Studio pode ser alternativa |
| API em processo | **WebApplicationFactory / Microsoft.AspNetCore.Mvc.Testing** | Teste HTTP real no ambiente Docker para validar proxy e TLS |
| Dependências reais em teste | **Testcontainers for .NET** para PostgreSQL e RabbitMQ | Docker Compose para suíte de integração completa |
| Carga | **k6** | NBomber, Locust ou outra ferramenta reproduzível |
| E2E de interface, se necessário | **Playwright** | Cobertura UI deve ser seletiva, sem substituir testes das APIs |

## Consequências positivas

- Evidência verificável dos RNF-01 a RNF-03 e das garantias da ADR-005.
- Feedback rápido de regressões nas regras e maior confiança em migrations, broker e contratos reais.
- Métricas de cobertura rastreáveis por serviço, sem mascarar um deles por média geral.
- Testes de carga e falhas separados dos unitários para permitir diagnósticos úteis.

## Riscos e mitigação

| Risco | Consequência | Mitigação |
| --- | --- | --- |
| Cobertura usada como proxy de qualidade | Testes superficiais atingem 80% sem provar RNFs | Exigir cenários explícitos de falha, fluxo e carga, além de cobertura |
| Testes com RabbitMQ/PostgreSQL instáveis | CI lento e testes intermitentes | Containers descartáveis, migrações reproduzíveis, dados isolados e readiness |
| Exigir todos os E2E em cada commit | Atraso em PRs | Separar gates rápidos de suítes completas e periódicas |
| Interpretação ambígua de 5% de perda | Evidência contestável | Documentar denominador, tipos de falha e janela; pedir confirmação |
| Metas medidas no ambiente inadequado | Resultado não representativo de PROD | Registrar perfil de carga e capacidade; HOM econômico não comprova carga/HA de PROD |
| Mock excessivo | Não validar atomicidade Outbox, broker ou idempotência | Testes de integração com componentes reais |
| Testes modificarem dados reais | Impacto em produção | Dados e credenciais de teste, execução destrutiva restrita a ambientes controlados |

## Critérios de validação propostos

1. **Unitários:** cobrir regras de domínio/aplicação e exceções de negócio relevantes.
2. **Cobertura:** aferir, se aprovada a meta, **≥80% de linhas em cada microsserviço**, com relatório reproduzível.
3. **Outbox:** registrar lançamento + evento na **mesma transação**, e demonstrar que erro de publish não apaga o lançamento confirmado.
4. **Consolidation indisponível:** registrar lançamentos durante a parada e conferir persistência; após retorno, processar backlog sem duplicidade.
5. **Consumidor:** reentregar mesmo `event_id` e confirmar que saldo não muda duas vezes.
6. **Segurança:** rejeitar 401 tokens inválidos e 403 usuário válido sem vínculo autorizado; impedir cruzamento de dados por comerciante.
7. **Carga:** comprovar 50 RPS no cenário acordado, contagem real de respostas úteis e taxa de falha de consultas válidas ≤5% pela interpretação provisória.
8. **Observabilidade:** demonstrar logs, métricas e traces correlacionáveis sem bloquear transação quando telemetria cair.
9. **Relatórios:** arquivar resultados e configurações relevantes para reprodução do teste.

## Detalhes ainda pendentes

- Escolher formalmente a opção A, B ou C e a stack de ferramentas.
- Definir se 80% é limiar bloqueante no CI para cada serviço, total agregado, ou meta progressiva por fase.
- Confirmar definição de perda de requisições e a janela oficial de RNF-03.
- Definir perfil de massa de dados, execução em LOCAL/DEV/HOM e infraestrutura com capacidade representativa para 50 RPS e testes de resiliência.
- Definir na ADR-011 em quais etapas do CI/CD cada suíte será obrigatória.
- Estabelecer política de testes de regressão, retenção de relatórios e responsabilidade por manutenção.

## Referências

- [RNFs e seus cenários de prova](../../requirements/non-functional-requirements.md)
- [ADR-005, Transactional Outbox e idempotência](ADR-005-outbox-consistency-idempotency.md)
- [ADR-008, observabilidade](ADR-008-observability.md)
- [ADR-009, ambientes e cloud](ADR-009-deployment-cloud.md)
- [Registro de ADRs](README.md)
