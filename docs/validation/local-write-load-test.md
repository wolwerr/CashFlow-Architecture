# Carga concorrente de escrita, somente ambiente local

Este teste é **complementar** à carga de 50 GET/s já aprovada. Ele cria
lançamentos financeiros reais e append-only, portanto use exclusivamente
um comerciante **local, descartável, sem outras operações simultâneas**.
Não executar em produção, nem repetir após erro sem conferir o histórico.

Script: `.github/scripts/local_write_load_probe.py`. Sem `--execute`
mostra somente um preview, sem autenticação ou gravação.

## Garantias e limites

- Antes de escrever, valida a membership, consulta saldo e histórico.
- Executa por padrão 12 créditos e 8 débitos de R$ 1,00 em concorrência,
  cada um com chave de idempotência única.
- Exige HTTP 201 de todos os POSTs e IDs distintos, sem retry automático.
- Após confirmação, reapresenta a primeira chave com payload igual (HTTP 201
  com ID anterior) e diferente (HTTP 409).
- Verifica exatamente 20 IDs distintos no histórico e atualização do saldo:
  créditos +R$12, débitos +R$8 e saldo líquido +R$4.
- Aguarda a projeção assíncrona e confirma que o resultado permanece estável.
- Nunca registra tokens, chaves de idempotência, IDs de comerciante ou transações
  no relatório `reports/write-load-local.json` (arquivo local ignorado pelo Git).
- Não demonstra automaticamente 50 **escritas** por segundo; mede throughput
  real do lote, separado da exigência de 50 **consultas** por segundo.

## PowerShell

```powershell
git checkout main
git pull origin main

python .github/scripts/local_write_load_probe.py `
  --merchant-id $merchantId --date "2026-11-17"

$env:CASHFLOW_WRITE_TOKEN = Read-Host "Access token"
# Refresh token da mesma sessao OIDC, caso necessario:
$env:CASHFLOW_WRITE_REFRESH_TOKEN = Read-Host "Refresh token"
try {
    python .github/scripts/local_write_load_probe.py `
      --merchant-id $merchantId `
      --date "2026-11-17" `
      --credits 12 --debits 8 --workers 8 `
      --recovery-timeout 90 --execute
    Write-Host "Exit code: $LASTEXITCODE"
} finally {
    Remove-Item Env:CASHFLOW_WRITE_TOKEN -ErrorAction SilentlyContinue
    Remove-Item Env:CASHFLOW_WRITE_REFRESH_TOKEN -ErrorAction SilentlyContinue
}
```

Testes offline:

```powershell
python -m unittest discover -s .github/scripts -p "test_local_write_load_probe.py"
```

**Se houver erro ou timeout**, não execute o teste outra vez automaticamente:
parte dos lançamentos pode ter sido persistida. Confira o saldo e o histórico
do comerciante descartável antes de decidir por um novo ensaio. A operação
financeira não é revertida.

## Evidência de execução local, 17/11/2026

**PASS (código 0)** na execução informada pelo responsável. Cenário de 12 CREDIT e 8 DEBIT, R$ 1,00 cada, disparados de forma concorrente contra comerciante local descartável. Relatório bruto local: `reports/write-load-local.json`, não versionado.

| Métrica | Resultado |
| --- | ---: |
| Confirmadas / únicas | 20 / 20 |
| Créditos / débitos | 12 / 8 |
| Duração do lote de escrita | 0,131 s |
| Throughput medido no lote | 152,63 escritas/s |
| p95 de escritas | 44,64 ms |
| Replay idempotente / conflito de chave | PASS / HTTP 409 |
| Histórico único / projeção estável exatamente uma vez | PASS / PASS |
| Variação esperada do saldo líquido | +R$ 4,00 |
| Captura UTC | 2026-11-17T17:11:30.539243+00:00 |

Os resultados são **evidência local informada, não execução da CI**. O throughput do lote curto **não equivale a throughput sustentado de escrita**, e não substitui o ensaio de 50 consultas/s registrado em [carga de leitura](local-load-test.md). Para rastreabilidade auditável, associar o relatório sanitizado, o SHA do código testado e recursos da máquina no manifesto de aceite. Não inclui validação em Azure ou produção.
