# CashFlow Architecture

MVP de fluxo de caixa multicomerciante, desenvolvido **localmente** com .NET 10, React/TypeScript, Keycloak, PostgreSQL, RabbitMQ e NGINX. As APIs Transactions e Consolidation têm bancos financeiros distintos e se comunicam de forma assíncrona via Outbox e RabbitMQ. O Keycloak é a fonte dos usuários e dos vínculos ADMIN/OPERATOR por loja, sem quarto banco de autorização.

## Começar localmente

Pré-requisitos: Docker Compose, ambiente local descartável e segredos de desenvolvimento configurados conforme [bootstrap e cadastro de comerciantes](docs/implementation/merchant-onboarding.md).

```bash
docker compose up --build -d
docker compose ps
```

A aplicação local usa `http://localhost:8080`; o Keycloak local usa `http://localhost:8081`. **Não execute `docker compose down -v`** se precisar manter os dados. Para ambientes existentes, importar novamente o realm não atualiza automaticamente suas configurações.

## Arquitetura e decisões

- [Arquitetura e diagramas](docs/architecture/building-block-view.md)
- [ADRs, identidade e autorização](docs/architecture/adr/ADR-006-identity-and-access.md)
- [Decisões consolidadas do MVP](docs/architecture/mvp-decisions.md)
- [Visão de implantação LOCAL e Azure futura](docs/architecture/deployment-view.md)
- [Plano de entrega por trilhas](docs/implementation/delivery-tracks.md)

## Testes e fechamento do MVP

- [Trilha 05, fluxo financeiro E2E](docs/implementation/trilha-05-local-e2e.md)
- [Trilha 06, segurança e consistência Keycloak](docs/implementation/trilha-06-keycloak-consistency.md)
- [Trilha 07, resiliência e observabilidade local](docs/implementation/trilha-07-resilience-observability.md)
- [Trilha 08, roteiro e manifesto de homologação](docs/implementation/trilha-08-local-acceptance.md)
- [Trilha 09, prontidão, pendências e backlog](docs/implementation/trilha-09-mvp-closeout.md)

Gerar um manifesto local de homologação com todos os cenários inicialmente `NOT_RUN`:

```bash
python3 .github/scripts/local_mvp_acceptance.py --init local-acceptance.json
python3 .github/scripts/local_mvp_acceptance.py --manifest local-acceptance.json
```

O segundo comando retorna `NOT_ACCEPTED` até existirem evidências reais, 14 cenários PASS e um commit SHA correspondente ao código testado. A **CI verde não significa MVP local homologado**. Scripts de carga, recuperação e lançamentos financeiros são opt-in e não rodam automaticamente na CI.

## Fora do escopo atual

Azure AKS (DEV/HOM/PROD/SHARED) permanece planejamento futuro. Estorno RF-06, MFA e hardening de produção exigem novas decisões e testes antes de implantação.
