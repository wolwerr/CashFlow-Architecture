# Observabilidade OTLP local (opt-in)

Esta stack estende o Compose do CashFlow **sem substituir os seis serviços do MVP**. Não é implantada no ambiente Azure. Adiciona Collector, Prometheus, Loki, Tempo e Grafana. Não execute em produção nem com dados sensíveis.

## O que a implementação entrega

- Instrumentação OpenTelemetry opt-in nas APIs Transactions e Consolidation: traces HTTP, chamadas HTTP de saída, métricas HTTP e logs de `ILogger` via OTLP.
- Collector recebe OTLP gRPC/HTTP, exporta traces ao Tempo, logs ao Loki e métricas no formato Prometheus.
- Prometheus coleta métricas, Grafana recebe três datasources locais configurados automaticamente.
- Ninguém precisa criar credenciais cloud. Grafana exige senha local única; porta 3000 acessível apenas via loopback.

**Limites atuais:** a instrumentação dos workers Outbox/Consumer e a propagação W3C de `traceparent` por RabbitMQ ainda devem ser demonstradas/integradas. Os exporters/consumidores de métricas de RabbitMQ, PostgreSQL e NGINX não estão incluídos nesta fase. Não marcar `observability` como PASS apenas porque os contêineres iniciaram. A coleta do Log da API via OTLP deve ser verificada no Loki, e o fluxo completo da transação até o consumidor precisa de prova independente.

## Iniciar opcionalmente no Windows PowerShell

```powershell
git checkout main
git pull origin main

# Nao grave esta senha em issues ou no repositorio.
$env:CASHFLOW_GRAFANA_PASSWORD = Read-Host "Senha local do Grafana" -MaskInput
$env:CASHFLOW_OTEL_ENABLED = "true"

docker compose -f compose.yaml -f compose.observability.yaml config --quiet
docker compose -f compose.yaml -f compose.observability.yaml up -d --build
docker compose -f compose.yaml -f compose.observability.yaml ps

# Nao remover volumes. Os dados financeiros e de telemetria ficam preservados.
```

Acesse `http://localhost:3000` com usuário `admin` e a senha configurada, e examine as fontes de dados provisionadas. Execute chamadas locais de leitura às APIs e confirme a chegada de métricas HTTP e logs no Grafana. Consulte os logs do Collector **localmente, sem publicá-los**:

```powershell
docker compose -f compose.yaml -f compose.observability.yaml logs --tail=70 otel-collector
```

Ao terminar, limpe os valores temporários do terminal:

```powershell
Remove-Item Env:CASHFLOW_GRAFANA_PASSWORD -ErrorAction SilentlyContinue
Remove-Item Env:CASHFLOW_OTEL_ENABLED -ErrorAction SilentlyContinue
```

### Parar apenas a stack extra

```powershell
docker compose -f compose.yaml -f compose.observability.yaml stop grafana prometheus loki tempo otel-collector
```

Se quiser voltar ao Compose sem exporters, defina `$env:CASHFLOW_OTEL_ENABLED = "false"` e recrie **somente** Transactions/Consolidation usando `docker compose -f compose.yaml up -d --force-recreate --no-deps transactions consolidation`. Não execute `docker compose down --volumes` e não apague os volumes do CashFlow.

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
