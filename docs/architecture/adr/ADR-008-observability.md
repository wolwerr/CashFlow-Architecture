# ADR-008: Observabilidade com OpenTelemetry

> **Status:** proposta para aprovação  
> **Data:** 2026-11-14  
> **Issue:** [#7, Documentar ADRs e trade-offs](https://github.com/wolwerr/CashFlow-Architecture/issues/7)  
> **Relacionadas:** [ADR-002](ADR-002-architecture-style-clean-architecture.md), [ADR-004](ADR-004-async-messaging.md), [ADR-005](ADR-005-outbox-consistency-idempotency.md), [ADR-007](ADR-007-clients-api-gateway.md)

## Contexto e problema

O desafio exige que Transactions permaneça disponível quando Consolidation falhar e que Consolidation suporte **50 RPS de consultas com no máximo 5% de perda de requisições**, conforme interpretação documentada em [requisitos não funcionais](../../requirements/non-functional-requirements.md). Os componentes assíncronos incluem **Outbox worker**, **RabbitMQ**, **Consolidation consumer** e **PostgreSQL**.

Para demonstrar o comportamento sob falha e sob carga, precisamos tornar observáveis os caminhos síncrono e assíncrono, sem confundir **auditoria financeira persistida** com telemetria técnica. A arquitetura-alvo apresenta um coletor de observabilidade. React, Gateway e Keycloak não são necessariamente componentes implementados no MVP financeiro; instrumentá-los é etapa condicional.

## Alternativas avaliadas

| Alternativa | Benefícios | Custos / riscos |
| --- | --- | --- |
| Apenas logs de console | Simples de adotar; útil no desenvolvimento | Baixa capacidade de correlacionar serviços, mensageria e carga; métricas e traces ausentes |
| SDKs específicos de cada fornecedor de monitoramento | Pode facilitar configuração inicial em uma cloud | Acoplamento ao destino, migração e múltiplas integrações por serviço |
| **OpenTelemetry em .NET + OTLP + Collector opcional conforme ambiente** | Instrumentação padronizada, correlação de traces, métricas e logs, destinos substituíveis | Configuração do pipeline, uso de recursos, cardinalidade e armazenamento exigem atenção |
| Plataforma gerenciada de APM | Operação e dashboards simplificados conforme produto | Custos, dependência de fornecedor e conectividade; pode não ser necessária ao MVP local |

OpenTelemetry é um conjunto de especificações, APIs, SDKs e ferramentas de coleta e exportação. **Não substitui sozinho o armazenamento, a visualização nem o alerta.**

## Decisão proposta

1. **Instrumentar Transactions e Consolidation com OpenTelemetry para .NET**, usando atributos de recurso como `service.name`, ambiente e versão; incluir requisições HTTP, chamadas ao banco e processamento de eventos.
2. **Logs estruturados** por meio das APIs de logging do .NET, contendo identificadores de correlação, resultado da operação, duração e categoria de erro. Não registrar tokens, credenciais, payloads financeiros sensíveis ou dados pessoais desnecessários.
3. **Traces distribuídos** propagando contexto entre API, Outbox worker, RabbitMQ e consumidor. Usar o padrão W3C Trace Context quando apropriado e transportar o contexto por headers das mensagens. Preservar `correlation_id`, `transaction_id` e `event_id` como identificadores de negócio/integração, sem confundi-los com `trace_id`.
4. **Métricas técnicas e de negócio operacional**, com nomes/labels de baixa cardinalidade e limites explícitos para evitar explosão de custos.
5. **OTLP** como protocolo preferido para exportação. **OpenTelemetry Collector** na arquitetura-alvo para processar/encaminhar sinais; em um MVP enxuto, pode-se exportar diretamente a um destino compatível para reduzir infraestrutura, desde que o comportamento esteja documentado.
6. **Backend de visualização/armazenamento:** no cenário local de referência, avaliar **Prometheus + Grafana** para métricas e **Jaeger ou Grafana Tempo** para traces. Logs podem começar no console/container com estrutura JSON, evoluindo para **Loki ou outro agregador** se o cenário justificar. **Esses produtos permanecem opções de implementação, não uma stack toda obrigatória e aprovada por esta ADR.**
7. Em cloud, o destino pode ser um serviço gerenciado, sem alterar o contrato de instrumentação, sujeito a integração e custos reais.

## Sinais e indicadores prioritários

| Componente | Indicadores propostos | O que permitem investigar |
| --- | --- | --- |
| **Transactions API** | HTTP RPS, erros, p50/p95/p99, duração da transação, falhas de commit | Registro indisponível ou degradado |
| **Outbox worker** | Total de pendências, idade máxima da pendência, tentativas, publicações confirmadas e retornos não roteáveis | Eventos que ainda não chegaram ao broker |
| **RabbitMQ** | Profundidade das filas, mensagens não confirmadas, taxa de publicação/consumo, conexões e DLQ | Acúmulo, reentregas e problemas de roteamento |
| **Consolidation consumer** | Eventos recebidos, processados, duplicados, falhas, retries, duração da consolidação | Projeção atrasada ou duplicação evitada |
| **Consolidation API** | RPS de consulta, proporção de respostas úteis, erros e latência p95 sob 50 RPS | RNF-02, RNF-03 e SLOs propostos |
| **PostgreSQL** | Conexões, duração de queries, deadlocks/erros e saturação do pool | Gargalos e falhas de persistência |
| **Keycloak / NGINX / React (quando implementados)** | Erros de login, disponibilidade, latência de borda e sinais relevantes | Saúde dos componentes adicionais |

**Cuidado:** não usar `transaction_id`, `event_id`, `user_id` ou `merchant_id` como labels de métricas Prometheus, pois geram cardinalidade elevada e podem expor informações. Esses identificadores podem estar em logs/traces com controles de acesso e minimização apropriados.

## Evidências a produzir no MVP

### Falha de Consolidation

1. Derrubar Consolidation e registrar lançamentos válidos.
2. Verificar sucesso das operações em Transactions e crescimento controlado do backlog (fila/Outbox conforme cenário).
3. Reativar Consolidation e medir escoamento do backlog, recuperação do saldo e latência de atualização.
4. Registrar logs, métricas e identificadores que permitam rastrear o evento sem exposição de dados sensíveis.

### Pico de consultas

1. Executar teste de **50 RPS** por janela definida nos RNFs (proposta atual: cinco minutos após aquecimento).
2. Calcular taxa de respostas úteis sobre requisições válidas efetivamente enviadas, conforme interpretação documentada de **até 5% sem resposta útil**.
3. Medir p95 e o comportamento de CPU, memória, pool, banco e filas; **p95 menor que 500 ms é SLO proposto, não requisito explícito do desafio**.
4. Preservar resultados e limitações do ambiente como evidências.

### Reentrega/idempotência

Reenviar evento existente; registrar ocorrência de duplicata e comprovar que o saldo não foi somado novamente. A execução deve produzir logs/traces suficientes para investigar o caminho sem depender de dados sensíveis.

## Consequências positivas

- Diagnóstico de falhas que cruzam limites de processos e mensageria.
- Evidência objetiva para resiliência, atraso de consolidação e 50 RPS.
- Instrumentação com menos dependência de um fornecedor específico.
- Separação clara entre auditoria financeira e telemetria operacional.

## Riscos e mitigação

| Risco | Impacto | Mitigação |
| --- | --- | --- |
| Coletor/backend indisponível | Perda ou atraso de telemetria | Pipeline com buffers, retries e limites; **não bloquear lançamento financeiro se observabilidade falhar** |
| Overhead de instrumentação | Consumo de CPU/memória e latência | Amostragem de traces, limites de exportação e testes de impacto |
| Alta cardinalidade | Crescimento de custo e instabilidade do backend | Labels de cardinalidade controlada, dashboards e limites de retenção |
| Exposição de dados sensíveis | Risco de segurança e LGPD | Sanitização, mínimo de atributos, controle de acesso e políticas de retenção |
| Excessos de ferramentas no MVP | Mais containers e manutenção que o necessário | Começar com métricas/logs essenciais e coleta compatível; adicionar armazenamento visual conforme valor demonstrado |
| Sampling de traces | Parte das execuções não aparecerá detalhada | Combinar métricas agregadas com logs, eventos de erro e sampling consciente |
| Confundir logs com auditoria financeira | Falta de integridade legal/operacional da trilha | Manter `AuditEvent` persistido segundo o modelo de dados e políticas específicas |

## Critérios de validação

1. Logs de Transactions e Consolidation exibem eventos relevantes com correlação sem segredos.
2. Há indicadores de sucesso, falha e duração das requisições financeiras.
3. O atraso e o backlog do processamento assíncrono são mensuráveis.
4. Pelo menos um cenário reproduzível permite acompanhar o caminho de um lançamento até a consolidação.
5. Falha do Collector ou backend de monitoramento não impede o registro financeiro.
6. Relatórios de falha e de carga podem ser reproduzidos para comprovar os RNFs.
7. A configuração local deixa claro quais componentes de telemetria foram realmente implementados.

## Decisões explicitamente pendentes

- Escolha final dos destinos (Prometheus/Grafana, Jaeger/Tempo e eventual agregação de logs).
- Retenção, custos, sampling, limites e alertas de produção.
- Observabilidade de Keycloak, borda e frontend quando esses componentes forem implementados.
- Política de métricas e dados sensíveis no ambiente real, alinhada à Issue #9 e à ADR-009.

## Referências

- [OpenTelemetry para .NET](https://opentelemetry.io/docs/languages/dotnet/)
- [OpenTelemetry Collector](https://opentelemetry.io/docs/collector/)
- [OpenTelemetry Protocol (OTLP)](https://opentelemetry.io/docs/specs/otlp/)
- [Prometheus](https://prometheus.io/docs/introduction/overview/)
- [Grafana](https://grafana.com/docs/grafana/latest/)
- [Jaeger](https://www.jaegertracing.io/docs/)
- [RNFs do CashFlow](../../requirements/non-functional-requirements.md)
- [Building Block View](../building-block-view.md)
