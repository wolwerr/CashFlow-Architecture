# Visão de Blocos de Construção (Building Block View)

> **Issue #6 · Milestone M2** | Arquitetura proposta

Esta visão mostra **de que partes o CashFlow é composto** e o papel de cada uma. Os diagramas utilizam o **C4 Model** com PlantUML.

## C4 Nível 1: Contexto

O **Comerciante** usa o CashFlow para registrar lançamentos e consultar saldos. O **Administrador de acessos** gerencia usuários e permissões. Ambos utilizam a aplicação Web/Mobile proposta; o C4 de Contexto apresenta o CashFlow como um único sistema e deixa os detalhes internos para o Nível 2.

![Contexto do CashFlow](images/c4-context.png)

[Fonte PlantUML](plantuml/c4-context.puml)

## C4 Nível 2: Contêineres

A **solução completa proposta** contém uma aplicação React responsiva, NGINX como reverse proxy definido para implementação no MVP, os **dois serviços financeiros independentes** (Lançamentos e Consolidado diário), o Keycloak como provedor de identidade, bancos separados por domínio, mensageria e coleta de observabilidade. O worker Outbox continua interno ao serviço de lançamentos.

![Contêineres do CashFlow](images/c4-containers.png)

[Fonte PlantUML](plantuml/c4-containers.puml)

> **Renderização pendente:** a fonte C4 Nível 2 foi atualizada para React + TypeScript e NGINX. Regenerar `images/c4-containers.png`. O papel de API Gateway dedicado e o BFF são evoluções futuras, conforme [ADR-007](adr/ADR-007-clients-api-gateway.md). A [ADR-008](adr/ADR-008-observability.md) está **decidida**: OpenTelemetry Collector, Prometheus, Loki, Tempo e Grafana. A imagem do C4 precisa ser atualizada após alteração da fonte.

| Bloco | Responsabilidade |
| --- | --- |
| Aplicação React (arquitetura-alvo) | React + TypeScript responsivo como cliente Web; aplicativo nativo não previsto no MVP. |
| NGINX (reverse proxy do MVP) | Serve a interface React e encaminha chamadas HTTP para as APIs no fluxo Web; não representa uma plataforma de API Gateway dedicada. Um Gateway com políticas adicionais fica para evolução futura. |
| Serviço de lançamentos (API + Outbox worker) | Receber e confirmar movimentações; publicar eventos pendentes em segundo plano. |
| Banco de lançamentos | Guardar lançamentos e eventos Outbox; manter trilha de auditoria proposta no mesmo domínio. |
| Mensageria | Entregar eventos ao serviço de consolidado. |
| Serviço de consolidado | Atualizar a visão diária e responder consultas de saldo. |
| Banco do consolidado | Guardar resultados calculados e eventos já processados. |
| Keycloak (provedor IAM adotado) | Gerenciar identidades, autenticação, sessões, emissão de tokens e chaves públicas OIDC/JWKS. Não concentra necessariamente os vínculos de negócio dos comerciantes. |
| Banco do Keycloak | Persistir usuários, credenciais protegidas, clientes OIDC e configurações do provedor. A autorização por comerciante é responsabilidade do CashFlow. |
| OpenTelemetry Collector | Receber e encaminhar os sinais OTLP produzidos pelas APIs e workers. |
| Prometheus | Armazenar e consultar métricas, coletadas por scraping de endpoints compatíveis. |
| Loki | Centralizar logs estruturados, com ingestão OTLP/HTTP configurada. |
| Tempo | Armazenar traces distribuídos de fluxos HTTP e assíncronos. |
| Grafana | Dashboards e consulta dos três backends, com data sources configurados. |

**Nota:** o bloco Web/Mobile é uma representação conceitual dos canais de entrada. Se forem aplicações implantadas separadamente, o C4 Nível 2 pode ser desdobrado em contêiner Web e contêiner Mobile após a escolha tecnológica na Issue #7.

## Protocolos propostos

| Comunicação | Protocolo |
| --- | --- |
| Usuários → aplicação Web/Mobile | HTTPS |
| React → NGINX | HTTPS / REST / JSON, com Bearer JWT |
| NGINX → APIs financeiras | Proxy HTTPS / REST / JSON; autenticação OIDC com Keycloak ocorre separadamente |
| Login e identificação | Keycloak com OpenID Connect / OAuth 2.0 sobre HTTPS; Authorization Code + PKCE conforme tipo de cliente |
| Cadastro, grupos e permissões | API administrativa REST / HTTPS (proposta) |
| Validação de JWT | JWKS / HTTPS, com validação local e cache das chaves |
| APIs e seus bancos | SQL / TCP, com proteção de conexão |
| Worker Outbox → broker → consumidor | AMQP 0-9-1 |
| APIs e workers → OpenTelemetry Collector | OTLP / gRPC ou HTTP, logs estruturados, métricas e traces |
| Collector → Prometheus | Exportação de métricas por endpoint de scrape HTTP |
| Collector → Loki | Logs via OTLP / HTTP |
| Collector → Tempo | Traces via OTLP / gRPC ou HTTP |
| Grafana → Prometheus, Loki e Tempo | Consultas às APIs dos backends configurados |

Os protocolos representam **propostas**, não decisões finais. Produtos e garantias serão formalizados nos ADRs da Issue #7. As APIs devem validar assinatura, emissor, audiência, prazo e permissões do token. A autorização também precisa verificar que o usuário possui acesso ao **comerciante/conta consultada**, e não somente que pertence a determinado grupo. Tokens podem ter permissões desatualizadas até sua expiração; revogação e política de atualização serão detalhadas na Issue #9.

## Escopo: desafio x solução completa

| Elemento | Situação |
| --- | --- |
| Registro de créditos e débitos | **Exigido pelo desafio** |
| Consulta ao consolidado diário | **Exigido pelo desafio** |
| Independência entre registro e consolidação | **Exigida pelo desafio** |
| Interfaces Web/Mobile e painel administrativo | **Proposta de solução completa**, não exigida |
| NGINX / API Gateway | **NGINX incluído no MVP como reverse proxy**; API Gateway dedicado é evolução futura, conforme ADR-007 |
| Keycloak e seu banco | **Escolha arquitetural decidida (ADR-006)**; implantação e administração completas podem ficar fora do MVP financeiro |
| Mensageria, Outbox e observabilidade | **Decisões de arquitetura propostas** para atender resiliência e operação |

A implementação planejada do MVP inclui NGINX no caminho Web e mantém o foco funcional nas duas APIs e no processamento financeiro; a infraestrutura completa ainda precisa ser integrada e testada. Isso evita confundir a apresentação com uma promessa de implementar todas as interfaces.

## Autenticação, autorização e validação de JWT

A interface Web/Mobile autentica usuários no **Keycloak**, utilizando **OpenID Connect (OIDC) sobre HTTPS**. Para clientes públicos, a direção é Authorization Code com PKCE; o armazenamento seguro de tokens, eventual BFF, MFA e detalhes por plataforma serão definidos nas Issues #7 e #9. Não será desenvolvido um servidor próprio para emissão de tokens.

As APIs de **Lançamentos** e **Consolidado** devem validar os JWTs **localmente**, verificando assinatura, emissor, audiência, validade e permissões. As chaves públicas do Keycloak podem ser obtidas por **JWKS/HTTPS** e mantidas em cache conforme a política de rotação e expiração. Assim, com tokens e chaves válidos, o registro financeiro não precisa chamar o IAM sincronicamente a cada operação.

**Keycloak autentica; as APIs do CashFlow autorizam.** O NGINX não substitui esses controles. A verificação de acesso também deve considerar o `merchant_id` autorizado. Uma role como `VIEWER` não concede, por si só, acesso aos dados de outros comerciantes. Política de revogação, propagação de mudanças de permissões e acessos administrativos serão detalhados na Issue #9.

## Telemetria e observabilidade

Os serviços de **Lançamentos** e **Consolidado** são prioritários para instrumentação. **Keycloak**, o NGINX e um eventual **API Gateway futuro** poderão emitir telemetria quando forem implantados e configurados. O C4 apresenta o pipeline em um bloco de plataforma, com seus componentes listados na documentação, para evitar linhas transversais em excesso.

| Sinal | Uso esperado |
| --- | --- |
| **Logs estruturados** | Diagnóstico de erros, tentativas de acesso e processamento, sem expor tokens, senhas ou dados financeiros sensíveis. |
| **Métricas** | Taxa de requisições, erros, latência, eventos pendentes na Outbox, tamanho das filas e atraso da consolidação. |
| **Traces distribuídos** | Acompanhar operações que passam por NGINX, APIs, mensageria e consumidor, propagando contexto de rastreamento. |

Conforme a **ADR-008 decidida**, OpenTelemetry instrumenta serviços .NET, OpenTelemetry Collector recebe/encaminha os sinais, **Prometheus** armazena métricas, **Loki** armazena logs, **Tempo** armazena traces e **Grafana** oferece dashboards. Alertas, retenção, volumes, segurança e HA serão detalhados na ADR-009 e nas issues operacionais. A coleta de componentes de terceiros exige exporters/agentes próprios quando aplicável. A falha na stack de observabilidade não pode bloquear a confirmação de lançamentos. A trilha de **auditoria financeira persistida** não é substituída por logs de aplicação.

## Controle de acesso e decisão de tecnologia

Os perfis `ADMIN`, `OPERATOR` e `VIEWER` são exemplos para discussão. O cadastro de usuários, as relações com comerciantes e os fluxos de aprovação ainda precisam ser refinados nos requisitos funcionais. A **ADR-006 escolhe Keycloak** para identidade e autenticação. As relações usuário/comerciante e as permissões financeiras continuam sob a responsabilidade do CashFlow, sem exigir um novo servidor próprio de autenticação. A forma de persistir e consultar esses vínculos será detalhada na Issue #9. A Issue #9 detalhará MFA, auditoria, segregação por comerciante, tratamento de segredos, expiração e revogação de acesso.

A indisponibilidade do Keycloak **não deve parar o registro de movimentações que já tenham token válido**, desde que a validação local e as políticas de acesso permitam. Essa condição precisa ser testada.

## C4 Nível 3: Componentes

A visão de componentes foi documentada em **[C4 Nível 3: Transactions e Consolidation](c4-level-3-components.md)**, com duas fontes PlantUML editáveis:

- [Transactions: API, domínio, persistência e worker Outbox](plantuml/c4-components-transactions.puml)
- [Consolidation: consulta diária, consumidor, projeção e idempotência](plantuml/c4-components-consolidation.puml)

Os componentes seguem a **ADR-002 (Clean Architecture)**, a **ADR-005 (Transactional Outbox e consumidor idempotente)** e a **ADR-006 (autorização financeira separada da autenticação do Keycloak)**. Os diagramas representam responsabilidades propostas e suas interações, não classes de código ou deployments já implementados. **Os PNGs dos dois diagramas novos ainda precisam ser renderizados** a partir das fontes.

## Próximas leituras

- [Diagramas de sequência](sequence-diagrams.md), para ver as interações passo a passo.
- [Casos de Uso](use-cases.md), para entender as ações do comerciante e os fluxos de negócio.
- [Modelo de Dados](data-model.md), para entender os registros financeiros, a Outbox, a projeção e a auditoria.
