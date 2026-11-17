# CashFlow | Roteiro de demonstração e defesa técnica

**Tempo sugerido:** 7 a 10 minutos. **Audiência:** avaliadores do desafio de Arquitetura de Soluções.

## 1. Problema e requisitos (1 min)

O comerciante precisa registrar débitos/créditos e consultar o saldo diário consolidado. A restrição crítica do enunciado é **independência do registro diante da queda do Consolidation**. Em pico, o consolidado recebe **50 requisições/s** com **no máximo 5% de perda**.

Abra [matriz de atendimento](../requirements/challenge-compliance-matrix.md) e explique que requisitos explícitos da banca são separados das metas adicionadas pelo projeto (por exemplo, p95 inferior a 500 ms).

## 2. Capacidades e arquitetura alvo (2 min)

Abra [C4/Building Block View](../architecture/building-block-view.md) e [domínios](../domain/business-capabilities-and-context-map.md).

- **Transactions (.NET 10):** valida crédito/débito, autoriza comerciante, persiste lançamento + evento na Outbox em uma única transação PostgreSQL.
- **RabbitMQ:** transporte assíncrono, com entrega eventual e confirmação do produtor.
- **Consolidation (.NET 10):** consumidor idempotente, ACK após persistência, projeção e consulta do saldo diário.
- **React/NGINX/Keycloak:** autenticação OIDC, controle de acesso por comerciante e interface de operação.
- **OpenTelemetry + Collector + Prometheus/Loki/Tempo/Grafana:** logs, métricas e traces locais.

Justifique o trade-off: consistência eventual do consolidado para preservar disponibilidade de registro. Traces não são auditoria contábil.

## 3. Demonstração funcional local (2 min)

Em um ambiente local previamente configurado, sem compartilhar credenciais:
1. Faça login como operador de loja de teste.
2. Mostre lançamento de crédito e débito e histórico.
3. Mostre consulta ao saldo diário após projeção.
4. Se disponível, mostre perfis administrativos, soft delete e isolamento entre lojas.

**Não refaça lançamentos apenas para melhorar a apresentação** se os dados de teste não forem descartáveis.

## 4. Testes de resiliência e desempenho (1 min)

Abra [relatório de carga](local-load-test.md): **15.000 respostas HTTP 200 em cinco minutos, 50 RPS, 0% de perda, p95 de 27,91 ms**, medição local informada.

Mostre [ensaio integrado](local-integrated-recovery-test.md): Transactions respondeu 201 durante interrupção do consumidor/broker e o saldo convergiu após restabelecimento. Explique idempotência por `event_id`, duplicatas e DLQ, mas não prometa ausência absoluta de falhas em produção.

## 5. Observabilidade e qualidade (1 min)

Abra [runbook OTLP](local-observability-stack.md) e, se o Docker estiver ligado, mostre no Tempo um **trace único HTTP → Outbox → consumer**, no Prometheus métricas HTTP e no Loki logs de ambos os serviços.

Abra o [run #577](https://github.com/wolwerr/CashFlow-Architecture/actions/runs/44163593607): Backend, Frontend, Validation offline e Sonar concluíram com sucesso. Os E2E locais disruptivos não são testes automáticos da CI.

## 6. Custos, riscos e próximos passos (1 min)

Mostre [estimativa de cloud](../architecture/four-environment-total-cost-plan.md) de **US$ 1.305,02/mês**, informando premissas e que o AKS **não foi provisionado**. Diga que HA horizontal, métricas de componentes de terceiros, alertas e TLS de produção fazem parte das evoluções futuras.

## Perguntas críticas para responder

| Pergunta provável | Resposta alinhada ao projeto |
| --- | --- |
| Por que dois serviços? | Separação do caminho de escrita do de leitura/projeção; falha do consolidado não interrompe a confirmação do lançamento |
| Como evitar perda do evento? | Transação PostgreSQL com Transactional Outbox, worker com publisher confirms e consumidor idempotente; falhas extremas e disaster recovery exigem estratégia operacional adicional |
| Por que não usar transação distribuída? | Outbox reduz acoplamento temporal e evita 2PC, com consistência eventual no saldo |
| Por que Keycloak? | IAM centraliza usuários/credenciais/grupos e as APIs fazem a verificação de autorização no domínio autorizado |
| Qual a prova dos 50 RPS? | Ensaio local histórico, 300 s, 15.000/15.000 respostas HTTP 200, não uma promessa de capacidade Azure/produção |
| Qual a maior limitação? | Execução exclusivamente local, proveniência de SHAs de ensaios antigos incompleta, ambiente de produção não homologado |
| Como observar a ponta a ponta? | Trace W3C persistido na Outbox e propagado nos headers RabbitMQ; confirmado manualmente no Grafana Tempo |

**Página central de evidências:** [Auditoria final da entrega](final-delivery-evidence.md).
