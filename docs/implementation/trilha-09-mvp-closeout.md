# Trilha 09 | Fechamento técnico e prontidão do MVP local

**Estado:** código e artefatos de homologação integrados; **aceite operacional ainda não demonstrado**. Este documento não é certificado de homologação nem autorização de deploy PROD. Referência da decisão: [ADR-006](../architecture/adr/ADR-006-identity-and-access.md), [ADR-009](../architecture/adr/ADR-009-deployment-cloud.md) e [Trilha 08](trilha-08-local-acceptance.md).

## Escopo e entregas verificáveis

- Dois serviços financeiros .NET 10, Transactions e Consolidation, com bancos lógicos separados e Outbox → RabbitMQ → consumidor idempotente.
- Frontend React/OIDC PKCE, proxy NGINX, Keycloak como fonte de grupos e papéis por comerciante. **Três** bancos lógicos no PostgreSQL local: Transactions, Consolidation e Keycloak. Não há banco lógico de autorização CashFlow.
- Cadastro e gestão de comerciantes/usuários no Keycloak, inclusive username, cargo, soft delete/reativação e checagem atual de autorização em operações protegidas.
- Testes unitários, de API, de integração e de arquitetura, integração com GitHub Actions e Sonar. **CI verde comprova apenas os checks executados**, não disponibilidade, segurança operacional ou metas de carga em máquina local.
- Probes locais *opt-in* para fluxo financeiro, auditoria, estado do Compose, carga, projeção, interrupções controladas e restauração. A execução real dos probes permanece responsabilidade da homologação local.

## Critérios de encerramento, sem inferir resultados

| Marco | Como comprovar | Estado na criação deste relatório |
| --- | --- | --- |
| Entregas das Trilhas 01–08 incorporadas na main | PRs e commits de merge no GitHub | Código integrado, não homologação |
| Build, testes e Sonar da Trilha 09 | Pipeline da PR atual | NOT_RUN até CI concluir |
| Manifesto de 14 cenários da Trilha 08 | `local_mvp_acceptance.py --manifest ...` com 14 PASS, SHA, arquivos reais e revisão | NOT_RUN, sem manifesto fornecido |
| OIDC, Keycloak, gestão de comerciantes e autorização | Navegador e HTTP reais no Compose local | NOT_RUN |
| Crédito, débito, idempotência, Outbox, histórico e saldo | E2E com duas contas distintas e dados descartáveis | NOT_RUN |
| Falhas/recuperação de Keycloak, RabbitMQ e Consolidation | Drills reais com integridade pós-recuperação | NOT_RUN |
| PostgreSQL, auditoria e backup/restore | Evidências de integridade e restauração independente | NOT_RUN |
| Carga 50 RPS/300 s, p95 < 500 ms e falhas <= 5% | Probe real com dados de ambiente | NOT_RUN |
| SLO de projeção 95% em 30 s com ao menos 20 amostras | Medidor real de projeção | NOT_RUN |
| Revisão e sincronização de diagramas | PlantUML e PNG correspondente, sem contradições | BLOCKED: legenda do Deployment View historicamente cita quarto banco |

**Critério final:** somente declarar `MVP LOCAL HOMOLOGADO` quando o manifesto da Trilha 08 contiver 14 cenários PASS, evidências sanitizadas revisadas, commit efetivamente testado e nenhuma pendência bloqueante. Este relatório não deve ser preenchido retroativamente a partir do sucesso da CI.

## Ordem de encerramento operacional

1. Após o merge, atualizar a `main` local. Preservar volumes e identidades já existentes. Usar lojas e dados financeiros descartáveis para os ensaios.
2. Executar o [runbook de aceite](trilha-08-local-acceptance.md), seguindo os procedimentos opt-in das Trilhas 05–07 e os relatórios de auditoria e backup.
3. Gerar `local-acceptance.json` local e mantê-lo fora do Git, junto aos arquivos de evidência sanitizados. O relatório verifica existência dos arquivos, não sua autenticidade.
4. Revisar cada FAIL/BLOCKED/NOT_RUN antes do aceite. Não reexecutar POST financeiro às cegas quando uma resposta for ambígua.
5. Corrigir e renderizar novamente o diagrama [Deployment View](../architecture/plantuml/deployment-view.puml) e seu PNG antes de declarar a documentação visual sincronizada.
6. Registrar SHA, ambiente, hardware, data, responsável pela revisão e decisão final; abrir novas issues/PRs para qualquer lacuna técnica não encerrada.

## Backlog pós-MVP (fora do escopo local atual)

| Prioridade | Tema | Condição ou decisão necessária |
| --- | --- | --- |
| Antes de produção | Segurança de Keycloak | MFA, política de senhas, convites, recuperação de contas, auditoria administrativa e reconciliação de provisionamento parcial |
| Antes de produção | Dados e retenção | Políticas de expurgo/replay de Outbox/ProcessedEvent, acesso e retenção legal de auditoria, backup e restore de Keycloak e mensageria |
| Antes de produção | Infraestrutura | TLS, secrets, segregação DEV/HOM/PROD, aprovação humana em Environments, network policies, DR e observabilidade operacional |
| Antes de produção | Capacidade e custos | Reavaliar preços e dimensionamento do Azure, HA, HPA, PDB, zonas, armazenamento e RPO/RTO mediante testes |
| Próxima versão funcional | Estorno (RF-06) | Desenho contábil e auditável, sem exclusão/edição de lançamentos confirmados |
| Próxima versão funcional | Cadastro empresarial ampliado | CNPJ, contatos e dados comerciais em domínio próprio se houver requisito real |

**Azure:** AKS em East US, DEV/HOM/PROD/SHARED e estimativas de custo continuam apenas **arquitetura futura**. Nenhum recurso deve ser criado nesta trilha.

## Documentos-fonte

- [Plano de trilhas](delivery-tracks.md)
- [Runbook Trilha 08](trilha-08-local-acceptance.md)
- [Runbook Trilha 07](trilha-07-resilience-observability.md)
- [Decisões consolidadas](../architecture/mvp-decisions.md)
- [Deployment View](../architecture/deployment-view.md)
- [ADR-006](../architecture/adr/ADR-006-identity-and-access.md)
- [ADR-009](../architecture/adr/ADR-009-deployment-cloud.md)
