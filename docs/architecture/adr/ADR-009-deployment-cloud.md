# ADR-009: Implantação local, alternativas de cloud e custo total

> **Status:** proposta para aprovação  
> **Data:** 2026-11-15  
> **Issue:** [#7, Documentar ADRs e trade-offs](https://github.com/wolwerr/CashFlow-Architecture/issues/7)  
> **Comparativo Kubernetes prioritário:** [AKS versus EKS, custo por serviço](../aks-eks-cost-breakdown.md)  
> **Comparativo original de modelos alternativos:** [ACA, AKS e ECS/Fargate](../cloud-cost-comparison.md)  
> **Relacionadas:** [ADR-002](ADR-002-architecture-style-clean-architecture.md), [ADR-003](ADR-003-persistence.md), [ADR-004](ADR-004-async-messaging.md), [ADR-005](ADR-005-outbox-consistency-idempotency.md), [ADR-006](ADR-006-identity-and-access.md), [ADR-007](ADR-007-clients-api-gateway.md), [ADR-008](ADR-008-observability.md)

## Contexto

CashFlow separa Transactions e Consolidation, com persistência PostgreSQL por domínio e publicação confiável via Transactional Outbox e RabbitMQ. A aplicação Web utiliza React com NGINX como reverse proxy; autenticação via Keycloak. A observabilidade foi decidida na ADR-008 com OpenTelemetry Collector, Prometheus, Loki, Tempo e Grafana.

É necessário demonstrar **continuidade do registro quando o consolidado falhar**, além da consulta a **50 RPS com até 5% de perda** segundo a interpretação documentada nos RNFs. A escolha de plataforma precisa considerar custos e operação de **toda a arquitetura**, não só das duas APIs.

O desafio não informou região obrigatória, orçamento ou SLA anual. Portanto o comparativo de cloud constitui **estudo de engenharia com premissas**. Não há cloud de produção escolhida nesta ADR.

## Alternativas avaliadas

| Alternativa | Benefícios | Trade-offs |
| --- | --- | --- |
| **Docker Compose local** | Reprodutibilidade, baixa barreira para demonstração, isolamento lógico entre containers | Sem HA automática, falha do host é falha de toda a solução; backups e secrets precisam definição |
| **Azure Container Apps (ACA)** | Runtime gerenciado, escalabilidade separada por serviço, menos administração das APIs stateless | RabbitMQ e observabilidade com estado exigem topologia adicional; preço depende da atividade/idle e conexões; arquitetura heterogênea |
| **Azure Kubernetes Service (AKS)** | Kubernetes padronizado, maior controle dos workloads e possibilidade de operar stack completa em cluster | Nós, SLA do control plane, rede, storage, atualização e gestão de recursos elevam custo fixo e complexidade; Kubernetes não garante HA sozinho |
| **Amazon Elastic Kubernetes Service (EKS) com nós EC2** | Kubernetes gerenciado na AWS; workloads e Helm/manifests semelhantes aos AKS, com PostgreSQL no RDS externo | EC2, plano de controle, EBS/S3, ALB/NAT e operação dos recursos com estado precisam ser cotados |
| **AWS ECS com Fargate** | Infraestrutura gerenciada para containers stateless, escala independente, modelo pay-per-resource, sem exigir Kubernetes | Não equivale a um cluster Kubernetes e requer componentes stateful adicionais, ALB/NAT/ECR/RDS/storage |
| **VMs + Docker Compose em cloud** | Familiaridade e menor esforço inicial | Escalabilidade, recuperação, isolamento de falhas e HA exigem engenharia manual; risco de ponto único |

## Propostas separadas para aprovação

### 1. MVP: Docker Compose

**Proposta:** adotar Docker Compose como ambiente de desenvolvimento, integração e demonstração. O arranjo local deve incluir, conforme código implementado:

- React estático servido por **NGINX**.
- **Transactions** e **Consolidation** com imagens, configurações, migrations e ciclos de vida independentes.
- **PostgreSQL** com bancos lógicos separados para Transactions, Consolidation e Keycloak, sem acesso cruzado direto.
- **RabbitMQ** com persistência, filas apropriadas, ACK manual, retries/DLQ e testes de redelivery.
- **Keycloak** usando OIDC/PKCE e base persistente.
- **OpenTelemetry Collector, Prometheus, Loki, Tempo e Grafana**, com pipelines reais e limites de armazenamento.

Não prometer HA em Compose com um nó para cada infraestrutura. Não interpretar monorepo como monólito de implantação. O Outbox worker está acoplado ao executável de Transactions segundo a ADR-002, requerendo exclusão/coordenação apropriada ao escalar.

**Situação atual:** existe template inicial de NGINX em [infra/nginx](../../../infra/nginx/README.md) e documentação arquitetural, mas ainda não foi demonstrada a integração de todos esses containers nem testes de carga. Uma decisão arquitetural não é prova de implantação.

### 2. Produção de referência: decisão pendente

**Proposta revisada:** priorizar uma comparação direta **AKS x Amazon EKS (managed EC2 node groups)**, pois ambos executam os workloads do CashFlow em nós Kubernetes, com o PostgreSQL externo e gerenciado, respectivamente **Azure Database for PostgreSQL Flexible Server** e **Amazon RDS for PostgreSQL**. Registrar **preferência provisória por AKS**, não aprovação final de cloud, até completar rede, persistência, segurança, HA e custos totais. [Veja o custo por serviço e por linha faturável](../aks-eks-cost-breakdown.md). ACA e ECS/Fargate permanecem alternativas já estudadas, mas são modelos de orquestração diferentes.

| Critério | ACA | AKS | ECS/Fargate |
| --- | --- | --- | --- |
| Operar serviços .NET stateless | Conveniente, poucas abstrações | Kubernetes mais completo, exige plataforma | Conveniente, tarefas e serviços ECS |
| Operar RabbitMQ | Preferir broker externo/gerenciado ou cluster cuidadosamente desenhado | Possível via operador, quorum queues, discos e múltiplos nós | Requer broker gerenciado/EC2/estratégia de estado |
| Logs/traces/metrics self-hosted | Pode exigir VMs/serviços de dados e redes | Viável no cluster, atenção a volumes/HA | Possível com recursos dedicados, serviços externos e custos de rede |
| Escala independente | Por Container App e réplicas | Por Deployment/consumer | Por ECS Service/task |
| Custos fixos | Menores para escala real a zero, mas não presumir zero para APIs financeiras | Nós e eventual cobrança de control plane | Task-hours, rede, ALB e estado externo |
| Operação | Simplifica runtime, não elimina operação stateful | Mais trabalho Kubernetes | Simplifica runtime, não elimina operações auxiliares |

A escolha da cloud também deve considerar capacidades e experiência da equipe, requisitos de rede, localização de dados, oferta de broker, acordos comerciais e custos de suporte.

## Comparação homogênea de Kubernetes (AKS x EKS)

| Item | AKS Standard (Azure East US) | Amazon EKS (AWS us-east-1) |
| --- | ---: | ---: |
| 3 nós de 4 vCPU e 16 GiB | **US$ 376,68** | **US$ 420,48** |
| Plano de controle | **US$ 73,00** | **US$ 73,00** |
| PostgreSQL gerenciado, compute | **US$ 99,28** | **US$ 94,17** |
| Storage PostgreSQL incluído | **US$ 3,68** | **não cotado** |
| **Subtotal parcial (USD/mês)** | **US$ 552,64** | **US$ 587,65** |

Os serviços como Transactions, Consolidation, RabbitMQ, Keycloak, NGINX, Collector, Prometheus, Loki, Tempo e Grafana **compartilham nós**. Portanto, seus custos de computação não aparecem separadamente na fatura: o [documento de rateio](../aks-eks-cost-breakdown.md) atribui valores ilustrativos por componente segundo vCPU hipotética e apresenta os recursos efetivamente faturáveis. **Não confundir rateio com cobrança por pod.** O cenário não inclui PVs/EBS, object storage, rede, load balancer, NAT, registry, backups, zonas ou HA.

## Comparação de custo já calculada

O [estudo FinOps](../cloud-cost-comparison.md) documenta 730 h/mês e tarifas verificadas em novembro/2026, para **East US** e **us-east-1**:

| Modelo | Subtotal parcial USD/mês | Limitações principais |
| --- | ---: | --- |
| ACA | **543,88** | Supõe todas as réplicas ativas 24x7, não abate franquia e não inclui vários itens de rede/storage |
| AKS Standard | **552,64** | Três nós com 12 vCPU/48 GiB, mais capacidade que os demais modelos; sem storage/rede/HA de dados |
| ECS/Fargate | **378,49** | Sem storage RDS, EBS, ALB, rede, backups ou HA; não é custo final |

Os totais são **não comparáveis como faturas completas**. Devem ser apresentados como **subtotais de itens precificados**, com escopo e capacidade diferentes. Valores de entrada podem mudar. Não atribuir custo zero a serviços sem preços calculados.

## Refinamento de custos: volumes, object storage e rede

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

**Aprovação ainda pendente:** manter AKS como preferência preliminar, com EKS como alternativa equivalente em Kubernetes, até termos uma versão com preço total de rede, storage, banco, egress e HA, além de teste de 50 RPS.

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

## Pendências para a decisão final de cloud

- Região e exigências de localização de dados.
- Distinção entre **demonstração cloud** e **produção HA**.
- Recursos realmente medidos sob carga, 50 RPS e volume de eventos/dados.
- Requisitos de RTO/RPO, retenção de dados, backup e disponibilidade.
- Preços completos de Azure LB/NAT, AWS RDS storage/ECR, rede, ingress, object storage, PVCs, backups, observabilidade, broker, certificados, secrets e suporte.
- Nível de operação aceito pela equipe e uso de serviços gerenciados.
- Escolher formalmente **AKS ou EKS** após concluir a comparação Kubernetes; ACA e ECS/Fargate são alternativas de outros modelos já avaliadas. Atualizar esta ADR para aceita somente depois da escolha.

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
