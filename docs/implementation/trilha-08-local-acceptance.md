# Trilha 08 | Homologação integrada e aceite do MVP local

**Status inicial:** NÃO HOMOLOGADO. A aprovação da CI comprova build, testes automatizados e análise de qualidade, mas não substitui a execução local contra Keycloak, PostgreSQL, RabbitMQ, NGINX e navegador. Este documento é o plano de homologação, não uma declaração de conclusão. Sem Azure.

## Sequência de trabalho

1. **Preparo seguro:** atualizar `main` somente após merge da PR, usar Docker Compose local com dados e comerciantes descartáveis, conferir `docker compose ps`, executar o snapshot read-only e registrar SHA do commit, data e recursos disponíveis. Não executar `docker compose down -v` e não apagar usuários/grupos do Keycloak para corrigir um cenário.
2. **Autenticação e cadastro:** validar no navegador Authorization Code + PKCE, login, logout, renovação do token, ausência de token em localStorage/sessionStorage; cadastrar duas lojas e usuários distintos, listar usuários em linhas separadas, verificar coluna Status, editar username, cargo, desativar e reativar. Usuário desativado deve perder acesso efetivo.
3. **Fluxo financeiro E2E:** seguir [Trilha 05](trilha-05-local-e2e.md), com dois merchants descartáveis e dois JWTs. Medir CREDIT R$ 100,00, DEBIT R$ 25,00, idempotência e segregação, incluindo replay de chave e saldo acumulado +R$ 75,00 em relação ao baseline. O script é preview por padrão; `--execute` produz lançamentos reais.
4. **Resiliência e observabilidade:** seguir [Trilha 07](trilha-07-resilience-observability.md). Capturar métrica da fila RabbitMQ, desconexões reais de Keycloak, RabbitMQ e Consolidation, convergência sem duplicidade e evidência de logs de correlação. Drills que interrompem serviços são opt-in e exigem ambiente descartável.
5. **Integridade e backup:** executar o [inventário de auditoria](local-audit-e2e-validation.md) e a [verificação de restauração](local-postgres-backup-verification.md). Conferir transações, audit trail e Outbox/ProcessedEvent conforme o roteiro, sem apagar histórico nem sobrescrever a base original.
6. **Carga e SLO:** executar somente em ambiente isolado os medidores de [carga](local-hardening-load.md) e [projeção](local-slo-observability.md). Requisitos: 50 RPS por 300 segundos; p95 **menor** que 500 ms; taxa de falhas no máximo 5%; 95% das projeções em no máximo 30 segundos, com pelo menos 20 amostras. Registrar hardware, taxa medida e janela. Não produzir tráfego na CI.
7. **Fechamento:** preencher o manifesto de aceite e conferir evidências e pendências. Qualquer `FAIL`, `BLOCKED` ou `NOT_RUN` impede declaração de MVP homologado.

## Manifesto e relatório de aceite

A ferramenta `.github/scripts/local_mvp_acceptance.py` opera **offline**, sem ler segredos e sem executar Docker ou requisições HTTP. Iniciar na raiz do projeto:

```powershell
python .github/scripts/local_mvp_acceptance.py --init local-acceptance.json
python .github/scripts/local_mvp_acceptance.py --manifest local-acceptance.json
```

O primeiro comando cria todos os 14 cenários com status `NOT_RUN`; falha se o arquivo já existir. O segundo produz JSON com `NOT_ACCEPTED` e código de saída 1 até que todas as verificações tenham `PASS`, apontem para arquivos de evidência existentes sob a pasta do manifesto, cumpram os limites numéricos nos cenários de carga e SLO e incluam o SHA completo de 40 caracteres. O programa não confere o conteúdo criptograficamente, não mede os serviços e não transforma declarações sem evidência em resultados observados.

Criar evidências sanitizadas na pasta local, por exemplo `evidence/compose-summary.json`, e alterar manualmente `status`, `evidence` e `metrics` de cada cenário após executar e revisar o teste. O manifesto de aceite deve conter `environment: LOCAL` e `commit` com o SHA que foi realmente testado. Evitar caminhos absolutos, URL externas, tokens, usernames reais, passwords, IDs privados e valores de clientes. Não colocar dados sensíveis no repositório.

Para o cenário `load_50rps_300s`, usar métricas `duration_seconds`, `request_rate_rps`, `p95_ms`, `failure_rate_pct`. Para `projection_slo`, `sample_count` e `within_30s_pct`. Apenas o relatório real do medidor autoriza preencher os valores; não inferir números do CI.

## Matriz de aceite

| Chave no manifesto | Evidência esperada |
| --- | --- |
| compose_readiness | Serviços, endpoints de saúde, dados do ambiente e SHA |
| browser_oidc | Fluxo de login/logout/refresh e armazenamento do navegador |
| merchant_management | Cadastro, nome, edição, Status, desativação e reativação |
| merchant_isolation | HTTP 401/403, vínculos Keycloak atuais e dois usuários distintos |
| financial_idempotency | HTTP 201/409, mesmo ID em replay, lançamento sem duplicidade |
| outbox_projection | RabbitMQ, convergência e saldo diário/acumulado |
| keycloak_fail_closed | Negação durante indisponibilidade, normalização após recuperação |
| rabbitmq_recovery | Outbox, recuperação do broker e saldo sem dupla aplicação |
| consumer_recovery | Lançamentos durante parada do consumer e convergência |
| postgres_restore | Dump/restore independente e integridade comparada |
| audit_integrity | Auditorias correspondentes e política de retenção revisada |
| observability | Correlação nos logs, ausência de segredos, estado e métricas |
| load_50rps_300s | Resultado real de 50 RPS/300 s, p95 e taxa de erros |
| projection_slo | 20 ou mais amostras e pelo menos 95% em 30 s |

## CI e limites de aceite

A CI executa apenas os testes **offline** do manifesto para proteger seu formato, seus thresholds e seu comportamento fail-closed:

```bash
python3 -m unittest discover -s .github/scripts -p 'test_local_mvp_acceptance.py'
```

Mantemos cobertura mínima de 80%, política High/Blocker do Sonar e testes atuais; nenhum limite será reduzido. **A CI não marca os cenários operacionais como PASS.** Se algo falhar durante a homologação local, registrar como `FAIL` ou `BLOCKED`, abrir correção e repetir o cenário. A Trilha 09 só poderá fechar o MVP após revisão desse relatório e das pendências.
