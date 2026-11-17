# Validação integrada local: RabbitMQ, consumidor e integridade de auditoria

Este cenário complementa o simples restart de RabbitMQ já informado como `RESTORED` em 17/11/2026. O restart sozinho **não valida entrega da fila ou integridade do saldo**, por isso `rabbitmq_recovery` ainda não deve ser marcado PASS com base apenas nele.

## Pré-requisitos e segurança

- Somente projeto local `cashflow-local` com os seis serviços em execução.
- **Comerciante isolado e descartável**, sem lançamentos concorrentes, com permissão OPERATOR/ADMIN.
- Gera dois lançamentos reais **append-only** de R$ 1,00: um CREDIT enquanto RabbitMQ está indisponível, um DEBIT enquanto Consolidation está parado.
- Desliga primeiro RabbitMQ e depois Consolidation, **em sequência**, nunca os dois simultaneamente. Tenta restaurar cada serviço em `finally` até em falhas; se a restauração falhar, verificar manualmente Docker.
- Não deve ser executado em produção, em ambiente compartilhado nem repetido após uma falha antes da inspeção de histórico.
- Tokens e identificadores financeiros não são incluídos nos relatórios. Use access token e refresh token da **mesma sessão** Keycloak.

## Execução PowerShell

```powershell
git checkout main
git pull origin main

$merchantId = "UUID_DO_COMERCIANTE_DESCARTAVEL"
$day = "2026-11-17"

# Preview sem qualquer impacto
python .github/scripts/local_integrated_recovery.py --merchant-id $merchantId --date $day

$env:CASHFLOW_RECOVERY_TOKEN = Read-Host "Access token" -MaskInput
$env:CASHFLOW_RECOVERY_REFRESH_TOKEN = Read-Host "Refresh token" -MaskInput
try {
    python .github/scripts/local_integrated_recovery.py `
      --merchant-id $merchantId --date $day `
      --outage-seconds 15 --recovery-timeout 120 --execute
    Write-Host "Codigo de saida: $LASTEXITCODE"
} finally {
    Remove-Item Env:CASHFLOW_RECOVERY_TOKEN -ErrorAction SilentlyContinue
    Remove-Item Env:CASHFLOW_RECOVERY_REFRESH_TOKEN -ErrorAction SilentlyContinue
}
```

## Condições para PASS

O teste exige:

1. Membership e saldo/histórico iniciais autorizados.
2. Com RabbitMQ parado, Transactions disponível e POST de CREDIT HTTP 201.
3. RabbitMQ restaurado.
4. Com Consolidation parado, Transactions disponível e POST de DEBIT HTTP 201.
5. Consolidation restaurado.
6. Replays idempotentes (mesmo ID) e conflitos (409) nos dois lançamentos.
7. Exatamente um registro de cada operação no histórico, tipos e valores corretos.
8. Totais de crédito e débito avançam R$ 1,00 cada; variação líquida zero. A projeção é observada duas vezes.
9. Inventário de auditoria PostgreSQL read-only: sem transações sem auditoria, auditorias órfãs nem IDs de correlação ausentes; verifica também política local de retenção.

Relatório: `reports/integrated-recovery-local.json`, sem credenciais ou IDs financeiros. `outcome: PASS` permite vincular o arquivo local aos cenários `rabbitmq_recovery`, `consumer_recovery` e `audit_integrity` no manifesto. A vinculação não deve ser automática se o script não foi executado.

**Limites:** não injeta uma entrega duplicada real do broker nem prova redelivery sob todos os modos de falha. Não demonstra garantia absoluta de exactly-once, RPO/RTO formal, retenção legal validada ou disponibilidade em produção. O inventário comprova consistência das relações de auditoria verificadas, não sua imutabilidade histórica ou completude de todos os campos.

Testes offline, sem Docker: `python -m unittest discover -s .github/scripts -p 'test_local_integrated_recovery.py'`.
