# Plano para orçamento total: LOCAL, DEV, HOM e PROD, AKS versus EKS

> **Revisão:** 2026-11-15 | **Status:** orçamento-base Azure aceito na ADR-009; valores reais sujeitos a consumo e validação  
> **ADR relacionada:** [ADR-009](adr/ADR-009-deployment-cloud.md)  
> **Ambientes:** DEV e HOM **isolados em clusters, bancos, segredos e redes**, não compartilhar cluster por economia.  
> **Premissas:** USD, East US Azure / us-east-1 AWS, 730 h/mês e preços sob demanda, sem desconto contratual.

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

## Decisão de isolamento e equivalência

- **LOCAL:** Docker Compose no computador; custo de cloud zero neste modelo, sem ignorar computador/energia/tempo.
- **DEV:** cluster Kubernetes e PostgreSQL separados, configuração mínima, observabilidade de retenção reduzida; testes funcionais. Base: 1 nó 4 vCPU / 16 GiB, sujeita a benchmark de capacidade.
- **HOM:** cluster Kubernetes e PostgreSQL **separados de DEV e PROD**. Normalmente 1 nó 4 vCPU / 16 GiB, e **permanece sem redimensionamento no orçamento aprovado**. Homologação funcional e de deploy não comprova a resiliência nem o desempenho de PROD; testes específicos devem ser planejados com capacidade adequada.
- **PROD:** 3 nós de 4 vCPU / 16 GiB inicialmente, distribuir workloads entre AZs, minReplicas 2 para APIs e NGINX; HPA/Cluster Autoscaler, PDB, probes e backup. **Não declarar HA concluída** sem banco HA, quorum RabbitMQ, volumes e teste de perda de nó/zona.
- DEV e HOM podem usar **AKS Free**, sem SLA de control plane; EKS cobra o plano de controle por cluster. Produção deve usar **AKS Standard** ou **EKS** (suporte padrão).
- Bancos lógicos Transactions, Consolidation e Keycloak precisam de segregação por ambiente. Um servidor gerenciado por ambiente é hipótese inicial, não segregação física por domínio.

## Correção essencial de HA do banco no Azure

**PostgreSQL Burstable B2ms NÃO suporta HA zone-redundant.** Portanto os números anteriores de PROD com B2ms não modelavam HA. Para um cenário preliminar **General Purpose D2s v3 (2 vCPU / 8 GiB)** custa **US$ 124,83/mês por instância** conforme página de preços Azure. Configuração zone-redundant usa primary + standby e armazenamento para ambos. Com **32 GiB por servidor** a US$ 0,115/GiB-mês: **US$ 249,66 de compute + US$ 7,36 de armazenamento = US$ 257,02/mês**, ainda sem adicionais de backup, IOPS e rede. **Confirmar tipo exato de SKU e elegibilidade regional na calculadora.** Fonte: [Azure PostgreSQL pricing](https://azure.microsoft.com/en-us/pricing/details/postgresql/flexible-server/), [restrições de HA](https://learn.microsoft.com/en-us/azure/postgresql/high-availability/concepts-high-availability).

Para **AWS RDS PostgreSQL Multi-AZ**, configurar a opção com standby (uma réplica standby) na AWS Pricing Calculator. O cenário inferior abaixo reserva provisoriamente **2 × US$ 94,17/mês de compute = US$ 188,34/mês** como *proxy de planejamento*, mas **não é tarifa RDS Multi-AZ confirmada**. Armazenamento, backups, IOPS e a precificação específica da opção Multi-AZ devem ser adicionados. Fonte: [RDS pricing](https://aws.amazon.com/rds/postgresql/pricing/).

## Estudo histórico anterior: subtotais parciais AKS versus EKS (substituídos pelo orçamento Azure aprovado)

| Ambiente | Azure AKS | Amazon EKS | Premissa |
| --- | ---: | ---: | --- |
| LOCAL | US$ 0,00 cloud | US$ 0,00 cloud | Docker Compose |
| DEV | US$ 161,78 | US$ 247,22 | 1 nó, PostgreSQL pequeno Single-AZ, PV básico, object storage reduzido |
| HOM em regime econômico | US$ 161,78 | US$ 247,22 | 1 nó, sem HA permanente, testar a configuração equivalente a PROD após scale-up |
| PROD **corrigido para banco HA** | **US$ 769,99** | **US$ 717,14 (proxy)** | Azure: substitui B2ms pelo D2s v3 zone-redundant; AWS: 2×compute RDS como proxy, sem RDS storage |
| **TOTAL BASE 24x7, três ambientes cloud** | **US$ 1.093,55** | **US$ 1.211,58 (proxy)** | **Ainda não é total faturável!** |

**Conferência das fórmulas:** Azure PROD = 615,93 - 99,28 - 3,68 + 249,66 + 7,36 = **769,99**. AWS PROD = 622,97 - 94,17 + 188,34 = **717,14**. Totais: Azure 161,78 × 2 + 769,99 = 1.093,55; AWS 247,22 × 2 + 717,14 = 1.211,58. Valores arredondados em centavos; as premissas são distintas de HA real do broker e storage.

### Como interpretar o TOTAL corretamente

Estes são **totais da base de recursos quantificados, não orçamento completo**. Ausências impedem afirmar o total de cloud ou o vencedor:

| Item ainda a cotar | DEV | HOM | PROD | Dado necessário |
| --- | --- | --- | --- | --- |
| LB/Ingress/IPv4 | Sim | Sim | Sim | Privado ou público? 1 LB ou múltiplos; consumo LCU/bytes; DNS/TLS |
| NAT, gateways/egress e endpoints | Sim | Sim | Sim, multi-AZ | Quantidade de NAT por AZ, GB processados e egress de internet |
| RDS volume gp3 e backups | AWS | AWS | AWS Multi-AZ | GB, IOPS, retenção, snapshots |
| Azure DB backups extras e IOPS | Azure | Azure | Azure | Retenção, GiB excedente, storage, IO |
| PVCs de broker, Prometheus e observabilidade | Sim | Sim | Sim | Número de PVCs, GiB, classe, IOPS, snapshots; 128 GiB por pool é apenas placeholder |
| Loki/Tempo object storage | Sim | Sim | Sim | GB/dia, retenção, PUT/GET/lifecycle e requests |
| Registry, Key Vault/Secrets Manager, DNS | Sim | Sim | Sim | Imagens, GB-mês, operações e segredos por ambiente |
| HA do RabbitMQ e N+1 | Não em regime normal | Habilitar ao simular PROD | Sim | 3 membros com quorum, volumes separados, réplicas e reserva de nós |
| HOM em modo espelho PROD | Conforme testes | **acréscimo temporário** | N/A | Dias/mês com 3+ nós, DB HA, réplica de broker |
| Suporte, monitoração adicional, impostos | Sim | Sim | Sim | Contratos, política corporativa e modalidade de cobrança |

Não preencher lacunas com zero. Para comparação simétrica, decidir a **mesma topologia** de exposição pública/privada e política de backup por provedor. As horas do cluster em HOM normal e em homologação HA devem ser computadas separadamente; ainda que reduza nós, o controle EKS continua cobrado.

## Modelo de reestimativa de custo operacional

Para cada ambiente e provedor, anotar:

```text
CUSTO MENSAL FINAL = nodes 24x7 (e node-hours extras de HOM)
                    + plano de controle Kubernetes
                    + PostgreSQL compute + storage + HA + backups + IOPS
                    + discos PV + snapshots + object storage + requests
                    + ingress/load balancer + IPs + NAT/endpoints + egress
                    + registry + secrets + DNS + certificados cobrados
                    + suporte / ferramentas cobradas
                    + impostos e contratos, se aplicáveis
```

**Passos para acompanhar o orçamento aceito e validar a implantação:**
1. Preservar a estimativa da **[Azure Pricing Calculator](https://azure.microsoft.com/en-us/pricing/calculator/)** para DEV, HOM econômico, PROD e SHARED; usar o estudo EKS apenas como histórico comparativo.
2. Exportar e versionar valores, região e recursos cotados; testar as premissas de capacidade em cada ambiente sem presumir scale-up obrigatório para HOM.
3. Cotar **Azure Standard Load Balancer/NAT** e **AWS ALB/NAT**, storage, backups, secrets e saída de internet por ambiente; nunca usar custo zero por falta de dado.
4. Validar Kubernetes requests/limits e capacidade de node failure (N+1), observabilidade e RabbitMQ quorum após teste de 50 RPS.
5. Registrar totais por ambiente, **total mensal**, **anual × 12**, impostos/FX se apresentar BRL e uma margem de incerteza **separada** da cobrança estimada.
6. Revisar ADR-009 quando custo real, requisitos de segurança, HA e resiliência justificarem alteração do desenho; a escolha AKS e o baseline já estão aprovados.

## Fontes

- https://azure.microsoft.com/en-us/pricing/details/postgresql/flexible-server/
- https://learn.microsoft.com/en-us/azure/postgresql/high-availability/concepts-high-availability
- https://learn.microsoft.com/en-us/azure/aks/free-standard-pricing-tiers
- https://aws.amazon.com/rds/postgresql/pricing/
- https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/USER_OnDemandDBInstances.html
- https://aws.amazon.com/eks/pricing/
- https://azure.microsoft.com/en-us/pricing/calculator/
- https://calculator.aws/
- [Rateio anterior e comparação detalhada](aks-eks-cost-breakdown.md)
