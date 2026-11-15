# Estimativa por componente: Azure AKS versus Amazon EKS

> **Data:** 2026-11-15. **Status:** hipótese de referência; não é cotação. **ADR:** [ADR-009](adr/ADR-009-deployment-cloud.md)  
> **Moeda:** USD/mês, 730 horas, sem impostos, reservas, descontos ou HA garantida.  
> **Regiões:** Azure East US e AWS us-east-1.  
> **Objetivo:** detalhar custo por workload *alocado* em Kubernetes e custo *cobrado* por infraestrutura.

## Arquitetura comparável

**Azure:** Azure Kubernetes Service (AKS Standard), três nós Linux Standard_D4as_v5 de 4 vCPU e 16 GiB cada, Azure Database for PostgreSQL Flexible Server externo ao cluster.

**AWS:** Amazon Elastic Kubernetes Service (EKS, suporte padrão), três nós EC2 m6i.xlarge de 4 vCPU e 16 GiB cada, Amazon RDS for PostgreSQL externo ao cluster.

**Em ambos:** NGINX/React, Transactions com Outbox worker, Consolidation com consumer, Keycloak, RabbitMQ, OpenTelemetry Collector, Prometheus, Loki, Tempo e Grafana rodam nos nós Kubernetes como workloads/pods. O banco PostgreSQL fica gerenciado fora. Os bancos lógicos Transactions, Consolidation e Keycloak compartilham uma instância gerenciada **somente na estimativa econômica**; em produção pode ser desejável isolamento físico, redundância ou tamanho diferente. O EKS avaliado usa **managed node groups EC2**, não Fargate nem EKS Auto Mode.

**Importante:** executar RabbitMQ, Loki, Tempo e Prometheus dentro do Kubernetes **não torna armazenamento, backups ou HA gratuitos**. É necessário provisionar volumes persistentes, possivelmente object storage, e definir retenção, resiliência e replicação. Kubernetes não cria capacidade extra: o tamanho do cluster deve suportar requests/limits, DaemonSets, kube-system e margem de falha.

## Preços e premissas reproduzíveis

| Item | AKS Azure | EKS AWS | Fonte |
| --- | ---: | ---: | --- |
| VM / EC2 por nó (4 vCPU, 16 GiB) | US$ 0,172/h | US$ 0,192/h | [1], valores regionais públicos de referência |
| Quantidade de nós | 3 | 3 | Premissa |
| Computação dos 3 nós, 730h | **US$ 376,68** | **US$ 420,48** | 3 × valor/h × 730 |
| Plano de controle Kubernetes | **US$ 73,00** | **US$ 73,00** | AKS Standard e EKS standard version: US$ 0,10/h, fontes oficiais [2], [3] |
| Banco PostgreSQL gerenciado, compute | **US$ 99,28** | **US$ 94,17** | Azure Flexible Server B2ms e AWS RDS db.t4g.large Single-AZ, fontes [4], [5] |
| Banco PostgreSQL, storage | **US$ 3,68**, 32 GiB | **a cotar** | Azure 32 × US$ 0,115/GiB; EBS/RDS AWS volume ainda não especificado |
| **Subtotal de itens precificados** | **US$ 552,64** | **US$ 587,65** | **Não são TCO, nem totalmente equivalentes** |

**Nota de comparabilidade:** Azure B2ms e AWS db.t4g.large têm 2 vCPU e 8 GiB, mas arquitetura, políticas de créditos, preços de storage, capacidade e desempenho podem variar. Bancos com classe burstable não asseguram comportamento desejado em carga financeira sustentada. Os três nós compartilham 12 vCPU e 48 GiB **antes de overhead**, e não há garantia de que todos os componentes com as reservas abaixo caibam em produção.

## Custo por serviço: rateio ilustrativo da capacidade dos nós

**Não há uma fatura de AKS/EKS por pod nesse modelo.** As VMs são cobradas por nó mesmo quando o pod não consome toda a capacidade. A tabela atribui parte do custo dos nós aos componentes **proporcionalmente a uma quota ilustrativa de vCPU**; é uma regra contábil simples para orientar o dimensionamento, **não um preço da cloud por serviço**. A memória precisa ser validada separadamente.

Fórmula: `custo_alocado = (vCPU_ilustrativa / 12) × custo_mensal_dos_3_nós`.

| Workload no Kubernetes | vCPU ilustrativa | Rateio AKS US$/mês | Rateio EKS US$/mês |
| --- | ---: | ---: | ---: |
| Transactions API + Outbox worker | 1,0 | 31,39 | 35,04 |
| Consolidation API + consumer | 1,0 | 31,39 | 35,04 |
| NGINX + React compilado | 0,5 | 15,70 | 17,52 |
| Keycloak | 1,0 | 31,39 | 35,04 |
| RabbitMQ | 1,0 | 31,39 | 35,04 |
| OpenTelemetry Collector | 0,5 | 15,70 | 17,52 |
| Prometheus | 1,0 | 31,39 | 35,04 |
| Loki | 0,5 | 15,70 | 17,52 |
| Tempo | 0,5 | 15,70 | 17,52 |
| Grafana | 0,5 | 15,70 | 17,52 |
| **Capacidade de nós atribuída a workloads** | **7,5** | **235,43** | **262,80** |
| **Capacidade residual (sistema, overhead, reserva e ociosidade)** | **4,5** | **141,25** | **157,68** |
| **Total dos nós (sem dupla contagem)** | **12,0** | **376,68** | **420,48** |

Os centavos do rateio são arredondados e ajustados na linha residual para reconciliar exatamente o total das VMs. A quota ilustrativa é **uma hipótese de alocação de custos**, não os pedidos de CPU/RAM do Deployment Kubernetes. Reserva residual não equivale necessariamente a capacidade utilizável; memória, scheduler, pods do sistema, storage IO e estratégia N+1 também limitam o cluster. O dimensionamento real poderá exigir *mais* de três nós.

**Despesas adicionais separadas da capacidade dos nós:** o plano de controle AKS/EKS, PostgreSQL gerenciado, discos PV e volumes de RabbitMQ/Prometheus, armazenamento de Loki/Tempo, load balancers/ingress, NAT, IPs, tráfego, registry, Key Vault/Secrets Manager, certificados, DNS, backups e suporte não podem ser rateados como se já estivessem incluídos nos valores por pod.

## Tabela de aquisição: custo real por linha faturável

| Linha faturável | AKS (USD/mês) | EKS (USD/mês) | Observação |
| --- | ---: | ---: | --- |
| Worker nodes (3 × 4vCPU/16GiB) | 376,68 | 420,48 | Compute pago mesmo com serviços ociosos |
| Control plane / gerenciamento do cluster | 73,00 | 73,00 | Standard/standard support |
| PostgreSQL compute | 99,28 | 94,17 | Uma instância pequena, Single-AZ, não HA |
| PostgreSQL storage | 3,68 | **não cotado** | AWS requer tamanho/tipo de volume e IOPS |
| PVs/discos de RabbitMQ e observabilidade | **não cotado** | **não cotado** | Especificar GiB, IOPS, snapshots |
| Object storage Loki/Tempo, se adotado | **não cotado** | **não cotado** | Azure Blob Storage / Amazon S3 |
| Ingress e Load Balancer | **não cotado** | **não cotado** | Azure Load Balancer / AWS ALB ou NLB |
| VNet/VPC, NAT, egress e IPs | **não cotado** | **não cotado** | Topologia de rede e zonas |
| Registry, segredos, DNS e suporte | **não cotado** | **não cotado** | ACR/Key Vault vs ECR/Secrets Manager |
| **Subtotal parcial** | **552,64** | **587,65** | Não inclui os itens sem cotação |

## Comparação de operação e resiliência

| Tema | AKS | EKS |
| --- | --- | --- |
| Kubernetes gerenciado | AKS Standard, control plane gerenciado | EKS, control plane gerenciado |
| Worker nodes | Azure VMs em node pools | EC2 em managed node groups |
| Banco fora do cluster | Azure Database for PostgreSQL Flexible Server | Amazon RDS for PostgreSQL |
| Storage persistente | Azure Disks/Files e/ou Blob via integrações adequadas | EBS/EFS e/ou S3 via integrações adequadas |
| RabbitMQ | Deployment com volumes e clustering planejados | Mesmo requisito |
| Observabilidade | Mesma stack OTel/Prometheus/Loki/Tempo/Grafana | Mesma stack, templates Kubernetes semelhantes |
| Operação | Atualizações de nós/cluster, volumes, monitoramento e IAM | Mesmo tipo de trabalho com integrações AWS |
| HA | Exige réplicas, zonas, storage e testes | Idem; não é consequência automática do EKS |

## Calculadora: quando é necessária?

**Para arquitetura e comparação inicial**, basta uma tabela transparente com fórmulas e fontes, como esta. **Para orçamento a ser apresentado como previsão de fatura**, usar as calculadoras oficiais: [Azure Pricing Calculator](https://azure.microsoft.com/en-us/pricing/calculator/) e [AWS Pricing Calculator](https://calculator.aws/). Elas ajudam a completar LB, NAT, armazenamento, backup, transferências, monitoramento e descontos contratados, e precisam ser complementadas com premissas de consumo e retenção.

## Refinamento de storage, entrada e rede (cenário econômico)

> **Revisado em 2026-11-15.** Esta tabela amplia o cenário anterior com volumes, object storage e alguns itens de rede. **Não corresponde à fatura completa nem à mesma topologia de HA**. As tarifas de terceiros regionais foram pesquisadas em 15/11/2026, e as premissas seguem abertas para a calculadora oficial.

### Premissas de dimensionamento para cotação

| Categoria | Hipótese de projeto | Trade-off |
| --- | --- | --- |
| RabbitMQ | 1 PV de 128 GiB | Não é quorum cluster/HA; volume de eventos e taxa de publicação podem exigir mais IOPS |
| Prometheus | 1 PV de 128 GiB | Retenção temporal, séries ativas e cardinalidade determinam espaço real |
| Grafana / componentes auxiliares | 1 PV de 128 GiB **compartilhado apenas para estimativa de preço** | Em Kubernetes, não presumir que vários pods compartilham um único disco RWO; separar PVCs conforme o desenho definitivo |
| Loki + Tempo | **200 GB de armazenamento de objetos no total** | Volume médio armazenado; quantidade de requests, lifecycle e taxa de ingestão não incluídos |
| RDS PostgreSQL | 32 GiB como alvo de comparação | Preço gp3 RDS **não inferido** de EBS; validar na calculadora RDS |
| LB público | 1 ingresso para o NGINX | EKS usa exemplo de ALB 1 LCU médio; AKS necessita preço regional de LB, IP e dados |
| NAT | Para AWS, 1 NAT Gateway, 50 GB processados/mês | Apenas cenário econômico de zona única; para HA em múltiplas AZs ou múltiplos gateways, custo aumenta |
| Tráfego de internet e entre zonas | Não quantificado | Pode superar armazenamento em ambientes com tráfego elevado |
| Snapshots e backup | Não cotados | Precisam de política de retenção e RPO/RTO |

**Atenção:** os três volumes de 128 GiB são uma **estimativa de capacidade provisionada**, não definição de três PVCs compartilhados, e os valores não garantem que RabbitMQ, Prometheus, Loki e Tempo estejam operacionais ou dimensionados. Azure Premium SSD P10 possui 128 GiB, enquanto EBS gp3 cobra por GB provisionado.

### Tarifas adicionais com fontes

| Meter | Azure East US | AWS us-east-1 | Evidência |
| --- | --- | --- | --- |
| Persistent block storage | P10 de 128 GiB, **US$ 0,027/h**, ~US$ 19,71/mês | EBS gp3 **US$ 0,08/GB-mês**, 128 GiB ~US$ 10,24/mês | Azure [S1], AWS [S2] |
| Object storage Hot/Standard | Azure Blob Hot LRS **US$ 0,0208/GB-mês** | Amazon S3 Standard **US$ 0,023/GB-mês** | [S3], [S4] |
| ALB da AWS | não se aplica | **US$ 0,0225/h** + **US$ 0,008/LCU-h** | AWS [S5] |
| NAT Gateway AWS | ainda não cotado na Azure | **US$ 0,045/h** + **US$ 0,045/GB processado** (cenário regional de referência) | AWS [S6] |
| Azure LB e NAT | **pendente de valor regional verificável** | não se aplica | Consultar calculadora [S7] |
| ACR Basic | **aprox. US$ 5/mês**, estimativa | Amazon ECR: cobrança por storage/transferência, sem cotação aqui | Guia de meters da Microsoft [S8] |

Tarifas podem variar com acordo, horário, redundância, operações e região. O preço de **EBS gp3 NÃO serve como preço do volume de RDS**.

### Custo mensal de componentes adicionais

| Item | AKS (USD/mês) | EKS (USD/mês) | Fórmula |
| --- | ---: | ---: | --- |
| **Subtotal anterior** (nós + control plane + PostgreSQL compute + storage Azure) | **552,64** | **587,65** | Tabela anterior |
| 3 volumes persistentes de 128 GiB | **59,13** | **30,72** | Azure 3 × 0,027 × 730; AWS 3 × 128 × 0,08 |
| 200 GB object storage | **4,16** | **4,60** | Azure 200 × 0,0208; AWS 200 × 0,023 |
| Registry | **~5,00** | **pendente** | ACR Basic, sem excedentes |
| ALB, assumindo 1 LCU médio | **pendente** | **22,27** | 730 × (0,0225 + 0,008) |
| NAT, 1 gateway + 50 GB | **pendente** | **35,10** | 730 × 0,045 + 50 × 0,045 |
| PostgreSQL RDS: 32 GiB storage | já incluído no subtotal Azure | **pendente** | Custo RDS não cotado |
| **SUBTOTAL AMPLIADO DOS ITENS PRECIFICADOS** | **~US$ 620,93** | **US$ 680,34** | Não inclui todos os mesmos itens |
| **Itens abertos** | LB, NAT, IP, egress, backups, snapshots e outros | RDS storage, ECR, IP, egress, backups, snapshots e outros | **Não tratar como zero** |

**Não use o subtotal ampliado para decretar vencedor:** AWS inclui ALB e NAT que ainda não foram precificados na Azure, enquanto Azure inclui ACR e storage PostgreSQL não cotados na AWS. Os dois ambientes também apresentam potencialmente diferentes arranjos de AZ, e os custos de discos podem ser revistos após sizing real.

### Variações úteis, sem inventar tarifas

| Cenário | Modificação de premissa | Impacto previsível |
| --- | --- | --- |
| **Econômico / laboratório cloud** | 1 nó RabbitMQ, menos retenção, 1 ingresso, 1 zona para dados | Menor custo, mas sem HA de broker/banco e falhas correlacionadas |
| **Base para testes de resiliência** | 3 nós Kubernetes, 2+ réplicas de APIs, medir requests/limits reais | Recursos disponíveis para pods diminuem; calcular se nós suportam carga e falha de um nó |
| **Produção com HA** | Múltiplas AZs, RabbitMQ quorum com pelo menos 3 réplicas, banco HA/Multi-AZ, retenção e backups | Exige novos PVCs/discos, mais throughput, control plane/ingress redundantes e possivelmente nós adicionais; **não há cotação de HA ainda** |
| **Uso alto de telemetria** | Mais GB/dia de logs e traces, maior retenção | Aumenta armazenamento, operações, rede e recursos do Collector/Loki/Tempo |

**Kubernetes não reduz a necessidade de infraestrutura:** manter serviços com estado em pods desloca sua operação para a equipe, em vez de eliminá-la. AKS/EKS são escolhas comparáveis para Kubernetes gerenciado, mas não asseguram HA nem bancos independentes.

### Fontes do refinamento

- **[S1]** Discos Premium SSD em East US, P10 (referência da Azure Retail Prices API, atualizada 05/11/2026): https://instances.usage.ai/azure/disks/category/premium-ssd ; documentação oficial https://azure.microsoft.com/en-us/pricing/details/managed-disks/
- **[S2]** EBS gp3 (US$ 0,08/GB-mês; IOPS/throughput além do baseline cobrado separadamente): https://aws.amazon.com/ebs/general-purpose/
- **[S3]** Azure Blob Hot LRS East US: https://instances.usage.ai/azure/blob-storage?region=eastus
- **[S4]** S3 Standard us-east-1: https://aws.amazon.com/s3/pricing/ ; referência de preços: https://github.com/janwilmake/state-of-cloud-pricing/blob/main/comparisons/storage.md
- **[S5]** ALB por hora e LCUs: https://aws.amazon.com/elasticloadbalancing/pricing/
- **[S6]** NAT Gateway: https://aws.amazon.com/vpc/pricing/
- **[S7]** Load Balancer Azure: https://azure.microsoft.com/en-us/pricing/details/load-balancer/ ; NAT Azure: https://azure.microsoft.com/en-us/pricing/details/virtual-network/
- **[S8]** Guia da Microsoft sobre meters de ACR Basic: https://github.com/microsoft/azure-skills/blob/main/skills/azure-app-onboard/prepare/references/pricing-guide-services.md

### Próxima validação para fechar o orçamento

Na **Azure Pricing Calculator** e **AWS Pricing Calculator**, informar nós, control plane, PostgreSQL gerenciado (32 GiB), volumes (384 GiB), object storage (200 GB), 1 ingress/LB, 1 NAT, registry, 50 GB de tráfego processado no NAT, serviços de secrets, e taxa de saída de internet. Salvar as premissas usadas, não apenas o total.

Para a variante HA, rodar outro cenário com múltiplas AZs e dimensionamento N+1, adicionando todas as cópias do RabbitMQ, PostgreSQL HA e storage/banda. Só depois declarar uma previsão de custo total de produção.

## Quatro ambientes: LOCAL, DEV, HOM e PROD (nova simulação)

> **Revisão 2026-11-15.** A solicitação é representar os **quatro ambientes separadamente**, com capacidade reduzida fora de produção. É um **estudo econômico de cluster isolado por ambiente**: DEV, HOM e PROD têm, cada um, seu próprio AKS/EKS e PostgreSQL; LOCAL não tem cloud e usa Docker Compose. Todos os custos de cloud abaixo são assumidos 24x7 durante 730 h/mês. As reservas não são medições, e a configuração completa deve ser testada antes de fechar as máquinas.

### Plano de capacidade e isolamento

| Ambiente | Plataforma | Nós e réplicas | PostgreSQL fora do cluster | Persistência e observabilidade | Objetivo |
| --- | --- | --- | --- | --- | --- |
| **LOCAL** | Docker Compose no computador do desenvolvedor | Sem nós cloud; uma instância por componente | PostgreSQL em container local com volume | RabbitMQ e observabilidade em volumes locais | Desenvolvimento, debugging e testes de integração |
| **DEV** | AKS Free / EKS standard | **1 nó de 4 vCPU/16 GiB**, 1 réplica dos serviços; HPA **desligado** inicialmente | Azure B1ms ou RDS db.t4g.small, Single-AZ | **1 disco de 128 GiB (referência financeira)** + 20 GB de object storage; métricas/logs/traces com retenção mínima | Integração contínua, validação funcional, baixo custo |
| **HOM** | AKS Free / EKS standard | **1 nó de 4 vCPU/16 GiB**, 1 réplica normalmente | Azure B1ms ou RDS db.t4g.small, Single-AZ | 1 disco 128 GiB + 20 GB object storage; backups mínimos a dimensionar | Testes de aceite; reproduzir contratos de PROD sem exigir HA |
| **PROD** | AKS Standard / EKS standard | **3 nós de 4 vCPU/16 GiB**, réplicas mín. 2 para APIs e NGINX, HPA; capacidade real N+1 a validar | Azure B2ms ou RDS db.t4g.large para orçamento inicial **sem HA de banco** | 3 discos 128 GiB e 200 GB object storage, observabilidade completa | Operação contínua e testes de pico |

**Alerta de sizing:** DEV/HOM podem não comportar toda a stack em **um único nó**. Precisaremos limitar requests/limits, reduzir retenção, sampling e réplicas e medir memória/CPU de RabbitMQ, Keycloak, Prometheus, Loki e Tempo. Se necessário, adicionar nó ou centralizar parte da observabilidade de não produção, com segregação de dados e custos explicitados. Não declarar que os serviços já executam nessa configuração.

**PROD não é HA comprovada:** três nós podem comportar réplicas mínimas, mas a falha de um nó/zona reduz capacidade. O RabbitMQ com quorum precisa de três membros distribuídos, PVs e estratégia de restauração; bancos gerenciados em Single-AZ não são HA, e o custo adicional de PostgreSQL HA/AWS Multi-AZ **não está incluído**. HPA escala pods, não replica dados ou substitui Cluster Autoscaler, PDB e topology spread.

### Custo base por ambiente, antes da rede e HA adicional

Tarifas de referência mantidas do comparativo: VM Azure D4as v5 **US$ 0,172/h**; EC2 m6i.xlarge **US$ 0,192/h**; AKS Free não cobra gerenciamento (sem SLA) em DEV/HOM; AKS Standard e EKS versão em suporte padrão **US$ 0,10/h** por cluster; banco Azure B1ms **US$ 12,41/mês**, B2ms **US$ 99,28/mês**; RDS db.t4g.small **US$ 0,032/h**, large **US$ 0,129/h**, Single-AZ. P10 128 GiB Azure **US$ 19,71/mês**, EBS gp3 128 GiB AWS **US$ 10,24/mês**; object storage Azure **US$ 0,0208/GB-mês**, AWS **US$ 0,023/GB-mês**. Calcular a mesma base para ambos, sem incluir ALB/NAT de apenas um lado.

| Linha calculada (USD/mês) | LOCAL | DEV AKS | HOM AKS | PROD AKS | DEV EKS | HOM EKS | PROD EKS |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Nodes compute | 0,00* | 125,56 | 125,56 | 376,68 | 140,16 | 140,16 | 420,48 |
| Plano de controle | 0,00 | 0,00 (Free) | 0,00 (Free) | 73,00 | 73,00 | 73,00 | 73,00 |
| PostgreSQL compute | 0,00* | 12,41 | 12,41 | 99,28 | 23,36 | 23,36 | 94,17 |
| PostgreSQL storage Azure 32 GiB | 0,00* | 3,68 | 3,68 | 3,68 | **não cotado** | **não cotado** | **não cotado** |
| Discos K8s: 128 GiB DEV/HOM; 3×128 GiB PROD | 0,00* | 19,71 | 19,71 | 59,13 | 10,24 | 10,24 | 30,72 |
| Object storage 20 GB DEV/HOM; 200 GB PROD | 0,00* | 0,42 | 0,42 | 4,16 | 0,46 | 0,46 | 4,60 |
| **Subtotal comparável de itens precificados** | **US$ 0,00 de cloud*** | **161,78** | **161,78** | **615,93** | **247,22** | **247,22** | **622,97** |
| **Total três ambientes cloud** |  |  | **AKS: US$ 939,49/mês** |  |  | **EKS: US$ 1.117,41/mês** |  |

*LOCAL não possui fatura de cloud neste modelo; computador, energia, disco, backups e esforço técnico não custam zero. Os itens **não cotados** não têm valor zero: storage RDS e rede EKS permanecem fora, assim como LB/NAT/IP/egress dos dois provedores.

**Arredondamento:** object storage Azure 20 GB = US$ 0,416, exibido como US$ 0,42 por ambiente. A soma é feita com valores antes do arredondamento. **Todos os subtotais são parciais e não equivalem à despesa mensal final.**

### Custo por ambiente com itens adicionais selecionados

Para manter rastreabilidade com o refinamento anterior, existem parcelas *já ilustradas* que podem ser adicionadas, mas não devem ser confundidas com comparação simétrica:

| Parcela adicional | AKS | EKS | Observação |
| --- | ---: | ---: | --- |
| ACR Basic | ~US$ 5/mês **para o conjunto**, caso um registry compartilhado atenda DEV/HOM/PROD | ECR pendente | Não cobrar três vezes o mesmo registry compartilhado |
| AWS ALB: 1 ALB com 1 LCU por ambiente | Azure LB pendente | ~US$ 22,27 **por ambiente** | Três ambientes separados implicariam ~US$ 66,81, se todos tiverem ALB público |
| AWS NAT Gateway: 1 por ambiente + 50 GB | Azure NAT pendente | ~US$ 35,10 **por ambiente** | Três NATs implicariam ~US$ 105,30; DEV/HOM podem adotar acesso controlado e outra topologia |
| Storage do PostgreSQL RDS | Incluído nos números Azure (32 GiB por ambiente) | Pendente | Cotar em RDS, não usar a taxa do EBS gp3 |
| Backups, secrets, DNS, tráfego e HA de banco | Pendente | Pendente | Exigências podem mudar significativamente os totais |

**Não somar esses adicionais de um provedor e comparar contra zero no outro.** A definição das redes, endpoints e retenções vem antes da cotação fechada.

### Estratégia de redução de custo por ambiente

1. **LOCAL:** Compose no computador, sem obrigação de disponibilidade contínua e sem cloud.
2. **DEV:** cluster pequeno, AKS Free sem SLA ou EKS de um nó com control plane cobrado; 1 réplica; reduzir sampling e retenção, parar workloads e eventualmente a capacidade dos nós fora do expediente **somente quando o pipeline permitir**. Persistência e cobranças do plano de controle EKS continuam mesmo com nós parados. Verificar quais recursos gerenciados podem ser suspensos e cobranças que permanecem.
3. **HOM:** manter também pequeno, mas reservar janelas de teste de carga e validação. Para executar 50 RPS na arquitetura equivalente a PROD, escalar HOM temporariamente para sizing semelhante ao de PROD e documentar o custo das horas extras.
4. **PROD:** 3 nós como ponto de partida, 2+ réplicas para APIs e NGINX, HPA, Cluster Autoscaler, topology spread, PDB, PostgreSQL HA e RabbitMQ quorum **a dimensionar**. Uma réplica extra só custa computação adicional se exigir nós extras, mas consome recursos reservados.
5. **Estratégia ainda mais econômica para DEV/HOM:** compartilhar um cluster não produtivo com namespaces, RBAC e quotas separados. Isso pode reduzir custos de plano de controle EKS e facilitar observabilidade compartilhada, mas adiciona **risco de interferência entre ambientes** e requer governança. **Não está calculado na tabela principal**, que modela clusters separados.
6. A imagem React é compilada uma vez por versão; as diferenças de configuração por ambiente não exigem replicar toda a estrutura de build. Registry compartilhado não significa dados e segredos compartilhados.

### Custos que precisam entrar na calculadora para aprovação

- NAT/LB/Ingress públicos ou privados por ambiente, IPs, egress, DNS, transferência entre AZ e operação de endpoints privados.
- RDS PostgreSQL storage, IOPS, backups, parâmetros de instância e eventual Multi-AZ; Azure Flexible Server HA, PITR e armazenamento.
- Discos realmente necessários aos vários PVCs, snapshots, object storage e request charges de Loki/Tempo.
- Key Vault/Secrets Manager, registry, certificado, suporte, atualizações de Kubernetes e logs de plataforma.
- Compatibilidade real de **B1ms ou t4g.small** com as três bases lógicas, sobretudo Keycloak, e capacidade do nó único em DEV/HOM.
- PROD multi-AZ e **custo de sobreviver à falha de um nó** mantendo as réplicas críticas.

### Fontes adicionais para os quatro ambientes

- [AKS Free vs Standard e SLA por ambiente](https://learn.microsoft.com/en-us/azure/aks/free-standard-pricing-tiers) (DEV/HOM Free não têm SLA)
- [Amazon EKS por cluster, suporte padrão/estendido](https://aws.amazon.com/eks/pricing/) (EKS standard US$ 0,10/h mesmo em DEV/HOM)
- [Azure Database for PostgreSQL Flexible Server](https://azure.microsoft.com/en-us/pricing/details/postgresql/flexible-server/)
- [RDS PostgreSQL preços regionais](https://cloudprice.net/aws/rds?filter=%5Edb%5C.t4g%5C.&region=us-east-1)
- [Azure Pricing Calculator](https://azure.microsoft.com/en-us/pricing/calculator/) e [AWS Pricing Calculator](https://calculator.aws/)

## Recomendação para a ADR-009

**Shortlist principal: AKS versus EKS**, com preferência técnica provisória por **AKS** para manter a arquitetura Kubernetes e aproveitar custos de compute ligeiramente menores na região de referência. Essa preferência **não é uma escolha final de cloud**: requer estimativa de rede/armazenamento/HA, execução dos testes de 50 RPS e avaliação de governança, equipe e contratos. Container Apps e ECS/Fargate permanecem documentados como alternativas, mas não são equivalentes diretos de *Kubernetes completo com pods de todos os componentes*.

**Ponto crítico:** se RabbitMQ, Loki, Tempo ou Prometheus ficarem no cluster, planejar explicitamente volumes persistentes, recuperação e HA. PostgreSQL gerenciado externo ao cluster reduz o custo operacional do banco, mas não elimina o risco de indisponibilidade.

## Fontes

1. Node pricing reference: https://instances.usage.ai/aws/ec2/m6i.xlarge/equivalents (Azure D4as v5 / AWS m6i.xlarge).
2. AKS Standard: https://learn.microsoft.com/en-us/azure/architecture/aws-professional/eks-to-aks/cost-management and https://azure.microsoft.com/en-us/pricing/details/kubernetes-service/
3. EKS: https://aws.amazon.com/eks/pricing/
4. Azure PostgreSQL: https://azure.microsoft.com/en-us/pricing/details/postgresql/flexible-server/
5. AWS RDS PostgreSQL: https://aws.amazon.com/rds/postgresql/pricing/ and the regional reference used in the earlier [three-model comparison](cloud-cost-comparison.md).

> Preços consultados em 2026-11-15; confirmar valores atuais, créditos, desconto de contrato, tipo de instância e custos não incluídos antes de contratar.
