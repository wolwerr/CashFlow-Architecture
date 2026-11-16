# CashFlow | Plano acelerado por trilhas verticais

> Executar uma funcionalidade de ponta a ponta em um PR por trilha, em vez de PR por classe ou camada.
> Desenvolvimento e testes LOCAL/CI. Azure permanece futuro.

## Trilha 01 | Registro financeiro seguro (PR #34)

**Pacote de entrega:** Keycloak JWT + vínculos atuais de merchants (ADMIN/OPERATOR) + endpoint POST v1 + HTTP 201/401/403/409/422/503 + Idempotency-Key + domínio e data São Paulo + PostgreSQL + auditoria + Outbox no commit + testes de API/Keycloak simulado/PostgreSQL + CI/Sonar + documentação de bootstrap.

**Critério de conclusão:** CI verde; teste local real Keycloak com usuário autorizado, usuário de outro merchant, revogação, Keycloak fora e indisponibilidade de PostgreSQL; nenhum acesso sem verificação. **Não concluir apenas por build verde.**

**Limite:** ainda não há interface React integrada nem entrega do evento pelo broker.

## Trilha 02 | Publicação e projeção resiliente

**Pacote:** BackgroundService Outbox com claim concorrente, publish confirms/mandatory, retry/backoff e DLQ de acordo com contrato operacional; consumer Consolidation idempotente; migrations DailyBalance/ProcessedEvent; saldo diário e acumulado na leitura; HTTP GET /api/v1/consolidation/daily; teste real de RabbitMQ/PostgreSQL, duplicidade, falha e recuperação. Consolidation e Transactions continuam independentes.

**Critério de conclusão:** replay não duplica saldo; ACK somente após commit; falha de Consolidation não bloqueia registro; consulta dia vazio/retroativo está correta.

## Trilha 03 | Experiência de usuário

**Pacote:** React OIDC Authorization Code + PKCE, token somente em memória, seleção de merchant autorizado, formulário BRL, data America/Sao_Paulo, POST com Idempotency-Key, respostas/erros, histórico GET paginado e NGINX rotas v1. Testar login/logout/refresh e isolamentos no navegador, sem persistir access tokens no browser storage.

**Critério de conclusão:** usuário consegue registrar e consultar lançamentos e saldo desde o navegador local, com isolamento entre comerciantes.

## Trilha 04 | Qualidade e hardening do MVP local

**Pacote:** teste de 50 RPS por cinco minutos, p95 < 500ms em consultas válidas, <=5% falhas, SLO 95% projeção até 30s, recuperação de broker/IAM e DB, auditoria 365 dias, observabilidade básica, revisão Sonar, recuperação local de backups e roteiro de demonstração.

**Critério de conclusão:** evidência de testes e relatório; nenhuma métrica é presumida. Azure e promoção em environments futuras.

**Progresso Trilha 04:** instrumento de carga local e testes automatizados documentados em [Hardening e evidências de carga](local-hardening-load.md). O benchmark real, as simulações de recuperação, auditoria, observabilidade e SLO de projeção continuam pendentes de execução e evidência.

**Progresso complementar Trilha 04:** executor de falhas locais e testes de segurança do procedimento documentados em [Drills de recuperação local](local-recovery-drills.md). Os cenários financeiros reais e os SLOs permanecem pendentes de comprovação.

## Regra de trabalho

- Criar **uma branch e um PR por trilha**, com testes e documentação juntos.
- Não fragmentar por camada (Domain/Application/Infrastructure/API). Corrigir CI no próprio PR antes de solicitar merge.
- Não usar módulos de autorização extras: Keycloak Groups segundo ADR-006.
- Registrar pendências de dependências externas (provisionamento Keycloak, rede, credenciais), sem substituí-las por mocks permissivos em runtime.
