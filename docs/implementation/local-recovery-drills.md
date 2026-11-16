# Trilha 04 | Drill de resiliência local (etapa 2)

> Ferramenta implementada e testada com mocks. Falhas reais, integridade financeira e SLOs **ainda não homologados**.

## O que esta ferramenta faz

`.github/scripts/local_recovery_drill.py` executa, **somente com autorização explícita**, um ciclo de parada temporária e restauração de **um** dos serviços do Compose local: `rabbitmq`, `consolidation` ou `keycloak`. Não altera volumes, dados, credenciais ou serviços cloud.

- Padrão: *preview*, sem Docker e sem efeitos colaterais.
- Execução real: flag `--execute`, com interrupção entre 1 e 120 s.
- Antes de atuar, valida a presença dos serviços esperados no Compose e exige que o serviço selecionado já esteja rodando.
- Faz `docker compose stop <service>` e sempre tenta `docker compose up -d <service>` no `finally`, mesmo quando o stop falha.
- Verifica se o container voltou ao estado **running** e registra resultado em `reports/local-recovery.json` (sem credenciais).
- O estado **RESTORED** significa apenas container executando, **não prova** processamento, correção financeira nem atendimento ao SLO.

## Como executar, exclusivamente no ambiente local descartável

1. Verificar que está na raiz do repositório CashFlow e que `docker compose ps` aponta para o projeto local correto.
2. Usar somente usuários e merchants de teste, nunca dados reais.
3. Conferir o preview:

```bash
python3 .github/scripts/local_recovery_drill.py --service rabbitmq
```

4. Após a confirmação do ambiente, executar um cenário autorizado:

```bash
python3 .github/scripts/local_recovery_drill.py --service rabbitmq --outage-seconds 15 --execute
```

5. Repetir, **separadamente**, para `consolidation` e `keycloak`:

```bash
python3 .github/scripts/local_recovery_drill.py --service consolidation --outage-seconds 15 --execute
python3 .github/scripts/local_recovery_drill.py --service keycloak --outage-seconds 15 --execute
```

Os serviços podem continuar indisponíveis mesmo que o container esteja running. Inspecionar `docker compose ps`, logs das APIs e health endpoints, e recuperar manualmente qualquer cenário que não volte a funcionar. Este script não manipula PostgreSQL, pois falhas de banco e backups devem ser executados em procedimento separado e comprovadamente seguro.

## Evidências manuais que faltam para homologação

| Cenário | Ação de verificação após reinício | Critério |
| --- | --- | --- |
| RabbitMQ | Criar transação de teste durante interrupção, consultar Outbox, recuperar broker e conferir projeção | Lançamento não perdido, evento republicado uma vez logicamente, saldo sem duplicação |
| Consolidation | Criar lançamento com consumer parado, religar e consultar saldo diário | Registro continua disponível, projeção alcança saldo correto sem dupla contabilização |
| Keycloak | Executar consulta com vínculo revogado/Keycloak fora, recuperar e repetir | Fail-closed; nenhum dado de outro merchant; sucesso após restabelecimento da autorização |
| PostgreSQL | Executar plano separado de backup/restauração em dados descartáveis | Consistência transacional e evidência de restauração |

Documentar commit, instante, duração real, inicialização/recuperação, logs sem secrets, event IDs de teste, consulta de saldo antes/depois e qualquer anomalia. Registrar **FAIL** quando não houver evidência. A validação de 95% de projeções até 30 s também precisa de medição própria e não pode ser inferida do restart.

## CI

Os testes `.github/scripts/test_local_recovery_drill.py` validam preview sem Docker, bloqueio de serviços não permitidos, verificação de projeto, serviço já parado, reinício normal e recuperação mesmo quando a parada retorna erro. A CI testa o código do drill **sem parar serviços reais**.

## Limitações

- `docker compose up -d` confirma a solicitação de restauração e um container running; não confirma prontidão de APIs ou RabbitMQ.
- Um desligamento forçado do processo Python ou da máquina pode impedir a execução do `finally`; recuperar manualmente se ocorrer.
- Não executa backup, restauração de dados, testes E2E, simulações de revogação nem injeção de falha no PostgreSQL.
- Azure, deployment cloud e validações de produção permanecem fora do escopo.
