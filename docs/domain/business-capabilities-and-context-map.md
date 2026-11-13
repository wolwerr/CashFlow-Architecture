# Mapeamento de capacidades de negócio e Bounded Contexts

> Projeto: CashFlow Architecture  
> Issue: #2 | Milestone: M1 - Descoberta e requisitos  
> Status: Proposta inicial para refinamento  
> Fonte: "Desafio - Arq. Soluções.pdf", seção "Descritivo da Solução" e requisitos relacionados.

## 1. Contexto e objetivo

**Requisito expresso no desafio:** um comerciante necessita registrar lançamentos financeiros de **débito e crédito** e consultar um relatório com o **saldo diário consolidado**.

O desafio cita expressamente dois serviços de negócio: **controle de lançamentos** e **consolidado diário**. Também determina que a indisponibilidade do consolidado não interrompa o serviço de lançamentos.

**Objetivo deste documento:** mapear o que o negócio precisa fazer, delimitar responsabilidades e estabelecer as relações conceituais entre domínios. A solução tecnológica, as regras financeiras ainda não especificadas e os contratos de integração serão detalhados em issues posteriores.

## 2. Atores e partes interessadas

| Ator / stakeholder | Papel no contexto | Origem |
| --- | --- | --- |
| Comerciante | Registra débitos/créditos e consulta o saldo consolidado diário | Expresso no desafio |
| Operação / suporte | Diagnostica falhas, divergências e atrasos | Proposta a validar |
| Equipe de segurança / governança | Define controles para dados e acesso | Proposta a validar |
| Sistemas externos | Possíveis fontes de lançamentos ou consumidores de relatórios | Não informado; não assumir integração |

Não se presume sistema legado, conta bancária, múltiplas lojas, usuários distintos ou integração com meios de pagamento.

## 3. Mapa de capacidades de negócio

| ID | Capacidade | Descrição | Classificação | Evidência / limite |
| --- | --- | --- | --- | --- |
| CAP-01 | Gestão de lançamentos financeiros | Registrar lançamentos de débito e crédito | Essencial | Requisito expresso |
| CAP-02 | Consolidação financeira diária | Disponibilizar saldo consolidado por dia | Essencial | Requisito expresso |
| CAP-03 | Rastreabilidade de lançamentos | Identificar lançamentos que contribuíram para um saldo e apoiar auditoria/reconciliação | Apoio (proposta) | Inferida da criticidade financeira; confirmar profundidade exigida |

### Decomposição funcional preliminar

- **CAP-01:** receber lançamento; validar os dados e regras aplicáveis; persistir lançamento aceito; sinalizar sua ocorrência a consumidores autorizados (mecanismo ainda não decidido).
- **CAP-02:** obter os lançamentos relevantes; calcular ou atualizar resultado diário; consultar saldo de um dia; lidar com atualização atrasada.
- **CAP-03:** consultar histórico e identificar origem/identificador do lançamento; permitir investigação de divergências. O escopo de auditoria e eventuais correções será refinado.

## 4. Domínios e Bounded Contexts propostos

### BC-01: Transactions (Lançamentos)

**Responsabilidade:** ser a autoridade sobre o registro e o histórico de lançamentos financeiros aceitos.

**Dentro do contexto:** identificação do lançamento; dados do débito/crédito; validação; persistência; sinalização de lançamentos registrados.

**Fora do contexto:** cálculo e disponibilização do saldo consolidado diário; apresentação de relatório consolidado.

**Linguagem ubíqua provisória:** Lançamento, Débito, Crédito, Valor, Data de referência, Lançamento registrado. A semântica de “débito” e “crédito” será validada antes de qualquer regra contábil.

### BC-02: Daily Consolidation (Consolidado diário)

**Responsabilidade:** produzir e consultar uma visão agregada do saldo por dia, a partir dos lançamentos registrados.

**Dentro do contexto:** consumo/obtenção de fatos de lançamentos; projeção ou cálculo do saldo do dia; consulta do consolidado; controle de atualização da projeção.

**Fora do contexto:** aceitar ou rejeitar lançamentos; alterar o histórico de lançamentos.

**Linguagem ubíqua provisória:** Saldo diário, Data de referência, Consolidação, Atualização do consolidado.

### Observações sobre a fronteira

- Dois Bounded Contexts representam separação semântica e de responsabilidades; **não obrigam** a utilização de microsserviços.
- O enunciado demanda dois serviços e independência de disponibilidade. A correspondência de um serviço por contexto é uma **hipótese arquitetural** a avaliar, não consequência obrigatória de DDD.
- Rastreabilidade (CAP-03) é transversal às capacidades, **não** foi promovida a terceiro Bounded Context sem justificativa de autonomia funcional.

## 5. Context Map preliminar

```mermaid
flowchart LR
    M["Comerciante"]
    T["BC-01: Transactions<br/>Autoridade dos lançamentos"]
    C["BC-02: Daily Consolidation<br/>Projeção e consulta do saldo diário"]
    M -->|"Registra débito/crédito"| T
    M -->|"Consulta saldo diário"| C
    T -.->|"Fatos de lançamentos registrados<br/>(contrato e transporte a definir)"| C
```

**Relação entre contextos:** Transactions é **upstream** e Daily Consolidation é **downstream** em relação aos fatos financeiros. A proposta é uma integração assíncrona para preservar o isolamento de falhas, mas broker, formato, entrega e padrões de consistência serão decididos nas ADRs.

**Regra de dependência:** o caminho crítico de aceitar um lançamento **não deve exigir** que Daily Consolidation esteja disponível.

## 6. Responsabilidades e invariantes preliminares

| Responsabilidade / invariante | Dono proposto | Situação |
| --- | --- | --- |
| Aceitar/rejeitar lançamento após validação | Transactions | Proposta baseada na separação |
| Preservar lançamentos aceitos | Transactions | Proposta baseada na criticidade do dado |
| Calcular e expor saldo consolidado diário | Daily Consolidation | Exigência de negócio |
| Impedir que falha do consolidado bloqueie registro | Fronteira entre contextos | Exigência não funcional |
| Evitar contabilização duplicada ao reprocessar | Daily Consolidation | Requisito derivado, a detalhar |
| Definir saldo inicial, dia financeiro e política de correção | Negócio, com apoio da arquitetura | **Pendente de refinamento** |

## 7. Questões em aberto para a Issue #3 e #4

1. Débito reduz e crédito aumenta o saldo? Existe convenção contábil específica?
2. O consolidado representa movimento líquido do dia ou saldo acumulado incluindo dias anteriores?
3. Existe saldo inicial? Quem o fornece?
4. Qual data prevalece: data da operação, data informada, processamento ou liquidação? Qual fuso horário?
5. É permitido excluir, corrigir ou estornar lançamentos? Como preservar histórico?
6. Um único comerciante ou vários? É necessário isolamento por comerciante/conta?
7. Há moeda única? Como tratar precisão decimal e arredondamento?
8. A consulta precisa refletir lançamentos imediatamente ou aceita consistência eventual? Qual atraso máximo?
9. Como interpretar “até 5% de perda de requisições” do consolidado? Não confundir com perda de dados financeiros.
10. Existem exigências específicas de retenção, auditoria e tratamento de dados pessoais?

As questões são **lacunas identificadas**, não funcionalidades já aprovadas.

## 8. Rastreabilidade com o desafio e próximas etapas

| Evidência do desafio | Resposta no mapeamento |
| --- | --- |
| Serviço de controle de lançamentos | CAP-01 / BC-01 |
| Serviço do consolidado diário | CAP-02 / BC-02 |
| Lançamentos permanecem disponíveis em falha do consolidado | Regra de isolamento entre contextos |
| Mapeamento de domínios funcionais e capacidades de negócio | Seções 3 a 5 |
| Refinamento de requisitos | Questões destinadas às Issues #3 e #4 |
| Escolha de padrões e ferramentas | Adiada para ADRs da Milestone M2 |

## 9. Critérios de aceite da Issue #2

- [x] Atores e capacidades de negócio identificados.
- [x] Domínios e Bounded Contexts propostos com justificativa.
- [x] Responsabilidades delimitadas.
- [x] Relacionamentos entre contextos mapeados.
- [x] Documento versionado no repositório.
- [ ] Revisão do conteúdo e validação das premissas com o responsável pelo desafio.

**Nota:** a revisão final é necessária para encerrar a issue; nenhuma decisão tecnológica foi consolidada nesta etapa.
