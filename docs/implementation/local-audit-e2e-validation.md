# Trilha 04 | Auditoria, retenção e homologação integrada local

> Implementação da inspeção de auditoria: concluída no código. Evidências reais e homologação do MVP: pendentes. Sem Azure nesta fase.

## Decisão de auditoria vigente

Conforme `docs/architecture/mvp-decisions.md` (decisões 07 e 13), a auditoria básica cobre **lançamentos confirmados** e conserva ação, transaction_id, merchant_id, actor_id, instante UTC e correlation_id, na mesma transação de negócio. A retenção inicial é de **365 dias**, sujeita à revisão legal e de LGPD.

O fluxo de criação já grava `TransactionAuditEntity` no commit conjunto de Transaction, Outbox e IdempotencyRecord. **Não criaremos rotina de expurgo automática nesta etapa**: o prazo de retenção não é uma autorização para apagar fatos financeiros nem prova de que obrigações legais foram revisadas.

## Inventário local não destrutivo

A ferramenta `.github/scripts/local_audit_inventory.py` inspeciona apenas `cashflow_transactions` no Compose local, sem incluir IDs, valores, tokens ou dados pessoais no resultado. O código usa consultas SQL literais e executa somente SELECT:

- Total de registros de auditoria.
- Registros com `occurred_at_utc` anterior a 365 dias.
- Transações sem auditoria CREATED correspondente ao mesmo transaction_id, merchant_id e actor_id.
- Auditorias sem transação correspondente.
- Auditorias sem correlation_id válido.

### Execução

```bash
# Preview: nenhuma consulta é executada
python3 .github/scripts/local_audit_inventory.py

# Apenas no ambiente local com dados descartáveis
docker compose ps
python3 .github/scripts/local_audit_inventory.py --execute
```

Resultados: `INTEGRITY_CHECK_PASSED` significa que as contagens verificadas não mostraram inconsistência; `INTEGRITY_FAILED` exige investigação; `RETENTION_REVIEW_REQUIRED` significa que existem registros com mais de 365 dias que exigem política aprovada. Nenhum deles autoriza apagar transações ou auditorias. O código de saída é diferente de zero se houver falha de integridade ou necessidade de revisão de retenção.

A verificação por contagens é um primeiro controle, **não é prova de imutabilidade**, ausência de manipulação administrativa, correção de todos os eventos, conformidade legal ou retenção automatizada.

Testes offline na CI:

```bash
python3 -m unittest discover -s .github/scripts -p 'test_local_audit_inventory.py'
```

## Roteiro de homologação E2E local do CashFlow

Usar apenas dados de teste, documentar commit, SO, versões Docker, especificações CPU/RAM, data, serviços e resultado dos comandos sem divulgar credenciais ou dados pessoais. **Não marcar PASS sem evidência observada.**

| Cenário | Ação real no ambiente local | Evidência necessária |
| --- | --- | --- |
| Inicialização | `docker compose up --build -d`, verificar `docker compose ps` | Todos os componentes operacionais, health checks respondendo |
| Login | Entrar pela SPA via Keycloak PKCE; sair e renovar | Login/logout/refresh sem persistência de tokens no storage |
| Autorização | Provisionar dois merchants e vínculos distintos; revogar um | HTTP 403 sem vazamento entre merchants; 503 fail-closed com Keycloak fora |
| Registro | Criar CREDIT e DEBIT, usar mesma Idempotency-Key em repetição | Um único lançamento, uma Outbox e uma auditoria por registro confirmado |
| Projeção | Consultar saldo e histórico antes e após consumer RabbitMQ | Consistência eventual, saldos BRL corretos, nenhum débito/crédito duplicado |
| Carga | Rodar medidor `local_load_probe.py` | 50 RPS por 300s, p95 < 500 ms, até 5% de falhas na janela |
| Frescor | Rodar `local_projection_slo.py` com merchant isolado | Mínimo 20 amostras, >=95% visíveis em até 30s |
| Resiliência | Drills RabbitMQ, Consolidation e Keycloak; recuperar | Outbox reaplicada sem duplicidade, nenhuma autorização indevida |
| Backup | Rodar `local_backup_restore_check.py` | Restauração estrutural; comparação lógica e RPO/RTO separadamente |
| Auditoria | Rodar `local_audit_inventory.py --execute` | Sem divergências em registros confirmados, revisão de retenção documentada |
| Observabilidade | Conferir logs e métricas de componentes e broker | Sem segredos, correlação verificável, telemetria suficiente para falhas |

Os procedimentos automatizados de carga, interrupção, backup e projeção são ferramentas de **experimento local**, não substitutos de homologação. Alguns efetuam alterações reais ou paradas temporárias, por isso precisam de autorização e ambiente descartável. Azure/AKS, deploy e aprovação de PROD continuam futuros.

## Lacunas que permanecem

- Não há execução E2E real demonstrada neste PR.
- Controle de acesso a auditoria, prevenção contra alterações por administrador e eventual armazenamento imutável exigem desenho operacional.
- O prazo de retenção/expurgo depende de revisão legal e deve ser implementado separadamente, com proteção da rastreabilidade.
- Métricas de broker, alertas, coleta centralizada de logs e rastreamento distribuído ainda precisam de evidência.
- Bancos Keycloak e backup da mensageria requerem estratégia e testes de recuperação próprios.
