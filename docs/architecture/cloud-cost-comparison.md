# Comparativo de custos: Azure Container Apps, AKS e AWS ECS/Fargate

> **ESTUDO HISTÓRICO, NÃO UTILIZAR COMO ORÇAMENTO APROVADO.** Os valores desta comparação são hipóteses iniciais de um único ambiente, com dimensionamentos e recursos diferentes entre plataformas. O PostgreSQL Azure B2ms de **US$ 99,28/mês** não corresponde ao PostgreSQL de PROD adotado. A [ADR-009 aceita](adr/ADR-009-deployment-cloud.md) selecionou **AKS em East US**, com os valores efetivamente configurados na Azure Pricing Calculator, documentados no [orçamento dos quatro ambientes](four-environment-total-cost-plan.md): **DEV US$ 204,34/mês; HOM US$ 204,34/mês; PROD US$ 890,44/mês; SHARED US$ 5,90/mês; total US$ 1.305,02/mês**. Dentro de cada ambiente, o PostgreSQL custa **US$ 16,09 (DEV), US$ 16,09 (HOM), US$ 257,02 (PROD, HA habilitada)**, total **US$ 289,20/mês**. Estes valores aprovados não recalculam os cenários históricos ACA ou ECS, que não foram precificados de forma completa.


> **Data da pesquisa:** 2026-11-15  
> **Status:** estudo preliminar de referência, **não cotação nem decisão de fornecedor**  
> **Vinculado à:** [ADR-009](adr/ADR-009-deployment-cloud.md)  
> **Moeda:** USD, sem impostos; estimativas mensais para 730 horas.  
> **Regiões:** Azure **East US** e AWS **us-east-1 (N. Virginia)**. São regiões distintas de provedores distintos, escolhidas para comparação e não por exigência do desafio.

## Resumo executivo

O objetivo é comparar **três modelos de hospedagem**, não apenas o preço dos dois serviços .NET:

1. **Modelo A, Azure Container Apps:** APIs, NGINX, Keycloak e Collector em containers ACA; RabbitMQ + stack Grafana em VM de suporte; PostgreSQL Flexible Server.
2. **Modelo B, AKS:** workloads de aplicação, RabbitMQ e observabilidade em nós AKS; PostgreSQL Flexible Server externo, com três nós como referência. Keycloak no cluster.
3. **Modelo C, ECS/Fargate:** serviços stateless Fargate; RabbitMQ + stack Grafana em EC2 de suporte; PostgreSQL RDS.

**Resultado do estudo:** os subtotais abaixo são **parciais e não plenamente equivalentes**. A diferença entre ACA/AKS/ECS pode ser invertida pela estratégia de estado, HA, armazenamento, tráfego, integração de rede e operação. **Não selecionar o vencedor apenas pelo menor subtotal.** A arquitetura deve ser testada a **50 RPS**, com independência de Transactions frente à falha de Consolidation.

## Premissas iguais de funcionalidade

| Parâmetro | Premissa |
| --- | --- |
| Horas | 730 h/mês, 24x7, preços sob demanda (sem descontos/contratos) |
| Frontend | React + TypeScript servido pelo NGINX |
| APIs | Transactions (API + Outbox worker) e Consolidation (API + consumidor), builds e escala independentes |
| Segurança | Keycloak e validação JWT local nas duas APIs |
| Dados | PostgreSQL em três bancos lógicos: Transactions, Consolidation e Keycloak; **um servidor pequeno compartilhado somente no cenário econômico**, reconhecendo falha correlacionada |
| Mensageria | RabbitMQ self-hosted, único nó no cenário de referência; quorum/HA não incluídos |
| Observabilidade | OpenTelemetry Collector, Prometheus, Loki, Tempo, Grafana, sem alta disponibilidade |
| Volume de testes | Pico de 50 consultas/s por 5 minutos é requisito de teste, **não pressupõe 50 RPS durante as 730 h mensais** |
| Retenção | A definir; não existe dimensionamento oficial de logs, traces e backups |
| Alta disponibilidade | **Não incluída** neste cenário econômico; uma variante HA exige réplicas, zonas e preços adicionais |

### Pegada ilustrativa para os containers

As reservas são **hipóteses iniciais**, não medições:

| Workload | vCPU | RAM GiB | Tipo |
| --- | ---: | ---: | --- |
| Transactions + worker Outbox | 1,0 | 2 | stateless para fins de hospedagem, dados em PostgreSQL |
| Consolidation + consumer | 1,0 | 2 | stateless, dados em PostgreSQL |
| NGINX / frontend React | 0,5 | 1 | stateless |
| Keycloak | 1,0 | 2 | stateless no container, persistência em PostgreSQL |
| OpenTelemetry Collector | 0,5 | 1 | stateless, buffers finitos |
| **Subtotal containers de entrada/aplicação** | **4,0** | **8** | ACA ou Fargate |
| RabbitMQ + Prometheus + Loki + Tempo + Grafana | **4,0** | **16** | VM de suporte no modelo A/C; nós AKS no modelo B |
| **Total lógico antes do banco** | **8,0** | **24** | Não inclui overhead de SO/orquestrador nem margem HA |

**Aviso de capacidade:** um único host 4 vCPU / 16 GiB para broker + stack de observabilidade é uma simplificação de custo, **não uma garantia de desempenho**, isolamento, persistência ou HA. Produção pode exigir hosts separados, mais memória, discos e object storage para Loki/Tempo. A simulação deve ser revisada após carga e retenção medidas.

## Tarifas de referência e fonte

| Insumo | Tarifa usada | Observação |
| --- | ---: | --- |
| ACA East US, vCPU ativa | US$ 0,0864/vCPU-h | Tarifa regional publicada em levantamento de 04/11/2026 [F1]; preços variam com atividade, estado idle e consumo |
| ACA East US, memória ativa | US$ 0,0108/GiB-h | [F1]; modelagem conservadora de **100% do tempo ativo** |
| ACA, franquia mensal | 180.000 vCPU-s + 360.000 GiB-s + 2 milhões requests | Não abatida dos valores abaixo; aplicação real depende da assinatura [F2] |
| VM Azure D4as v5 Linux East US | US$ 0,172/h, 4 vCPU / 16 GiB | Listagem de preços de referência [F3] |
| AKS Standard, plano de controle | US$ 0,10/h por cluster | Microsoft Learn [F4]; nós, storage e redes cobrados à parte |
| PostgreSQL Azure Flexible Server B2ms (**cenário antigo, sem HA**) | US$ 99,28/mês, 2 vCores / 8 GiB | Tabela Azure [F5]; classe Burstable não é garantia de performance |
| PostgreSQL Azure, disco Premium SSD | US$ 0,115/GiB-mês | [F5]; 32 GiB ilustrativos = US$ 3,68 |
| AWS Fargate Linux x86 us-east-1 | US$ 0,04048/vCPU-h + US$ 0,004445/GB-h | Fonte oficial [F6], cobrança por recurso e segundo |
| AWS EC2 m6i.xlarge | US$ 0,192/h, 4 vCPU / 16 GiB | Preço público de referência [F7] |
| AWS RDS PostgreSQL db.t4g.large Single-AZ | US$ 0,129/h, 2 vCPU / 8 GiB | Referência de preços 14/11/2026 [F8], **sem armazenamento** |
| AWS ALB (se utilizado) | US$ 0,0225/h + LCUs | Fonte oficial [F9]; **não incluído** nos subtotais |

Os valores de terceiros são evidências de mercado consultadas na data e podem mudar; **confirmar na calculadora do provedor na data da contratação**. As classes, CPU, memória, políticas de crédito, storage e HA de PostgreSQL diferem entre provedores.

## Cálculo mensal, cenário econômico e parcialmente precificado

Todos os valores em **USD/mês**, 730 h, sem HA. O cenário inclui 4 vCPU / 8 GiB em componentes de aplicação, e recurso de suporte para RabbitMQ/observabilidade. **Subtotal não é TCO.**

| Linha precificada | A: Azure Container Apps | B: AKS Standard | C: ECS/Fargate |
| --- | ---: | ---: | ---: |
| Containers das APIs, NGINX, Keycloak, Collector | **315,36** = 730 × (4 × 0,0864 + 8 × 0,0108), todos ativos | **Incluído nos nós** | **144,16** = 730 × (4 × 0,04048 + 8 × 0,004445) |
| RabbitMQ + Grafana stack (compute) | **125,56** = 730 × 0,172, 1 VM D4as v5 | **Incluído nos nós** | **140,16** = 730 × 0,192, 1 EC2 m6i.xlarge |
| Nós AKS para aplicação e suporte | Não se aplica | **376,68** = 3 × 730 × 0,172, 3 nós D4as v5 | Não se aplica |
| Gerenciamento de AKS Standard | Não se aplica | **73,00** = 730 × 0,10 | Não se aplica |
| PostgreSQL gerenciado, compute | **99,28** = Azure B2ms | **99,28** = Azure B2ms | **94,17** = 730 × 0,129, RDS t4g.large |
| Storage do banco incluído no subtotal | **3,68** = 32 GiB Azure | **3,68** = 32 GiB Azure | **Não precificado** (RDS volume a cotar) |
| **SUBTOTAL PARCIAL** | **US$ 543,88/mês** | **US$ 552,64/mês** | **US$ 378,49/mês** |
| **Situação do subtotal** | Não deduz franquias ACA, sem ingress/storage de suporte | 3 nós incluem margem teórica; sem volumes/storage/rede | Sem RDS storage, ALB, EBS, rede ou serviços de segurança |

**Leitura correta:** a comparação dos subtotais não significa que o ECS seja o custo final mais baixo. ACA foi calculado com todas as réplicas **ativas 24/7**; o custo real pode ser menor pelo idle/franquias. Já Fargate cobra pela reserva durante a execução e o subtotal não inclui custos relevantes de serviços compartilhados. AKS possui **12 vCPU / 48 GiB de capacidade de nós**, versus **8 vCPU / 24 GiB reservados** nos outros modelos, com overhead e disponibilidade a analisar. A capacidade e o nível de redundância não são equivalentes.

## Custos importantes AINDA NÃO incluídos

| Categoria | Azure ACA | AKS | AWS ECS/Fargate | O que falta definir |
| --- | --- | --- | --- | --- |
| Entrada HTTPS / balanceamento | ACA ingress ou componente de entrada, DNS e certificados | Load Balancer/Ingress, IPs e certificados | ALB/NLB, LCUs, certificados e DNS | Topologia pública e TLS |
| Rede privada e saída | VNet, private endpoints e egress/NAT conforme desenho | VNet, Load Balancer, NAT e IPs | VPC, NAT Gateway, public IPs, endpoints, transferência entre zonas | Número de AZs, NAT e tráfego |
| Persistência broker e observabilidade | Discos da VM, snapshots, object storage para Loki/Tempo conforme configuração | PVs/discos e object storage | EBS para EC2, snapshots e S3/EFS quando apropriado | GiB, IOPS, taxa de logs/traces, retenção |
| Banco de dados | Backups excedentes e HA por zona, eventualmente servidores independentes | Mesmo Azure PostgreSQL | Storage RDS, backup excedente, Multi-AZ | RPO/RTO, backup/restore, IOPS |
| Registry / secrets | ACR, Key Vault | ACR, Key Vault | ECR, Secrets Manager/KMS | Imagens, repetições de deploy, chaves |
| Operação / suporte | Administração da VM RabbitMQ/observabilidade | Administração Kubernetes, updates, pods, nós, quotas | Administração EC2 RabbitMQ/observabilidade | Horas da equipe, SRE, suporte |
| Segurança e compliance | WAF, monitoramento, auditoria e redes | Idem | WAF/CloudWatch/CloudTrail e redes | Requisitos de dados financeiros |
| Alta disponibilidade real | Réplicas de APIs, broker e observabilidade | Múltiplas zonas, réplicas e bancos HA | ALB, tarefas redundantes, DB Multi-AZ, broker HA | SLA, zonas, failover e testes |

**Implicação:** os subtotais parciais servem para comparar **componentes precificados**, nunca para apresentar um orçamento completo à banca. Não atribuir valor zero aos itens não precificados.

## Cenários de sensibilidade

1. **Consumo intermitente:** ACA pode ficar mais econômico quando parte do tempo é idle, com réplicas mínimas > 0 onde necessárias. Não adotar escala a zero indiscriminadamente para APIs sensíveis a cold start, nem contar com plano free sem validar o contrato.
2. **Uso constante:** ECS/Fargate tem cobrança previsível de vCPU/RAM reservados; AKS amortiza capacidade dos nós quando muitos serviços compartilham o cluster, desde que haja utilização adequada.
3. **HA:** duas ou mais réplicas por API, PostgreSQL com réplica/HA, 3+ nós RabbitMQ quando adotado quorum, múltiplos nós/zones de observabilidade e rede redundante podem alterar fortemente custos. O cenário acima **não** é HA.
4. **Banco por domínio:** se precisarmos de três servidores físicos/gerenciados em vez de três bancos lógicos em um único servidor, a parcela de compute do banco pode crescer aproximadamente com o número e tamanho de instâncias, mas capacidade e backups devem ser cotados separadamente.
5. **Observabilidade:** volume de logs, traces, cardinalidade e retenção podem dominar os custos. Determinar GB/dia e sampling antes da decisão.

## Recomendação de engenharia e próxima decisão

- **MVP local:** Docker Compose, sem custos de infraestrutura cloud obrigatórios e sem promessas de HA.
- **Cloud:** manter **ACA, AKS e ECS/Fargate** como alternativas em avaliação. Se a organização tiver uma plataforma Kubernetes pronta e equipe que opere nós, AKS pode reduzir atrito de integração da stack; caso contrário, ACA reduz a responsabilidade de operação do runtime para serviços stateless. ECS/Fargate exige análise completa de ALB, rede e persistência antes de comparação de TCO.
- **Antes de aprovar ADR-009:** estimar a variante de **produção com HA** e a de **demonstração em cloud**, usando calculadoras de Azure/AWS para os itens acima; medir requisitos de recursos com teste de 50 RPS e volume de eventos; decidir região e política de dados.

## Fontes e reprodução

- **[F1]** Valores ACA East US consultados em 15/11/2026: https://vmprices.com/serverless/container-apps/ (preços regionais conferidos em 04/11/2026)
- **[F2]** Preços e franquias oficiais ACA: https://azure.microsoft.com/en-us/pricing/details/container-apps/ e https://learn.microsoft.com/en-us/azure/container-apps/billing
- **[F3]** VM D4as v5 East US, 0,172 USD/h: https://www.economize.cloud/resources/azure/pricing/virtual-machine/d4asv5/
- **[F4]** AKS Standard 0,10 USD/h: https://learn.microsoft.com/en-us/azure/architecture/aws-professional/eks-to-aks/cost-management e https://azure.microsoft.com/en-us/pricing/details/kubernetes-service/
- **[F5]** Azure PostgreSQL Flexible Server (B2ms e storage): https://azure.microsoft.com/en-us/pricing/details/postgresql/flexible-server/
- **[F6]** AWS Fargate x86 Linux US East: https://aws.amazon.com/fargate/pricing/
- **[F7]** EC2 m6i.xlarge us-east-1: https://www.economize.cloud/resources/aws/pricing/ec2/m6i.xlarge/
- **[F8]** AWS RDS db.t4g.large us-east-1: https://cloudprice.net/aws/rds?filter=%5Edb%5C.t4g%5C.&region=us-east-1
- **[F9]** AWS ALB e LCU: https://aws.amazon.com/elasticloadbalancing/pricing/

**Para reproduzir os números:** preço por hora × 730; Azure B2ms 99,28 + (32 × 0,115); somar apenas linhas explicitamente precificadas. Todas as taxas e premissas podem ser revisadas independentemente.
