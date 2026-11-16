# Trilha 04 | Backup e restauração PostgreSQL local (etapa 3)

> Status: procedimento automatizado de verificação implementado; restauração real, tempos de recuperação e integridade financeira ainda não homologados.

## Proteções

O script `.github/scripts/local_backup_restore_check.py` aceita apenas `transactions` ou `consolidation`. Por padrão executa apenas preview, sem conectar ao Docker. A execução real exige `--execute` e requer o projeto Compose CashFlow.

O fluxo gera um dump customizado `pg_dump -Fc` do banco financeiro escolhido para arquivo temporário anônimo, cria **exclusivamente** `cashflow_transactions_restore_check` ou `cashflow_consolidation_restore_check` se o nome estiver livre, restaura com `pg_restore --exit-on-error`, verifica a existência de tabelas no schema public e remove **somente** o banco de verificação que o próprio script criou. Nunca usa `--clean`, nunca restaura nos bancos da aplicação e não executa `dropdb` sobre um banco original.

Se o banco temporário já existir, o script se recusa a modificá-lo. Um crash ou erro de cleanup pode deixar o banco temporário, que exigirá inspeção manual antes de nova execução. Os dados do backup podem conter informações sensíveis, por isso usar exclusivamente dados descartáveis de desenvolvimento, não publicar o conteúdo da memória, arquivos temporários ou logs de banco. O relatório resumido sai em stdout.

## Execução no terminal local, a partir da raiz do repositório

```bash
docker compose ps
python3 .github/scripts/local_backup_restore_check.py --database transactions
python3 .github/scripts/local_backup_restore_check.py --database consolidation
```

Após verificar que o Docker aponta para o Compose local correto, com dados exclusivamente de teste:

```bash
python3 .github/scripts/local_backup_restore_check.py --database transactions --execute
python3 .github/scripts/local_backup_restore_check.py --database consolidation --execute
```

Cada execução retorna `RESTORE_VERIFIED` apenas quando o dump é produzido, o `pg_restore` conclui com êxito, existem tabelas restauradas e a remoção do banco temporário é concluída. O relatório inclui bytes do dump e duração total. Isso é uma **verificação estrutural**, não equivale à comprovação da correção dos saldos, de todos os registros ou do RPO/RTO requerido.

Para validar a ferramenta sem acessar Docker:

```bash
python3 -m unittest discover -s .github/scripts -p 'test_local_backup_restore_check.py'
```

## Evidências ainda necessárias

- Comparar contagem e checksums lógicos de transações, Outbox, auditoria, saldos diários e eventos processados entre banco de origem e cópia, com dados descartáveis e consultando tabelas reais.
- Medir RPO e RTO, simular falha após o backup, validar reprocessamento de eventos e assegurar ausência de duplicidade.
- Exercitar backup/restauração do banco Keycloak separadamente, com cuidado para não expor credenciais.
- Executar benchmarks e drills de resiliência do Compose, validar SLO de 95% das projeções em até 30 segundos.
- Verificar observabilidade, retenção de auditoria e smoke E2E de login e revogação.

A aprovação da CI confirma somente os testes unitários das proteções. Não comprova a restauração real ou homologação da Trilha 04. A Azure continua futura.
