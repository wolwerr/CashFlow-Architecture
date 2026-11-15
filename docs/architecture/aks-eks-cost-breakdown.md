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
