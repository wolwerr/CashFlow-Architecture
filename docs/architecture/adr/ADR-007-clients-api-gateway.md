# ADR-007: Canais Web/Mobile, API Gateway e proteção da sessão

> **Status:** decidida  
> **Data:** 2026-11-14  
> **Issue:** [#7, Documentar ADRs e trade-offs](https://github.com/wolwerr/CashFlow-Architecture/issues/7)  
> **Relacionadas:** [ADR-001](ADR-001-language-framework.md), [ADR-002](ADR-002-architecture-style-clean-architecture.md), [ADR-006](ADR-006-identity-and-access.md)

## Contexto e escopo

O desafio CashFlow exige APIs para registrar créditos/débitos e consultar o consolidado diário. A arquitetura-alvo no [C4 Nível 2](../building-block-view.md) apresentava Aplicação Web/Mobile, um Gateway genérico e Keycloak, mas **o enunciado não exige frontend Web, aplicativo mobile nativo nem Gateway**. Estas capacidades facilitam uso e evolução, mas não devem comprometer a comprovação do requisito central: Transactions continua registrando quando Consolidation está indisponível.

A ADR-001 fixou C# / .NET 10 LTS para as APIs financeiras. A ADR-006 adotou **Keycloak** para identidade e autenticação com OIDC/OAuth 2.0; autorização por comerciante permanece nas APIs CashFlow.

Esta ADR avalia **tecnologia do cliente, experiência mobile, necessidade de Gateway e padrão de sessão Web**. Os produtos da infraestrutura cloud serão decididos na ADR-009.

## 1. Alternativas de frontend

| Alternativa | Vantagens | Custos e riscos |
| --- | --- | --- |
| **React + TypeScript** | Flexibilidade, componentes reutilizáveis, implementação incremental de interface responsiva | Exige decisões de roteamento, gerenciamento de estado e organização de bibliotecas |
| **Angular + TypeScript** | Estrutura e convenções integradas, adequação a aplicações administrativas maiores | Mais convenções e estrutura inicial para um MVP pequeno |
| **Blazor com .NET** | Linguagem C# compartilhada, integração ao ecossistema Microsoft | Equipe precisa dominar modelo de renderização e trade-offs do ambiente Blazor; não há requisito que exija uniformizar a linguagem do frontend |
| **Sem frontend no MVP** | Prioriza as APIs e os testes fundamentais | Demonstração para usuários menos visuais |

**Decisão registrada:** utilizar **React + TypeScript**, com interface responsiva. **Não criar aplicações Android e iOS nativas no MVP**. Uma PWA pode ser avaliada futuramente se houver requisitos de instalação, offline ou notificações; a simples responsividade não garante capacidades PWA.

## 2. API Gateway: alternativas

| Alternativa | Benefícios | Riscos e custos |
| --- | --- | --- |
| **YARP em ASP.NET Core** | Roteamento/reverse proxy no stack .NET, configuração de rotas, integração com middlewares e políticas | Novo processo e ponto de falha se mal operado; autenticação/rate limiting não acontecem automaticamente sem configuração |
| **NGINX / proxy de borda** | Ferramenta consolidada de proxy, TLS e roteamento | Configuração e stack operacional adicionais; recursos de gateway de aplicação podem exigir módulos ou componentes |
| **Gateway gerenciado na cloud** | Operação de borda delegada, funcionalidades condicionadas ao produto | Dependência do provedor e custos variáveis; menor reprodução em Docker Compose |
| **Acesso direto às duas APIs** | Menos infraestrutura e menor latência inicial | Clientes conhecem múltiplas origens/endereços; políticas de borda dispersas |

**Decisão de escopo decidida, revisada em 2026-11-14:** **implementar NGINX como reverse proxy de entrada do MVP**, servindo o React e encaminhando as chamadas de API para Transactions e Consolidation. **Não implementar API Gateway dedicado no MVP**: roteamento via NGINX não equivale automaticamente a uma plataforma completa de API management. O Gateway dedicado permanece evolução futura, após avaliação de políticas de borda, segurança, limites e necessidades reais. YARP permanece alternativa caso haja demanda por extensões em C#.

Um Gateway futuro pode centralizar TLS (conforme infraestrutura), roteamento `/api/transactions/*` e `/api/consolidation/*`, limites de requisição, correlação e logs. **O Gateway não deve ser a única camada de autenticação/autorização**: cada API financeira valida JWT e verifica escopo do comerciante.

## 3. Autenticação Web, Mobile e BFF

### Alternativas de cliente OAuth no navegador

| Padrão | Funcionamento | Trade-off |
| --- | --- | --- |
| **SPA pública com Authorization Code + PKCE** | React realiza o fluxo OIDC diretamente com Keycloak e usa access token nas APIs | Mais simples de operar, mas tokens podem ficar acessíveis ao JavaScript e a riscos de XSS; exigir controles de segurança apropriados |
| **Backend for Frontend (BFF)** | Backend confidencial realiza OIDC com Keycloak, mantém tokens no servidor e expõe sessão ao navegador por cookie seguro | Protege melhor os tokens da exposição ao JavaScript, mas exige sessão, proteção CSRF, disponibilidade do BFF e uma implantação adicional |
| **Aplicação Web renderizada no servidor** | Servidor administra autenticação/sessão e gera parte ou toda a interface | Pode reduzir exposição de tokens, com outro modelo de desenvolvimento e renderização |

**Decisão de escopo decidida:** para uma interface React no **MVP**, adotar SPA pública com OIDC Authorization Code + PKCE no Keycloak, controles rigorosos contra XSS e sem armazenamento persistente de tokens em `localStorage`. **BFF fica explicitamente fora do MVP e é evolução futura a avaliar para produção Web financeira**, com ASP.NET Core como candidato e detalhamento na Issue #9. Não confundir BFF com o Gateway: o BFF administra a sessão do navegador; o Gateway fornece roteamento e políticas de borda. Eles podem compartilhar infraestrutura *somente após* desenho explícito e testes, não por pressuposição.

O BFF deve usar OIDC Authorization Code com cliente confidencial, cookies `HttpOnly`, `Secure`, política `SameSite` adequada, proteção anti-CSRF, sessão/expiração, armazenamento seguro de tokens e logout. Não colocar refresh tokens em `localStorage`.

**Mobile:** se futuramente houver aplicativo nativo, utilizar fluxo próprio para cliente público (Authorization Code + PKCE) e armazenamento seguro do sistema operacional. Não reutilizar automaticamente o desenho de cookie/BFF Web em apps nativos.

O Keycloak é acessado pelo cliente (ou BFF) para login. **As APIs validam access tokens, não ID tokens**, usando issuer, audience, assinatura, expiração e permissões. `merchant_id` é autorizado por regras do CashFlow, não apenas pela identidade reconhecida no Keycloak.

## Complemento do MVP, 15/11/2026\n\nA React SPA guarda access tokens **somente na memória da aplicação**, sem `localStorage` nem `sessionStorage`, mantendo Authorization Code + PKCE. O formulário de lançamento inicia na data atual de `America/Sao_Paulo` e é editável; a API continua validando competência futura e autorização. Prefixos REST públicos são **`/api/v1/transactions`** e **`/api/v1/consolidation`**, preservados pelo NGINX. Isso não implica BFF nem Gateway dedicado. Conferir XSS/CSP, login/reload/logout e refresh em teste. [Decisões consolidadas](../mvp-decisions.md).\n\n## 4. Fluxo de referência no desenho-alvo

```text
Comerciante / Administrador
   -> React Web responsivo
   -> Keycloak (login OIDC + PKCE no MVP)
   -> NGINX reverse proxy (MVP)
      -> Transactions API (JWT + autorização por comerciante)
      -> Consolidation API (JWT + autorização por comerciante)

API Gateway dedicado e BFF: somente evolução futura

Transactions -> PostgreSQL Transactions (lançamento + Outbox)
Outbox worker -> RabbitMQ -> Consolidation consumer -> PostgreSQL Consolidation
```

**Futuro opcional:** React -> BFF .NET (sessão por cookie; tokens no servidor) -> NGINX -> APIs. O fluxo acima é **lógico**. Os diagramas C4 e de sequência foram alinhados à decisão registrada: NGINX faz parte do MVP como reverse proxy, enquanto API Gateway dedicado e BFF ficam para o futuro.

**Implementação:** o template inicial de NGINX está em [infra/nginx/default.conf.template](../../../infra/nginx/default.conf.template), com [orientações de integração](../../../infra/nginx/README.md). A integração funcional depende dos containers e dos contratos de rota reais; ainda não foi executada nem validada.

## 5. Consequências positivas

- Interface responsiva evita multiplicar aplicativos logo no início.
- React permite entrega incremental de formulários de lançamentos, consulta de saldo e administração.
- Adiar um Gateway dedicado reduz componentes. O NGINX é a entrada HTTP do MVP para servir React e encaminhar requisições financeiras.
- BFF pode reduzir exposição de tokens em aplicações Web sensíveis.
- Mantém os serviços financeiros independentes do canal de entrada.

## 6. Consequências negativas, riscos e mitigação

| Risco | Consequência | Mitigação |
| --- | --- | --- |
| Frontend, Gateway e BFF ampliam o escopo | Entrega financeira atrasa | Priorizar duas APIs, integração e testes exigidos pelo desafio |
| Gateway futuro centralizado | Pode virar ponto único de falha | Antes da adoção, definir HA, health checks, observabilidade e regras de roteamento |
| BFF único ou sessão não compartilhada | Sessões indisponíveis na falha do BFF | HA e estratégia de estado da sessão em produção |
| XSS em SPA no MVP | Roubo de tokens ou ações em nome do usuário | CSP, dependências atualizadas, saneamento apropriado, evitar armazenamento persistente de tokens; reavaliar BFF quando houver usuários reais |
| CSRF em BFF | Ações indevidas usando cookies | Proteção antifalsificação, SameSite, verificação de origem e testes |
| Autorização somente no Gateway | Bypass de controles e vazamento entre comerciantes | Revalidar JWT e autorização de negócio em cada API |
| Falha no Keycloak | Login e renovação afetados | Cache seguro de chaves para tokens válidos, sem aceitar tokens expirados; ADR-006 |
| Escopo de clientes ainda desconhecido | Tecnologia escolhida pode não atender requisitos futuros | Reavaliar React/Angular/Blazor e estratégia mobile se surgirem requisitos reais |

## 7. Testes e critérios de aceite

1. React (caso implementado) consome contratos explícitos das APIs sem fazer cálculos financeiros como fonte de verdade.
2. No MVP, testar NGINX como reverse proxy para as duas APIs, preservação de cabeçalhos necessários, tratamento de erros e disponibilidade. Os próprios serviços devem validar JWT e autorização, mesmo quando acessados diretamente em testes internos. Ao adotar um Gateway no futuro, validar roteamento, correlação, limites e observabilidade.
3. Transactions e Consolidation rejeitam tokens inválidos mesmo se chamadas diretamente, sem passar pelo Gateway.
4. Usuário autenticado não acessa `merchant_id` sem vínculo autorizado.
5. Se o BFF for adotado, tokens OAuth não ficam disponíveis ao JavaScript do navegador; testar cookie, CSRF, sessão e logout.
6. Falha no Consolidation não interrompe Transactions, independentemente do canal.
7. Testes de carga de 50 RPS medem o caminho efetivamente exposto do Consolidation, incluindo NGINX no caminho de medição quando o teste representar a experiência Web.
8. Documentar componentes **planejados versus implementados** para não atribuir propriedades de produção a uma demonstração local.

## 8. Quando revisitar

- Se houver exigência de Angular, Blazor, aplicativos nativos ou padrões corporativos específicos.
- Se a aplicação Web for apenas uma interface de demonstração sem usuários reais.
- Se o volume, políticas de segurança ou necessidade de múltiplos clientes justificar Gateway gerenciado ou BFF dedicado.
- Se a complexidade operacional do BFF superar os benefícios no contexto de uso.

## Referências externas

- [Microsoft: hospedar ASP.NET Core com NGINX](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/linux-nginx?view=aspnetcore-10.0)
- [Microsoft: YARP como alternativa de proxy .NET](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/yarp-overview?view=aspnetcore-10.0)
- [Keycloak: JavaScript adapter](https://www.keycloak.org/securing-apps/javascript-adapter)
- [RFC 10017 (agosto de 2026): OAuth 2.0 para aplicações de navegador](https://www.rfc-editor.org/rfc/rfc10017.html)

## Referências internas

- [ADR-006](ADR-006-identity-and-access.md)
- [Building Block View](../building-block-view.md)
- [Diagramas de sequência](../sequence-diagrams.md)
