# Requisitos funcionais do fluxo de caixa

> **Projeto:** CashFlow Architecture  
> **Issue:** #3 | **Milestone:** M1 - Descoberta e requisitos  
> **Status:** requisitos do desafio identificados; decisões adicionais do MVP aprovadas em 15/11/2026  
> **Referências:** enunciado do desafio e [mapeamento de domínios](../domain/business-capabilities-and-context-map.md)

## 1. Objetivo

Descrever, de forma simples e verificável, o que o sistema precisa fazer para que um comerciante registre lançamentos financeiros e consulte o saldo diário consolidado.

**Regra de leitura:** o que aparece como **exigido** foi solicitado diretamente no desafio. O que aparece como **proposto** é uma sugestão para tornar a aplicação utilizável e deverá ser validado antes de entrar no escopo definitivo. Não foram definidos endpoints, tecnologias ou estruturas de banco nesta etapa.

## 2. Quem utiliza o sistema

**Ator principal:** comerciante.

**Situações de uso:**
1. O comerciante informa um crédito ou débito.
2. O sistema registra a movimentação.
3. O comerciante consulta o resultado financeiro de uma determinada data.

Consulta de histórico, estorno e múltiplos comerciantes são possibilidades a validar, não exigências expressas.

## 3. Requisitos funcionais

| ID | Requisito | Descrição | Origem | Prioridade sugerida |
| --- | --- | --- | --- | --- |
| RF-01 | Registrar crédito | Permitir que o comerciante registre uma movimentação classificada como crédito. | Exigido | Essencial |
| RF-02 | Registrar débito | Permitir que o comerciante registre uma movimentação classificada como débito. | Exigido | Essencial |
| RF-03 | Consultar consolidado diário | Permitir consultar o saldo consolidado referente a um dia. | Exigido | Essencial |
| RF-04 | Validar dados do lançamento | Recusar lançamentos incompletos ou inválidos e informar o problema ao comerciante. | Proposto | Essencial para o MVP |
| RF-05 | Consultar histórico | Consultar lançamentos por intervalo de competência, com paginação e segregação por comerciante. | Proposto, **aprovado para MVP** | MVP |
| RF-06 | Corrigir por estorno | Permitir anular o efeito de um lançamento incorreto preservando o histórico. | Proposto, **fora do MVP** | Próxima versão |
| RF-07 | Manter trilha de auditoria | Auditar registros financeiros confirmados com ator, comerciante, identificador, instante UTC e correlação. | Proposto, **aprovado para MVP** | MVP |

**Decisão de negócio registrada em 15/11/2026:** a consulta do consolidado exibirá **movimento líquido diário e saldo acumulado**, além dos totais de crédito e débito. O enunciado não exige expressamente essa combinação; é uma decisão do projeto. **RN-03 decidida:** saldo inicial de **R$ 0,00 por comerciante no MVP**. **RN-09 decidida:** em dias sem lançamentos, totais de crédito e débito e movimento líquido são zero, e o saldo acumulado é mantido. **RN-04 decidida:** o comerciante informa a data de competência; o sistema registra o instante da criação em UTC. **RN-04 complementada:** aceitar competências passadas ou atuais e rejeitar competências futuras; lançamentos retroativos corrigem os saldos acumulados desde a competência afetada.

## 4. Histórias de usuário e critérios de aceite

### HU-01: Registrar crédito (RF-01)

**Como** comerciante, **quero** registrar um crédito **para** acompanhar as entradas financeiras do negócio.

**Critérios de aceite:**
- Dado um crédito com dados válidos, quando o registro for solicitado, então o sistema deverá confirmar que o lançamento foi registrado.
- O lançamento registrado compõe o consolidado de sua data de competência (RN-04); quando retroativo, modifica também os saldos acumulados afetados.
- Dados inválidos não devem produzir um lançamento confirmado (validação proposta em RF-04).

### HU-02: Registrar débito (RF-02)

**Como** comerciante, **quero** registrar um débito **para** acompanhar as saídas financeiras do negócio.

**Critérios de aceite:**
- Dado um débito com dados válidos, quando o registro for solicitado, então o sistema deverá confirmar que o lançamento foi registrado.
- O lançamento registrado deverá permanecer disponível para compor o consolidado.
- Dados inválidos não devem produzir um lançamento confirmado (validação proposta em RF-04).

### HU-03: Consultar consolidado diário (RF-03)

**Como** comerciante, **quero** consultar o saldo consolidado de uma data **para** acompanhar o resultado financeiro do negócio.

**Critérios de aceite:**
- Dada uma data válida, quando o comerciante solicitar o consolidado, então o sistema deverá apresentar um resultado referente à data informada.
- A consulta deverá retornar totais de crédito e débito, movimento líquido diário e saldo acumulado, conforme a decisão RN-02.
- Para um dia sem lançamentos, a consulta deve retornar créditos, débitos e movimento líquido de **R$ 0,00**, mantendo o último saldo acumulado disponível. Antes da primeira movimentação, esse saldo é **R$ 0,00** (RN-03/RN-09).

### HU-04: Validar lançamento (RF-04, proposto)

**Como** comerciante, **quero** receber uma explicação quando um lançamento não puder ser registrado **para** corrigir a informação.

**Critérios de aceite:**
- O sistema deverá exigir os campos mínimos definidos para o lançamento.
- Um valor inválido não poderá ser registrado, incluindo zero, negativos e valores com mais de duas casas decimais (RN-07).
- O comerciante deverá receber uma mensagem compreensível sem que um lançamento rejeitado seja contabilizado.

**Decisões adicionais aprovadas:** o dia atual é determinado em `America/Sao_Paulo`; o formulário sugere hoje, editável. Competências passadas são aceitas sem limite de dias para usuário autorizado. `Idempotency-Key` é obrigatório no registro e a confirmação HTTP é **201 Created somente após commit de Transaction + Outbox**, sem esperar a consolidação.\n\n**Campos do registro:** valor, tipo de movimentação e data de competência informada pelo comerciante (RN-04). Identificador do lançamento pode ser gerado pelo sistema. **RN-07 decidida:** BRL com duas casas, valor estritamente positivo, rejeição de precisão maior que duas casas, sem arredondamento automático de entradas. A data padrão da interface permanece a definir.

### HU-05: Consultar histórico (RF-05, proposto)

**Como** comerciante, **quero** visualizar lançamentos anteriores **para** conferir o que contribuiu para o consolidado.

**Critérios de aceite propostos:**
- Permitir localizar os lançamentos de um intervalo de datas.
- Apresentar as informações necessárias para reconhecer cada lançamento, paginando resultados e aplicando autorização por `merchant_id`.

### HU-06: Estornar lançamento (RF-06, proposto)

**Como** comerciante, **quero** corrigir o efeito de um lançamento incorreto **para** manter o resultado financeiro confiável.

**Critérios de aceite propostos:**
- O estorno deverá referenciar o lançamento original.
- O histórico deverá permitir identificar o lançamento e sua correção.
- A regra de qual dia recebe o efeito do estorno deverá ser definida pelo negócio.

### HU-07: Rastrear ações financeiras (RF-07, proposto)

**Como** responsável pela segurança ou auditoria, **quero** rastrear operações financeiras relevantes **para** investigar alterações e inconsistências.

**Critérios de aceite propostos:**
- Ações relevantes deixam registro de auditoria com identificador da operação, data/hora, resultado e ator quando conhecido.
- Um lançamento financeiro confirmado tem rastreabilidade consistente com a gravação.
- O acesso aos registros é restrito e os detalhes não expõem credenciais ou dados pessoais desnecessários.
- A consulta administrativa de auditoria, se necessária, será definida separadamente; não foi pedida no desafio.

Veja o [modelo de dados e auditoria](../architecture/data-model.md).

## 5. Fluxos principais

### Registro de lançamento

1. O comerciante informa um crédito ou débito.
2. O sistema verifica as informações exigidas.
3. Se as informações forem válidas, registra o lançamento e informa o resultado.
4. O lançamento passa a estar disponível para a composição do consolidado.

**Fluxo alternativo:** caso as informações estejam inválidas, o sistema informa o erro e não registra a movimentação.

**Condição não funcional do desafio:** uma falha no consolidado não pode impedir o registro de lançamentos. A estratégia técnica será detalhada na arquitetura e verificada nos testes.

### Consulta do consolidado diário

1. O comerciante informa a data desejada.
2. O sistema localiza o consolidado correspondente.
3. O sistema apresenta o resultado segundo a regra de cálculo aprovada.

**Fluxos alternativos a detalhar:** consulta de data inválida, consolidado ainda não atualizado e indisponibilidade temporária. Para dia sem movimentações, aplicar RN-09.

## 6. Exemplo para discutir com o negócio

Considere que, em um mesmo dia, sejam registrados:

| Movimentação | Valor |
| --- | ---: |
| Crédito | R$ 200,00 |
| Débito | R$ 50,00 |
| **Movimento líquido do dia** | **R$ 150,00** |

Esse exemplo pressupõe, **apenas para ilustração**, que crédito soma e débito subtrai.

O **movimento líquido** deste exemplo é R$ 150,00. O **saldo acumulado** será o saldo anterior mais R$ 150,00. No MVP, o saldo inicial de cada comerciante será **R$ 0,00**. Em dias sem movimentos, o saldo acumulado permanece inalterado (RN-09). Competências passadas ou atuais são aceitas, futuras são rejeitadas, e os saldos acumulados são corrigidos a partir da data retroativa (RN-04).

## 7. Regras e dúvidas que precisam de decisão

| ID | Pergunta | Por que importa |
| --- | --- | --- |
| RN-01 | Crédito aumenta e débito diminui o saldo? | Define a interpretação das movimentações. |
| RN-02 | **Decidido: apresentar movimento líquido diário e saldo acumulado.** | Movimento líquido = créditos do dia - débitos do dia; saldo acumulado = saldo inicial + soma dos movimentos líquidos até a data. O saldo inicial no MVP será R$ 0,00 por comerciante (RN-03). |
| RN-03 | **Decidido: saldo inicial de R$ 0,00 por comerciante no MVP.** | O saldo acumulado parte de zero e soma os movimentos líquidos registrados no CashFlow. Não há ajuste manual de abertura no MVP. |
| RN-04 | **Decidido: data de competência informada pelo comerciante e instante de registro em UTC.** | `reference_date` representa a data de negócio; `created_at` registra o instante efetivo em UTC. Datas passadas ou atuais são aceitas; futuras são rejeitadas. Após registro retroativo, saldos acumulados na data e posteriores devem refletir o movimento. A data padrão da interface e o fuso usado para definir a data atual ainda serão especificados. |
| RN-05 | Um lançamento pode ser excluído, editado ou estornado? | Define correções e preservação do histórico. |
| RN-06 | É necessário separar dados de diferentes comerciantes? | Define o escopo da consulta e da identificação. |
| RN-07 | **Decidido: somente BRL, com duas casas decimais e montantes positivos no MVP.** | Usar `decimal` no .NET e `numeric(19,2)` no PostgreSQL; rejeitar `amount <= 0` e valores com mais de duas casas, sem arredondamento silencioso. `CREDIT`/`DEBIT` define o sinal contábil; saída no consolidado mantém duas casas. |
| RN-08 | Quanto tempo após o registro o consolidado precisa refletir o lançamento? | Define a expectativa funcional de atualização; a meta técnica será tratada na Issue #4. |
| RN-09 | **Decidido: movimento diário zero, mantendo o saldo acumulado no dia sem lançamentos.** | Crédito, débito e líquido = R$ 0,00; saldo acumulado é o último valor anterior, ou R$ 0,00 antes do primeiro registro. Não implica gravar linhas vazias no banco. |

**Tratamento proposto:** caso não seja possível obter respostas da empresa, documentar as premissas adotadas para o MVP, explicitar seus impactos e evitar apresentá-las como requisitos fornecidos pela banca.

## 8. Aprovações de MVP\n\nO escopo de histórico paginado, auditoria básica de registros confirmados, multicomerciante, papéis ADMIN/OPERATOR, fuso `America/Sao_Paulo`, persistência de totais diários com acumulado calculado na leitura e API versionada foram **aprovados pelo projeto**, não impostos pelo enunciado. A especificação integral das 24 decisões encontra-se em [Decisões consolidadas do MVP](../architecture/mvp-decisions.md). Estorno e exclusão/edição de fatos confirmados ficam fora do MVP.\n\n## 9. Rastreabilidade e conclusão

| Requisito | Contexto responsável | Validação prevista |
| --- | --- | --- |
| RF-01 / RF-02 / RF-04 | Lançamentos | Testes de registro válido e inválido |
| RF-03 | Consolidado diário | Testes de consulta e cálculo com regra aprovada |
| RF-05 / RF-06 (se aprovados) | Lançamentos | Testes de histórico e estorno |

### Checklist da Issue #3

- [x] Ator principal e jornadas de uso descritos.
- [x] Requisitos funcionais identificados e numerados.
- [x] Histórias de usuário e critérios de aceite documentados.
- [x] Fluxos principais e alternativos descritos.
- [x] Exigências separadas de propostas e dúvidas em aberto.
- [x] Documento versionado no repositório.
- [ ] Validar premissas e aprovar escopo final para o MVP.

**Próximo passo:** especificar os contratos e implementar as regras aprovadas; os testes e o aceite de execução permanecem pendentes.
