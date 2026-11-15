# ADR-003: PostgreSQL e Entity Framework Core para persistência

> **Status:** decidida  
> **Data:** 2026-11-14  
> **Issue:** [#7, Documentar ADRs e trade-offs](https://github.com/wolwerr/CashFlow-Architecture/issues/7)  
> **Relacionadas:** [ADR-001](ADR-001-language-framework.md), [ADR-002](ADR-002-architecture-style-clean-architecture.md), ADR-005 (Outbox e consistência)

## Contexto e requisitos

O CashFlow mantém lançamentos de crédito/débito, Outbox transacional, projeções de saldo diário e identificadores de eventos consumidos. O desafio requer que o registro continue quando o consolidado estiver indisponível (RNF-01) e que consultas ao consolidado suportem 50 RPS, com até 5% de perda de requisições conforme interpretação pendente (RNF-02/RNF-03).

A ADR-002 define **dois microsserviços com propriedade independente de dados**, sem consultas cruzadas aos bancos alheios. A ADR-001 define C#/.NET 10 LTS. Precisamos selecionar um SGBD e uma estratégia de persistência que favoreçam **transações, integridade, migrations, testes e reprodutibilidade local**.

## Decisão de modelo de dados: relacional versus não relacional

Antes de escolher o produto PostgreSQL, a arquitetura propõe **persistência relacional como modelo primário dos dois serviços financeiros**. Essa é uma decisão de adequação ao domínio, **não uma afirmação de que bancos NoSQL sejam incapazes de garantir transações ou consistência**.

| Critério | Relacional (PostgreSQL / SQL Server) | Não relacional (ex.: MongoDB ou chave-valor) |
| --- | --- | --- |
| Lançamento + Outbox no mesmo commit | Transação ACID e constraints naturais para o modelo | Pode ser viável, inclusive com transações em certos produtos, mas exige avaliar modelagem, limites e operação específicos |
| Integridade e idempotência | Chaves únicas, índices, integridade referencial quando aplicável e transações | Pode oferecer índices únicos e transações; garantias variam por produto e modelo de dados |
| Saldo diário por comerciante e data | Boa adequação a agregações e consultas SQL com índices | Possível por projeções ou agregações específicas, sem benefício claro demonstrado neste caso |
| Flexibilidade de esquema | Migrations e esquema explícito | Frequentemente mais flexível para estruturas variáveis |
| Escalabilidade | Replicação, particionamento e opções gerenciadas, sujeitos a desenho e operação | Alguns produtos priorizam escalabilidade horizontal, com trade-offs próprios |
| Complexidade neste MVP | Menos decisões adicionais para um domínio financeiro estruturado | Demandaria justificar modelagem e consistência específicas sem ganho comprovado |

### Justificativa arquitetural

1. **Integridade financeira:** crédito/débito confirmado e respectivo evento Outbox devem persistir juntos. A transação no banco de Transactions reduz o risco de registro sem evento para consolidação.
2. **Atualização idempotente:** no Consolidation, a projeção de saldo e o identificador do evento aplicado devem ser gravados atomicamente, com unicidade verificável para prevenir contabilização duplicada.
3. **Modelo de dados estruturado:** lançamentos, comerciantes, datas, tipos de movimento e saldos possuem relações e restrições conhecidas. Esquema explícito, constraints e SQL favorecem revisão e auditoria.
4. **Simplicidade e evidência:** duas bases relacionais por domínio permitem implementar e testar essas garantias sem introduzir complexidade adicional de modelagem NoSQL.

### Trade-offs e condições de revisão

- Um banco relacional **não garante ausência de perda, duplicidade ou corrida por si só**: são necessárias transações corretamente delimitadas, índices/constraints, estratégia de concorrência e testes.
- Bancos não relacionais **não são descartados por falta genérica de ACID**. MongoDB, por exemplo, oferece transações multi-documento; uma solução NoSQL poderia ser válida com modelo, requisitos e garantias demonstrados.
- A decisão pode ser revista caso apareçam requisitos de volume, distribuição geográfica, estrutura documental altamente variável ou métricas de operação que justifiquem outro modelo. Adoção pontual de cache, mecanismo de busca ou armazenamento documental não substitui automaticamente o banco transacional de lançamentos.
- Não propomos **polyglot persistence** no MVP: introduzir outro motor de dados só fará sentido com benefício medido.

Assim, a escolha segue **em duas etapas**: (1) modelo relacional para os registros financeiros; (2) PostgreSQL como SGBD relacional preferido frente ao SQL Server, detalhado abaixo.

## Alternativas de banco de dados

| Alternativa | Vantagens | Riscos / custos |
| --- | --- | --- |
| **PostgreSQL** | Código aberto; transações ACID; restrições únicas; índices; suporte .NET via Npgsql; Docker simples; opções gerenciadas em múltiplas clouds | Operação e tuning precisam ser planejados; especialidades da equipe podem favorecer outro SGBD |
| **SQL Server** | Integração madura com o ecossistema Microsoft, EF Core e Azure SQL; recursos de operação e monitoramento | Licenciamento/custo dependem de edição e implantação; portabilidade de scripts e dependência de fornecedor |
| **Banco documental ou NoSQL como armazenamento primário dos lançamentos** | Flexibilidade de modelo e alguns padrões de escala | Não oferece vantagem clara para os invariantes relacionais e transações exigidos neste MVP; adiciona risco de modelagem |

Os dois bancos relacionais são tecnicamente adequados ao desafio. Não há benchmark ou requisito corporativo que prove superioridade de performance de um sobre o outro.

## Alternativas de acesso aos dados

| Alternativa | Vantagens | Desvantagens |
| --- | --- | --- |
| **Entity Framework Core 10** | Mapeamento de entidades, migrations, LINQ, suporte transacional, DI e integração com .NET | Pode gerar consultas ineficientes se usado sem análise de SQL e índices |
| **Dapper** | Micro-ORM leve, controle explícito de SQL e mapeamento enxuto | Exige SQL manual e uma estratégia separada de migrations/modelagem |
| **ADO.NET/Npgsql diretamente** | Controle máximo e poucas abstrações | Mais código repetitivo de conexão, parâmetros e mapeamento |

## Decisão decidida

1. Adotar **PostgreSQL** como SGBD dos dois domínios financeiros.
2. Utilizar **Entity Framework Core 10**, com o provider **Npgsql.EntityFrameworkCore.PostgreSQL** compatível, como acesso a dados principal.
3. **Um banco lógico por microsserviço** desde o MVP:
   - `cashflow_transactions`: lançamentos, auditoria financeira, Outbox.
   - `cashflow_consolidation`: projeções/saldos diários, eventos processados.
4. No Docker Compose local, os bancos podem compartilhar uma instância PostgreSQL, mas não compartilhar tabelas nem credenciais de escrita; em produção, a separação física e a disponibilidade devem ser avaliadas conforme risco e custo.
5. O serviço de IAM, caso implementado, terá persistência própria, definida pela ADR-006; não participa dos bancos financeiros.
6. Considerar **Dapper ou SQL explícito apenas após medição** de consultas específicas. Não adotar duas abstrações por padrão.

### Regras transacionais e de integridade

- **Transactions:** persistir lançamento e evento Outbox em **uma única transação**. Confirmar HTTP apenas após commit; publicação no broker ocorre em outro momento, conforme ADR-005.
- **Consolidation:** inserir identificador de evento processado e atualizar projeção de saldo em **uma única transação**. Usar chave única em `event_id` (com escopo apropriado), para que reentregas não dupliquem o saldo.
- Para consultas concorrentes e atualizações de projeção, definir uma estratégia segura (por exemplo, `INSERT ... ON CONFLICT DO UPDATE` ou bloqueio/concorrência controlada), a comprovar em teste; não assumir que leitura seguida de gravação é segura em concorrência.
- Valores financeiros devem usar `decimal` no .NET e `numeric(p,s)` no PostgreSQL, com precisão, escala, moeda, arredondamento e semântica de saldo definidos nos requisitos antes de fixar o esquema.
- Assegurar `merchant_id` nos índices e consultas pertinentes, evitando vazamento entre comerciantes. A política completa de autorização fica na ADR-006 / Issue #9.
- Migrations versionadas **por serviço**, executadas por processo controlado; não aplicar migrations automaticamente em toda subida de réplica de produção.
- Backups, restauração, retenção, índices e segurança devem ser definidos para o ambiente real; Docker Compose não fornece alta disponibilidade por si só.

## Dependências com Clean Architecture

- **Domain:** entidades, value objects, invariantes; sem dependência de EF Core.
- **Application:** casos de uso e portas, como `ITransactionRepository` e `IUnitOfWork` somente quando fizerem sentido; evitar abstrações genéricas redundantes.
- **Infrastructure:** `DbContext`, mapeamentos, migrations, queries e implementações de persistência.
- **Api / Host:** composição de dependências e configuração via DI, sem queries de negócio em endpoints.

## Consequências e riscos

| Aspecto | Benefício ou risco | Mitigação |
| --- | --- | --- |
| PostgreSQL no MVP | Licenciamento simples e execução local reprodutível | Fixar versão de imagem e documentar setup, backups e restore |
| EF Core | Menor esforço para CRUD, migrations e transações | Inspecionar SQL, índices, `AsNoTracking` em leituras, paginação e métricas |
| Bases lógicas separadas | Autonomia de esquema e propriedade dos dados | Credenciais separadas; proibir joins/foreign keys entre serviços |
| Instância física compartilhada no ambiente local | Menos recursos para demonstração | Declarar explicitamente que é isolamento **lógico**, não garantia de HA |
| Concorrência no consolidado | Risco de perda de atualização de saldo | UPSERT/transação/constraint e teste concorrente |
| Falha na publicação Outbox | Eventos pendentes após commit | Worker com retry, confirmação de publicação e monitoramento, detalhados na ADR-005 |
| Mudanças de schema | Migrações incompatíveis podem interromper serviços | Evolução compatível, migrations por domínio e teste de rollback/restore |

## Como validar

1. Subir PostgreSQL localmente via Docker Compose e aplicar migrations dos dois serviços.
2. Comprovar banco e credenciais separados.
3. Testar inserção atômica de lançamento + Outbox, inclusive rollback após falha simulada.
4. Testar idempotência e consistência sob reentrega e concorrência, verificando saldo final.
5. Parar Consolidation e continuar registrando em Transactions.
6. Executar consultas de saldo com **50 RPS**, medir latência, taxa de sucesso, índices e recursos usados.
7. Registrar SQL/planos de execução das consultas problemáticas antes de introduzir Dapper ou ajustes específicos.

## Alternativas descartadas e critérios para revisitar

**Não selecionar SQL Server neste momento** por falta de requisito corporativo explícito ou ganho demonstrado para este desafio, não por incapacidade técnica. Reavaliar se a operação real exigir SQL Server/Azure SQL, existir restrição de plataforma ou houver evidência mensurada de custo/benefício.

**Não selecionar Dapper como padrão inicial**, mas avaliar sua adoção pontual no caminho de consulta se o EF Core gerar gargalos confirmados.

## Referências externas

- [Npgsql: provider de EF Core para PostgreSQL](https://www.npgsql.org/efcore/)
- [Pacote Npgsql.EntityFrameworkCore.PostgreSQL 10](https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL/)
- [EF Core: provider SQL Server](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/)
- [EF Core: migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing)
- [PostgreSQL: constraints](https://www.postgresql.org/docs/current/ddl-constraints.html)
- [Dapper: projeto oficial](https://github.com/DapperLib/Dapper)

## Referências internas

- [Requisitos não funcionais](../../requirements/non-functional-requirements.md)
- [Visão de Blocos de Construção](../building-block-view.md)
- [Modelo de Dados](../data-model.md)
