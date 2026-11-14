# Diagramas da arquitetura CashFlow

> **Issue:** #6 | **Milestone:** M2 - Arquitetura da solução  
> **Estado:** proposta para revisão  
> **Formato:** PlantUML + C4-PlantUML

## 1. Visão geral

O comerciante registra débitos e créditos e consulta o saldo diário. A principal restrição do desafio é que **o registro de lançamentos deve continuar funcionando se o consolidado estiver indisponível**.

Os diagramas C4 usam a biblioteca **C4-PlantUML**, com símbolos padronizados para pessoa, sistema, contêiner, banco de dados e mensageria. Os fluxos de sequência usam PlantUML convencional, pois não representam um nível do C4 Model.

## 2. Diagramas ilustrados

As imagens abaixo são capturas anteriores. **Depois das alterações nos arquivos `.puml`, elas precisam ser geradas e substituídas novamente** para refletir a arquitetura atual. Os arquivos `.puml` são a fonte oficial.

**C4 Nível 1: Contexto**

![C4 Nível 1: Contexto do CashFlow](images/c4-context.png)

**C4 Nível 2: Contêineres**

![C4 Nível 2: Contêineres do CashFlow](images/c4-containers.png)

**Sequência: registro de lançamento**

![Sequência de registro de lançamento](images/sequence-register.png)

**Sequência: falha do consolidado**

![Sequência de falha do consolidado](images/sequence-failure.png)

**Sequência: consulta do consolidado**

![Sequência de consulta do consolidado](images/sequence-query.png)

> **Atenção:** os PNGs existentes foram gerados antes da atualização da arquitetura. Renderize novamente os arquivos `.puml` e substitua os PNGs para manter documentação e imagens sincronizadas.

## 3. Arquivos editáveis

| Visão | Pergunta que responde | Fonte |
| --- | --- | --- |
| **C4 nível 1: Contexto** | Quem utiliza o CashFlow? | [c4-context.puml](c4-context.puml) |
| **C4 nível 2: Contêineres** | Como dividimos os serviços e seus dados? | [c4-containers.puml](c4-containers.puml) |
| **Sequência: registro** | Como registrar e depois consolidar um lançamento? | [sequence-register.puml](sequence-register.puml) |
| **Sequência: falha** | Por que registrar continua possível com o consolidado fora do ar? | [sequence-failure.puml](sequence-failure.puml) |
| **Sequência: consulta** | Como o comerciante consulta o resultado do dia? | [sequence-query.puml](sequence-query.puml) |

## 4. Responsabilidades na arquitetura proposta

- **Serviço de lançamentos:** recebe, valida e confirma registros financeiros.
- **Banco de lançamentos:** guarda lançamentos confirmados e eventos pendentes na mesma transação.
- **Worker Outbox (interno ao serviço de lançamentos):** lê eventos pendentes no próprio banco do serviço e publica na mensageria, sem constituir um terceiro serviço de negócio.
- **Mensageria:** entrega eventos ao consumidor, inclusive após falhas temporárias.
- **Serviço de consolidado:** mantém a visão agregada por data e oferece consulta.
- **Banco do consolidado:** guarda resultados calculados e identificadores já processados.
- **Provedor de identidade (externo, a definir):** realiza autenticação; os serviços verificam tokens localmente com chaves públicas, evitando exigir chamada ao provedor em cada requisição.
- **Coleta de observabilidade (proposta):** recebe telemetria dos serviços; aplicações também podem produzir logs estruturados em stdout para coleta pela infraestrutura.

O fluxo de registro **não chama o consolidado**. O processamento assíncrono pode atrasar, mas um lançamento já confirmado deve permanecer preservado.

A arquitetura contempla a proposta de **Transactional Outbox** e **idempotência**. A biblioteca de publicação é um componente **interno ao serviço de lançamentos**, mesmo quando executada por processo ou worker isolado em implantação futura. A escolha de tecnologias, a política de retentativas e as garantias operacionais serão formalizadas nas ADRs e nos documentos de resiliência das próximas issues.

## 5. Protocolos e responsabilidades transversais

| Comunicação | Protocolo proposto | Situação |
| --- | --- | --- |
| Cliente → APIs | HTTPS / JSON, API REST | Proposta de interface |
| Autenticação de usuário | OpenID Connect sobre HTTPS | Proposta, provedor a escolher |
| Autorização de chamadas | OAuth 2.0 Bearer JWT com validação local e JWKS | Proposta, escopos/permissões a definir |
| Serviços → seus bancos | SQL sobre TCP com conexão protegida | Proposta, banco a escolher |
| Worker Outbox → broker → consolidado | AMQP 0-9-1 | Proposta, depende da escolha do broker |
| Serviços → coleta de traces/métricas | OTLP por gRPC | Proposta, infraestrutura de observabilidade a escolher |
| Logs | Logs estruturados, preferencialmente stdout + coleta da plataforma | Proposta |

O **protocolo** especifica como os componentes se comunicam, enquanto a **tecnologia** será escolhida nas ADRs. Por exemplo, podemos propor OAuth 2.0/OIDC sem decidir ainda entre provedores de identidade. A autenticação não deve introduzir uma dependência síncrona do provedor em cada lançamento, e falhas de autenticação ou de banco continuam sendo fatores independentes da disponibilidade do consolidado.

O **C4 Nível 2** mostra os serviços, suas dependências e integrações. O worker Outbox é componente interno do serviço de lançamentos; não é um serviço de negócio adicional. O detalhamento de classes ou componentes internos pode ser acrescentado futuramente no C4 Nível 3, se necessário.

## 6. Decisões ainda pendentes

- **Issue #3:** definição de saldo diário, histórico e política de estorno.
- **Issue #7:** linguagem, framework, persistência, mensageria e trade-offs.
- **Issue #8:** estratégias de recuperação, reentrega, DLQ e reconciliação.
- **Issue #9:** segurança, proteção de dados e LGPD.
- **Issue #10:** implantação de referência, custos e impactos operacionais.

Não há definição de cloud no enunciado; os diagramas são independentes de fornecedor.

## 7. Como visualizar ou editar

Os arquivos **.puml** são a fonte dos diagramas e podem ser abertos no **VS Code com a extensão PlantUML** ou renderizados com o PlantUML CLI. Os diagramas C4 incluem a biblioteca que já vem na distribuição moderna do PlantUML:

```plantuml
!include <C4/C4_Container>
```

Para gerar imagens localmente com uma instalação do PlantUML, execute a partir da raiz do projeto:

```bash
plantuml -tsvg docs/architecture/*.puml
```

**Observação:** o GitHub não renderiza automaticamente arquivos `.puml`. Os PNGs devem ser atualizados após cada alteração da fonte. A renderização e conferência visual desta revisão ainda estão pendentes.

## 8. Critérios da Issue #6

- [x] C4 contexto criado usando elementos C4.
- [x] C4 contêineres criado usando elementos C4.
- [x] Fluxos de registro, falha e consulta criados em PlantUML.
- [x] Padrão de nomenclatura e formato de fontes definidos.
- [x] Outbox representado como componente interno e protocolos propostos identificados.
- [x] Identidade e observabilidade representadas sem antecipar fornecedor.
- [x] Fontes editáveis versionadas no repositório.
- [ ] Renderizar e revisar layout visual e legibilidade.
- [ ] Aprovação final.
