# Diagramas de Sequência

> **Issue #6 · Milestone M2** | Interações da arquitetura-alvo, revisadas na [Validação 02](validations/sequence-validation.md)

Este documento reúne os **diagramas de sequência**. Eles detalham em que ordem os participantes interagem. Complementam os [Casos de Uso](use-cases.md), mostrando como os serviços interagem. A semântica dos fluxos foi revisada conforme as ADRs na [Validação 02](validations/sequence-validation.md). Retentativas limitadas, backoff, DLQ e reprocessamento controlado estão aprovados; parâmetros e operação ainda exigem detalhamento.

## Autenticação e autorização por comerciante

Fluxo conceitual de autenticação da **React SPA** no **Keycloak** com OIDC Authorization Code + PKCE, seguida de acesso às APIs pelo NGINX com Bearer JWT. Cada API valida o token localmente e consulta JWKS quando necessário. O CashFlow mantém vínculos em banco lógico próprio de autorização, com cache curto e invalidação; Keycloak apenas autentica. A decisão está formalizada na [ADR-006](adr/ADR-006-identity-and-access.md).

![Autenticação no Keycloak e autorização por comerciante](images/sequence-auth.png)

[Fonte PlantUML](plantuml/sequence-auth.puml)

## Registro de lançamento e publicação assíncrona

A interface envia o registro com **data de competência informada pelo comerciante (RN-04)** ao NGINX, que encaminha ao serviço de lançamentos. O backend registra o **instante de criação em UTC**, usa o dia atual de `America/Sao_Paulo`, exige `Idempotency-Key` e responde **201 Created** após o commit de Transaction + Outbox + auditoria básica. Competências anteriores ou iguais à data atual são aceitas, futuras são rejeitadas; um evento retroativo altera o movimento do dia e deve refletir nos acumulados posteriores após o processamento assíncrono. A API financeira valida o token, as permissões e o comerciante autorizado antes de gravar o lançamento. O registro é confirmado após o **commit atômico de Transaction e OutboxEvent**, sem esperar a publicação do evento. O worker só marca o registro Outbox como publicado após publisher confirm positivo e roteamento ao destino esperado. O consumidor aplica idempotência e envia ACK após commit.

![Registro e publicação assíncrona](images/sequence-register.png)

[Fonte PlantUML](plantuml/sequence-register.puml)

## Falha do consolidado

Mesmo com o consolidado indisponível, o serviço de lançamentos consegue registrar uma movimentação. O registro pode prosseguir durante a falha de Consolidation; eventos permanecem pendentes na Outbox ou no broker, conforme durabilidade e retenção configuradas. A recuperação inclui deduplicação e persistência antes do ACK.

![Falha do consolidado](images/sequence-failure.png)

[Fonte PlantUML](plantuml/sequence-failure.puml)

## Consulta do consolidado diário

A aplicação React consulta o consolidado por meio do NGINX, que encaminha a chamada ao serviço responsável. O serviço de consolidado valida o JWT e as permissões de leitura e acessa sua própria base, filtrando pelo comerciante autorizado. Não precisa consultar o serviço de lançamentos. Em dias sem lançamentos, créditos, débitos e movimento líquido são zero, e o saldo acumulado anterior permanece (RN-09). Isso não exige linha para cada dia. O acumulado é **calculado pela soma dos totais diários persistidos até a data**. Datas inválidas, resposta a dados em atraso e convenções HTTP ainda dependem do contrato.

![Consulta ao consolidado](images/sequence-query.png)

[Fonte PlantUML](plantuml/sequence-query.puml)

**Renderização pendente:** as quatro fontes foram atualizadas após a Validação 02 e os PNGs ainda precisam ser regenerados e inspecionados. Regenerar `sequence-auth.png`, `sequence-register.png`, `sequence-failure.png` e `sequence-query.png`. Nos desenhos, NGINX faz parte do **fluxo Web adotado para o MVP**; acesso direto às APIs fica restrito a testes internos, quando necessário. O API Gateway dedicado fica para evolução futura.

**Nota:** as ADRs de comunicação, autenticação e consistência foram decididas. **Os prefixos de rota são referências do template NGINX**, não contratos OpenAPI concluídos; status de sucesso, regra do saldo e tratamento de eventual atraso ainda exigem definição. [Leia a matriz de inconsistências e pendências da Validação 02](validations/sequence-validation.md).

**Complemento aprovado para MVP em 15/11/2026:** rotas `/api/v1/transactions` e `/api/v1/consolidation` via NGINX, 201 após commit, tokens SPA em memória, RBAC ADMIN/OPERATOR, histórico paginado e auditoria básica. Estorno fica fora do MVP. As fontes PlantUML foram atualizadas e os **PNGs devem ser novamente renderizados**. Veja [Decisões consolidadas](mvp-decisions.md).
