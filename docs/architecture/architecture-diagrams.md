# Documentação da arquitetura CashFlow

> **Issue #6 · Milestone M2** | Arquitetura proposta para revisão

A documentação foi dividida por objetivo para facilitar a leitura sem duplicar informações. **Fase atual: implementação e testes somente LOCAL com Docker Compose.** Azure AKS/DEV/HOM/PROD e seus custos são planejamento futuro. As decisões e os diagramas devem ser confrontados com a [matriz de requisitos do desafio](../requirements/challenge-compliance-matrix.md); testes de execução são documentados separadamente.

| Documento | O que apresenta |
| --- | --- |
| **[Decisões consolidadas do MVP](mvp-decisions.md)** | 24 decisões aprovadas para regras financeiras, escopo, IAM, contratos, qualidade e operação; distinção entre decisão e comprovação. |
| **[Requisitos e atendimento do desafio](../requirements/challenge-compliance-matrix.md)** | Rastreabilidade dos requisitos, decisões, implementação e evidências ainda necessárias. |
| **[Visão de Blocos de Construção (Building Block View)](building-block-view.md)** | **C4 Níveis 1, 2 e 3 em uma página**, com diagramas, responsabilidades, protocolos, portas, componentes e justificativas técnicas. |
| **[Deployment View](deployment-view.md)** | Implantação de LOCAL, DEV, HOM, PROD e SHARED; visão Azure e capacidades equivalentes em AWS/GCP. |
| **[Diagramas de Sequência](sequence-diagrams.md)** | Interações detalhadas para registro, falha do consolidado e consulta. |
| **[Casos de Uso (Use Case View)](use-cases.md)** | Ações do comerciante, fluxos principais e alternativas, com requisitos confirmados e propostas separados. |
| **[Modelo de Dados (Data Model View)](data-model.md)** | Entidades por domínio, integridade, Outbox, idempotência e trilha de auditoria financeira. |

## Arquivos editáveis e imagens

- [Fontes PlantUML](plantuml/) mantidas separadamente das imagens.
- [Imagens renderizadas](images/) incorporadas nos documentos correspondentes.

Para gerar imagens a partir dos arquivos editáveis, utilize PlantUML em ambiente local:

```bash
plantuml -tpng docs/architecture/plantuml/*.puml
```

O comando gera PNGs na pasta das fontes; para atualizar as imagens referenciadas, mova ou copie os PNGs para `docs/architecture/images/`. Os arquivos PlantUML são a referência de edição. Renderizações devem ser atualizadas sempre que as fontes mudarem.

