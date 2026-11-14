# Priorização de requisitos, premissas e riscos

> **Projeto:** CashFlow Architecture  
> **Issue:** #5 | **Milestone:** M1 - Descoberta e requisitos  
> **Situação:** proposta para revisão  
> **Referências:** [Requisitos funcionais](functional-requirements.md), [Requisitos não funcionais](non-functional-requirements.md) e [domínios de negócio](../domain/business-capabilities-and-context-map.md)

## 1. Por que este documento existe

Precisamos decidir o que é essencial para demonstrar o fluxo de caixa, o que pode ser acrescentado depois e quais problemas merecem atenção antes de implementar.

O desafio exige registro de débitos/créditos e consolidado diário. Também exige que uma falha no consolidado não interrompa o registro e que o consolidado suporte pico de 50 requisições por segundo com no máximo 5% de perda de requisições. **Não foram informados** sistemas legados, integrações externas, cloud obrigatória nem regras completas de cálculo de saldo.

Este documento apresenta uma **priorização proposta**, não um acordo já validado com a empresa.

## 2. Prioridade dos requisitos

Vamos usar a classificação **MoSCoW**:
- **Must Have:** indispensável na entrega.
- **Should Have:** importante; priorizar se não comprometer o essencial.
- **Could Have:** desejável, mas adiável.
- **Won't Have (neste MVP):** fora do escopo de implementação inicial, podendo ser reavaliado.

| Prioridade | Requisito | Motivo |
| --- | --- | --- |
| Must Have | RF-01 Registrar crédito | Exigência do desafio. |
| Must Have | RF-02 Registrar débito | Exigência do desafio. |
| Must Have | RF-03 Consultar consolidado diário | Exigência do desafio; regra exata de saldo pendente. |
| Must Have | RF-04 Validar lançamentos | Proposta para evitar registros inválidos. |
| Must Have | RNF-01 Continuar registrando lançamentos se o consolidado cair | Exigência expressa. |
| Must Have | RNF-02 Suportar 50 RPS no consolidado | Exigência expressa. |
| Must Have | RNF-03 No máximo 5% de perda de requisições | Exigência expressa, interpretação a validar. |
| Must Have | RNF-04 Não perder lançamentos confirmados; RNF-05 não contabilizar em dobro | Propostas para proteger a integridade financeira. |
| Should Have | RF-05 Consultar histórico de lançamentos | Apoia conferência e investigação; não é exigência expressa. |
| Should Have | RF-07 Trilha de auditoria financeira | Proposta para rastrear ações sobre dados financeiros; pode ser implementada parcialmente no MVP. |
| Should Have | RNF-07 Observabilidade e RNF-08 proteção de dados/acessos | Facilitam operação segura e avaliação da arquitetura. |
| Should Have | RNF-06 Latência previsível e RNF-09 evolução de capacidade | Metas e medidas propostas, a testar. |
| Could Have | RF-06 Estorno de lançamentos | Importante na operação real, mas depende de regras não fornecidas. |
| Won't Have neste MVP | Integração com sistemas legados e múltiplas moedas | Não solicitadas nem caracterizadas; avaliar somente se surgirem requisitos. |

**Limite importante:** o fato de RF-05 ou RF-06 não serem essenciais para o teste não significa que seriam opcionais em uma operação financeira real. Essa decisão precisaria ser reavaliada com o negócio.

## 3. Premissas e dúvidas

Premissa é uma hipótese adotada para seguir com o desenho quando não há informação suficiente. **Não é requisito confirmado.**

| ID | Premissa proposta / dúvida | Impacto | Quando resolver |
| --- | --- | --- | --- |
| PRE-01 | Crédito aumenta e débito reduz o saldo, por convenção inicial. | Regras de cálculo. | Antes da implementação. |
| PRE-02 | O consolidado diário poderá significar movimento líquido ou saldo acumulado; ainda não decidido. | Cálculo e testes. | Antes da implementação. |
| PRE-03 | Saldo inicial não foi informado. | Resultado do consolidado. | Antes da implementação. |
| PRE-04 | Data de referência e fuso horário ainda não definidos. | Agrupamento por dia. | Antes da implementação. |
| PRE-05 | O enunciado não especifica se existe um ou vários comerciantes. | Identificação e segregação de dados. | Antes do modelo de dados. |
| PRE-06 | “Até 5% de perda de requisições” será inicialmente medido como consultas válidas sem resposta útil; definição oficial pendente. | Teste de carga e aceite. | Antes do teste de carga. |
| PRE-07 | A consulta do consolidado pode apresentar dados temporariamente desatualizados; limite tolerável pendente. | Consistência e experiência do usuário. | Antes de fechar a arquitetura. |
| PRE-08 | Não há sistema legado descrito; não presumir migração. | Arquitetura de transição e custos. | Somente se houver contexto adicional. |
| PRE-09 | Cloud e linguagem não são impostas pelo desafio; linguagem deve ser justificada. | Escolha tecnológica e custos. | Na Milestone M2. |
| PRE-10 | Regras de correção/estorno, moeda, precisão e retenção ainda não foram definidas. | Integridade e auditoria. | Antes de implementar tais comportamentos. |
| PRE-11 | Registro de auditoria financeira é uma proteção proposta, não exigência expressa; eventos críticos devem ser íntegros e ter acesso controlado. | Modelo de dados, privacidade e custo operacional. | Issues #7 e #9. |

**Decisões pendentes da Issue #3:** definir modalidade de saldo diário, estorno no MVP e consulta de histórico. Enquanto não forem aprovadas, essas opções permanecem abertas.

## 4. Registro e classificação de riscos

Escala **qualitativa e inicial**:
- Probabilidade: baixa / média / alta.
- Impacto: baixo / médio / alto / crítico.
- Prioridade: crítica / alta / média / baixa, definida pela combinação de impacto, probabilidade e criticidade do negócio.

Não existe histórico operacional para estimar probabilidades reais. As classificações devem ser revisitadas após testes.

| ID | Risco | Prob. | Impacto | Prioridade |
| --- | --- | --- | --- | --- |
| R-01 | Perda de lançamento já confirmado. | Média | Crítico | Crítica |
| R-02 | Falha no consolidado impedir novos registros. | Média | Crítico | Crítica |
| R-03 | Contabilização duplicada após repetição de processamento. | Alta | Alto | Alta |
| R-04 | Consolidado apresentar saldo desatualizado ou divergente. | Alta | Alto | Alta |
| R-05 | Consultas falharem além do limite de 5% no pico de 50 RPS. | Média | Alto | Alta |
| R-06 | Exposição indevida de dados financeiros ou pessoais. | Média | Alto | Alta |
| R-07 | Recuperação de falha exigir intervenção manual excessiva. | Média | Médio | Média |
| R-08 | Complexidade de implantação e manutenção acima da necessidade do negócio. | Média | Médio | Média |
| R-09 | Auditoria incompleta, alterável ou com exposição desnecessária de dados. | Média | Alto | Alta |

### Como pretendemos reduzir os riscos

| Risco | Mitigação proposta | Responsável por função (proposto) | Risco residual esperado* |
| --- | --- | --- | --- |
| R-01 | Persistência transacional, publicação confiável, reconciliação e testes de falha. | Engenharia / Arquitetura | Baixo a médio |
| R-02 | Evitar dependência síncrona do consolidado no registro; testar desligamento do serviço. | Arquitetura / Engenharia | Baixo |
| R-03 | Identificador único de lançamento, processamento idempotente e teste de reentrega. | Engenharia | Baixo |
| R-04 | Monitorar atraso e divergência; possibilitar reprocessamento/reconciliação. | Engenharia / Operação | Médio |
| R-05 | Teste de 50 RPS, capacidade dimensionável e limites monitorados. | Engenharia / Operação | Médio |
| R-06 | Autenticação/autorização, criptografia, segregação de acesso e minimização de dados. | Segurança / Engenharia | Médio |
| R-07 | Procedimentos de recuperação, métricas, alertas e testes operacionais. | Operação / Engenharia | Médio |
| R-08 | MVP mínimo, ADRs sobre alternativas e avaliação de custo/complexidade. | Arquitetura | Baixo a médio |
| R-09 | Registros de auditoria append-only, privilégios mínimos, integridade, retenção e proteção de dados sensíveis. | Segurança / Engenharia | Médio |

*Risco residual **estimado**, condicionado à implementação e à verificação dos controles. As funções são sugestões de responsabilidade, não pessoas ou equipes designadas pela empresa.*

## 5. Impacto nas áreas e na operação

| Área ou público | Possível impacto da solução | O que precisará ser combinado |
| --- | --- | --- |
| **Comerciante** | Poderá registrar lançamentos mesmo se a consulta do consolidado estiver indisponível; o saldo poderá apresentar atraso temporário. | Como indicar relatório desatualizado e qual atraso é aceitável. |
| **Suporte / atendimento** | Precisará investigar diferenças de saldo e falhas de consulta. | Dados de diagnóstico, procedimentos de atendimento e canais de escalonamento. |
| **Operações / infraestrutura** | Precisará acompanhar saúde dos serviços, fila de processamento, capacidade e recuperação. | Alertas, responsáveis operacionais, backups e procedimento de recuperação. |
| **Segurança / privacidade** | Precisará avaliar acesso, proteção, retenção e eventual presença de dados pessoais. | Classificação de dados, exigências legais e controles de LGPD aplicáveis. |
| **Gestão de dados** | Precisará esclarecer qual registro é a fonte confiável e como reconstruir o consolidado. | Regras de integridade, auditoria, reconciliação e retenção. |

Essas áreas são **stakeholders potenciais**, não uma descrição da estrutura da empresa. Não presumimos banco legado ou integrações corporativas já existentes.

## 6. Restrições e dependências

- **Restrições confirmadas:** dois serviços de negócio, isolamento da disponibilidade de lançamentos, 50 RPS no consolidado, até 5% de perda de requisições; documentação, testes, execução local e repositório público exigidos no desafio.
- **Dependência de negócio:** esclarecer saldo, datas, estorno, moeda e escopo de comerciante para concluir regras de cálculo e persistência.
- **Dependência técnica:** definir mecanismo de desacoplamento, estratégia de integridade, segurança e teste na Milestone M2.
- **Limite da demonstração:** a prova local valida cenários controlados; não demonstra por si só disponibilidade contínua em produção ou conformidade formal com LGPD.

## 7. Critérios para avançar à arquitetura

Antes de consolidar ADRs e o modelo de dados:
1. Adotar explicitamente, como premissas do MVP, as regras de saldo e data ainda não confirmadas.
2. Identificar quais riscos têm mitigação implementada e quais terão apenas proposta documentada.
3. Garantir que toda decisão técnica importante esteja vinculada a um requisito ou risco identificado.
4. Manter requisitos não confirmados claramente separados das exigências do desafio.

## 8. Checklist da Issue #5

- [x] Requisitos priorizados com MoSCoW e justificativas.
- [x] Premissas, restrições, dependências e dúvidas listadas.
- [x] Riscos avaliados com probabilidade, impacto, prioridade, mitigação e risco residual estimado.
- [x] Responsabilidades e impactos operacionais propostos.
- [x] Documento versionado no repositório.
- [ ] Revisão final e validação das premissas antes do encerramento da Issue.

**Próximo passo após aprovação:** completar as decisões de negócio pendentes na Issue #3 e iniciar a arquitetura alvo na Milestone M2.
