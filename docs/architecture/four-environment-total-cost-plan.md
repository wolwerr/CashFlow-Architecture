# CashFlow | Orçamento Azure dos quatro ambientes

> **Status:** orçamento-base decidido na [ADR-009](adr/ADR-009-deployment-cloud.md) em 2026-11-15.  
> **Fonte dos valores:** telas da Azure Pricing Calculator configuradas para o CashFlow pelo responsável pelo projeto.  
> **Região:** East US. **Moeda:** USD. **Cobrança:** referência de 730 horas/mês e Pay as you go, sem descontos contratuais.  
> **Escopo:** LOCAL, DEV, HOM, PROD e serviços SHARED. Este documento substitui os estudos preliminares AKS/EKS e ACA/AKS/ECS.  
> **Visão de implantação:** [Deployment View, ambientes e portabilidade](deployment-view.md).

## Visão de implantação e arquitetura multi-cloud

A distribuição dos workloads, clusters, bancos, registry e serviços compartilhados está documentada na **[Deployment View](deployment-view.md)**, com [diagrama PlantUML editável](plantuml/deployment-view.puml). Azure AKS em East US permanece a plataforma **decidida**; a tabela de equivalência AWS/GCP mostra **alternativas conceituais**, não custos cotados ou migração já planejada. O detalhamento financeiro continua nesta página.

## Resumo executivo

| Grupo | Mensal (USD) | Descrição |
| --- | ---: | --- |
| LOCAL | 0,00 de cloud | Docker Compose no computador, sem cobrança Azure |
| DEV | **204,34** | AKS econômico, serviços e dados segregados |
| HOM | **204,34** | Mesma configuração de DEV, em recursos próprios |
| PROD | **890,44** | AKS Standard com SLA, três nós e PostgreSQL com HA habilitada |
| SHARED | **5,90** | ACR Basic e Azure DNS público |
| **Total mensal** | **1.305,02** | Soma dos quatro grupos Azure |
| **Total anual estimado** | **15.660,24** | 12 × subtotal mensal agregado, sujeito a arredondamento |

Os valores são a **estimativa de recursos selecionados na calculadora**, não um limite de faturamento nem uma garantia de disponibilidade ou desempenho.

## Detalhamento por produto, USD/mês

| Produto / configuração | DEV | HOM | PROD | SHARED |
| --- | ---: | ---: | ---: | ---: |
| AKS: computação, plano de controle e OS Disks | 145,27 | 145,27 | 508,81 | - |
| Managed Disks para dados (DEV/HOM: 3 × P4; PROD: 4 × P10) | 15,84 | 15,84 | 78,84 | - |
| PostgreSQL Flexible Server | 16,09 | 16,09 | 257,02 | - |
| Blob Storage para Loki/Tempo | 1,46 | 1,46 | 6,49 | - |
| Load Balancer Standard | 18,35 | 18,35 | 18,75 | - |
| Public IP Standard de entrada e de saída (2 × 3,65) | 7,30 | 7,30 | 7,30 | - |
| Key Vault | 0,03 | 0,03 | 0,03 | - |
| Bandwidth, Internet Egress configurado | 0,00 | 0,00 | 0,00 | - |
| Snapshots de volumes, **100 GB provisionados para estimativa PROD** | - | - | 13,20 | - |
| Azure Container Registry Basic | - | - | - | 5,00 |
| Azure DNS público, 1 zona + 1 milhão de consultas | - | - | - | 0,90 |
| **Subtotal** | **204,34** | **204,34** | **890,44** | **5,90** |

*Valores das telas arredondados por linha; o valor anual apresentado para cada produto pela calculadora pode divergir em centavos de 12 vezes o valor mensal exibido.*

### DEV e HOM, independentes e econômicos

- Cada ambiente possui **seu próprio cluster AKS**, 1 nó Linux **Standard_D4as_v5**, com 4 vCPU e 16 GiB RAM, Tier Standard e opção **No SLA** para gerenciamento do cluster (US$ 0,00), mais **um OS Disk P10, 128 GiB**.
- Cada ambiente tem **3 Managed Disks P4 de 32 GiB** (96 GiB em conjunto) para dados de workloads Kubernetes, sujeito a ajuste de PVC/IOPS após implantação.
- Cada um usa **Azure Database for PostgreSQL Flexible Server Burstable B1ms**, 1 vCore, **32 GiB Premium SSD**, backup redundante LRS, sem custo de backup excedente estimado. Três bancos lógicos: Transactions, Consolidation e Keycloak.
- **Blob Storage Hot LRS de 20 GB** por ambiente, com operações configuradas, para dados de Loki/Tempo.
- **Load Balancer Standard**, uma regra e 20 GB processados/mês; **dois IPv4 Standard estáticos** por ambiente; egress de 20 GB/mês com custo US$ 0,00 mostrado na calculadora; **Key Vault** com operações comuns estimadas.
- **HOM permanece com o porte de DEV**, sem ampliação automática ou temporária incluída no orçamento. É independente para testes funcionais, integração e implantação. **Não equivale a PROD em capacidade nem comprova HA e 50 RPS**, que deverão ser verificados em testes com recursos suficientes.

### PROD, ponto de partida de alta disponibilidade

- **AKS Standard com SLA**, **um cluster**, **3 nós Standard_D4as_v5** (12 vCPU e 48 GiB agregados) e **3 OS Disks Premium SSD P10, 128 GiB**, contabilizados no componente AKS.
- **4 Managed Disks Premium SSD P10, 128 GiB por disco** para dados: hipótese de 3 volumes de RabbitMQ em quorum e 1 de Prometheus. O disco de Grafana pode exigir PVC próprio, ou dashboards/configurações provisionados como código, de acordo com desenho técnico posterior.
- **Azure Database for PostgreSQL Flexible Server General Purpose D2s v3, 2 vCores, 32 GiB Premium SSD, 1 servidor na calculadora com High Availability habilitada**. Valor exibido **US$ 257,02/mês**, composto por compute primário US$ 124,83, storage US$ 3,68 e HA US$ 128,51. O toggle de HA precifica standby e não deve ser duplicado aumentando `Servers or Nodes`. **Confirmar zone-redundant na implantação**, pois a calculadora não demonstrou a modalidade.
- **Azure Blob Storage GPv2, Standard Hot ZRS, 200 GB**, com operações configuradas, para Loki e Tempo. Orçamento exibido: **US$ 6,49/mês**. Algumas categorias da interface não forneceram preço de transferência; reestimar com dados reais.
- **Load Balancer Standard**, 1 regra, 100 GB processados/mês: **US$ 18,75**; dois IPv4 Standard: **US$ 7,30**; Internet Egress de 100 GB exibiu **US$ 0,00**, não garantia de isenção agregada.
- **Key Vault US$ 0,03**; **100 GB de armazenamento de snapshots US$ 13,20**, sem inferir daí sete dias de retenção ou backup consistente.

### SHARED

- **Um Azure Container Registry Basic (US$ 5,00/mês)** para imagens versionadas dos três ambientes, com identidades e políticas separadas. **Private Endpoint não está incluído no Basic**, um risco de segurança/arquitetura a reavaliar se exigirmos redes privadas.
- **Uma zona pública no Azure DNS (US$ 0,90/mês)** e aproximadamente 1 milhão de consultas mensais; domínio e certificados eventualmente cobrados fora deste item.
- Não duplicar ACR ou DNS nas abas DEV, HOM e PROD.

## Premissas, riscos e itens não garantidos pelo orçamento

| Tema | Premissa adotada | Risco / trabalho posterior |
| --- | --- | --- |
| Entrada e saída AKS | 2 IPs públicos Standard por ambiente, saída pelo Standard Load Balancer, sem NAT Gateway dedicado | Confirmar recursos efetivamente provisionados, SNAT e necessidade futura de NAT |
| Internet Egress | DEV 20 GB, HOM 20 GB, PROD 100 GB; zero cobrado nas três linhas | Franquia gratuita pode ser compartilhada pelo contrato; dados reais podem cobrar egress agregado |
| PostgreSQL PROD | General Purpose com HA habilitada | Confirmar zone-redundant, backup, restore, RPO/RTO e compatibilidade de capacidade |
| RabbitMQ PROD | Três discos previstos para quorum | Quorum exige implantação, distribuição entre zonas/nós, restauração e testes; snapshots isolados não garantem consistência |
| AKS PROD | 3 nós e intenção de HPA, topology spread, PDB e Cluster Autoscaler | Réplicas/HA, falha de nó/zona e capacidade N+1 ainda não comprovadas |
| Observabilidade | Collector, Prometheus, Loki, Tempo, Grafana dentro do AKS; Blob para logs/traces | Ajustar retenção, cardinalidade, sampling, requests e custos à telemetria efetiva |
| Snapshot | 100 GB provisionados para estimativa | Volume de snapshots não define quantidade, consistência ou prazo de retenção; restauração a testar |
| DEV/HOM | Um nó de 4 vCPU/16 GiB, sem HA | Confirmar que a stack completa cabe, com requests/limits e margem para kube-system |
| Serviços adicionais | Infraestrutura escolhida na calculadora | DNS de domínio, certificados cobrados, Private Endpoints, operações extras, egress inter-AZ, IOPS, backups adicionais, suporte avançado e impostos não integralmente estimados |

## Critérios de revisão

1. Exportar e arquivar a estimativa da Azure Pricing Calculator, inclusive identificação da região, unidades e preços exibidos; os prints constituem a base da validação atual.
2. Implementar LOCAL com Docker Compose e os três ambientes AKS de forma **isolada**; o orçamento não é prova de provisionamento.
3. Validar 50 RPS, falha de Consolidation sem bloquear Transactions, Outbox/consumer idempotente, falha do RabbitMQ e recuperação.
4. Confirmar PostgreSQL zone-redundant, retenções e backups/restauração, custo de storage e banda agregado, capacidade N+1 e autoscaling.
5. Recalcular custo com métricas reais, sem alterar retroativamente o orçamento-base decidido: registrar revisões com data e escopo.

## Referências

- [ADR-009 decidida](adr/ADR-009-deployment-cloud.md)
- [Deployment View](deployment-view.md)
- [Azure Pricing Calculator](https://azure.microsoft.com/en-us/pricing/calculator/)
- [AKS pricing tiers](https://learn.microsoft.com/en-us/azure/aks/free-standard-pricing-tiers)
- [Azure Database for PostgreSQL Flexible Server](https://azure.microsoft.com/en-us/pricing/details/postgresql/flexible-server/)
- [PostgreSQL HA](https://learn.microsoft.com/en-us/azure/postgresql/high-availability/concepts-high-availability)
- [Azure Managed Disks](https://azure.microsoft.com/en-us/pricing/details/managed-disks/)
- [Azure Blob Storage](https://azure.microsoft.com/en-us/pricing/details/storage/blobs/)
