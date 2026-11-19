# ADR-009: Implantação LOCAL e Azure AKS em DEV, HOM e PROD

## Contexto e problema

O CashFlow deve registrar créditos/débitos sem depender da disponibilidade de Consolidation, disponibilizar consultas de saldos diários e suportar pico de 50 RPS na consulta, respeitando o requisito de perda a esclarecer. A arquitetura contempla dois serviços independentes em .NET 10, Transactions com Transactional Outbox, Consolidation com consumidor idempotente, RabbitMQ, PostgreSQL, Keycloak, frontend React + NGINX e OpenTelemetry Collector, Prometheus, Loki, Tempo e Grafana.

Precisamos de ambientes segregados, implantação reprodutível e orçamento que inclua computação, persistência, observabilidade, rede e serviços gerenciados. Kubernetes permite compartilhar nós entre pods, mas não elimina custo de discos, backups ou operação dos workloads com estado.

## Alternativas avaliadas

A decisão foi tomada após avaliar o **modelo de operação**, as dependências com estado, a independência dos microsserviços e os custos que cada alternativa exige. As opções permanecem documentadas mesmo sem uma cotação AWS completa, pois não seria correto apresentar seus preços preliminares como equivalentes ao orçamento Azure.

| Opção | Como hospedar os componentes | Vantagens | Limitações e trade-offs | Resultado |
| --- | --- | --- | --- | --- |
| **Azure Kubernetes Service (AKS)** | Transactions, Consolidation, NGINX/React, Keycloak, RabbitMQ, Collector, Prometheus, Loki, Tempo e Grafana nos nós AKS; PostgreSQL Flexible Server externo | Kubernetes, padronização, isolamento de deploy, controle de recursos, HPA e possibilidade de cluster autoscaler | Custo fixo de nós; operação de componentes stateful, discos, quorum, retenção e HA exige equipe e testes | **Escolhida** |
| **Amazon Elastic Kubernetes Service (EKS + EC2)** | Workloads nos nós EKS e PostgreSQL no Amazon RDS, com EBS/S3 e infraestrutura de rede AWS | Mantém Kubernetes, manifests/Helm e independência dos serviços; viável tecnicamente | Taxa por cluster, EC2, EBS, ALB, NAT, RDS e observabilidade precisam de estimativa AWS integral; migração de operação Azure para AWS | **Alternativa Kubernetes considerada, não escolhida** |
| **Azure Container Apps (ACA)** | APIs e componentes stateless em Container Apps; broker, Keycloak e observabilidade stateful em serviços externos ou VMs | Reduz administração dos serviços stateless, escala por serviço e modelo de cobrança por utilização | Topologia híbrida para RabbitMQ e observabilidade, múltiplos modelos operacionais, persistência/HA adicionais | **Não escolhida** |
| **Amazon ECS com AWS Fargate** | Containers stateless em Fargate e componentes stateful externos/gerenciados ou EC2 | Menos gestão de nós para as APIs e escala por tasks | Não oferece um cluster Kubernetes único para toda a stack; precisa compor broker, métricas, logs, storage, ALB, RDS e rede | **Não escolhida** |
| **VMs com Docker Compose em cloud** | Serviços e dependências em VMs | Simples de iniciar, mantém ferramentas conhecidas | HA, recuperação de host, atualizações, escala e isolamento de falhas dependem de engenharia adicional | **Não escolhida para cloud** |
| **Docker Compose local** | Containers no computador do desenvolvedor | Menor custo e reprodução simples do MVP | Não oferece disponibilidade ou escala de produção | **Escolhida apenas em LOCAL** |

**Por que AKS:** permite executar todas as cargas previstas, inclusive os componentes de mensageria e observabilidade, sob um modelo Kubernetes comum, com PostgreSQL gerenciado fora do cluster. A escolha **não** afirma que AKS seja universalmente mais barato que EKS, ACA ou Fargate; a vantagem é a adequação ao desenho e à estratégia operacional definida, usando um orçamento Azure efetivamente configurado.

## Decisão registrada

1. **LOCAL:** Docker Compose no computador do desenvolvedor, sem custo de Azure na estimativa.
2. **DEV:** AKS em East US, ambiente próprio e econômico, um nó 4 vCPU / 16 GiB, opção de gerenciamento sem SLA; PostgreSQL Flexible Server Burstable B1ms com bancos lógicos Transactions, Consolidation e Keycloak.
3. **HOM:** mesmo tamanho econômico de DEV, mas **cluster, banco, dados, segredos, armazenamento e rede próprios**. **Não compartilhar DEV e HOM**. Manter HOM como está no orçamento de referência, sem redimensionamento obrigatório para espelhar PROD.
4. **PROD:** AKS Standard com SLA, um cluster com **3 nós Standard_D4as_v5**, PostgreSQL Flexible Server General Purpose D2s v3 com **HA habilitada**, persistência para RabbitMQ e observabilidade, e plano de réplicas e autoscaling para workloads críticos.
5. **SHARED:** uma instância ACR Basic e uma zona pública Azure DNS para os três ambientes. Cada workload usa identidades, permissões e dados específicos do ambiente.
6. **Região Azure:** East US como premissa de orçamento e referência de implantação. **Provedor selecionado:** Azure, não Amazon EKS.

A escolha é de **arquitetura e orçamento-base**. Não significa que já exista implantação, que a HA tenha sido testada ou que os custos reais não possam variar.

## Sequenciamento de execução decidido após a ADR

**Fase atual do CashFlow: executar exclusivamente em LOCAL usando Docker Compose, sem provisionar Azure.** A decisão de usar AKS como alvo de implantação em DEV/HOM/PROD permanece registrada aqui, mas **essa implantação foi adiada para uma fase futura**. O dimensionamento e os custos citados abaixo são apenas estimativas de planejamento, não recursos ativos nem compromissos de gasto agora. Os testes iniciais de aplicação, resiliência e carga serão locais; não demonstram HA/SLA/capacidade de produção no AKS.\n\n## Orçamento de referência da Azure Pricing Calculator

| Grupo | Mensal (USD) | Configuração de referência |
| --- | ---: | --- |
| LOCAL | 0,00 de cloud | Docker Compose |
| DEV | **204,34** | 1 nó AKS, 3 PVs P4, PostgreSQL B1ms, Blob LRS 20 GB, LB, 2 IPs, Key Vault, egress |
| HOM | **204,34** | Configuração econômica equivalente a DEV, totalmente segregada |
| PROD | **890,44** | 3 nós AKS Standard, 4 PVs P10, PostgreSQL General Purpose com HA, Blob ZRS 200 GB, LB, 2 IPs, Key Vault, snapshots 100 GB |
| SHARED | **5,90** | ACR Basic + 1 zona pública DNS |
| **TOTAL MENSAL** | **1.305,02** | Somente itens e premissas orçados |
| **TOTAL ANUAL ESTIMADO** | **15.660,24** | 12 × total mensal aproximado |

### PostgreSQL: valores definidos na calculadora

| Ambiente | Serviço | Mensal |
| --- | --- | ---: |
| DEV | Flexible Server Burstable B1ms, 1 vCore, 32 GiB Premium SSD, sem HA | **US$ 16,09** |
| HOM | Instância equivalente à DEV, mas independente, sem HA | **US$ 16,09** |
| PROD | Flexible Server General Purpose D2s v3, 2 vCores, 32 GiB Premium SSD, **Enable High Availability** | **US$ 257,02** |
| **Total Azure PostgreSQL** | | **US$ 289,20** |

A opção **Enable High Availability** já adicionou o standby na calculadora PROD (US$ 128,51 de HA). O valor **US$ 257,02** contém US$ 124,83 de compute primário, US$ 3,68 de storage e US$ 128,51 de HA. **Não duplicar o número de servidores** para cobrar standby novamente. A modalidade **zone-redundant** deve ser confirmada ao implantar; a tela de custos não demonstra em qual zona ficará a réplica.

Mais detalhes dos demais itens e seus preços estão concentrados **somente** no [orçamento dos quatro ambientes](../four-environment-total-cost-plan.md). Este ADR registra decisão e justificativa, sem reproduzir tabelas preliminares.

## Consequências positivas

- Três ambientes Azure com isolamento real e custos previsíveis por recursos contratados.
- HOM econômico sem compartilhar banco, cluster ou segredos com DEV.
- Serviços .NET e infraestrutura de mensageria/observabilidade executados no AKS, com PostgreSQL gerenciado fora do cluster.
- Possibilidade de HPA e Cluster Autoscaler para escalar workloads stateless; disponibilidade não depende de uma réplica única em PROD.
- ACR e DNS centralizados, evitando cobrança triplicada dos serviços compartilhados.

## Riscos e mitigação

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

## Critérios de validação

1. Criar Compose local para React/NGINX, Transactions, Consolidation, PostgreSQL com bancos lógicos, RabbitMQ, Keycloak e stack de observabilidade. O template inicial de NGINX está em [infra/nginx](../../../infra/nginx/README.md).
2. Criar pipelines independentes de build/deploy e configurações isoladas por ambiente.
3. Garantir que falha de Consolidation não bloqueie gravação em Transactions; retomar consumo via Outbox e RabbitMQ com idempotência.
4. Validar a consulta com **50 RPS** e registrar taxa de sucesso, timeouts e latências. Interpretar formalmente o limite de 5% de perda.
5. Confirmar PostgreSQL **zone-redundant**, estratégia de backup de banco e dados persistentes, quorum do broker e restauração.
6. Validar em PROD a distribuição dos pods entre nós/zonas, HPA, PDB, probes e capacidade de continuar operando após falha de nó (N+1).
7. Comparar custo e consumo real pós-implantação ao [orçamento-base](../four-environment-total-cost-plan.md) e registrar revisões com data.

## Detalhes de implantação ainda pendentes

- Confirmar a modalidade **zone-redundant** do PostgreSQL e testar failover, backup e restauração.
- Validar capacidade de três nós, quotas, réplicas mínimas, HPA, Cluster Autoscaler, PDB e resiliência a falha de nó/zona.
- Dimensionar o quorum do RabbitMQ, os volumes persistentes e a política de snapshots e recuperação consistente.
- Confirmar topologia de entrada/saída do AKS, dois IPs previstos, SNAT e custos agregados de egress.
- Medir consumo de logs, métricas e traces, capacidade de Blob e retenção de observabilidade.
- Rever a necessidade de rede privada para o ACR, cujo plano Basic é a premissa financeira atual.
- Manter HOM econômico e independente; para testes equivalentes a PROD, provisionar capacidade apropriada e cotar esse esforço separadamente.

A ADR-009 permanece **Decidida**. Estas verificações são tarefas de implementação e operação, não comprovação já concluída.

## Referências

- [Orçamento Azure dos quatro ambientes](../four-environment-total-cost-plan.md)
- [Azure Pricing Calculator](https://azure.microsoft.com/en-us/pricing/calculator/)
- [Azure AKS](https://azure.microsoft.com/en-us/pricing/details/kubernetes-service/)
- [Azure Database for PostgreSQL](https://azure.microsoft.com/en-us/pricing/details/postgresql/flexible-server/)
- [Building Block View](../building-block-view.md)
