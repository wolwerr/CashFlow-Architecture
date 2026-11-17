# Observabilidade OTLP integrada ao Compose local

Esta stack agora faz parte do **compose.yaml principal** e sobe automaticamente junto dos seis serviços do MVP. Não é implantada no ambiente Azure. Adiciona Collector, Prometheus, Loki, Tempo e Grafana, totalizando 11 serviços. A exportação OTLP fica habilitada por padrão no Compose local. Não execute em produção nem com dados sensíveis.

## O que a implementação entrega

- Instrumentação OpenTelemetry habilitada por padrão no Compose local nas APIs Transactions e Consolidation: traces HTTP, chamadas HTTP de saída, métricas HTTP e logs de `ILogger` via OTLP.
- Span de publicação do Outbox e span de consumo do Consolidation conectados pelo header W3C `traceparent` no RabbitMQ. Ambos registram apenas `correlation_id` no formato UUID nos logs, sem payload financeiro.
- Collector recebe OTLP gRPC/HTTP, exporta traces ao Tempo, logs ao Loki e métricas no formato Prometheus.
- Prometheus coleta métricas, Grafana recebe três datasources locais configurados automaticamente.
- Ninguém precisa criar credenciais cloud. Grafana exige senha local única; porta 3000 acessível apenas via loopback.

**Situação após a implementação desta PR:** o header W3C `traceparent` da requisição HTTP é salvo **na mesma transação PostgreSQL** que cria o lançamento e o registro de Outbox, usando a coluna nullable `outbox_messages.trace_parent`. A migração `202611170001_AddOutboxTraceParent` não altera lançamentos nem exige descarte dos volumes. O Outbox Publisher inicia seu span sob o contexto persistido e injeta o novo `traceparent` no RabbitMQ; o Consumer continua esse contexto. Linhas antigas, sem contexto, continuam publicáveis como traces independentes. A correlação de negócio permanece separada dos dados de tracing e nunca influencia autorização/idempotência.

**Limites atuais:** o encadeamento completo **precisa ser verificado no Tempo depois da atualização local** e não constitui uma garantia sobre requisições passadas. Métricas específicas dos workers e exporters de métricas de RabbitMQ/PostgreSQL/NGINX não fazem parte deste escopo. Não marcar `observability` como PASS apenas porque os contêineres iniciaram.

## Subir o ambiente completo no Windows PowerShell

```powershell
git checkout main
git pull origin main

# Nao grave esta senha em issues ou no repositorio.
$env:CASHFLOW_GRAFANA_PASSWORD = Read-Host "Senha local do Grafana" -MaskInput
docker compose config --quiet
docker compose up -d --build
docker compose ps

# Nao remover volumes. Os dados financeiros e de telemetria ficam preservados.
```

Acesse `http://localhost:3000` com usuário `admin` e a senha configurada, e examine as fontes de dados provisionadas. Execute chamadas locais de leitura às APIs e confirme a chegada de métricas HTTP e logs no Grafana. Consulte os logs do Collector **localmente, sem publicá-los**:

```powershell
docker compose logs --tail=70 otel-collector
```

Ao terminar, limpe os valores temporários do terminal:

```powershell
Remove-Item Env:CASHFLOW_GRAFANA_PASSWORD -ErrorAction SilentlyContinue
```

### Parar apenas os serviços de observabilidade

```powershell
docker compose stop grafana prometheus loki tempo otel-collector
```

Se precisar desabilitar temporariamente a exportação OTLP, defina `$env:CASHFLOW_OTEL_ENABLED = "false"` e recrie somente Transactions/Consolidation com `docker compose up -d --force-recreate --no-deps transactions consolidation`. Não execute `docker compose down --volumes` e não apague os volumes do CashFlow.

## Homologação final, sem apagar dados

1. Atualize a branch `main` após o merge e confira `git rev-parse HEAD`. Guarde esse SHA como **código da nova verificação**, não o atribua a ensaios antigos.
2. Execute `docker compose up -d --build` (o Compose aplicará a migração de schema em Transactions). Não execute `down --volumes`.
3. Em `docker compose ps`, confirme os 11 contêineres e confira em Grafana **Connections > Data sources** as fontes Prometheus, Loki e Tempo. Um datasource conectado não comprova ingestão.
4. Em **Explore > Prometheus**, consulte `up` e `http_server_request_duration_seconds_count` (ou busque pelo prefixo `http_server` no seletor de métricas). Registre se há séries HTTP dos serviços. O nome exato depende da normalização do Collector.
5. Em **Explore > Loki**, encontre logs recentes de `cashflow-transactions` e `cashflow-consolidation` pelo seletor de labels, sem copiar payloads, tokens ou dados pessoais.
6. Em **Explore > Tempo > Search**, filtre por `cashflow-transactions`, e depois por `cashflow-consolidation`. Registre um resultado de trace HTTP e um de consumer, identificando o mesmo trace ID.
7. Usando **uma loja descartável**, registre **um único lançamento** e confirme no Tempo o encadeamento esperado `HTTP POST -> rabbitmq publish transaction.recorded -> rabbitmq consume transaction.recorded` dentro do **mesmo trace ID**. Verifique também que o lançamento aparece uma vez no histórico e apenas uma vez no consolidado. Não repita POST diante de atraso da projeção.
8. Com o coletor parado por poucos segundos (`docker compose stop otel-collector`), repita apenas com **outra operação de teste deliberada** e confirme que a API permanece disponível e registra corretamente, depois **sempre** restaure com `docker compose up -d otel-collector`. Não execute esse ensaio em dados reais; fazer a restauração mesmo se a verificação falhar. Se não desejar gravar uma segunda operação, deixe esse item pendente.
9. Guarde evidência sanitizada com data, SHA, resultados de Tempo/Loki/Prometheus e disponibilidade durante falha do Collector. Somente então atualize `observability` no manifesto local de `NOT_RUN` para `PASS`, seguindo as regras de `.github/scripts/local_mvp_acceptance.py`. Não versionar `.env`, JWT ou dados dos comerciantes.

## Evidência para aceite

Validar efetivamente:

1. `Grafana` provisiona Prometheus, Loki e Tempo; os três estão saudáveis e recebendo dados.
2. Traces HTTP de Transactions e Consolidation são consultáveis no Tempo.
3. Logs estruturados de ambas APIs são consultáveis no Loki.
4. Métricas de RPS, latência e erro HTTP aparecem no Prometheus.
5. Um lançamento de teste mantém **o mesmo trace ID** desde a requisição HTTP em Transactions, pela Outbox/RabbitMQ, até o consumidor em Consolidation, sem expor dados sensíveis.
6. Indisponibilidade do Collector **não impede** registrar lançamento financeiro (teste controlado com comerciante descartável).
7. Rastreabilidade do código testado e relatório sanitizado.

**Não existe PASS automático** enquanto os itens acima não tiverem resultados reais e o manifesto continuar `observability: NOT_RUN`. A verificação deve ser realizada após CI verde e com os serviços realmente em execução. A aprovação de testes de integração não substitui essa homologação.

**Nota de bootstrap:** o Grafana exige `CASHFLOW_GRAFANA_PASSWORD` no ambiente ou no `.env` local ignorado pelo Git. No primeiro bootstrap, as credenciais de serviço Keycloak exigidas pelas APIs também precisam estar configuradas conforme o README. Não rodar `docker compose down --volumes` sobre dados relevantes.

## Evidência visual relatada pelo operador, 17/11/2026

O operador forneceu capturas reais de **Grafana Explore** no ambiente local:

- **Prometheus:** `up{job="cashflow-otel-collector"}` com valor `1` após a recuperação e cinco séries de `http_server_request_duration_seconds_count` identificando `cashflow-transactions` e `cashflow-consolidation`.
- **Loki:** resultados contendo logs dos dois serviços `cashflow-transactions` e `cashflow-consolidation`.
- **Tempo:** trace da requisição `POST /api/v1/transactions` com HTTP `201`, span `rabbitmq publish transaction.recorded` e span de consumo `cashflow-consolidation` na mesma árvore distribuída (5 spans, duração total relatada de 320,46 ms).
- **Falha do Collector:** operador informou que o lançamento foi aceito enquanto o Collector estava parado e não apareceu no Grafana, comportamento consistente com telemetria não disponível. O Collector voltou a responder posteriormente (`up=1`). **Falta confirmar explicitamente a persistência desse lançamento no histórico e sua projeção no saldo.**

As capturas foram compartilhadas na conversa, **não são arquivos de evidência versionados no repositório**, e não substituem verificação automatizada de disponibilidade/integração. O SHA local efetivamente testado não foi disponibilizado. Portanto, não atribuir `PASS` ao cenário `observability` nem marcar `14/14` até guardar evidência sanitizada com SHA e confirmar histórico e saldo.
