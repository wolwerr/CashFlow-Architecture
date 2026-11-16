# Validações da arquitetura CashFlow

> **Fase atual:** revisão de desenho e especificações, antes da implementação.
> **Execução inicial:** LOCAL, com Docker Compose e ferramentas de teste no computador de desenvolvimento.
> **Azure/AKS:** arquitetura-alvo e orçamento de referência para uma **fase futura**, sem provisionamento na etapa atual.

## O que significa validação nesta fase?

**Validação documental** é a revisão de consistência de requisitos, ADRs, diagramas, modelo de dados e contratos. Podemos concluir essa revisão sem aplicação executando, identificando e corrigindo contradições.

**Validação de implementação** é diferente: demonstra que o código realmente atende ao desenho mediante testes automatizados, integração com PostgreSQL/RabbitMQ/Keycloak, simulações de falha, medição de carga, recuperação, segurança e observabilidade. Essas evidências **ainda não existem** apenas porque os documentos estão revisados.

## Revisões documentais realizadas

| Etapa | Relatório | Resultado |
| --- | --- | --- |
| **01** | [Requisitos e rastreabilidade](requirements-validation.md) | Revisão documental realizada; decisões do MVP consolidadas posteriormente |
| **02** | [Diagramas de sequência](sequence-validation.md) | Fluxos revisados com Outbox, JWT, falhas, idempotência e consultas |
| **03** | [Modelo de dados](data-model-validation.md) | Isolamento, integridade lógica, tipos de identidade e concorrência revisados |
| **04** | [Contratos REST e eventos](contracts-validation.md) | OpenAPI e JSON Schema iniciais, com convenções específicas ainda propostas |

As conclusões dessas revisões são **do desenho**. Elas não são certificação, homologação de ambiente ou prova de disponibilidade e desempenho.

## O que vamos validar com a aplicação rodando localmente?

- Testes de domínio: créditos/débitos, BRL com duas casas, competência, fuso horário, saldo diário e acumulado, dias vazios e retroatividade.
- Testes de API: OpenAPI, erros HTTP, JWT, papéis ADMIN/OPERATOR, segregação por comerciante e `Idempotency-Key`.
- Testes de persistência: migrations, constraints, commit atômico de lançamento + Outbox + auditoria + idempotência, e isolamento dos bancos lógicos.
- Testes de mensageria: confirmação/roteamento RabbitMQ, reentrega, processamento idempotente, concorrência, DLQ e reconciliação.
- Falha e recuperação: Consolidation desligado, broker indisponível e retomada sem dupla contabilização.
- Desempenho local: **50 consultas/s por 5 minutos**, até **5% de consultas válidas sem resposta útil**, **p95 inferior a 500 ms** e **95% dos registros visíveis em até 30 s** durante operação saudável. Os resultados locais terão hardware e condições documentados; **não equivalem a benchmark ou SLA de produção em Azure**.

## Fase futura, não executar agora

Azure AKS, DEV/HOM/PROD, ACR/DNS, serviços gerenciados, políticas de Kubernetes, custos de produção, aprovação de GitHub Environments para PROD, HA e backups/restauração em cloud **permanecem como arquitetura e planejamento futuro**. Não é necessário provisionar recursos Azure para concluir a primeira implementação do CashFlow.

Os pré-requisitos para liberação de PROD, como aprovação manual e restauração testada, continuam **válidos quando essa fase começar**.

## Fontes

- [24 decisões do MVP](../mvp-decisions.md)
- [Checklist de implementação e testes](../mvp-implementation-checklist.md)
- [Contratos iniciais](../contracts/cashflow-openapi.yaml)
- [Deployment View](../deployment-view.md)
- [Índice da arquitetura](../architecture-diagrams.md)
