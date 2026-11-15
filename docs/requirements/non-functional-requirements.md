# Requisitos não funcionais e metas de qualidade

> **Projeto:** CashFlow Architecture  
> **Issue:** #4 | **Milestone:** M1 - Descoberta e requisitos  
> **Situação:** proposta para revisão  
> **Base:** enunciado do desafio de Arquitetura de Soluções e [requisitos funcionais](functional-requirements.md)

## 1. O que precisamos garantir

O sistema permite que o comerciante registre débitos e créditos e consulte o consolidado diário. Além de funcionar corretamente, ele precisa continuar útil quando algum componente falhar e suportar as consultas em períodos de maior movimento.

O enunciado apresenta **dois requisitos não funcionais explícitos**:

1. O serviço de lançamentos **não pode ficar indisponível** se o consolidado diário cair.
2. Em dias de pico, o consolidado recebe **50 requisições por segundo**, com **no máximo 5% de perda de requisições**.

O documento também solicita considerar escalabilidade, resiliência, segurança, desempenho, disponibilidade, documentação e observabilidade. As metas numéricas adicionais abaixo são **propostas para o teste**, não exigências expressas da empresa.

## 2. Requisitos e como verificar

| ID | Requisito | Origem | Como verificar |
| --- | --- | --- | --- |
| **RNF-01** | A indisponibilidade do consolidado não deve interromper o registro de lançamentos. | Exigido | Simular falha do consolidado e executar registros válidos. |
| **RNF-02** | O consolidado deve suportar carga de 50 requisições/segundo em pico. | Exigido | Executar teste de carga com 50 RPS. |
| **RNF-03** | A perda de requisições no consolidado deve ser de no máximo 5% no pico. | Exigido, com interpretação pendente | Medir a proporção de requisições sem resposta útil no teste de carga. |
| **RNF-04** | Um lançamento confirmado deve permanecer registrado, mesmo que o consolidado não esteja disponível. | Proposto como critério de integridade | Registrar dados durante a falha, recuperar o serviço e conferir os lançamentos. |
| **RNF-05** | Repetir o processamento de um lançamento não deve contabilizá-lo duas vezes. | Proposto | Simular reentrega e comparar saldos antes e depois. |
| **RNF-06** | A consulta do consolidado deve ter tempo de resposta previsível. | Proposto | Medir latência em percentis durante a carga. |
| **RNF-07** | O sistema deve disponibilizar informações para detectar e investigar falhas. | Proposto | Verificar logs, indicadores de erro e atraso de atualização utilizando a stack da [ADR-008](../architecture/adr/ADR-008-observability.md), sem confundir telemetria com auditoria financeira. |
| **RNF-08** | Acessos e informações sensíveis devem ser protegidos. | Recomendação arquitetural do desafio, detalhamento proposto | Testes de acesso, verificação de segredos e políticas de armazenamento. |
| **RNF-09** | A solução deve permitir ampliar capacidade sem alteração das regras de negócio. | Recomendação arquitetural do desafio, detalhamento proposto | Explicar estratégia de escala e validar cenários de carga. |

## 3. Metas sugeridas para o MVP

Uma **métrica** é o que medimos; um **SLI** é o indicador que mostra a qualidade percebida; um **SLO** é a meta para esse indicador. As metas desta tabela são sugestões iniciais para o desafio, sujeitas à validação e à capacidade do ambiente de testes.

| Indicador (SLI) | Meta proposta (SLO) | Janela de medição | Evidência |
| --- | --- | --- | --- |
| **Sucesso das consultas** | Pelo menos **95%** das consultas válidas ao consolidado obtêm resposta útil durante 50 RPS | Teste sustentado de **5 minutos**, após aquecimento | Resultado de teste de carga |
| **Tempo de resposta da consulta** | **p95 abaixo de 500 ms** em 50 RPS | Mesmos 5 minutos | Relatório de latências |
| **Continuidade dos lançamentos** | **100% dos registros válidos de teste** seguem sendo aceitos enquanto apenas o consolidado está desligado | Cenário controlado de falha por **2 minutos** | Teste de resiliência e conferência dos registros |
| **Integridade de lançamentos confirmados** | **0 perdas** dos lançamentos confirmados no cenário de teste | Falha e recuperação completas | Contagem, identificadores e conferência da persistência |
| **Processamento sem duplicidade** | **0 duplicidades no saldo** mesmo após reentrega simulada | Teste de repetição de eventos | Comparação dos resultados |
| **Atualização do consolidado** | **95% dos lançamentos refletidos em até 30 s** após registro, em operação saudável | Teste de integração de 5 minutos | Diferença entre horário do registro e atualização |
| **Recuperação após retorno do consolidado** | Fila de lançamentos pendentes processada em até **2 minutos**, para o volume definido no teste | Cenário de recuperação controlada | Medição do backlog e do atraso |

**Observações:** p95 significa que 95% das respostas foram concluídas dentro do tempo indicado. Metas de latência, atraso e recuperação dependem do volume de dados e do ambiente. Por isso não devem ser vendidas como garantias universais ou produção real. O limite de 95% de sucesso está baseado na interpretação inicial de “até 5% de perda” como requisições que não obtêm resposta útil. Essa interpretação precisa ser confirmada.

## 4. Cenários que precisamos testar

### Cenário A: o consolidado fica indisponível

1. Colocar os dois serviços em funcionamento.
2. Interromper **somente** o consolidado diário.
3. Enviar lançamentos válidos ao serviço de lançamentos.
4. Confirmar que esses registros foram aceitos e persistidos.
5. Restabelecer o consolidado e verificar se os dados pendentes são processados.

**Resultado esperado:** o comerciante continua registrando seus movimentos; o relatório pode ficar temporariamente indisponível ou desatualizado, mas os lançamentos confirmados não se perdem.

**Limite da garantia:** isso testa a falha do consolidado, não a indisponibilidade simultânea do banco de lançamentos ou de toda a infraestrutura.

### Cenário B: pico de consultas

1. Preparar dados representativos e aquecer a aplicação.
2. Enviar **50 consultas por segundo** ao consolidado durante 5 minutos (15.000 requisições previstas).
3. Registrar respostas bem-sucedidas, erros, timeouts e latências.
4. Conferir se pelo menos 95% das requisições válidas receberam resposta útil.
5. Registrar os recursos utilizados e as limitações do ambiente.

**Resultado esperado para a meta proposta:** pelo menos 14.250 respostas úteis entre 15.000 requisições planejadas, desde que todas sejam válidas e enviadas. O cálculo deve considerar **requisições efetivamente enviadas**, e não apenas as planejadas.

### Cenário C: processamento repetido

1. Registrar lançamentos conhecidos.
2. Simular que uma informação de lançamento seja entregue novamente ao consolidado.
3. Conferir se o resultado permanece correto.

**Resultado esperado:** o mesmo lançamento não altera o saldo duas vezes.

### Cenário D: recuperação

1. Registrar lançamentos durante a indisponibilidade do consolidado.
2. Restabelecer seu funcionamento.
3. Medir o tempo de processamento dos pendentes e verificar o resultado final.

**Resultado esperado:** atualização eventual, sem perda ou duplicação de registros confirmados.

## 5. O que significa “5% de perda de requisições”?

Essa frase aparece no desafio, mas não explica quais falhas contam como “perda”.

**Interpretação inicial para o MVP:** uma requisição de consulta é considerada sem sucesso quando termina em erro do servidor, timeout ou falta de resposta útil. Entradas inválidas (por exemplo, uma data em formato incorreto) não contam como falha de disponibilidade do serviço.

**Medição proposta:**

```text
Taxa de falha (%) =
    consultas válidas sem resposta útil
    ----------------------------------- x 100
    total de consultas válidas enviadas
```

A empresa deve confirmar se aceita essa interpretação e qual janela de medição deseja. **Perder uma requisição de consulta não significa perder um lançamento financeiro.** Para registros confirmados, propomos tolerância zero a perda de dados no teste.

## 6. Segurança, operação e limites da solução

Para a arquitetura alvo, vamos detalhar na Milestone M2:

- **Segurança:** acesso autorizado, TLS, gestão de segredos, proteção de dados armazenados, logs sem exposição desnecessária e avaliação de LGPD.
- **Operação:** alertas de erro, consumo de recursos, atraso do consolidado e rastreabilidade de uma operação.
- **Escalabilidade:** possibilidade de aumentar a capacidade de consulta independentemente do registro de lançamentos.
- **Recuperação:** retomada após falhas e tratamento de mensagens pendentes.

A execução local com Docker Compose servirá para demonstrar os comportamentos. Para observabilidade, a [ADR-008](../architecture/adr/ADR-008-observability.md) aprovou OpenTelemetry Collector, Prometheus, Loki, Tempo e Grafana. Os dashboards ajudam a investigar, mas a medição de 50 RPS e de até 5% sem resposta útil deve continuar apoiada no relatório do gerador de carga. A implantação da stack ainda não foi validada. Alta disponibilidade de infraestrutura, metas anuais de uptime, recuperação de desastres e conformidade formal exigem informações e recursos que **não foram fornecidos**; não serão presumidos como implementados.

## 7. Pontos pendentes

1. Qual a definição oficial de “perda de requisições” e a janela da meta de 5%?
2. O consolidado aceita dados temporariamente desatualizados? Por quanto tempo?
3. Qual volume de lançamentos e tamanho do histórico devem ser considerados?
4. Há metas corporativas de disponibilidade, retenção, recuperação (RTO/RPO) ou segurança?
5. Os testes precisam considerar uma cloud específica ou somente o ambiente local?

Se não houver respostas, registraremos explicitamente as premissas do MVP. Nenhuma meta proposta será apresentada como exigência confirmada.

## 8. Checklist da Issue #4

- [x] Requisitos não funcionais identificados e numerados.
- [x] Exigências explícitas separadas de metas sugeridas.
- [x] Indicadores, metas, janelas de medição e evidências definidos.
- [x] Cenários de falha e 50 RPS detalhados.
- [x] Documento versionado no repositório.
- [ ] Revisão e validação das metas propostas.

**Próximo passo:** Issue #5, priorização de requisitos, premissas e riscos.
