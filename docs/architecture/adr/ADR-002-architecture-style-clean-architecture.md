# ADR-002: Microsserviços financeiros com Clean Architecture

> **Status:** decidida  
> **Data:** 2026-11-14  
> **Issue:** [#7, Documentar ADRs e trade-offs](https://github.com/wolwerr/CashFlow-Architecture/issues/7)  
> **Decisão relacionada:** [ADR-001: C# / ASP.NET Core / .NET 10 LTS](ADR-001-language-framework.md)

## 1. Contexto

O CashFlow precisa registrar créditos e débitos e disponibilizar o consolidado diário. O serviço de lançamentos **deve continuar operando quando o serviço de consolidado estiver indisponível** (RNF-01). O consolidado deve atender pico de **50 RPS**, com limite de até **5% de requisições perdidas**, de acordo com a interpretação ainda aberta (RNF-02/RNF-03).

A [Building Block View](../building-block-view.md) propõe uma solução completa com Web/Mobile, API Gateway, IAM e observabilidade, mas o MVP avaliado precisa demonstrar sobretudo os **dois serviços financeiros independentes**. A [ADR-001](ADR-001-language-framework.md) já definiu C# / .NET 10 LTS e ASP.NET Core.

Esta ADR decide **as fronteiras de implantação e a estrutura interna do backend**. Não define banco de dados, broker, tecnologia do IAM ou detalhes da cloud.

## 2. Alternativas e trade-offs

| Alternativa | Benefícios | Custos / riscos | Avaliação |
| --- | --- | --- | --- |
| **Monólito tradicional em camadas** | Entrega inicial simples e menos serviços | Isolamento da falha do consolidado e escala independente ficam menos explícitos; risco de acoplamento | Não escolhida para o MVP |
| **Monólito modular com Clean Architecture** | Baixo custo operacional, módulos bem definidos e implantação simples | Processo e ciclo de deploy compartilhados; exige cuidados adicionais para cumprir a independência durante falhas | Viável, mas menos aderente ao desenho decidido |
| **Dois microsserviços financeiros, cada um com Clean Architecture** | Processos e deploys independentes, isolamento de dados, possibilidade de escalar consultas separadamente | Maior complexidade de integração, observabilidade, testes e operação | **Proposta escolhida** |
| **Microsserviços com muitas subdivisões e camadas próprias** | Separação detalhada de responsabilidades | Fragmentação, latência, custo e desenvolvimento excessivos para o escopo | Não escolhida |

**Observação:** separar processos não garante resiliência automaticamente. Ela depende da inexistência de chamadas síncronas ao consolidado no caminho de registro, da persistência confiável e dos testes de falha.

## 3. Decisão registrada

Adotar **dois serviços financeiros implantáveis de forma independente**:

1. **Transactions (Lançamentos):** expõe API de crédito e débito, valida regras financeiras, grava transação e Outbox atomicamente; o worker Outbox permanece **no mesmo limite de propriedade do serviço**, ainda que possa ter processo de execução distinto no futuro.
2. **Consolidation (Consolidado):** consome eventos, mantém projeções e consulta saldos diários, garantindo idempotência na atualização.

Adotar **Clean Architecture como organização interna de cada serviço**, com a regra das dependências apontando para o núcleo de negócio:

```text
API / Host  --->  Application  --->  Domain
      |                ^
      v                |
Infrastructure --------+
```

A figura representa **dependências de código**, não chamadas em tempo de execução. `Infrastructure` implementa contratos definidos em `Application` ou `Domain`; a API/Host atua como *composition root* e conecta implementações concretas pela injeção de dependências.

### Responsabilidades das camadas

| Camada | Responsabilidades | Não deve conter |
| --- | --- | --- |
| **Domain** | Entidades, value objects, invariantes e regras financeiras puras | ASP.NET Core, ORM, RabbitMQ, acesso a rede |
| **Application** | Casos de uso, orquestração, comandos, consultas e portas/interfaces necessárias | SQL, bibliotecas de mensageria, controllers |
| **Infrastructure** | Persistência, implementação de repositórios/portas, broker, Outbox publisher e integrações | Decisões financeiras de negócio |
| **Api / Host** | Endpoints HTTP, autenticação no limite, DI, health checks e ciclo de vida de workers | Cálculos financeiros e acesso a dados direto no controller |

As regras de autorização por `merchant_id` devem ser verificadas no limite apropriado e o escopo autorizado propagado aos casos de uso; não confiar em `merchant_id` arbitrário fornecido pelo cliente.

### Estrutura inicial da solução (.NET 10)

```text
CashFlow.sln
src/
  Services/
    Transactions/
      CashFlow.Transactions.Domain/
      CashFlow.Transactions.Application/
      CashFlow.Transactions.Infrastructure/
      CashFlow.Transactions.Api/
    Consolidation/
      CashFlow.Consolidation.Domain/
      CashFlow.Consolidation.Application/
      CashFlow.Consolidation.Infrastructure/
      CashFlow.Consolidation.Api/
tests/
  CashFlow.Transactions.UnitTests/
  CashFlow.Transactions.IntegrationTests/
  CashFlow.Consolidation.UnitTests/
  CashFlow.Consolidation.IntegrationTests/
```

**Uma única `CashFlow.sln` não significa monólito nem uma única aplicação:** ela é apenas o agrupador dos projetos no monorepo. `CashFlow.Transactions.Api` e `CashFlow.Consolidation.Api` são aplicações executáveis distintas, com processos, imagens Docker, bancos lógicos, pipelines e ciclos de implantação independentes. Não há fase planejada de 'extrair módulos' para criar microsserviços: eles já são independentes desde o MVP.**

Os dois projetos `.Api` são os hosts de execução da versão inicial. O `BackgroundService` do Outbox pode ser registrado no host de Transactions com implementação/portas de Infrastructure. **Não criar um terceiro microsserviço apenas para o publisher**. Se a implantação demandar escala ou isolamento adicional, o worker pode ganhar host separado sem mudar a propriedade dos dados ou as regras do Domain.

É permitido compartilhar componentes técnicos pequenos (por exemplo, convenções de correlação e observabilidade), **mas não compartilhar entidades de domínio, repositórios ou acesso direto ao banco de outro serviço**. Contratos de eventos devem ser estáveis e versionáveis, sem forçar dependências diretas entre aplicações.

## 4. Fluxos importantes e limites

**Registro financeiro:**
- API de Transactions valida os dados e a autorização.
- Application executa o caso de uso.
- Infrastructure persiste o lançamento e o evento Outbox em uma única transação.
- A API confirma o registro sem aguardar o Consolidation.
- O worker publica o evento posteriormente; confirmá-lo no broker não equivale a concluir a consolidação.

**Consolidação:**
- Consumidor recebe o evento do broker.
- Application aplica regra de projeção e verifica a idempotência.
- Infrastructure grava `ProcessedEvent` e o saldo atualizado na **mesma transação**.
- ACK ao broker ocorre após commit.

**Consulta de saldo:** a API de Consolidation lê o banco de projeções próprio, sem consultar Transactions.

**Falhas:** o sistema pode ficar temporariamente desatualizado quando o broker ou o consolidado falham, mas a gravação de novos lançamentos confirmados não depende da disponibilidade desses componentes. Essa garantia não abrange falhas no próprio banco de Transactions ou em toda a borda Web/Gateway.

## 5. Consequências positivas

- Separação do núcleo de negócio dos frameworks e detalhes de infraestrutura.
- Testes unitários rápidos das regras financeiras sem banco, broker ou HTTP.
- Isolamento do ciclo de implantação dos dois serviços financeiros.
- Menor risco de contaminação do domínio por dependências de ASP.NET Core, ORM ou mensageria.
- Flexibilidade para substituir a implementação de persistência e integração sem reescrever casos de uso.

## 6. Consequências negativas e riscos

| Risco | Consequência | Mitigação |
| --- | --- | --- |
| Excesso de abstração | Mais arquivos, interfaces sem uso e menor velocidade de entrega | Criar apenas portas para dependências reais; evitar repository/service genéricos desnecessários |
| Dois serviços independentes | Mais deploys, configuração e observabilidade | Docker Compose, health checks, logs estruturados e documentação |
| Mensagens duplicadas/fora de ordem | Saldo incorreto se não houver idempotência e regra de ordenação | ADR-005, identificação por evento, atualização atômica e testes |
| Dependências ocultas entre serviços | Falha do consolidado contamina lançamentos | Proibir chamada síncrona ao consolidado no fluxo de registro |
| Compartilhamento de código de domínio | Acoplamento e evolução simultânea dos serviços | Domínios separados e contratos de integração explícitos |
| Inexperiência com a organização dos projetos .NET | Entrega mais lenta | Solução mínima, convenções simples e validação via testes |

## 7. Qualidade, operação e verificação

- A estratégia de testes e eventuais metas de cobertura (como 80% de linhas) serão detalhadas no planejamento de desenvolvimento, fora do registro de ADRs; esta decisão assegura *testabilidade* por desenho. A [ADR-010](ADR-010-ci-cd-github-actions.md) define o uso de GitHub Actions para CI/CD.
- Testes unitários focam Domain e Application; integração comprova transações de banco, Outbox e consumo idempotente; testes de resiliência validam o isolamento; testes de carga validam 50 RPS.
- A [ADR-010](ADR-010-ci-cd-github-actions.md) registra GitHub Actions como ferramenta de CI/CD; os *quality gates* concretos serão definidos na implementação dos workflows e na estratégia de testes de desenvolvimento.
- Traces, métricas e logs devem correlacionar API, worker e consumer. Auditoria financeira persistida é distinta de logs operacionais.
- É esperado que cada serviço tenha seu próprio banco **lógico**; infra física pode ser compartilhada no ambiente local sem permitir consultas cruzadas aos dados de outro domínio.

## 8. Critérios de aceite da decisão

1. Os projetos compõem e compilam respeitando as dependências **Domain ← Application ← Infrastructure/Api**.
2. O Domain não referencia projetos de Infrastructure, Api ou bibliotecas de acesso a dados.
3. Transactions e Consolidation podem ser iniciados e interrompidos separadamente.
4. Desligar Consolidation não impede registrar crédito/débito válido em Transactions.
5. Não existem leituras ou escritas diretas no banco de outro domínio.
6. O worker Outbox e o consumidor podem ser testados com substitutos e em integração real.
7. O README e os testes permitem reproduzir os cenários essenciais.

## 9. Quando revisitar

Revisar se a divisão em dois processos se mostrar desproporcional ao contexto real, se a operação exigir isolamento maior do worker, ou se a modelagem de domínios e requisitos financeiros for refinada de modo significativo. Não aumentar o número de microsserviços apenas para refletir camadas de código.

## 10. Referências

- [ADR-001](ADR-001-language-framework.md)
- [Requisitos não funcionais](../../requirements/non-functional-requirements.md)
- [Visão de Blocos de Construção](../building-block-view.md)
- [Diagramas de sequência](../sequence-diagrams.md)
