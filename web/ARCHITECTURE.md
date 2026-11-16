# Arquitetura do frontend CashFlow

**Padrão:** React + TypeScript, arquitetura modular por funcionalidades (*feature-based*) com separação entre apresentação, orquestração e integrações. O objetivo é permitir alterações em uma funcionalidade sem editar um único componente de centenas de linhas.

```text
web/src/
├── App.tsx                             # Composition root, somente monta a tela
├── features/
│   ├── app/
│   │   └── useCashFlowController.ts     # Hook de estado, fluxos e orquestração
│   ├── auth/
│   │   ├── AppHeader.tsx                # Login, estado da sessão e logout
│   │   └── WelcomePanel.tsx             # Tela de acesso
│   ├── merchants/
│   │   └── AdminPanel.tsx               # Cadastro de loja e contas ADMIN/OPERATOR
│   └── finance/
│       ├── FinancialDashboard.tsx       # Composição das três áreas financeiras
│       ├── TransactionForm.tsx          # Crédito, débito, validação de entrada
│       ├── BalancePanel.tsx             # Consulta do consolidado
│       └── HistoryPanel.tsx             # Extrato e paginação
├── api/
│   ├── cashflow.ts                      # Contratos, chamadas HTTP e tratamento de erros
│   ├── brl.ts                           # Valor brasileiro → decimal invariante da API
│   └── projection.ts                    # Consulta eventual do saldo pós-registro
├── auth/oidc.ts                         # OIDC Authorization Code com PKCE
└── time.ts                              # Data de referência
```

## Responsabilidades

- **App.tsx:** não chama APIs, não formata dinheiro e não implementa regras de negócio. Escolhe a visão ADMIN ou COMERCIANTE segundo a permissão informada pelo backend.
- **features:** cada tela vive na pasta da funcionalidade a que pertence. Os componentes são de apresentação. `useCashFlowController` coordena a sessão e ações das telas; sem estado global de terceiros para o tamanho atual do MVP.
- **api:** lida com HTTP e contratos, sem conhecimento de componentes React. A camada de normalização monetária usa string para não introduzir erros de ponto flutuante na representação financeira.
- **auth:** fluxo OIDC com Keycloak, independente da apresentação.
- **testes:** testes do controller/visão e das APIs por componente, cobertura de crédito, débito, conversão BRL, polling do consolidado, ADMIN/OPERATOR e autenticação. Backends mantêm validação de autorização por merchant; ocultar botão não é segurança.

## Como evoluir

1. Mantenha campos, efeitos e handlers de uma funcionalidade no respectivo módulo. `App.tsx` deve permanecer uma composição pequena.
2. Se o controller crescer novamente, dividi-lo por responsabilidades em `useSession`, `useMerchants` e `useTransactions`, com testes próprios. Não adicionar estado global só por preferência arquitetural.
3. Para novas funcionalidades (ex.: relatórios), criar `features/reports` e extrair os contratos HTTP correspondentes para `api`.
4. Antes de produção, tratar auditoria das operações administrativas, proteção do onboarding, unicidade concorrente de usernames, recuperação de senha e perfis/rotas autorizados.

## Executar

```powershell
cd web
npm ci
npm run build
npm run test:coverage
```

No Docker Compose local, depois do merge: `docker compose build transactions web` e `docker compose up -d --force-recreate --no-deps transactions web`. Não usar `docker compose down -v`, para preservar os comerciantes e usuários já criados no Keycloak.
