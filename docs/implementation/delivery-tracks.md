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

**Progresso complementar Trilha 04:** verificador estrutural de backup/restauração local PostgreSQL, sem sobrescrever bancos da aplicação, descrito em [Backup e restauração local](local-postgres-backup-verification.md). Execução real, integridade financeira, RPO/RTO e observabilidade continuam pendentes.

**Progresso complementar Trilha 04:** logs HTTP estruturados e medidor opt-in da latência de projeção, documentados em [SLO e observabilidade local](local-slo-observability.md). Ainda é necessária execução real do SLO, revisão de auditoria, métricas de broker e homologação E2E.

**Progresso complementar Trilha 04:** inventário não destrutivo de auditoria e roteiro integrado de homologação local, descritos em [Auditoria e E2E](local-audit-e2e-validation.md). Os testes reais, comprovação de retenção, segurança e performance ainda precisam de execução com evidências.

## Trilha 05 | Integração E2E e aceite funcional local

**Pacote:** teste opt-in pelo NGINX real com dois comerciantes descartáveis e tokens Keycloak distintos. Exercitar HTTP 401/403 entre lojas, POST 201 com `Idempotency-Key`, replay com mesmo ID, conflito 409, CREDIT R$ 100 / DEBIT R$ 25, histórico paginado e saldo projetado em Consolidation (+R$ 75 em relação ao baseline). Versão offline do probe na CI, documentação dos passos manuais de login/gestão/reativação, sem executar mutações em GitHub Actions.

**Implementação:** [Runbook e script da Trilha 05](trilha-05-local-e2e.md), `.github/scripts/local_e2e_mvp.py` e respectivos testes unitários. A CI verifica o probe; homologação integrada com duas contas reais permanece **NOT RUN** até haver evidência coletada no Docker Compose local.

**Critério de conclusão:** checks automatizados e pipeline verdes **mais** evidências reais E2E e navegador. Projeção em um ensaio de 30 s não substitui SLO estatístico; carga e hardening continuam nas trilhas seguintes.

## Trilha 06 | Segurança e consistência Keycloak (PR #53)

**Integrado:** fail-closed de vínculos conflitantes, compensação controlada de usuários recém-criados, nome de usuário editável sem troca de identidade e listagem por usuário. A homologação com falhas reais de Keycloak e reconciliação permanece pendente.

## Trilha 07 | Resiliência, observabilidade e desempenho local

**Pacote:** corrigir avisos recorrentes de Sonar e runtime Node.js 20 no workflow; adicionar snapshot opt-in, read-only, de serviços Compose e profundidade das filas RabbitMQ. Consolidar num runbook as ferramentas já existentes de carga 50 RPS/300 s, SLO de projeção 95%/30 s, recovery drills, backup/restauração e seus requisitos de evidência.

**Implementação:** [Runbook Trilha 07](trilha-07-resilience-observability.md). **Não executar falhas, tráfego de carga ou lançamentos automaticamente na CI.** Encerrar somente quando os testes automatizados e as evidências reais locais forem aprovados.

## Trilha 08 | Homologação integrada e aceite do MVP local

**Pacote:** consolidar a matriz E2E, testes reais de isolamento e idempotência, Keycloak, RabbitMQ, Consolidation, PostgreSQL, auditoria, backup, observabilidade, carga e SLO em um único manifesto de aceite. Gerador offline começa em `NOT_RUN`, exige evidências locais para PASS e não permite declarar homologação com FAIL/BLOCKED/NOT_RUN.

**Implementação:** [Runbook da Trilha 08](trilha-08-local-acceptance.md), `.github/scripts/local_mvp_acceptance.py` e testes offline integrados à CI. **Execução real e aceite do MVP local permanecem pendentes** até coleta e revisão dos relatórios; o pipeline não inventa evidências nem realiza testes disruptivos.

## Trilha 09 | Fechamento técnico e prontidão para aceite local

**Pacote:** consolidar o status das nove trilhas e as pendências reais, reconciliar documentos com a ADR-006 (três bancos lógicos, Keycloak como fonte dos vínculos), criar relatório técnico de encerramento e backlog pós-MVP, sem provisionar Azure.

**Implementação:** [Trilha 09, relatório de fechamento](trilha-09-mvp-closeout.md). Código e documentação integrados não equivalem a aceite funcional: a homologação real da Trilha 08 permanece **NOT RUN** enquanto não forem entregues o manifesto com 14 PASS e as evidências correspondentes. Uma inconsistência histórica na legenda do diagrama Deployment View precisa de renderização sincronizada e continua explicitamente pendente.

## Regra de trabalho

- Criar **uma branch e um PR por trilha**, com testes e documentação juntos.
- Não fragmentar por camada (Domain/Application/Infrastructure/API). Corrigir CI no próprio PR antes de solicitar merge.
- Não usar módulos de autorização extras: Keycloak Groups segundo ADR-006.
- Registrar pendências de dependências externas (provisionamento Keycloak, rede, credenciais), sem substituí-las por mocks permissivos em runtime.
