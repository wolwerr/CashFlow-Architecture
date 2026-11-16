# Trilha 04 | Observabilidade e medição de projeção

> Implementação do medidor e logs HTTP, não homologação do SLO. Azure permanece futura.

## Telemetria HTTP

Transactions e Consolidation registram no logger .NET, em formato parametrizado, método, nome do endpoint, status HTTP, latência (ms) e identificador interno do trace. Não registram `Authorization`, cookies, corpos, UUID de merchant, query string nem chaves de idempotência.

Os logs são úteis para diagnóstico local mas não equivalem a rastreamento distribuído fim a fim. Rever o nível de log e a política de retenção antes de ambientes não locais.

## Medidor de SLO de projeção

`.github/scripts/local_projection_slo.py` mede o intervalo **após o HTTP 201** de um POST CREDIT até o aumento do `credit_total` no endpoint de consolidação. O indicador de projeto é **pelo menos 95% dos registros observáveis em até 30 segundos**. A experiência exige **no mínimo 20 amostras**, e o script faz uma transação real de **R$ 0,01 por amostra**, exclusivamente em um **merchant isolado com dados descartáveis, sem outros escritores ou processamento pendente**. O experimento não mede o tempo anterior ao HTTP 201, e não prova causalidade evento a evento se houver escritas concorrentes.

O script fixa o host em `127.0.0.1:8080`, exige UUID e data válidos, não grava dados em arquivo, e não envia nenhuma transação sem `--execute`. O JWT fica apenas em variável de ambiente e não aparece no relatório. HTTP 401/403/503 interrompe o experimento; POST ambíguo **não é repetido**, para evitar dupla criação.

### Execução

```bash
# Primeiro, sem escrita:
python3 .github/scripts/local_projection_slo.py \
  --merchant-id "68a5baac-d97f-45b4-9119-b4c5b9de7c9b" \
  --date "2026-11-16"

# Após provisionar um merchant EXCLUSIVO de teste e validar access token:
export CASHFLOW_SLO_TOKEN="<token-de-teste-valido>"
python3 .github/scripts/local_projection_slo.py \
  --merchant-id "<uuid-do-merchant-isolado>" \
  --date "2026-11-16" \
  --samples 20 --poll-seconds 0.5 --execute
unset CASHFLOW_SLO_TOKEN
```

Os UUIDs e datas são ilustrativos, adequar ao ambiente. Cada amostra pode consumir até 30 segundos. Se o token expirar durante o experimento, os resultados deixam de ser válidos e é necessário obter token com duração suficiente. Não usar merchants reais, valores financeiros de produção nem credenciais privilegiadas.

Testes offline, executados pela CI:

```bash
python3 -m unittest discover -s .github/scripts -p 'test_local_projection_slo.py'
```

## Evidência e limitações

Registrar data, commit, configuração Docker, hardware, estado do broker, worker, volume de eventos, tempo de expiração da sessão, contagem de amostras e resultado. A saída resume percentual no prazo e p95 observado, mas não contém IDs e não se destina a auditoria financeira.

Um resultado PASS deste experimento **não** substitui: testes de carga 50 RPS, replay da Outbox, autorização revogada com Keycloak real, backup/restauração com conferência lógica de saldos, política de retenção da auditoria de 365 dias, métricas de fila e alertas, nem E2E da SPA. Para prova rigorosa de latência de ponta a ponta, instrumentar uma correlação event_id/transaction_id entre registro e projeção, pois o delta de saldo em merchant isolado é uma aproximação observacional.

A Trilha 04 só se encerra com evidência real desses cenários, não apenas CI verde.
