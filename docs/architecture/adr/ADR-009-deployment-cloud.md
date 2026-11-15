# ADR-009: Implantação local, alternativas de cloud e custo total

> **Status:** aceita  
> **Data:** 2026-11-15  
> **Issue:** [#7, Documentar ADRs e trade-offs](https://github.com/wolwerr/CashFlow-Architecture/issues/7)  
> **Orçamento por ambiente e lacunas para total:** [Plano LOCAL, DEV, HOM, PROD](../four-environment-total-cost-plan.md)  
> **Comparativo Kubernetes prioritário:** [AKS versus EKS, custo por serviço](../aks-eks-cost-breakdown.md)  
> **Comparativo original de modelos alternativos:** [ACA, AKS e ECS/Fargate](../cloud-cost-comparison.md)  
> **Relacionadas:** [ADR-002](ADR-002-architecture-style-clean-architecture.md), [ADR-003](ADR-003-persistence.md), [ADR-004](ADR-004-async-messaging.md), [ADR-005](ADR-005-outbox-consistency-idempotency.md), [ADR-006](ADR-006-identity-and-access.md), [ADR-007](ADR-007-clients-api-gateway.md), [ADR-008](ADR-008-observability.md)

## Contexto

CashFlow separa Transactions e Consolidation, com persistência PostgreSQL por domínio e publicação confiável via Transactional Outbox e RabbitMQ. A aplicação Web utiliza React com NGINX como reverse proxy; autenticação via Keycloak. A observabilidade foi decidida na ADR-008 com OpenTelemetry Collector, Prometheus, Loki, Tempo e Grafana.

É necessário demonstrar **continuidade do registro quando o consolidado falhar**, além da consulta a **50 RPS com até 5% de perda** segundo a interpretação documentada nos RNFs. A escolha de plataforma precisa considerar custos e operação de **toda a arquitetura**, não só das duas APIs.

O desafio não informou região obrigatória, orçamento ou SLA anual. Portanto o comparativo de cloud constitui **estudo de engenharia com premissas**. A decisão final de referência é **Azure AKS em East US**, com serviços de aplicação no Kubernetes e Azure Database for PostgreSQL Flexible Server externo ao cluster. O estudo AWS EKS permanece como alternativa histórica documentada.

## Decisão aprovada em 2026-11-15: Azure AKS e orçamento-base da calculadora

**ADR-009 aceita.** A plataforma de referência é **Azure Kubernetes Service (AKS), região East US**, com PostgreSQL gerenciado externo ao Kubernetes. Amazon EKS permanece alternativa analisada, **não escolhida**. A decisão aceita o **orçamento-base de referência**, não aprova aquisição nem afirma HA validada em execução.

A estimativa foi preenchida manualmente na **Azure Pricing Calculator** com 730 horas/mês, tarifas exibidas em USD, sem descontos contratuais. Os valores são os totais exibidos nos grupos, com arredondamentos por produto.

| Ambiente / grupo | Mensal (USD) | Recursos e limites adotados |
| --- | ---: | --- |
| **LOCAL** | 0,00 de cloud | Docker Compose no computador de desenvolvimento; hardware e operação fora da conta Azure |
| **DEV** | **204,34** | AKS sem SLA, 1 nó D4as v5 com OS Disk P10, 3 PV P4, PostgreSQL B1ms/32 GiB, Blob Hot LRS 20 GB, Standard LB, 2 IPv4 Standard, Key Vault, banda 20 GB |
| **HOM** | **204,34** | Infraestrutura própria, isolada de DEV e PROD, **mesma capacidade econômica de DEV**; sem redimensionamento de HOM incluído no orçamento aprovado |
| **PROD** | **890,44** | AKS Standard com SLA, 3 nós D4as v5 e 3 OS Disks P10, 4 PV P10, PostgreSQL General Purpose D2s v3/32 GiB com HA habilitada, Blob Hot ZRS 200 GB, Standard LB, 2 IPv4 Standard, Key Vault, banda 100 GB, 100 GB de snapshot estimados |
| **SHARED** | **5,90** | 1 ACR Basic, 1 zona pública Azure DNS (1 milhão de consultas/mês) |
| **TOTAL DE REFERÊNCIA** | **1.305,02** | 204,34 + 204,34 + 890,44 + 5,90 |
| **TOTAL ANUAL DE REFERÊNCIA** | **15.660,24** | 12 vezes o valor mensal agregado, arredondado |

**HOM permanece econômico por decisão expressa.** Valida funcionalidade, contratos, integração e deploy em ambiente independente. **Não prova por si só** latência ou disponibilidade de PROD sob a mesma carga. Testes de 50 RPS e resiliência devem ter capacidade adequada e resultados medidos; nenhum custo de expansão temporária está previsto neste baseline.

### Valores detalhados confirmados na calculadora

| Componente | DEV | HOM | PROD | SHARED |
| --- | ---: | ---: | ---: | ---: |
| AKS com discos de sistema | 145,27 | 145,27 | 508,81 | - |
| Managed Disks (dados) | 15,84 | 15,84 | 78,84 | - |
| PostgreSQL Flexible Server | 16,09 | 16,09 | 257,02 | - |
| Blob Storage | 1,46 | 1,46 | 6,49 | - |
| Load Balancer | 18,35 | 18,35 | 18,75 | - |
| Public IPs (2 por ambiente) | 7,30 | 7,30 | 7,30 | - |
| Key Vault | 0,03 | 0,03 | 0,03 | - |
| Bandwidth (Internet egress) | 0,00 | 0,00 | 0,00 | - |
| Snapshots (100 GB, PROD) | - | - | 13,20 | - |
| Azure Container Registry Basic | - | - | - | 5,00 |
| Azure DNS público | - | - | - | 0,90 |
| **Subtotal por grupo** | **204,34** | **204,34** | **890,44** | **5,90** |

**Precisão:** o total anual é aproximado e não inclui impostos nem câmbio; a interface pode apresentar diferença de centavos por arredondamento. Os **100 GB de snapshots** correspondem a volume de snapshot cobrado, não comprovam retenção de sete dias ou recuperação consistente de RabbitMQ. A calculadora informou **HA habilitada** para PostgreSQL PROD, mas não comprovou modalidade *zone-redundant*. Os **2 IPv4** por ambiente são hipótese de entrada e saída; confirmar a topologia real. O valor de **Internet egress igual a zero** resulta do preenchimento por grupo e não garante gratuidade agregada no contrato. O ACR Basic não oferece Private Endpoint e a política de segurança de PROD precisa considerar o trade-off.

**Custos/riscos não encerrados por esta ADR:** consumo real e retenção de logs/traces, operações de storage, backups e restauração consistentes, saída e transferências entre zonas, serviços de proteção/segredos adicionais, expansão do Cluster Autoscaler, escalabilidade/HA do RabbitMQ, disponibilidade N+1, custo de capacidade adicional em HOM para testes não funcionais, eventuais políticas de rede privada e suporte. São pendências de execução e reestimativa, **não condição para marcar a ADR-009 aceita**.

## Alternativas avaliadas

| Alternativa | Benefícios | Trade-offs |
| --- | --- | --- |
| **Docker Compose local** | Reprodutibilidade, baixa barreira para demonstração, isolamento lógico entre containers | Sem HA automática, falha do host é falha de toda a solução; backups e secrets precisam definição |
| **Azure Container Apps (ACA)** | Runtime gerenciado, escalabilidade separada por serviço, menos administração das APIs stateless | RabbitMQ e observabilidade com estado exigem topologia adicional; preço depende da atividade/idle e conexões; arquitetura heterogênea |
| **Azure Kubernetes Service (AKS)** | Kubernetes padronizado, maior controle dos workloads e possibilidade de operar stack completa em cluster | Nós, SLA do control plane, rede, storage, atualização e gestão de recursos elevam custo fixo e complexidade; Kubernetes não garante HA sozinho |
| **Amazon Elastic Kubernetes Service (EKS) com nós EC2** | Kubernetes gerenciado na AWS; workloads e Helm/manifests semelhantes aos AKS, com PostgreSQL no RDS externo | EC2, plano de controle, EBS/S3, ALB/NAT e operação dos recursos com estado precisam ser cotados |
| **AWS ECS com Fargate** | Infraestrutura gerenciada para containers stateless, escala independente, modelo pay-per-resource, sem exigir Kubernetes | Não equivale a um cluster Kubernetes e requer componentes stateful adicionais, ALB/NAT/ECR/RDS/storage |
| **VMs + Docker Compose em cloud** | Familiaridade e menor esforço inicial | Escalabilidade, recuperação, isolamento de falhas e HA exigem engenharia manual; risco de ponto único |

## Decisões adotadas e fundamentos

### 1. MVP: Docker Compose

**Decisão:** adotar Docker Compose como ambiente de desenvolvimento, integração e demonstração. O arranjo local deve incluir, conforme código implementado:

- React estático servido por **NGINX**.
- **Transactions** e **Consolidation** com imagens, configurações, migrations e ciclos de vida independentes.
- **PostgreSQL** com bancos lógicos separados para Transactions, Consolidation e Keycloak, sem acesso cruzado direto.
- **RabbitMQ** com persistência, filas apropriadas, ACK manual, retries/DLQ e testes de redelivery.
- **Keycloak** usando OIDC/PKCE e base persistente.
- **OpenTelemetry Collector, Prometheus, Loki, Tempo e Grafana**, com pipelines reais e limites de armazenamento.

Não prometer HA em Compose com um nó para cada infraestrutura. Não interpretar monorepo como monólito de implantação. O Outbox worker está acoplado ao executável de Transactions segundo a ADR-002, requerendo exclusão/coordenação apropriada ao escalar.

**Situação atual:** existe template inicial de NGINX em [infra/nginx](../../../infra/nginx/README.md) e documentação arquitetural, mas ainda não foi demonstrada a integração de todos esses containers nem testes de carga. Uma decisão arquitetural não é prova de implantação.

### 2. Produção de referência: AKS selecionado

**Decisão final:** **Azure AKS** em East US com PostgreSQL Flexible Server externo. O EKS segue como alternativa avaliada, sem necessidade de conclusão do custo integral AWS para esta decisão. [Comparativo histórico de AKS x EKS](../aks-eks-cost-breakdown.md).

| Critério | ACA | AKS | ECS/Fargate |
| --- | --- | --- | --- |
| Operar serviços .NET stateless | Conveniente, poucas abstrações | Kubernetes mais completo, exige plataforma | Conveniente, tarefas e serviços ECS |
| Operar RabbitMQ | Preferir broker externo/gerenciado ou cluster cuidadosamente desenhado | Possível via operador, quorum queues, discos e múltiplos nós | Requer broker gerenciado/EC2/estratégia de estado |
| Logs/traces/metrics self-hosted | Pode exigir VMs/serviços de dados e redes | Viável no cluster, atenção a volumes/HA | Possível com recursos dedicados, serviços externos e custos de rede |
| Escala independente | Por Container App e réplicas | Por Deployment/consumer | Por ECS Service/task |
| Custos fixos | Menores para escala real a zero, mas não presumir zero para APIs financeiras | Nós e eventual cobrança de control plane | Task-hours, rede, ALB e estado externo |
| Operação | Simplifica runtime, não elimina operação stateful | Mais trabalho Kubernetes | Simplifica runtime, não elimina operações auxiliares |

A escolha da cloud também deve considerar capacidades e experiência da equipe, requisitos de rede, localização de dados, oferta de broker, acordos comerciais e custos de suporte.

> **Aviso de leitura:** as três seções comparativas a seguir mantêm os **números históricos usados na análise das alternativas**, antes da escolha do AKS e da configuração de DEV/HOM/PROD. O PostgreSQL Azure **US$ 99,28** era um **B2ms sem HA**; no orçamento aceito, o PostgreSQL de PROD é **US$ 257,02 com HA habilitada**, e DEV/HOM são **US$ 16,09 cada**. As tabelas antigas de ACA, AKS e ECS/Fargate **não representam custo total**, nem substituem a tabela aprovada de **US$ 1.305,02/mês** no início desta ADR.

## Comparação histórica de Kubernetes (AKS x EKS, pré-decisão)

| Item | AKS Standard (Azure East US) | Amazon EKS (AWS us-east-1) |
| --- | ---: | ---: |
| 3 nós de 4 vCPU e 16 GiB | **US$ 376,68** | **US$ 420,48** |
| Plano de controle | **US$ 73,00** | **US$ 73,00** |
| PostgreSQL gerenciado, compute | **US$ 99,28** | **US$ 94,17** |
| Storage PostgreSQL incluído | **US$ 3,68** | **não cotado** |
| **Subtotal parcial (USD/mês)** | **US$ 552,64** | **US$ 587,65** |

Os serviços como Transactions, Consolidation, RabbitMQ, Keycloak, NGINX, Collector, Prometheus, Loki, Tempo e Grafana **compartilham nós**. Portanto, seus custos de computação não aparecem separadamente na fatura: o [documento de rateio](../aks-eks-cost-breakdown.md) atribui valores ilustrativos por componente segundo vCPU hipotética e apresenta os recursos efetivamente faturáveis. **Não confundir rateio com cobrança por pod.** O cenário não inclui PVs/EBS, object storage, rede, load balancer, NAT, registry, backups, zonas ou HA.

## Comparação histórica de custos calculados (pré-decisão)

O [estudo FinOps](../cloud-cost-comparison.md) documenta 730 h/mês e tarifas verificadas em novembro/2026, para **East US** e **us-east-1**:

| Modelo | Subtotal parcial USD/mês | Limitações principais |
| --- | ---: | --- |
| ACA | **543,88** | Supõe todas as réplicas ativas 24x7, não abate franquia e não inclui vários itens de rede/storage |
| AKS Standard | **552,64** | Três nós com 12 vCPU/48 GiB, mais capacidade que os demais modelos; sem storage/rede/HA de dados |
| ECS/Fargate | **378,49** | Sem storage RDS, EBS, ALB, rede, backups ou HA; não é custo final |

Os totais são **não comparáveis como faturas completas**. Devem ser apresentados como **subtotais de itens precificados**, com escopo e capacidade diferentes. Valores de entrada podem mudar. Não atribuir custo zero a serviços sem preços calculados.

## Refinamento histórico de custos: volumes, object storage e rede

O [comparativo AKS x EKS](../aks-eks-cost-breakdown.md) agora inclui um **cenário ampliado de itens precificados**, ainda não equivalente a TCO e sem HA:

| Item | AKS East US | EKS us-east-1 |
| --- | ---: | ---: |
| Subtotal inicial: nós, cluster e PostgreSQL básico | US$ 552,64 | US$ 587,65 |
| 3 volumes de 128 GiB para persistência | US$ 59,13 | US$ 30,72 |
| Object storage 200 GB para Loki/Tempo | US$ 4,16 | US$ 4,60 |
| Registry ACR Basic | ~US$ 5,00 | Pendente ECR |
| Load balancer | **Pendente Azure LB** | US$ 22,27 (ALB, 1 LCU médio) |
| NAT e 50 GB processados | **Pendente Azure NAT** | US$ 35,10 (1 gateway) |
| Storage RDS PostgreSQL 32 GiB | Incluído no subtotal Azure | **Pendente AWS RDS storage** |
| **Subtotal ampliado, componentes cotados** | **~US$ 620,93/mês** | **US$ 680,34/mês** |

**Não comparar as duas últimas cifras como fatura completa:** há componentes presentes na coluna AWS ainda não cotados na Azure e vice-versa. Os itens pendentes não têm custo zero. Discos, IOPS, snapshots, egress, IPs, Load Balancer/NAT Azure, RDS storage, ECR e backups exigem confirmação nas calculadoras oficiais. Nessa configuração **não há HA garantida**.

A decisão sobre armazenamento deve distinguir PVs (RabbitMQ, Prometheus e estado auxiliar) de object storage (Loki/Tempo), pois executar pods dentro dos nós Kubernetes **não elimina a cobrança de persistência**. As hipóteses de três volumes 128 GiB são apenas *sizing financeiro preliminar*; não justificam compartilhar um disco RWO entre pods distintos nem garantem capacidade sob carga.

**Decisão aceita:** AKS selecionado com orçamento-base **US$ 1.305,02/mês**. Custo efetivo, HA e 50 RPS dependem de validação técnica e operacional; não foram comprovados pelo orçamento.

## Decisão de isolamento de ambientes e revisão de orçamento (2026-11-15)

**Definido pelo projeto:** DEV e HOM **não compartilham Kubernetes, PostgreSQL, dados, credenciais nem infraestrutura de ambiente**. LOCAL continua Docker Compose. HOM **permanece econômico sem ampliação obrigatória** no orçamento aprovado; valida funcionalidades, contratos e implantação. A homologação nesse porte não substitui ensaios de carga e HA de PROD, que deverão ocorrer com infraestrutura capaz de reproduzir os requisitos.

**Correção de viabilidade:** o PostgreSQL Azure Burstable B2ms usado no preço anterior de PROD **não suporta HA zone-redundant**. O [plano de custo por ambiente](../four-environment-total-cost-plan.md) substitui essa premissa para PROD por **General Purpose D2s v3 com primary/standby zone-redundant**, a cotar definitivamente. A cifra anterior não serve para produção com HA.

**Estimativa final do escopo aprovado na calculadora Azure:** **US$ 1.305,02/mês**, incluindo DEV, HOM, PROD e SHARED conforme tabela no início desta ADR. Os valores anteriores de US$ 1.093,55 (Azure parcial) e US$ 1.211,58 (AWS proxy) são estudos históricos e não representam o orçamento aprovado.

**Faturamento real pode variar** com rede, operações de dados, backup/retenção, políticas de segurança, escalabilidade, disponibilidade e contratação. O orçamento é uma referência de arquitetura e não equivale a compromisso comercial nem teste de produção.

## Ambientes obrigatórios: LOCAL, DEV, HOM e PROD

A arquitetura será organizada em **quatro ambientes**, com separação explícita de configurações, dados, credenciais e deploys:

| Ambiente | Hospedagem proposta | Economia | Disponibilidade proposta |
| --- | --- | --- | --- |
| **LOCAL** | Docker Compose no computador do desenvolvedor | Sem gastos de cloud modelados | Não há HA; para desenvolvimento |
| **DEV** | AKS Free ou EKS, **1 nó 4vCPU/16 GiB**, banco pequeno gerenciado externo | Uma réplica, baixo volume e retenção mínima; possibilidade de desligar não produção fora de uso | Sem HA; ambiente descartável/reconstruível |
| **HOM** | AKS Free ou EKS, **1 nó 4vCPU/16 GiB**, banco pequeno gerenciado externo | Uma réplica; ampliar temporariamente para testar carga semelhante à PROD | Sem HA contínua; staging funcional e de aceite |
| **PROD** | AKS Standard ou EKS, **3 nós 4vCPU/16 GiB** como baseline, banco gerenciado externo | Nodes compartilhados por pods, HPA para APIs com mínimo de 2 réplicas | **HA planejada, ainda não comprovada**, requer banco HA/Multi-AZ, quorum RabbitMQ, topology spread, PDB e capacidade N+1 |

A [planilha textual do estudo AKS x EKS](../aks-eks-cost-breakdown.md) apresenta o rateio dos componentes e detalha os **custos por ambiente**. A escolha do **AKS Free em DEV/HOM** é uma premissa econômica sem garantia de SLA de control plane; EKS cobra control plane para cada cluster mesmo sem nós.

### Custo cloud por ambiente: subtotal preliminar de itens precificados

| Ambiente | AKS Azure East US (USD/mês) | EKS AWS us-east-1 (USD/mês) |
| --- | ---: | ---: |
| LOCAL (Docker Compose) | 0,00 de cloud | 0,00 de cloud |
| DEV | **161,78** | **247,22** |
| HOM | **161,78** | **247,22** |
| PROD | **615,93** | **622,97** |
| **Total três ambientes cloud** | **939,49** | **1.117,41** |

**Não são faturas totais, nem prova de custo menor do AKS.** Os números incluem nós/cluster, instância PostgreSQL, apenas armazenamento PostgreSQL Azure, uma reserva ilustrativa de PVs e object storage. Excluem, entre outros, **LB/NAT/ingress em ambos, RDS storage, registry, backups, IPs, egress, secrets e produção HA**. A estimativa de DEV/HOM com um nó precisa ser testada: Keycloak, RabbitMQ e stack Grafana podem exigir mais recursos, especialmente em HOM durante testes de carga.

**Decisão:** manter DEV e HOM em **clusters e bancos separados**. O porte econômico de HOM fica inalterado; ensaios de escala/HA que exijam mais recursos serão planejados e cotados separadamente, sem premissa de aumento no orçamento aprovado.

**Estratégia HPA:** usar HPA apenas quando há métricas, requests de CPU e capacidade de nós; com HPA desativado em DEV/HOM como baseline econômico, sem SLA. Em PROD, HPA para Transactions e Consolidation com `minReplicas: 2` é ponto de partida, junto a topology spread e probes, **não garantia de HA sem nós e banco capazes de sobreviver a falhas**.

## Disponibilidade, consistência e recuperação

**RNF-01:** falha de Consolidation deve ser isolada; Transactions confirma gravação de lançamento + Outbox no banco próprio e continua operando enquanto esse banco e seu caminho de entrada estiverem disponíveis. Falha de banco de Transactions, rede/NGINX ou host compartilhado não é coberta pela garantia.

**RabbitMQ:** Outbox protege eventos ainda não entregues, mas não substitui durabilidade e política de broker. Cluster com quorum queues e múltiplos nós/zonas, quando necessário, acrescenta custo e operação. Um nó único não garante HA.

**PostgreSQL:** utilizar serviço gerenciado em produção quando justificado, com backups, PITR, TLS e controle de rede; separar bancos lógicos por domínio. Servidor compartilhado no cenário econômico gera modo comum de falha, devendo ser revisado para produção HA.

**Keycloak:** planejar segredos, TLS, banco, persistência, replicação, recuperação e continuidade limitada de JWT localmente validado conforme ADR-006.

**Observabilidade:** falha de Collector, Loki, Tempo, Prometheus ou Grafana não deve interromper transações financeiras. Sinais possuem retenção/custos; auditoria financeira persistida permanece independente.

**NGINX:** é reverse proxy do MVP, não API Gateway dedicado; testar erros e disponibilidade no caminho Web. BFF e gateway de API continuam evoluções futuras, ADR-007.

## Testes de validação

1. Com Consolidation parado, lançar créditos/débitos via NGINX e comprovar commits no PostgreSQL de Transactions.
2. Recuperar Consolidation e verificar backlog e idempotência, sem duplicidade de saldo.
3. Desligar RabbitMQ, validar persistência Outbox, restabelecer e verificar publish confirms e consumo.
4. Executar 50 RPS no caminho NGINX → Consolidation, registrar sucesso, erros, timeout e p95; **5% de perda** continua dependente da interpretação oficial.
5. Interromper observabilidade e confirmar que Transactions permanece funcional.
6. Reiniciar containers, comprovar persistência em volumes e validar mecanismos de restore.
7. Ao avaliar produção, executar testes com réplicas, banco, broker e AZs efetivamente dimensionados, sem declarar HA antes disso.

## Trade-offs e riscos

| Risco | Consequência | Mitigação |
| --- | --- | --- |
| Menor subtotal ser confundido com menor TCO | Seleção errada da plataforma | Cotejar os itens omitidos e capacidade comparável; custos completos por cenário |
| Mais serviços que os requeridos pelo desafio | MVP atrasado | Foco primeiro nos dois microsserviços e prova de RNFs; infraestrutura com escopo incremental |
| Broker e observabilidade em VM única | Falha correlacionada e risco de perda/indisponibilidade | Produção requer separação, HA, backup e storage adequados |
| AKS subutilizado | Custo fixo sem ganho proporcional | Dimensionar nós e comparar alternativas gerenciadas |
| Escala a zero para serviço crítico | Cold start/latência, possíveis erros | Réplicas mínimas onde o SLO exigir |
| Custos invisíveis de NAT/egress, ALB, logs | Orçamento subestimado | Calculadoras oficiais, teste de tráfego e retenção |
| Subestimar esforço operacional | Incidentes e atrasos de patch | Definir equipe responsável e rotinas de runbook |
| Persistência compartilhada | Falha simultânea de dados de múltiplos domínios | Isolamento lógico no MVP; avaliar instâncias/HA separadas em produção |

## Pendências de implementação e revisão da estimativa

- Confirmar a modalidade **zone-redundant** do PostgreSQL PROD no provisionamento, além de backup/restore e RPO/RTO.
- Validar recursos necessários para a stack completa em DEV/HOM e N+1 para PROD, incluindo RabbitMQ quorum, HPA, Cluster Autoscaler, PDB e zonas.
- Confirmar IPs de entrada/saída e saída via Standard Load Balancer, limites SNAT e custos agregados de egress e tráfego entre zonas.
- Rever snapshots de 100 GB, retenção, recuperação consistente e crescimento de logs/traces no Blob.
- Validar o modelo de segurança do ACR Basic compartilhado, acessos de produção e segredos isolados.
- Validar 50 RPS e a interpretação do limite de 5% de perda no ensaio de carga; HOM econômico não substitui esse teste.
- Revisar custos após métricas reais, autoscaling, eventuais contratos e operação. A ADR-009 está **aceita** com essas restrições registradas.

## Referências

- [Comparativo Kubernetes AKS versus EKS e custo por serviço](../aks-eks-cost-breakdown.md)
- [Comparativo FinOps original: ACA, AKS e ECS/Fargate](../cloud-cost-comparison.md)
- [Preços Amazon EKS](https://aws.amazon.com/eks/pricing/)
- [Preços Azure Container Apps](https://azure.microsoft.com/en-us/pricing/details/container-apps/)
- [Preços AKS](https://azure.microsoft.com/en-us/pricing/details/kubernetes-service/)
- [Preços ECS/Fargate](https://aws.amazon.com/fargate/pricing/)
- [Preços PostgreSQL Azure](https://azure.microsoft.com/en-us/pricing/details/postgresql/flexible-server/)
- [Preços Amazon RDS](https://aws.amazon.com/rds/postgresql/pricing/)
- [Building Block View](../building-block-view.md)
