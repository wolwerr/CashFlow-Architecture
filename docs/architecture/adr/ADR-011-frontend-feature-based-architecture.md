# ADR-011: Arquitetura modular por funcionalidades no frontend React

## 1. Contexto

A [ADR-007](ADR-007-clients-api-gateway.md) já definiu **React + TypeScript** para a interface Web do CashFlow. Esta ADR **não reabre a escolha do framework**; decide **como organizar o código-fonte do frontend**.

O MVP local passou a incluir login OIDC/PKCE com Keycloak, dois contextos de acesso (ADMIN da plataforma e COMERCIANTE/OPERATOR), cadastro de comerciantes e usuários, registro de créditos/débitos, saldo diário com consistência eventual e histórico paginado.

A implementação inicial concentrou estado, efeitos de autenticação, integração HTTP, validações e renderização em um `App.tsx` grande. A expansão aumentou a complexidade cognitiva, dificultou testes isolados e produziu falhas no gate do Sonar. O frontend precisa crescer de forma compreensível, sem aplicar automaticamente todas as camadas da Clean Architecture do backend a uma SPA ainda pequena.

**Escopo:** organização interna do cliente React, dependências, composição de telas e testabilidade. Autenticação/autorização, disponibilidade das APIs, mensageria, persistência e deployment continuam regidos pelas ADRs relacionadas.

## 2. Alternativas e trade-offs

| Alternativa | Benefícios | Custos / riscos | Avaliação |
| --- | --- | --- | --- |
| **1. Componentes e lógica centralizados em `App.tsx` (estrutura simples por tipo de arquivo)** | Menos arquivos, navegação inicial rápida e baixa configuração | Componente crescente, estados e efeitos acoplados, manutenção difícil, testes frágeis e complexidade cognitiva elevada | Não escolhida |
| **2. Arquitetura modular por funcionalidades (*feature-based*) com componentes, hook de orquestração e camada de API** | Coesão por domínio funcional, divisão incremental, componentes testáveis, dependências fáceis de compreender e uso de React/TypeScript sem novas bibliotecas | Mais arquivos e necessidade de convenções claras; um hook central pode voltar a crescer se responsabilidades não forem extraídas | **Proposta escolhida** |
| **3. Clean Architecture completa no frontend (Domain/Application/Infrastructure/Presentation, portas e adaptadores)** | Fronteiras formais, inversão de dependências e potencial isolamento de regras complexas | Muitas interfaces e abstrações para formulários e consultas simples, duplicação de conceitos do backend e maior custo inicial de manutenção | Não escolhida para o MVP |

**Observação:** modularizar por funcionalidade não dispensa regras de dependência nem testes. Também não implica que regras financeiras ou de autorização possam ser confiadas ao navegador. As APIs continuam responsáveis por validar lançamentos e permissões.

## 3. Decisão registrada

Adotar **Feature-Based Architecture** no frontend **React + TypeScript**, com separação explícita entre:

1. **Composição da aplicação (`App.tsx`):** monta o layout e escolhe a visão conforme permissões retornadas pela API. Não implementa HTTP, cálculos financeiros ou provisionamento de usuários.
2. **Funcionalidades (`features/`):** agrupam interfaces e comportamento por responsabilidade de negócio. No MVP, `auth`, `merchants` e `finance`.
3. **Orquestração (`features/app/useCashFlowController.ts`):** coordena sessão, seleção do comerciante, ações, carregamentos e estado compartilhado entre as telas. É um hook React, não um novo serviço ou backend.
4. **Integração (`api/` e `auth/`):** contratos HTTP, tratamento de erros, normalização monetária, consulta da projeção assíncrona e implementação de OIDC/PKCE. Componentes de apresentação não devem construir requisições HTTP diretamente.

### Direção das dependências

```text
App.tsx (composition root)
      |
      +--> features/auth      (componentes de sessão)
      +--> features/merchants (gestão administrativa)
      +--> features/finance   (lançamento, saldo, histórico)
      |
      +--> features/app/useCashFlowController
                    |
                    +--> api/       (contratos e integração HTTP)
                    +--> auth/      (OIDC / PKCE)
                    +--> time.ts    (data de referência)
```

O desenho representa **dependências de código**, não chamadas de rede. Os componentes podem receber propriedades tipadas do controller para exibição e ações; as APIs não importam componentes React. **Não introduzir Redux, microfrontends ou um framework adicional de estado** no MVP sem necessidade demonstrada.

### Estrutura inicial

```text
Frontend/src/
  App.tsx
  features/
    app/
      useCashFlowController.ts
    auth/
      AppHeader.tsx
      WelcomePanel.tsx
    merchants/
      AdminPanel.tsx
    finance/
      FinancialDashboard.tsx
      TransactionForm.tsx
      BalancePanel.tsx
      HistoryPanel.tsx
  api/
    cashflow.ts
    brl.ts
    projection.ts
  auth/
    oidc.ts
  time.ts
```

**Status da implementação:** a estrutura está no [PR #45](https://github.com/wolwerr/CashFlow-Architecture/pull/45) e será integrada à `main` por merge separado. **A aprovação desta ADR não atesta, por si, o merge ou a execução local das duas novas visões.** O detalhamento técnico e as convenções de manutenção ficam em [`Frontend/ARCHITECTURE.md`](../../../Frontend/ARCHITECTURE.md), disponibilizado pelo PR #45.

## 4. Regras de arquitetura e limites

| Responsabilidade | Onde deve ficar | O que evitar |
| --- | --- | --- |
| Composição do layout e seleção da visão | `App.tsx` | Chamadas HTTP, regras monetárias e manipulação direta de grupos |
| Autenticação visual e boas-vindas | `features/auth` | Armazenar tokens no DOM ou em `localStorage` |
| Gestão de comerciante e usuários | `features/merchants` | Chamar diretamente a Admin API do Keycloak ou expor client secret |
| Lançamento, saldo e histórico | `features/finance` | Replicar invariantes financeiras do backend como única validação |
| Estado e fluxos entre telas | Hooks de `features/app` | Efeitos não relacionados distribuídos arbitrariamente entre componentes |
| HTTP, contratos e tratamento de erros | `api/` | Imports de componentes React e lógica visual |
| OIDC Authorization Code + PKCE | `auth/oidc.ts` | Criar mecanismo de login próprio ou persistir tokens indiscriminadamente |

- **ADMIN e COMERCIANTE:** separar as funcionalidades visíveis conforme perfil, mas exigir autorização **nas APIs** em toda operação. Um `canManageMerchants` enviado pelo backend orienta a interface, não substitui verificação no endpoint.
- **Cadastro:** somente ADMIN de bootstrap pode criar comerciantes no ambiente local; a conta COMERCIANTE é vinculada à loja como OPERATOR, sem acesso administrativo. O Keycloak permanece fonte de usuários, senhas e grupos, conforme ADR-006.
- **BRL:** a interface aceita valores em convenção brasileira e os normaliza em string para o contrato decimal invariante; não usar operações de ponto flutuante do JavaScript para saldos financeiros.
- **Consistência eventual:** HTTP 201 indica lançamento registrado, não saldo projetado. O polling de Consolidation é limitado, não deve repetir POST financeiro, e a UI deve distinguir projeção pendente de erro de lançamento.
- **Estado:** usar hooks React enquanto o escopo for pequeno. Se a orquestração central concentrar fluxos demais, extrair hooks coesos (`useSession`, `useMerchants`, `useTransactions`) e testar separadamente, antes de adotar biblioteca global.

## 5. Consequências positivas

- `App.tsx` torna-se uma composição pequena, reduzindo a complexidade cognitiva.
- Alterações no formulário financeiro não exigem editar o código da gestão de comerciantes.
- Reutilização das integrações e das regras de formatação por componentes distintos.
- Testes unitários e de componentes podem focar fluxos específicos, inclusive regressões de crédito/débito, autorização e atualização eventual do saldo.
- A organização acompanha o vocabulário de negócio sem criar abstrações que ainda não têm utilidade.
- Não há novas dependências de estado, roteamento ou frameworks para sustentar o MVP.

## 6. Consequências negativas e riscos

| Risco | Consequência | Mitigação |
| --- | --- | --- |
| Hook de orquestração cresce sem limites | Novo componente central difícil de testar e manter | Extrair hooks por funcionalidade quando surgirem responsabilidades independentes |
| Passagem de controller por propriedades | Componentes podem conhecer estados de que não precisam | Usar interfaces de props menores na evolução e preservar testes de cada componente |
| Duplicação de estado entre módulos | Consulta desatualizada ou inconsistência de seleção | Definir uma fonte de verdade para merchant/sessão e limpar dados ao mudar contexto |
| Ocultar botões confundido com autorização | Acesso indevido via chamadas HTTP diretas | Manter JWT e autorização por merchant em cada API, ADR-006 |
| Extração excessiva de componentes | Arquivos sem coesão e mais navegação | Extrair por responsabilidade real; não criar interface e camada para cada input |
| Assincronia do Consolidation | Histórico mostra débito antes do saldo | Polling limitado, mensagem explícita e testes de consistência eventual |

## 7. Qualidade, operação e verificação

- CI com **build TypeScript/Vite**, testes de frontend e cobertura conforme política do repositório e ADR-010. Não diminuir as regras de Sonar para justificar a organização escolhida.
- Testes de lógica pura da normalização BRL e de polling devem ser independentes de APIs reais. Validar `100`, `100,00` e `1.234,56`, com conversão adequada ao contrato.
- Testes de componentes cobrem disponibilidade das seções por perfil, cadastro de comerciante, usuário já existente, campos inválidos, registro de crédito e débito, histórico vazio e paginação.
- Testes de integração/E2E local devem comprovar que COMERCIANTE não consegue acessar endpoints administrativos, mesmo manipulando requisições HTTP.
- As APIs continuam validando JWT, vínculo ao `merchant_id`, valores, idempotência e transições; não duplicar essas garantias apenas no React.
- **Escopo operacional atual:** `npm run build`, `npm run test:coverage` e Docker Compose local. Azure, BFF e gateway dedicado continuam etapas futuras conforme ADR-007/ADR-009.

## 8. Critérios de aceite da decisão

1. `App.tsx` atua como composition root, sem chamadas HTTP diretas nem lógica de negócio financeira.
2. As funcionalidades de autenticação, comerciantes e finanças estão separadas em módulos próprios e com imports explícitos.
3. Componentes de apresentação não importam credenciais nem chamam a Admin API do Keycloak.
4. O perfil ADMIN visualiza cadastro e consulta financeira; COMERCIANTE visualiza somente lançamento, saldo diário e histórico, com controles protegidos por backend.
5. Cadastro e login do usuário da loja não permitem autoatribuir privilégios ADMIN.
6. Crédito de R$ 100,00 e débito de R$ 25,00 resultam em R$ 75,00 após a consolidação, sem publicação financeira duplicada para forçar atualização visual.
7. Testes e quality gates passam na CI; login e fluxo financeiro dos dois perfis são verificados em Docker Compose local.

## 9. Quando revisitar

Revisar se o frontend ganhar muitos fluxos independentes, times autônomos, regras complexas específicas do cliente ou múltiplos canais; se o hook principal voltar a concentrar responsabilidades; se houver evidência concreta de necessidade de roteamento estruturado, biblioteca de cache/estado ou adoção de BFF. **Não migrar automaticamente para Clean Architecture completa nem para microfrontends apenas por crescimento no número de arquivos.**

## 10. Referências

- [ADR-002: Clean Architecture no backend](ADR-002-architecture-style-clean-architecture.md)
- [ADR-006: Keycloak e autorização](ADR-006-identity-and-access.md)
- [ADR-007: React + TypeScript e sessão Web](ADR-007-clients-api-gateway.md)
- [ADR-010: GitHub Actions](ADR-010-ci-cd-github-actions.md)
- [Documentação da arquitetura frontend](../../../Frontend/ARCHITECTURE.md), criada no PR #45
- [PR #45: Separação ADMIN/COMERCIANTE e refatoração do frontend](https://github.com/wolwerr/CashFlow-Architecture/pull/45)
