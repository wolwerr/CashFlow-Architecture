# Validação local de carga de leitura

A ferramenta `.github/scripts/local_load_probe.py` exercita apenas consultas ao
consolidado. O endereço padrão é `http://127.0.0.1:8080` e os proxies do
sistema são ignorados, evitando o atraso observado no Python no Windows.

## Autenticação

O Keycloak local emite access tokens com validade configurada de 300 segundos.
Para executar a carga de 300 segundos, utilize um access token e um refresh token
da **mesma sessão** do cliente público `cashflow-spa`. O script renova o token
localmente, em memória, 30 segundos antes de expirar. Não é necessário alterar
a duração dos tokens do realm ou habilitar o password grant.

Não cole tokens em issues, mensagens, logs ou relatórios. O relatório não contém
tokens e fica em `reports/load-local.json`, ignorado pelo Git.

## PowerShell

```powershell
$env:CASHFLOW_LOAD_TOKEN = Read-Host "Access token"
$env:CASHFLOW_LOAD_REFRESH_TOKEN = Read-Host "Refresh token"

try {
    python .github/scripts/local_load_probe.py `
        --merchant-id $merchantId `
        --date "2026-11-17" `
        --rate 50 `
        --duration 300 `
        --workers 64 `
        --timeout 5
    Write-Host "Codigo de saida: $LASTEXITCODE"
} finally {
    Remove-Item Env:CASHFLOW_LOAD_TOKEN -ErrorAction SilentlyContinue
    Remove-Item Env:CASHFLOW_LOAD_REFRESH_TOKEN -ErrorAction SilentlyContinue
}
```

Para ensaios curtos, o refresh token é opcional, desde que a validade
restante do access token ultrapasse a duração do ensaio mais 30 segundos.
Sem esse requisito, o script encerra antes de iniciar a carga.

## Interpretação

O relatório contém p50, p95 e p99 para HTTP 2xx, distribuição de status HTTP,
atraso no agendamento, duração do agendamento, drenagem, throughput entregue
e conclusão das requisições por segundo. Verifique o throughput observado,
além da quantidade enviada, para avaliar sustentação da carga.

Critérios do teste local: falhas até 5%, p95 abaixo de 500 ms,
envio de todas as requisições previstas e agendamento dentro da janela,
com pequena tolerância de 1 segundo. O p95 de 500 ms é SLO adicional do MVP.

Testes offline executáveis sem containers:

```powershell
python -m unittest discover -s .github/scripts -p 'test_local_load_probe.py'
```

O cenário de escrita é uma validação separada e não é demonstrado por esta carga de leitura.

## Resultado local informado em 17/11/2026

**Resultado: PASS, código de saída 0**, conforme saída de terminal fornecida pelo responsável pelo projeto. Ambiente: Docker local no Windows, endpoint GET de Consolidation via 127.0.0.1, 64 workers, timeout de 5 s. Relatório bruto: `reports/load-local.json` na máquina de execução (não versionado). Não constitui prova de desempenho em Azure/produção.

| Indicador | Medição |
| --- | ---: |
| Taxa solicitada / entregue | 50 / 50,00 RPS |
| Janela / duração real | 300 / 300,001 s |
| Previstas / enviadas / bem-sucedidas | 15.000 / 15.000 / 15.000 |
| Falhas / agendamentos perdidos | 0 / 0 |
| HTTP 200 | 15.000 |
| p50 / p95 / p99 | 5,68 / 27,91 / 29,70 ms |
| p95 do atraso de agendamento | 0,59 ms |
| Drenagem | 0,020 s |
| Data de captura UTC | 2026-11-17T17:01:19.520578+00:00 |

**Escopo comprovado:** carga de leitura autenticada do saldo diário sob condições locais, com menos de 5% de falhas e p95 inferior ao SLO adicional de 500 ms. Não demonstra carga de escrita, idempotência sob carga, escalabilidade horizontal ou performance em produção. Preservar o JSON original e vincular o SHA local da execução no manifesto de aceite para rastreabilidade completa.
