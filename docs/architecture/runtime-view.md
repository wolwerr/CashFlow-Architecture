# Visão de Execução (Runtime View)

> **Issue #6 · Milestone M2** | Comportamento do sistema em operação

Esta visão descreve **o que acontece quando o CashFlow está funcionando**, especialmente diante de falhas. Diferentemente da [Visão de Blocos de Construção](building-block-view.md), que mostra a estrutura, aqui descrevemos os cenários e o resultado esperado. Os detalhes de mensagens aparecem nos [diagramas de sequência](sequence-diagrams.md).

## Cenário 1: registro em condições normais

1. O comerciante envia um débito ou crédito por uma API HTTPS.
2. O serviço de lançamentos valida autorização e dados do pedido.
3. O banco confirma **uma única transação** contendo o lançamento e o evento pendente da Outbox.
4. O comerciante recebe a confirmação de registro.
5. O worker interno busca o evento pendente e publica na mensageria.
6. O consolidado consome o evento, atualiza sua visão e registra o identificador processado na mesma transação.

**Resultado:** o registro não aguarda a atualização do consolidado. O saldo pode ser atualizado depois, de forma eventual.

[Ver sequência de registro](sequence-diagrams.md#registro-de-lançamento-e-publicação-assíncrona)

## Cenário 2: consolidado indisponível

1. O serviço de consolidado fica indisponível.
2. O comerciante continua registrando movimentações no serviço de lançamentos.
3. Os eventos publicados aguardam entrega ou processamento conforme a política do broker.
4. Quando o consolidado retorna, consome os eventos pendentes.
5. O processamento idempotente impede que a mesma movimentação seja contabilizada duas vezes.

**Resultado esperado:** a falha do consolidado não bloqueia a gravação de lançamentos, mas consultas ao saldo podem ficar indisponíveis ou desatualizadas.

[Ver sequência de falha](sequence-diagrams.md#falha-do-consolidado)

## Cenário 3: mensageria indisponível

1. O lançamento e seu evento pendente continuam sendo gravados no banco de lançamentos.
2. O worker tenta publicar; se não conseguir, mantém o evento pendente e tenta novamente segundo a política a definir.
3. Com a mensageria recuperada, os eventos voltam a ser publicados e processados.

**Resultado esperado:** a indisponibilidade do broker não provoca perda de lançamentos já confirmados. Para isso, a Outbox, o worker e o banco precisam funcionar conforme as garantias propostas.

Este cenário **ainda não possui um diagrama de sequência próprio**; será detalhado na Issue #8.

## Cenário 4: consulta do consolidado

1. O comerciante consulta o resultado financeiro de uma data.
2. O serviço de consolidado valida o pedido e consulta sua própria base.
3. O serviço apresenta o resultado disponível.

**Ponto a decidir:** o significado exato de saldo diário (movimento líquido ou saldo acumulado), a data de referência e a indicação de atraso dependem da Issue #3.

[Ver sequência de consulta](sequence-diagrams.md#consulta-do-consolidado-diário)

## Requisitos que esses cenários ajudam a demonstrar

| Requisito ou risco | Cenários relacionados | Evidência futura |
| --- | --- | --- |
| RNF-01: lançamentos independentes do consolidado | 1 e 2 | Teste de falha do consolidado |
| RNF-04: preservação dos registros confirmados | 1, 2 e 3 | Conferência da persistência e reprocessamento |
| RNF-05: não contabilizar duplicado | 1 e 2 | Teste de reentrega |
| RNF-02 / RNF-03: 50 RPS e até 5% de falha nas consultas | 4 | Teste de carga |
| Riscos de atraso e divergência de saldo | 2, 3 e 4 | Métricas de atraso, reconciliação e testes |

## Limites e pendências

- O desenho cobre o isolamento de falhas entre os dois serviços; **não garante disponibilidade caso o banco de lançamentos falhe**.
- Retentativas, DLQ, recuperação de eventos e reconciliação serão detalhadas na Issue #8.
- Autenticação, proteção de dados e logs serão detalhados na Issue #9.
- Os componentes e protocolos descritos são a **arquitetura proposta**, não funcionalidades comprovadamente implementadas.
