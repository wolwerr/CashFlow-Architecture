# CashFlow: scaffold local

A estrutura inicial foi preparada conforme ADR-002 e as 24 decisões em [MVP decisions](../architecture/mvp-decisions.md). Ainda **não** existem endpoints financeiros, autenticação/autorização por comerciante, migrations, Outbox, consumer, testes ou telemetria funcional.

## Executar localmente

Pré-requisitos: Docker + Docker Compose V2.

```bash
docker compose up --build -d
docker compose ps
curl http://localhost:8080/nginx-health
```

- Página inicial: http://localhost:8080
- RabbitMQ Management: http://localhost:15672
- Keycloak Admin: http://localhost:8081
- Encerrar: `docker compose down`
- Apagar volumes de desenvolvimento: `docker compose down -v` (destrutivo)

As credenciais default do Compose são **exclusivas para ambiente local**. Não expor as portas para redes externas. O Keycloak inicia sem realm CashFlow configurado. Os bancos lógicos estão reservados para Transactions, Consolidation, autorização e Keycloak; ainda não há schema/migrations. `/health` verifica o processo e não dependências.

## Próximos passos

1. Especificar o proprietário/contrato de autorização e RBAC ADMIN/OPERATOR, cache curto invalidável e fail-closed.
2. Implementar regras financeiras e testes do domínio BRL/data de competência.
3. Criar migrations EF Core; atomicidade Transactions + Outbox + auditoria + idempotência.
4. Implementar publisher/consumer RabbitMQ com ProcessedEvent atômico.
5. Integrar JWT do Keycloak às APIs e OIDC Authorization Code + PKCE na SPA com token apenas em memória.
6. Verificar compile, testes, containers e critérios de carga/indisponibilidade com evidências em docs/architecture/validations.

O scaffold **não foi executado nem homologado** nesta etapa; Azure permanece planejamento futuro.
