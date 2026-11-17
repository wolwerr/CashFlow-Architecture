# Evidência local: backup e restauração PostgreSQL

**Data:** 17/11/2026. **Resultado informado:** `RESTORE_VERIFIED` para os bancos Transactions e Consolidation, com limpeza dos bancos temporários confirmada.

As duas execuções foram feitas pelo responsável, usando `.github/scripts/local_backup_restore_check.py --execute` no Docker Compose local. O script cria um dump em formato customizado, restaura o conteúdo em um banco descartável distinto, verifica a existência de tabelas no schema `public` e remove o banco temporário. Ele não substitui o banco original.

| Medida | Transactions | Consolidation |
| --- | ---: | ---: |
| Status | RESTORE_VERIFIED | RESTORE_VERIFIED |
| Tamanho do backup | 17.732 bytes | 6.195 bytes |
| Tabelas restauradas | 5 | 3 |
| Tempo total informado | 0,815 s | 0,841 s |
| Limpeza do banco temporário | true | true |
| Captura UTC | 2026-11-17 17:29:03.263522 | 2026-11-17 17:29:11.813125 |

**Escopo da evidência:** o `pg_dump` e o `pg_restore` concluíram sem erro; o banco restaurado continha tabelas e seu descarte foi bem-sucedido. **Não comprova recuperação de desastre completa, integridade de todas as linhas, aplicação após restauração, restauração do Keycloak, retenção histórica ou RPO/RTO de produção.** O tempo informado é a execução desse procedimento local e não um RTO formal.

Registrar capturas sanitizadas, SHA do código testado e características do host no manifesto de aceitação. Não publicar dumps, credenciais ou dados de comerciantes.
