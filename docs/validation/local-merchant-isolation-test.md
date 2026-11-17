# Evidência local: isolamento entre comerciantes e identidade

**Data:** 17/11/2026. **Origem:** saída do terminal Windows PowerShell compartilhada pelo responsável pelo projeto, não execução de CI. **Resultado:** PASS, código de saída 0.

## Cenário executado

Script: `.github/scripts/local_e2e_mvp.py --execute`. Dois usuários Keycloak diferentes, cada um associado a um comerciante distinto e papel `OPERATOR`. Chamadas passaram pelo proxy local `127.0.0.1:8080`.

O teste confirmou as verificações `unauthenticated_denied`, `two_distinct_merchants`, `cross_merchant_denied`, `baseline_recorded`, `credit_and_http_idempotency`, `debit_created`, `history_has_two_unique_transactions` e `rabbitmq_consolidation_and_balance`.

Acesso não autenticado ao diretório foi negado (401). O segundo usuário não obteve autorização para consultar histórico, consultar saldo nem efetuar crédito no comerciante do primeiro usuário (403). Na conta autorizada, foram registrados um crédito de R$ 100,00 e um débito de R$ 25,00, com replay idempotente e conflito 409 confirmados. Histórico e projeção assíncrona convergiram para incremento líquido de R$ 75,00.

O teste **não cobre** permissões de ADMIN versus OPERATOR em todas as operações, revogação dinâmica, escala horizontal ou produção na Azure. A primeira tentativa falhou por engano no fornecimento de dois tokens do mesmo usuário. A tentativa seguinte, com identidades distintas, foi aprovada.

**Segurança:** tokens completos foram expostos durante a troca de informações e devem ser revogados/ter suas sessões encerradas. Não incluir os tokens, IDs pessoais, URLs com dados sensíveis ou capturas das credenciais em evidências versionadas.

**Rastreabilidade pendente:** anexar no manifesto local evidência sanitizada e SHA do commit efetivamente testado, sem declarar os demais cenários como aprovados.
