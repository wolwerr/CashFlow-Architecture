# Observabilidade OTLP integrada ao Compose local

Esta stack agora faz parte do **compose.yaml principal** e sobe automaticamente junto dos seis serviços do MVP. Não é implantada no ambiente Azure. Adiciona Collector, Prometheus, Loki, Tempo e Grafana, totalizando 11 serviços. A exportação OTLP fica habilitada por padrão no Compose local. Não execute em produção nem com dados sensíveis.

## O que a implementação entrega

- Instrumentação OpenTelemetry habilitada por padrão no Compose local nas APIs Transactions e Consolidation: traces HTTP, chamadas HTTP de saída, métricas HTTP e logs de `ILogger` via OTLP.
- Span de publicação do Outbox e span de consumo do Consolidation conectados pelo header W3C `traceparent` no RabbitMQ. Ambos registram apenas `correlation_id` no formato UUID nos logs, sem payload financeiro.
- Collector recebe OTLP gRPC/HTTP, exporta traces ao Tempo, logs ao Loki e métricas no formato Prometheus.
- Prometheus coleta métricas, Grafana recebe três datasources locais configurados automaticamente.
- Ninguém precisa criar credenciais cloud. Grafana exige senha local única; porta 3000 acessível apenas via loopback.

**Limites atuais:** os spans da publicação e do consumo devem ser comprovados em execução real. O vínculo com a requisição HTTP original ainda não é garantido, pois o contexto `traceparent` dessa requisição não é persistido no Outbox; os spans da publicação iniciam um novo trace. A coleta de métricas específicas dos workers ainda precisa ser implementada. Os exporters/consumidores de métricas de RabbitMQ, PostgreSQL e NGINX não estão incluídos nesta fase. Não marcar `observability` como PASS apenas porque os contêineres iniciaram. A coleta do Log da API via OTLP deve ser verificada no Loki, e o fluxo completo da transação até o consumidor precisa de prova independente.

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

## Evidência para aceite

Validar efetivamente:

1. `Grafana` provisiona Prometheus, Loki e Tempo; os três estão saudáveis.
2. Traces HTTP de Transactions e Consolidation são consultáveis no Tempo.
3. Logs estruturados de ambas APIs são consultáveis no Loki.
4. Métricas de RPS, latência e erro HTTP aparecem no Prometheus.
5. Um lançamento identificado por correlação sanitizada pode ser seguido de Transactions, Outbox/RabbitMQ até Consolidation, sem dados sensíveis.
6. Indisponibilidade do Collector **não impede** registrar lançamento financeiro (teste controlado com comerciante descartável).
7. Rastreabilidade do código testado e relatório sanitizado.

**Não existe PASS automático** enquanto os itens acima não tiverem resultados reais e o manifesto continuar `observability: NOT_RUN`. A verificação deve ser realizada após CI verde. A alteração de dependências .NET e de imagens do Compose também precisa passar por build/testes antes do merge.

**Nota de bootstrap:** o Grafana exige `CASHFLOW_GRAFANA_PASSWORD` no ambiente ou no `.env` local ignorado pelo Git. No primeiro bootstrap, as credenciais de serviço Keycloak exigidas pelas APIs também precisam estar configuradas conforme o README. Não rodar `docker compose down --volumes` sobre dados relevantes.
