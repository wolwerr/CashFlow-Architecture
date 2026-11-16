# CashFlow | Checklist de implementação do MVP

> **Status:** decisões aprovadas em 15/11/2026; código, contratos executáveis e testes ainda pendentes
> **Fonte das regras:** [24 decisões consolidadas](mvp-decisions.md)

## Antes de iniciar a codificação

- [x] Criar **primeira versão proposta do OpenAPI v1** para /api/v1/transactions e /api/v1/consolidation: [contrato inicial](contracts/cashflow-openapi.yaml). Ainda faltam aprovação dos detalhes, validação de schema e integração com APIs reais.
- [ ] Definir **contrato Idempotency-Key** obrigatório por criação: escopo por comerciante/rota, hash do request, gravação atômica, reuso de resultado, conflito de payload e prazo de retenção.
- [ ] Especificar **autorização CashFlow**: interface e dono do banco lógico independente, tabela Merchant/Membership, matriz ADMIN/OPERATOR, TTL curto, invalidação/revogação e fail-closed.
- [ ] Definir **envelope JSON versionado** dos eventos e JSON Schema: event_id, event_type, schema_version, correlation_id, transaction_id, merchant_id e campos financeiros.
- [ ] Detalhar número de **retries**, backoff, DLQ, recuperação, retenção configurável e expurgo seguro de Outbox/ProcessedEvent.

## Implementação

- [ ] React com Keycloak Authorization Code + PKCE, access tokens apenas em memória e validações de UX; formulário inicia com data atual em America/Sao_Paulo, editável.
- [ ] Transactions: lançamento financeiro BRL positivo e até duas casas, competência passada/atual, criação UTC, Idempotency-Key, Outbox e auditoria de criação confirmada no mesmo commit; 201 após commit.
- [ ] Transactions: histórico filtrável por período, paginado, autorizado por merchant_id. Estorno não entra no MVP.
- [ ] Consolidation: atualização atômica dos totais diários e ProcessedEvent único; saldo acumulado **derivado na leitura** pela soma de net_amount até a data solicitada; dias vazios e retroativos.
- [ ] Banco lógico próprio de autorização, separado dos bancos Transactions, Consolidation e Keycloak; interfaces controladas e migrations/credenciais independentes.
- [ ] Worker Outbox com claim concorrente, publisher confirm mais roteamento, retry limitado/backoff/DLQ; consumidores com ACK somente após commit.
- [ ] PostgreSQL/EF Core migrations, constraints, índices por merchant_id/data, restrições de moeda, concorrência e integridade; auditoria com retenção inicial 365 dias.
- [ ] NGINX com rotas versionadas e cabeçalhos Authorization, Idempotency-Key, correlação; validar nginx -t e integração real.

## Testes de aceitação

- [ ] Falha de Consolidation e broker não impede confirmação do lançamento no Transactions quando suas dependências locais estão disponíveis.
- [ ] Rollback Transaction + Outbox + auditoria + idempotência; nenhuma confirmação parcial.
- [ ] Reentrega, duplicidade e concorrência de eventos não provocam saldo incorreto.
- [ ] Repetição de requisição com mesma Idempotency-Key e payload preserva o resultado; payload diferente causa conflito.
- [ ] Testar competência retroativa sem limite, futura rejeitada, fuso America/Sao_Paulo, dias vazios e negativos apenas nos saldos calculados.
- [ ] Testar autorização 401/403, segregação entre comerciantes, ADMIN/OPERATOR, invalidação de cache e fail-closed.
- [ ] Executar **50 RPS por 5 minutos** após aquecimento, **até 5% de falhas** nas consultas válidas enviadas e **p95 < 500 ms**, com relatório de carga.
- [ ] Medir **95% de lançamentos refletidos em até 30 segundos** em operação saudável.

## Entrega e operação

- [ ] CI/CD separado por serviço; promover a mesma imagem pelos ambientes e exigir **aprovação manual GitHub Environments em PROD**.
- [ ] Configurar TLS, AKS, PostgreSQL HA, rede, Kubernetes PDB/HPA, RabbitMQ quorum/volumes, observabilidade e alertas.
- [ ] Backups automáticos e **teste documentado de restauração antes de liberar PROD**; RTO/RPO e retenções ainda a definir.
- [ ] Inspecionar custo real do banco lógico adicional de autorização, do dimensionamento e das políticas de recuperação.
- [ ] **Regenerar PNGs** dos arquivos PlantUML alterados e revisar os diagramas renderizados.

**Importante:** a aprovação das 24 decisões não significa que os 24 comportamentos foram implementados ou comprovados. Este checklist é o registro dos trabalhos e evidências ainda necessários.
