# Entendimento do negócio e organização dos domínios

## 1. O problema que precisamos resolver

Um comerciante precisa acompanhar a movimentação de dinheiro do negócio. Para isso, o sistema deverá:

1. Registrar entradas e saídas de valores, chamadas no desafio de **créditos** e **débitos**.
2. Disponibilizar um **consolidado diário** para consulta do saldo.

Há uma condição essencial: **se o consolidado diário estiver fora do ar, o comerciante ainda precisa conseguir registrar lançamentos**.

O desafio não especifica todas as regras financeiras. Onde houver dúvidas, vamos registrá-las e defini-las no refinamento de requisitos, sem inventar comportamentos.

## 2. Quem utiliza ou participa

| Participante | Necessidade |
| --- | --- |
| **Comerciante** | Registrar lançamentos e consultar o consolidado diário. |
| **Equipe de suporte (possível)** | Investigar falhas ou divergências, caso exista operação assistida. |

O comerciante é o único ator explicitamente descrito no desafio. Equipes de suporte, integrações externas e outros usuários são possibilidades, não requisitos confirmados.

## 3. Capacidades do negócio

Capacidade de negócio é **o que o negócio precisa conseguir fazer**, independentemente da tecnologia usada.

| Capacidade | O que permite fazer | Origem |
| --- | --- | --- |
| **Registrar movimentações financeiras** | Informar um débito ou crédito e manter o lançamento registrado. | Solicitada no desafio |
| **Consultar o resultado financeiro diário** | Visualizar o saldo consolidado referente a um dia. | Solicitada no desafio |
| **Consultar o histórico das movimentações** | Identificar os registros que contribuíram para um resultado e investigar divergências. | Sugestão a validar |

A última capacidade é útil para um sistema financeiro, mas não foi exigida expressamente.

## 4. Divisão das responsabilidades (domínios)

Para manter as responsabilidades claras, propomos dois **contextos de negócio (Bounded Contexts)**. Um contexto é uma área em que termos e regras possuem um significado bem definido. Ele não precisa corresponder obrigatoriamente a um microsserviço.

### 4.1. Lançamentos

Cuida do registro das movimentações financeiras.

**Responsabilidades:**
- Receber um lançamento de débito ou crédito.
- Verificar se os dados necessários foram preenchidos corretamente.
- Registrar e preservar os lançamentos aceitos.
- Disponibilizar os dados necessários à consolidação, sem depender de ela estar funcionando.

**Não é responsável por:** montar o resultado diário consolidado.

### 4.2. Consolidado diário

Cuida de reunir os lançamentos e apresentar o resultado de um dia.

**Responsabilidades:**
- Considerar os lançamentos registrados para uma data.
- Calcular ou atualizar o consolidado diário conforme as regras que ainda serão refinadas.
- Disponibilizar a consulta desse resultado.

**Não é responsável por:** criar, aprovar ou alterar lançamentos financeiros.

### 4.3. Como os contextos se relacionam

```mermaid
flowchart LR
    A["Comerciante"]
    B["Lançamentos"]
    C["Consolidado diário"]
    A -->|"Registra débitos e créditos"| B
    A -->|"Consulta resultado diário"| C
    B -.->|"Informa lançamentos registrados"| C
```

O consolidado utiliza as informações registradas em **Lançamentos**. A forma de transmitir essas informações será definida mais adiante, na arquitetura da solução.

**Regra fundamental:** uma falha no consolidado não pode impedir o registro de um novo lançamento.

## 5. Linguagem ubíqua: glossário do negócio

Em DDD, *linguagem ubíqua* é o vocabulário compartilhado entre negócio e equipe técnica. Não basta listar palavras: precisamos definir o que cada uma significa, como é usada e quais dúvidas ainda existem.

As definições abaixo são **propostas iniciais**. Elas deverão ser validadas durante o refinamento para que todos usem os termos da mesma forma.

| Termo | Significado no projeto | Exemplo de uso |
| --- | --- | --- |
| **Comerciante** | Pessoa ou negócio que utiliza o sistema para acompanhar suas movimentações financeiras. | “O comerciante consulta o resultado de hoje.” |
| **Lançamento** | Registro individual de uma movimentação financeira que deve ser considerada pelo sistema. | “Foi registrado um lançamento de R$ 80,00.” |
| **Crédito** | Tipo de lançamento que, na convenção inicialmente proposta, aumenta o saldo. **A confirmar com o negócio.** | “Uma entrada de R$ 200,00 foi registrada como crédito.” |
| **Débito** | Tipo de lançamento que, na convenção inicialmente proposta, diminui o saldo. **A confirmar com o negócio.** | “Uma saída de R$ 50,00 foi registrada como débito.” |
| **Data do lançamento** | Dia ao qual uma movimentação pertence para fins de consolidação. É necessário definir se corresponde à ocorrência ou ao registro no sistema. | “Esse lançamento pertence ao dia 13/11.” |
| **Saldo diário** | Valor apresentado para um dia. **Ainda é necessário decidir** se representa apenas o resultado líquido daquele dia ou o saldo acumulado até a data. | “Qual é o saldo diário de 13/11?” |
| **Consolidado diário** | Resultado da reunião dos lançamentos considerados para uma data, conforme regras de cálculo definidas pelo negócio. | “O consolidado de ontem já está disponível.” |
| **Histórico de lançamentos** | Conjunto de movimentações registradas que permite consultar e conferir o que aconteceu. **Capacidade sugerida, não confirmada.** | “Vamos conferir o histórico para explicar a diferença no consolidado.” |

**Exemplo ilustrativo, ainda não aprovado como regra de cálculo:** se o comerciante registrar um crédito de R$ 200,00 e um débito de R$ 50,00 no mesmo dia, o movimento líquido desse dia será de R$ 150,00. Isso **não define**, por si só, como será calculado o saldo diário exibido, pois pode existir saldo anterior.

Este glossário deve ser atualizado sempre que o significado de algum termo for esclarecido pelo negócio.

## 6. Decisões pendentes para o refinamento

As perguntas abaixo serão tratadas nas Issues #3 e #4:

1. O débito diminui e o crédito aumenta o saldo, conforme a convenção proposta?
2. O consolidado apresenta o movimento do dia ou o saldo acumulado?
3. Existe um saldo inicial?
4. Qual data conta para a consolidação? Há um fuso horário definido?
5. É possível corrigir ou estornar um lançamento?
6. O sistema atende um ou vários comerciantes?
7. Qual moeda será utilizada?
8. Quanto tempo o consolidado pode levar para refletir um novo lançamento?
9. O que significa “até 5% de perda de requisições” no enunciado? Isso não deve ser confundido com perda de lançamentos financeiros.
10. Existem regras de retenção ou tratamento de dados pessoais?

## 7. Resultado desta etapa

- [x] Ator principal identificado.
- [x] Capacidades essenciais do negócio mapeadas.
- [x] Dois contextos de negócio definidos e responsabilidades separadas.
- [x] Relação entre os contextos representada.
- [x] Glossário inicial com definições e exemplos.
- [x] Questões ainda não esclarecidas registradas.
- [x] Documento salvo no repositório.
- [ ] Revisar e validar as definições propostas antes de encerrar a Issue #2.

**Próximo passo:** Issue #3, *Refinar requisitos funcionais*. Nela, vamos transformar as dúvidas em regras e critérios de aceite, sem antecipar tecnologias nesta etapa.
