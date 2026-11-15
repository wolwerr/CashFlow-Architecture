# Validação 02 | Diagramas de sequência e consistência arquitetural

> **Status:** revisão documental executada e fontes PlantUML ajustadas; sem validação em runtime  
> **Data:** 2026-11-15  
> **Escopo:** login/autorização, registro/Outbox, falha/recuperação e consulta diária  
> **ADRs de referência:** [ADR-002](adr/ADR-002-architecture-style-clean-architecture.md), [ADR-004](adr/ADR-004-async-messaging.md), [ADR-005](adr/ADR-005-outbox-consistency-idempotency.md), [ADR-006](adr/ADR-006-identity-and-access.md), [ADR-007](adr/ADR-007-clients-api-gateway.md)

## Objetivo e metodologia

A validação compara a ordem das interações nos quatro arquivos PlantUML existentes com as decisões arquiteturais já registradas, a [Building Block View](building-block-view.md) e os [requisitos](requirements-validation.md). A conclusão se aplica ao **desenho documentado**, não à correção de um sistema executado. As fontes foram editadas em 2026-11-15; os PNGs deverão ser **regenerados e inspecionados visualmente**.

## Resultado dos quatro fluxos

| Fluxo | Fonte PlantUML | Resultado após ajuste | Comportamento que precisa ser comprovado em testes |
| --- | --- | --- | --- |
| **Autenticação e autorização** | [sequence-auth.puml](plantuml/sequence-auth.puml) | **Alinhado ao desenho**: SPA → Keycloak OIDC/PKCE; NGINX proxy, APIs validam JWT localmente com JWKS cacheado | JWT válido/inválido, expiração, issuer/audience, vínculo `merchant_id`, 401/403, rotação JWKS |
| **Registro e publicação** | [sequence-register.puml](plantuml/sequence-register.puml) | **Alinhado ao desenho**: commit `Transaction + OutboxEvent` antes da resposta; worker assíncrono; confirmação de publicação **e roteamento**; deduplicação | Rollback atômico, publish confirm perdido, retorno unroutable, concorrência da Outbox, reentrega e ACK |
| **Falha do consolidado** | [sequence-failure.puml](plantuml/sequence-failure.puml) | **Alinhado ao desenho**: registro confirmado com Consolidation fora; pendência preservada quando broker falha; recuperação e commit idempotente antes do ACK | Desligar somente Consolidation; desligar broker; backlog; reentrega e reconstrução do consolidado |
| **Consulta diária** | [sequence-query.puml](plantuml/sequence-query.puml) | **Parcial**: consulta sem dependência de Transactions; autorização e caminhos de erro desenhados. Movimento líquido e acumulado definidos; saldo inicial de R$ 0,00 decidido; dia sem projeção definido na RN-09: movimento zero e saldo acumulado mantido | Data inválida, 401/403, dia vazio, divergência/atraso e regra de saldo aprovada |

## Inconsistências encontradas e corrigidas

1. **Cliente nomeado como Web/Mobile:** as fontes antigas ainda usavam `Aplicação Web/Mobile`, embora a [ADR-007](adr/ADR-007-clients-api-gateway.md) tenha decidido **React SPA** para o MVP. Padronizado para React SPA no navegador; mobile nativo fica fora do MVP.
2. **Rotas HTTP antigas:** `POST /lançamentos` e `GET /consolidados?data=...` eram tratados como se fossem os caminhos finais. O [template NGINX](../../infra/nginx/default.conf.template) prevê os prefixos `/api/transactions/*` e `/api/consolidation/*`. Os diagramas agora usam esses **prefixos de referência**, sem inventar endpoint definitivo. Como o proxy preserva prefixos, o contrato da API deverá ser compatível.
3. **HTTPS no salto interno:** as fontes antigas sugeriam HTTPS entre NGINX e as APIs. O template atual expõe HTTP interno na porta **8080**, e o HTTPS externo **443** ainda depende de Ingress/TLS. As etiquetas foram diferenciadas.
4. **Publisher confirm isolado:** o diagrama de registro anterior só mostrava ACK do broker antes de marcar a Outbox. A [ADR-005](adr/ADR-005-outbox-consistency-idempotency.md) exige confirmação positiva **e verificação de que a mensagem foi roteada ao destino esperado**; mensagem `unroutable` ou confirmação incerta não autoriza marcar a Outbox como concluída.
5. **Consumidor e ACK sem estado explícito:** agora a sequência distingue `ProcessedEvent` único e atualização de `DailyBalance` em commit atômico, e ACK ao RabbitMQ **somente após persistência segura**, inclusive em reentrega já processada.
6. **Falha e recuperação superficiais:** o fluxo anterior não mostrava o banco Consolidation na recuperação, tampouco o efeito da indisponibilidade do próprio RabbitMQ. Acrescentados cenários alternativos para confirmação/roteamento e persistência da Outbox.
7. **Autenticação na borda:** a fonte anterior não diferenciava consulta de JWKS por cache de introspecção obrigatória por chamada. Agora o fluxo mostra o Keycloak para login e consulta de metadata/JWKS conforme necessidade. **NGINX não valida o token no template atual**, e uma integração `auth_request` futura não é requisito do desenho vigente.
8. **Consulta com apenas resposta 200:** inseridos caminhos de JWT inválido, ausência de autorização, parâmetros inválidos e ausência de projeção. O status HTTP e a semântica financeira dos últimos casos continuam **pendentes de contrato**, sem apresentar uma proposta como decidida.

## Garantias e limitações preservadas

- O lançamento confirmado depende do **commit local de Transactions**, não da publicação RabbitMQ nem da disponibilidade de Consolidation.
- Há **entrega ao menos uma vez**, não garantia de entrega exatamente uma vez; confirmações perdidas podem gerar duplicações.
- O efeito financeiro da reentrega é **idempotente** se o `event_id` tiver restrição única, o saldo for atualizado de forma concorrente segura e o commit for atômico.
- As mensagens podem permanecer pendentes na Outbox ou em fila durável, **sujeitas a limites, retenção e política operacional**. Não pressupor recuperação ilimitada nem perda impossível.
- O servidor de autenticação **emite tokens e disponibiliza JWKS**; cada API financeira valida assinatura e autorização de comerciante. O NGINX encaminha os tokens.
- Consulta diária usa **exclusivamente o banco próprio de Consolidation**, com consistência eventual.
- Portas nos diagramas indicam **destino e configuração de referência**, não serviços provisionados nem TLS interno comprovado.

## Pendências identificadas

| Prioridade | Pendência | Efeito |
| --- | --- | --- |
| **Decidido em 15/11/2026** | RN-02/RN-03/RN-09: líquido diário e acumulado, abertura em zero e carry-forward em dias vazios | Consulta deve responder também sem linha `DailyBalance` na data; retroatividade decidida, recomposição física a validar |
| **Parcialmente decidido em 15/11/2026** | API REST | Prefixos /api/v1/transactions e /api/v1/consolidation, HTTP 201 e Idempotency-Key obrigatória | Ainda produzir OpenAPI, schemas, erros e caminhos específicos |
| **Parcialmente decidido em 15/11/2026** | Retentativas RabbitMQ | Limitadas com backoff, DLQ e replay controlado | Definir contagens, intervalos, roteamento e confirmar em testes |
| **Parcialmente decidido em 15/11/2026** | Idempotência HTTP | Idempotency-Key obrigatório e event_id estável; totais diários concorrentes | Especificar armazenamento, expiração e teste de corrida |
| **Parcialmente decidido em 15/11/2026** | Autorização por comerciante | Banco lógico próprio, ADMIN/OPERATOR e cache curto invalidável | Definir interface, TTL e revogação e testar fail-closed |
| **Decidido em 15/11/2026** | **RN-04**: competência informada pelo comerciante; criação UTC | O consolidado agrupa por `reference_date`; fuso de referência/exibição e estratégia física de recomposição retroativa ainda pendentes; RN-07 já fixou BRL com duas casas e rejeição de precisão excessiva |
| **Decidido em 15/11/2026** | **Complemento RN-04**: aceitar competência passada/atual, rejeitar futura, recompor saldos acumulados desde a data afetada | Testar reentrega e eventos fora de ordem sem dupla contabilização; estratégia física ainda pendente |
| **Média** | Fixar mecanismo de reconciliação e recuperação de backlog | Recuperação deve ser reproduzível; RabbitMQ não é um log histórico ilimitado |
| **Média** | Regenerar PNGs e validar visualmente os quatro PlantUML | As imagens Markdown podem apresentar a versão anterior até atualização |

## Critérios para aceitar a validação de execução

- [x] Fluxo **React → Keycloak → NGINX → APIs** compatível com ADR-006/007, considerando que login vai diretamente ao Keycloak e o proxy não autentica.
- [x] Commit de lançamento e Outbox antes da resposta; publish e consumo assíncronos.
- [x] Roteamento/confirm do broker e tratamento de retorno ou timeout representados no desenho.
- [x] Consumo idempotente, ACK pós-commit, falha de Consolidation e recuperação documentados.
- [x] Consultas independentes de Transactions, com erros e dados ausentes considerados sem inventar regra de saldo.
- [ ] Executar testes de integração com PostgreSQL/RabbitMQ, incluindo falhas/crash entre etapas.
- [ ] Renderizar e conferir visualmente os quatro PNGs atualizados.
- [ ] Fechar regras de negócio e contratos pendentes e ajustar os fluxos se necessário.

**Conclusão:** revisão documental da etapa 02 efetuada. A arquitetura descrita nos quatro fluxos está alinhada às ADRs após as correções, com **semântica financeira, políticas operacionais e testes ainda pendentes**. Não se trata de validação de funcionamento real.

## Próxima etapa

**Validação 03: modelo de dados**, avaliando `Transaction`, `OutboxEvent`, `DailyBalance`, `ProcessedEvent`, restrições de unicidade, concorrência, limites de domínio e separação IAM/autorização CashFlow.

## Fontes

- [Diagramas de sequência](sequence-diagrams.md)
- [Building Block View](building-block-view.md)
- [ADR-002](adr/ADR-002-architecture-style-clean-architecture.md)
- [ADR-004](adr/ADR-004-async-messaging.md)
- [ADR-005](adr/ADR-005-outbox-consistency-idempotency.md)
- [ADR-006](adr/ADR-006-identity-and-access.md)
- [ADR-007](adr/ADR-007-clients-api-gateway.md)
- [Requisitos não funcionais](../requirements/non-functional-requirements.md)

## Revalidação das 24 decisões aprovadas em 15/11/2026

As conclusões e pendências históricas anteriores devem ser lidas com o complemento de [Decisões consolidadas do MVP](mvp-decisions.md). **Escopo e políticas foram aprovados, mas nenhuma prova em execução foi produzida apenas por esta atualização.** Histórico paginado, auditoria básica, multi-comerciante, Authorization CashFlow com banco próprio, REST v1, Idempotency-Key, 201 pós-commit, eventos versionados, retry/backoff/DLQ, SLOs de 50 RPS/5 min/até 5%, p95 < 500 ms e frescor de 95% em 30 s são a nova referência. O acumulado é calculado na leitura sobre totais diários persistidos. Restam contratos, código, migrations, testes, configuração de infraestrutura e PNGs atualizados.
