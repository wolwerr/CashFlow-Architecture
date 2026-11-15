# Documentação da arquitetura CashFlow

> **Issue #6 · Milestone M2** | Arquitetura proposta para revisão

A documentação foi dividida por objetivo para facilitar a leitura sem duplicar informações.

| Documento | O que apresenta |
| --- | --- |
| **[Visão de Blocos de Construção (Building Block View)](building-block-view.md)** | Estrutura estática da solução: C4 Nível 1 e 2, responsabilidades, protocolos propostos e futura visão de componentes. |
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

## Decisões pendentes

- **Issue #3:** definição da regra financeira do saldo diário, histórico e correção/estorno.
- **Issue #7:** ADRs para linguagem, banco de dados, mensageria, autenticação e observabilidade.
- **Issue #8:** política de falhas, retentativas, DLQ e reconciliação.
- **Issue #9:** controles de segurança, retenção, acesso à auditoria e proteção de dados.
- **Issue #10:** custo, implantação e impactos operacionais.

A descrição da arquitetura não comprova, por si só, implementação ou execução de testes. A Issue #6 foi concluída. Após a [ADR-008](adr/ADR-008-observability.md), o C4 Nível 2 passou a mostrar a stack de observabilidade escolhida (OpenTelemetry Collector, Prometheus, Loki, Tempo e Grafana). É necessário **regenerar `images/c4-containers.png`** para refletir a fonte PlantUML atualizada.
