# Validação 04 | Contratos REST v1 e eventos financeiros

> **Status:** contratos iniciais criados e revisados contra as 24 decisões; implementação e testes pendentes
> **Data de referência:** 15/11/2026
> **Base de decisões:** [MVP aprovado](../mvp-decisions.md)
> **Natureza:** especificação de interface, não API funcionando, endpoint implantado ou teste executado

## Artefatos

- [OpenAPI 3.0.3 do CashFlow](../contracts/cashflow-openapi.yaml)
- [JSON Schema do evento TransactionRecorded](../contracts/transaction-recorded.schema.json)
- [NGINX com prefixos versionados](../../../infra/nginx/default.conf.template)

## Matriz de conformidade documental

| Critério | Decisão aprovada | Contrato proposto |
| --- | --- | --- |
| Registro | POST, `Idempotency-Key` obrigatório, 201 após commit, saldo eventual | `POST /api/v1/transactions`; resposta `201`, recurso e `Location` |
| Histórico | Filtrar por data, paginar, isolar comerciante | `GET /api/v1/transactions?merchant_id&from&to&page&page_size` |
| Consulta individual | Necessária para suportar a localização retornada no 201 | `GET /api/v1/transactions/{transaction_id}?merchant_id` (rota **proposta**) |
| Consolidado | Crédito, débito, líquido e acumulado, dia vazio com zeros no movimento, acumulado derivado | `GET /api/v1/consolidation/daily?merchant_id&date` (rota **proposta**) |
| Segurança | JWT Keycloak e autorização por vínculo merchant/role | `bearerAuth`; contrato descreve validação servidor-side e 401/403 |
| Valores | BRL, positivo, duas casas; saldo derivado pode ser negativo | Valores como **strings decimais** no JSON, sem ponto flutuante binário |
| Datas | Competência selecionada pelo usuário, sem futuro em America/Sao_Paulo; instante UTC | `reference_date` date e `created_at` date-time |
| Eventos | JSON versionado, `event_id` estável, `correlation_id`, dados financeiros | `transaction.recorded` com `schema_version: "1"` e dados mínimos |
| Idempotência e entrega | Sem duplicar transação ou projeção, ACK após commit | Header e identificador especificados; implementação e índices pendentes |
| Erros | Diferenciar autenticação, autorização, payload e conflito | Respostas 400, 401, 403, 409, 422 e 404 propostas em `application/problem+json` |

## Decisões já aprovadas versus detalhamentos propostos

As decisões aprovadas em [mvp-decisions.md](../mvp-decisions.md) são vinculantes ao desenho: prefixos `/api/v1/transactions` e `/api/v1/consolidation`; 201 após commit; `Idempotency-Key` obrigatório; auditoria; BRL duas casas; eventual consistency; envelope versionado, e segregação por comerciante.

Os itens abaixo são **detalhamentos técnicos sugeridos neste contrato**, não foram individualmente aprovados pelo usuário e podem ser revisados antes de codificar:

1. **`GET /api/v1/transactions/{transaction_id}`** para localizar o recurso do `Location`, além do histórico já aprovado;
2. **`GET /api/v1/consolidation/daily`** com query `date`;
3. **`merchant_id` no corpo de criação e query de leitura**, sempre conferido contra vínculo confiável, e não fonte de autorização;
4. strings decimais JSON (`"125.50"`) para evitar conversão imprecisa por clientes JavaScript;
5. **paginação por número de página**, tamanho padrão 20 e máximo 100; outras estratégias como cursor permanecem possíveis;
6. resposta de erro no formato **Problem Details** e códigos 400/401/403/404/409/422;
7. evento de domínio nomeado `transaction.recorded`, schema_version `"1"`, data contendo `transaction_id`, `merchant_id`, tipo, valor, moeda e competência.

### Semântica de idempotência a implementar

- Escopo exclusivo do comerciante e endpoint, com uma chave por intenção financeira.
- Primeira tentativa confirma `Transaction`, `OutboxEvent`, `HttpIdempotencyRecord` e `AuditEvent` em uma única transação.
- Repetição com mesma chave e **payload canônico equivalente** retorna o **mesmo recurso**, sem publicar um segundo evento; um replay poderá retornar 201 e o mesmo Location se esta for a convenção adotada.
- Mesma chave com payload diferente retorna 409.
- Testar corridas entre duas requisições idênticas, queda após commit antes da resposta e expiração segura do registro. **O prazo de retenção da chave ainda não está decidido.**

### Semântica de evento a implementar

- `event_id` único, persistido na Outbox e estável em cada reenvio.
- `correlation_id` facilita correlação operacional, mas não substitui event_id, merchant_id ou transaction_id.
- Publicação confiável requer confirmação positiva do broker **e verificação de roteamento**; confirmação incerta mantém a obrigação de retry.
- Consolidation verifica unicidade `ProcessedEvent(event_id)` e atualiza o total diário na mesma transação. ACK após commit seguro, inclusive para duplicata já confirmada.
- Eventos com competência antiga afetam o total daquele dia; saldo acumulado é recalculado na consulta via soma histórica.
- Detalhar exchange, routing key, `mandatory`, bindings, retries/backoff, DLQ, retention e replay para implantar; **schema_version não define sozinho compatibilidade de consumidores**.

## Casos de teste obrigatórios para aceitar a Validação 04

| Teste | Resultado esperado |
| --- | --- |
| POST crédito e débito válidos | 201, Location, transação/Outbox/auditoria gravadas, resposta não bloqueada por Consolidation |
| POST duplicado com mesma chave e payload | Apenas uma transação e um evento criado, resultado estável |
| POST mesma chave com payload alterado | 409 sem nova transação |
| Valor zero, negativo, com três casas ou overflow | Rejeição sem arredondar silenciosamente nem confirmar |
| Competência futura em America/Sao_Paulo | Rejeição; competência retroativa autorizada é aceita |
| Histórico paginado e usuário não associado ao comerciante | Filtro correto, 403/404 conforme política de não divulgação, sem vazamento |
| Consulta em dia sem movimento | Crédito/débito/líquido zero, acumulado calculado |
| Evento repetido e consumidor com crash antes do ACK | Aplicação única do efeito contábil |
| Evento retroativo recebido fora de ordem | Totais e acumulado corretos após projeção |
| Requisição sem JWT ou com papel/merchant incorreto | 401 ou 403 |

## Riscos e pendências a resolver na implementação

- **Ainda não há testes de contrato executados.** Testar OpenAPI com validador apropriado e consumidores com contrato de eventos.
- **Autorizações:** contrato da interface entre serviços financeiros e banco lógico de autorização, matriz ADMIN/OPERATOR, revogação/invalidação de cache e fail-closed.
- **Concorrência:** implementação EF Core para idempotência HTTP, índice único e isolamento; *lost update* em `DailyBalance`.
- **Operação:** retenção de chave HTTP, retenção configurável Outbox/ProcessedEvent, retry/backoff/DLQ e estratégia de reconciliação.
- **Escalabilidade:** custo da soma histórica em consultas a 50 RPS, medição p95 e frescor de 95% em 30 segundos.
- **Compatibilidade:** versão/eventos, eventual expansão do JSON e tipagem monetária rigorosa.
- **NGINX:** testar `nginx -t`, encaminhamento dos prefixos completos e preservação de Authorization, Idempotency-Key, X-Correlation-ID.

**Conclusão:** os contratos de referência refletem as 24 decisões do MVP, mas as rotas específicas, paginação e formatos de payload/erros são **propostas iniciais de especificação**, sem evidência de implementação ou aceite de execução.

**Próxima validação:** segurança/autorização, ou evolução destes contratos a partir de implementações e testes.
