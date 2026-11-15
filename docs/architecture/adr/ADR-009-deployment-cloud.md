# ADR-009: Implantação LOCAL e Azure AKS em DEV, HOM e PROD

> **Status:** aceita  
> **Data da decisão:** 2026-11-15  
> **Issue:** [#7, Documentar ADRs e trade-offs](https://github.com/wolwerr/CashFlow-Architecture/issues/7)  
> **Orçamento detalhado aprovado:** [CashFlow | Orçamento Azure dos quatro ambientes](../four-environment-total-cost-plan.md)  
> **Relacionadas:** [ADR-002](ADR-002-architecture-style-clean-architecture.md), [ADR-003](ADR-003-persistence.md), [ADR-004](ADR-004-async-messaging.md), [ADR-005](ADR-005-outbox-consistency-idempotency.md), [ADR-006](ADR-006-identity-and-access.md), [ADR-007](ADR-007-clients-api-gateway.md), [ADR-008](ADR-008-observability.md)

## Contexto e requisitos

O CashFlow deve registrar créditos/débitos sem depender da disponibilidade de Consolidation, disponibilizar consultas de saldos diários e suportar pico de 50 RPS na consulta, respeitando o requisito de perda a esclarecer. A arquitetura contempla dois serviços independentes em .NET 10, Transactions com Transactional Outbox, Consolidation com consumidor idempotente, RabbitMQ, PostgreSQL, Keycloak, frontend React + NGINX e OpenTelemetry Collector, Prometheus, Loki, Tempo e Grafana.

Precisamos de ambientes segregados, implantação reprodutível e orçamento que inclua computação, persistência, observabilidade, rede e serviços gerenciados. Kubernetes permite compartilhar nós entre pods, mas não elimina custo de discos, backups ou operação dos workloads com estado.

## Decisão

1. **LOCAL:** Docker Compose no computador do desenvolvedor, sem custo de Azure na estimativa.
2. **DEV:** AKS em East US, ambiente próprio e econômico, um nó 4 vCPU / 16 GiB, opção de gerenciamento sem SLA; PostgreSQL Flexible Server Burstable B1ms com bancos lógicos Transactions, Consolidation e Keycloak.
3. **HOM:** mesmo tamanho econômico de DEV, mas **cluster, banco, dados, segredos, armazenamento e rede próprios**. **Não compartilhar DEV e HOM**. Manter HOM como está no orçamento aprovado, sem redimensionamento obrigatório para espelhar PROD.
4. **PROD:** AKS Standard com SLA, um cluster com **3 nós Standard_D4as_v5**, PostgreSQL Flexible Server General Purpose D2s v3 com **HA habilitada**, persistência para RabbitMQ e observabilidade, e plano de réplicas e autoscaling para workloads críticos.
5. **SHARED:** uma instância ACR Basic e uma zona pública Azure DNS para os três ambientes. Cada workload usa identidades, permissões e dados específicos do ambiente.
6. **Região Azure:** East US como premissa de orçamento e referência de implantação. **Provedor selecionado:** Azure, não Amazon EKS.

A escolha é de **arquitetura e orçamento-base**. Não significa que já exista implantação, que a HA tenha sido testada ou que os custos reais não possam variar.

## Orçamento aprovado, Azure Pricing Calculator

| Grupo | Mensal (USD) | Configuração de referência |
| --- | ---: | --- |
| LOCAL | 0,00 de cloud | Docker Compose |
| DEV | **204,34** | 1 nó AKS, 3 PVs P4, PostgreSQL B1ms, Blob LRS 20 GB, LB, 2 IPs, Key Vault, egress |
| HOM | **204,34** | Configuração econômica equivalente a DEV, totalmente segregada |
| PROD | **890,44** | 3 nós AKS Standard, 4 PVs P10, PostgreSQL General Purpose com HA, Blob ZRS 200 GB, LB, 2 IPs, Key Vault, snapshots 100 GB |
| SHARED | **5,90** | ACR Basic + 1 zona pública DNS |
| **TOTAL MENSAL** | **1.305,02** | Somente itens e premissas orçados |
| **TOTAL ANUAL ESTIMADO** | **15.660,24** | 12 × total mensal aproximado |

### PostgreSQL: valores efetivamente aprovados

| Ambiente | Serviço | Mensal |
| --- | --- | ---: |
| DEV | Flexible Server Burstable B1ms, 1 vCore, 32 GiB Premium SSD, sem HA | **US$ 16,09** |
| HOM | Instância equivalente à DEV, mas independente, sem HA | **US$ 16,09** |
| PROD | Flexible Server General Purpose D2s v3, 2 vCores, 32 GiB Premium SSD, **Enable High Availability** | **US$ 257,02** |
| **Total Azure PostgreSQL** | | **US$ 289,20** |

A opção **Enable High Availability** já adicionou o standby na calculadora PROD (US$ 128,51 de HA). O valor **US$ 257,02** contém US$ 124,83 de compute primário, US$ 3,68 de storage e US$ 128,51 de HA. **Não duplicar o número de servidores** para cobrar standby novamente. A modalidade **zone-redundant** deve ser confirmada ao implantar; a tela de custos não demonstra em qual zona ficará a réplica.

Mais detalhes dos demais itens e seus preços estão concentrados **somente** no [orçamento dos quatro ambientes](../four-environment-total-cost-plan.md). Este ADR registra decisão e justificativa, sem reproduzir tabelas preliminares.

## Alternativas consideradas e trade-offs

| Alternativa | Vantagens | Motivo de não adoção como referência |
| --- | --- | --- |
| Azure Kubernetes Service (AKS) | Kubernetes padronizado, operar serviços financeiros, broker e observabilidade nos mesmos nós, autoescalabilidade e integração ao Azure PostgreSQL | **Selecionado**, com custo fixo dos nós e necessidade de administrar workloads stateful |
| Amazon EKS com EC2 | Kubernetes equivalente na AWS, possibilidade de reutilizar manifests e Helm | Exigiria preços e operação AWS separados de EC2, EKS, RDS, EBS, S3, ALB/NAT e secrets; não escolhido |
| Azure Container Apps (ACA) | Menor administração de serviços stateless, cobrança conforme uso | RabbitMQ e observabilidade com estado precisariam de infraestrutura complementar, criando desenho heterogêneo |
| AWS ECS/Fargate | Tasks gerenciadas, escala de serviços stateless | Não fornece o mesmo modelo Kubernetes e exige gestão/composição adicional de broker, banco e telemetria |
| VMs + Compose na cloud | Simplicidade inicial, familiaridade | Falha do host, HA, escala e recuperação exigiriam mecanismos próprios |
| Compose LOCAL | Bom para desenvolvimento e reproduzir dependências | **Adotado apenas em LOCAL**, não garante HA |

Os valores preliminares que antes constavam em estudos separados não são reproduzidos aqui porque não representavam o escopo aprovado de quatro ambientes. Os trade-offs técnicos acima preservam a fundamentação da decisão sem misturar cálculos de cenários não equivalentes.

## Consequências positivas

- Três ambientes Azure com isolamento real e custos previsíveis por recursos contratados.
- HOM econômico sem compartilhar banco, cluster ou segredos com DEV.
- Serviços .NET e infraestrutura de mensageria/observabilidade executados no AKS, com PostgreSQL gerenciado fora do cluster.
- Possibilidade de HPA e Cluster Autoscaler para escalar workloads stateless; disponibilidade não depende de uma réplica única em PROD.
- ACR e DNS centralizados, evitando cobrança triplicada dos serviços compartilhados.

## Consequências negativas e riscos

| Risco | Consequência | Mitigação |
| --- | --- | --- |
| Três ambientes Kubernetes | Mais custo fixo e operação do que ambientes menores ou compartilhados | Isolamento exigido; DEV e HOM com um nó e sem SLA |
| HOM menor que PROD | Homologação funcional não comprova desempenho, N+1, 50 RPS nem HA | Testes não funcionais com capacidade apropriada; cotar eventuais ambientes de teste ampliados quando necessários |
| RabbitMQ, Prometheus e Keycloak em pods | Consomem recursos dos nós, PVCs, backups e operação | Dimensionar requests/limits; operador/quorum, persistência e restauração |
| HPA interpretado como HA | Pods escalam, mas recursos podem faltar e dados não são replicados por HPA | minReplicas adequadas, topology spread, probes, PDB, Cluster Autoscaler, capacidade N+1 e testes |
| PostgreSQL HA apenas cotada | Sem prova de zone-redundant, failover e RPO/RTO | Confirmar configuração zone-redundant e executar restore e testes de falha |
| Blobs/snapshots subdimensionados | Perda de observabilidade ou custo variável de retenção | Medir ingestão e retenção de Loki/Tempo; snapshots são capacidade, não garantia de backup |
| 2 IPs e saída via LB | Configuração real de egress/SNAT pode diferir | Validar IPs provisionados, tráfego e necessidade futura de NAT |
| ACR Basic compartilhado | Não oferece Private Endpoint e requer governança de acesso | Revisar exigência de rede privada e avaliar upgrade para Premium se necessário |
| Cobrança de banda igual a zero na simulação | Egress agregado entre ambientes pode exceder franquia ou incluir outros tipos de tráfego | Medir banda real, inter-AZ e consumo da assinatura/contrato |
| Disponibilidade de observabilidade | Falha da stack de métricas/logs/traces não deve bloquear Transactions | Telemetria assíncrona e desacoplada, limites e tolerância a falhas |

## Implantação e testes necessários

1. Criar Compose local para React/NGINX, Transactions, Consolidation, PostgreSQL com bancos lógicos, RabbitMQ, Keycloak e stack de observabilidade. O template inicial de NGINX está em [infra/nginx](../../../infra/nginx/README.md).
2. Criar pipelines independentes de build/deploy e configurações isoladas por ambiente.
3. Garantir que falha de Consolidation não bloqueie gravação em Transactions; retomar consumo via Outbox e RabbitMQ com idempotência.
4. Validar a consulta com **50 RPS** e registrar taxa de sucesso, timeouts e latências. Interpretar formalmente o limite de 5% de perda.
5. Confirmar PostgreSQL **zone-redundant**, estratégia de backup de banco e dados persistentes, quorum do broker e restauração.
6. Validar em PROD a distribuição dos pods entre nós/zonas, HPA, PDB, probes e capacidade de continuar operando após falha de nó (N+1).
7. Comparar custo e consumo real pós-implantação ao [orçamento-base](../four-environment-total-cost-plan.md) e registrar revisões com data.

**Status da ADR-009 permanece aceita**, com esses testes e limites como trabalho de implementação e operação, sem confundir aprovação de arquitetura com certificação de HA.

## Referências

- [Orçamento Azure dos quatro ambientes](../four-environment-total-cost-plan.md)
- [Azure Pricing Calculator](https://azure.microsoft.com/en-us/pricing/calculator/)
- [Azure AKS](https://azure.microsoft.com/en-us/pricing/details/kubernetes-service/)
- [Azure Database for PostgreSQL](https://azure.microsoft.com/en-us/pricing/details/postgresql/flexible-server/)
- [Building Block View](../building-block-view.md)
