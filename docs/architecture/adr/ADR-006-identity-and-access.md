# ADR-006: Identidade, autenticação e autorização por comerciante

> **Status:** decidida  
> **Data:** 2026-11-14; **revisão da autorização:** 2026-11-16  
> **Issue:** [#7, Documentar ADRs e trade-offs](https://github.com/wolwerr/CashFlow-Architecture/issues/7)  
> **Relacionadas:** [ADR-002](ADR-002-architecture-style-clean-architecture.md), [Building Block View](../building-block-view.md) e Issue #9 (segurança)

## Contexto

A arquitetura do CashFlow contempla cliente Web responsivo, Keycloak para identidade e, NGINX como reverse proxy adotado no MVP. Um API Gateway dedicado e o BFF ficam para evolução futura conforme a ADR-007. Usuários podem estar vinculados a comerciantes distintos; autenticar um usuário não lhe concede autorização sobre qualquer `merchant_id`. O MVP de demonstração prioriza os dois serviços financeiros, mas a arquitetura deve evitar desenvolvimento de autenticação caseira e reduzir dependências de autorização sem comprometer a revogação de vínculos. Para o MVP, a verificação de vínculo atualizado no Keycloak pode exigir consulta interna síncrona por operação financeira.

## Alternativas avaliadas

| Alternativa | Benefícios | Custos e riscos |
| --- | --- | --- |
| **Keycloak (servidor IAM pronto, self-hosted)** | OIDC/OAuth 2.0, gestão de usuários e sessões, execução em containers, demonstração local | Requer instalação, hardening, upgrades, HA, backup e configuração segura; permissões de comerciantes ainda exigem desenho de domínio |
| **Microsoft Entra External ID (gerenciado)** | Integração com ecossistema Microsoft, protocolos padronizados, operação do provedor delegada | Dependência de cloud/tenant e custos/limites; configuração externa torna a demonstração local menos autocontida |
| **IAM desenvolvido do zero em ASP.NET Core** | Controle integral da implementação e experiência uniforme em .NET | Alto risco de erros em senhas, sessão, assinatura de tokens, recuperação e MFA; não recomendado |
| **Autenticação simplificada apenas para demonstração** | Menor esforço inicial | Pode mascarar falhas de segurança; não deve ser apresentada como pronta para produção |

As comparações tratam de **soluções de identidade**, não de desempenho comprovado. A decisão sobre produto e integração com o ambiente corporativo deve poder ser revisitada.

## Decisão registrada

1. **Protocolos:** utilizar OpenID Connect (OIDC) para autenticação de usuários e OAuth 2.0 para autorização delegada, com fluxos seguros apropriados para o tipo de cliente; para Web/Mobile, avaliar Authorization Code + PKCE. Comunicação via HTTPS.
2. **Provedor:** adotar **Keycloak como provedor de identidade da arquitetura CashFlow**, para desenvolvimento e eventual demonstração local. O **Microsoft Entra External ID** segue como alternativa futura caso políticas corporativas e análise de custo/gestão justifiquem migração. A decisão de adotar Keycloak **não obriga implementar IAM completo no MVP financeiro**.
3. **IAM no C4:** representar explicitamente o **Keycloak** e o **Banco do Keycloak**; este banco armazena identidade, credenciais e configuração OIDC. **Não criar um microsserviço próprio emissor de tokens nem armazenar cópia de senhas no CashFlow**. Os vínculos usuário/comerciante são regras de negócio do CashFlow. **Revisão aprovada em 16/11/2026:** o próprio Keycloak é a fonte dos vínculos usuário/comerciante e papéis no MVP. Não haverá banco lógico adicional de autorização nem serviço `CashFlow.Authorization`.
4. **JWT access tokens:** os serviços Transactions e Consolidation devem validar assinatura e algoritmo permitido, emissor (`iss`), audiência (`aud`), validade (`exp`/`nbf`) e autorização necessária, usando mecanismos suportados por ASP.NET Core. **ID token não é access token** e não deve ser aceito para chamar APIs financeiras.
5. **JWKS:** obter chaves públicas de forma segura por metadata OIDC/JWKS, mantendo cache e política explícita de renovação/rotação. Com chaves em cache, as APIs podem validar tokens localmente sem chamada ao IAM por requisição.
6. **Autorização de negócio:** verificar `merchant_id`, associação do sujeito ao comerciante e permissões exigidas no serviço de negócio, não apenas no Gateway. O MVP adota **ADMIN e OPERATOR por comerciante**; VIEWER não integra o MVP. A matriz de permissões por ação será especificada.
7. **Dados:** identidades e segredos de autenticação ficam sob responsabilidade do provedor. Vínculos e papéis por comerciante serão gerenciados no Keycloak (Groups). As **regras de autorização de cada operação** continuam pertencendo às APIs do CashFlow, por meio de um gateway de Application com implementação na Infrastructure. Não há acesso direto das APIs aos bancos financeiros umas das outras nem ao banco interno do Keycloak.

## Histórico e revisão da decisão de autorização

**15/11/2026 (substituída):** foi aprovada a criação de um banco lógico próprio de autorização CashFlow para Merchant/Membership/Role, com cache e invalidação. Essa decisão deixou de ser vigente após a revisão de 16/11/2026. O registro original permanece nas decisões consolidadas como histórico, com indicação de substituição.

**16/11/2026 (vigente no MVP LOCAL):** usar **Keycloak como fonte de autenticação e de autorização por comerciante**, sem adicionar o módulo/serviço `CashFlow.Authorization` e sem quarto banco lógico de autorização. Os dois serviços financeiros continuam Transactions e Consolidation.

### Modelo de vínculos e permissões no Keycloak

- Realm `cashflow`; usuários identificados pelo `sub` do JWT validado.
- **Grupos organizados por identificador estável de comerciante**, por exemplo `/merchants/{merchant_uuid}/ADMIN` e `/merchants/{merchant_uuid}/OPERATOR`. A associação do usuário ao grupo determina seu papel **somente naquele comerciante**. Não confiar em nomes de exibição como identificadores.
- Papéis **ADMIN** e **OPERATOR** são contextuais ao comerciante, não roles globais com acesso irrestrito. Não inferir acesso com base apenas em `realm_access.roles`, `resource_access.roles` ou no `merchant_id` enviado pelo cliente.
- Matriz inicial do MVP: ambos podem criar e consultar lançamentos e consultar o consolidado **do seu comerciante**. Apenas ADMIN pode gerenciar membros/papéis do **mesmo comerciante**, mediante operação administrativa protegida e validação de que um vínculo não pode elevar seus próprios privilégios indevidamente. Sem papel VIEWER.
- Cadastro inicial de comerciante e primeiro ADMIN requer processo de bootstrap administrativo de acesso restrito; **nenhum endpoint público de autoatribuição**. A existência de dados comerciais adicionais (CNPJ, contratos, etc.) será revista se necessária, e não deve ser simulada com atributos IAM genéricos.

### Provisionamento de comerciantes e administradores no aplicativo (evolução local)

- O usuário autorizado para bootstrap local pode cadastrar um comerciante informando somente seu nome na SPA. A API Transactions, usando um **cliente confidencial de gestão separado** do cliente somente leitura, cria um UUID e os grupos Keycloak ADMIN e OPERATOR e atribui o primeiro ADMIN ao próprio usuário autenticado.
- Apenas em Development e para o `preferred_username` assinado que corresponde ao valor de `CASHFLOW_BOOTSTRAP_USERNAME` o cadastro inicial é permitido. Não há autoatribuição irrestrita nem disponibilização pública dessa capacidade para produção.
- Um ADMIN do comerciante pode vincular outros usuários como ADMIN ou solicitar criação com senha temporária no Keycloak. Somente o backend guarda as credenciais do cliente de gestão; nenhum segredo administrativo é exposto à SPA.
- O nome da loja é armazenado como `displayName` no grupo do comerciante, apenas para facilitar a seleção. Continua **sem banco de autorização próprio**. Novos requisitos fiscais/comerciais poderão exigir modelo de domínio distinto.
- Contrato e procedimento de bootstrap local em [merchant-onboarding.md](../../implementation/merchant-onboarding.md). Atribuição de permissões à Service Account de gestão é uma configuração inicial restrita, não uma tarefa por comerciante.
- **Limite conhecido:** a criação dos grupos/vínculos não é uma transação distribuída. Provisionamento parcial, auditoria administrativa e política de produção devem ser endurecidos antes do deployment externo.

### Perfis ADMIN da plataforma e COMERCIANTE (MVP local)

- **ADMIN da plataforma**, restrito à conta de bootstrap configurada em Development, tem a interface completa e pode cadastrar uma loja com **nome, username exclusivo no realm e senha inicial**. Uma API protegida retorna a permissão administrativa com base na identidade autenticada, nunca em sinalização do frontend.
- **COMERCIANTE**, com login próprio no mesmo realm Keycloak, recebe vínculo apenas no grupo `/merchants/{uuid}/OPERATOR` e visualiza exclusivamente Novo lançamento, Saldo Diário e Histórico. Não acessa administração, cadastro de lojas ou gestão de usuários, inclusive se invocar diretamente os endpoints protegidos.
- O bootstrap permanece com ADMIN do grupo da loja para consultas e manutenção. A criação da nova conta OPERATOR não a torna ADMIN e não autoriza outros comerciantes.
- Credenciais são controladas somente pelo Keycloak. Username tem unicidade em todo o realm; o serviço não reutiliza automaticamente usuário preexistente como operador de nova loja. Senha inicial de 12 a 128 caracteres é restrição mínima local, **não política de produção**.
- **Evoluções obrigatórias antes de produção**: senhas mais fortes, verificação de comprometimento, MFA, recuperação, convite, auditoria e unificação das regras de unicidade/idempotência de provisionamento em concorrência. Cadastro comercial ampliado (CNPJ, razão social, contatos, endereço) fica para uma fase posterior. Não colocar dados comerciais em JWT.
- Fluxos e limitações estão em [merchant-onboarding.md](../../implementation/merchant-onboarding.md). Todos os cenários financeiros exigem testes unitários e E2E, sobretudo crédito/débito e projeção assíncrona.

### Integração, validade e revogação

- Transactions e Consolidation verificam o **access token** via assinatura RS256, JWKS, issuer, audience e expiração localmente, conforme a implementação de JWT já existente para Transactions. O Keycloak não substitui as verificações de negócio dentro da API.
- Para **cada operação financeira protegida**, a implementação de `ICreateTransactionAuthorizationGateway` (e futuros gateways de consulta) valida o vínculo **atual** `sub + merchant_id + ação` usando uma integração de serviço autenticada com o Keycloak, com escopo administrativo mínimo e sem expor credenciais ao frontend.
- **Sem cache positivo de permissões nesta etapa**, para não continuar autorizando um usuário revogado com base somente em JWT ou TTL. Tokens de acesso de **5 minutos** no realm local; expiração do JWT não é garantia suficiente de revogação. Consulta indisponível, resultado ambíguo ou resposta incompleta implicam **fail closed** (rejeição/indisponibilidade, nunca acesso concedido). A revogação torna-se efetiva após o Keycloak confirmar a alteração e a consulta subsequente observá-la; o tempo real depende do Keycloak e precisa de teste.
- HTTP **401** para credencial ausente/inválida, **403** para usuário autenticado sem papel/vínculo e **503** para falha técnica da verificação de autorização; não revelar detalhes internos do IAM.
- Tratar o impacto de consulta síncrona em disponibilidade e latência, especialmente na meta de 50 RPS. Avaliar cache curto e invalidação **somente futuramente**, mediante evidência de revogação segura e testes de falha.
- `Merchant` e associação por grupos **não** representam um domínio comercial completo. A opção atual evita uma persistência de autorização própria no MVP, sem impedir evolução para modelo dedicado se requisitos adicionais justificarem.

### Limites de implementação

Esta é uma decisão **documentada**, não comprovação de autorização pronta. O PR de JWT de Transactions implementa autenticação e rota diagnóstica apenas; ainda faltam adaptador para consultas seguras ao Keycloak, configuração de credenciais mínimas de serviço, provisionamento/revogação de grupos, autorização do Consolidation e testes de isolamento, revogação e falhas. **Não expor endpoints financeiros até concluir essas proteções.** Azure continua futuro.

## Fluxos e decisões de segurança

**Login:** cliente inicia fluxo OIDC com o provedor, obtém token de acesso para a API; desenho detalhado do cliente e armazenamento de tokens pertencem à ADR-007 e Issue #9.

**Operação financeira:** o cliente Web chama as APIs através do NGINX como reverse proxy, e **cada API** valida access token. A autorização do comerciante deve derivar de associação confiável, com verificação servidor-side. Não aceitar `merchant_id` enviado pelo usuário como prova de autorização.

**Falha de IAM:** a validação criptográfica do JWT pode continuar com JWKS em cache, mas **isso não é autorização financeira**. Enquanto a verificação atual do vínculo no Keycloak estiver indisponível ou incerta, **negar operações financeiras**. Login e refresh também podem falhar. Nunca aceitar token expirado nem operar em fail-open.

**Permissões revogadas:** a validação local de JWT não revoga instantaneamente papéis presentes em tokens antigos. Por isso, no MVP, consultar os vínculos atuais no Keycloak por operação e não autorizar com base apenas em claims estáticas; executar testes reais de revogação e indisponibilidade.

**Segurança em trânsito e repouso:** TLS, armazenamento seguro de segredos, MFA quando aplicável, proteção a brute force, auditoria de acessos, minimização de claims e cuidados LGPD. Não registrar credenciais nem tokens completos nos logs.

## Consequências positivas

- Não desenvolver fluxos sensíveis de identidade do zero.
- Keycloak gerencia também vínculos e papéis de comerciantes, eliminando o banco lógico adicional do MVP. As APIs mantêm a responsabilidade de verificar autorização por operação e comerciante.
- Validação criptográfica local evita consulta ao IAM para cada assinatura JWT; a verificação dos vínculos atuais, entretanto, **continua dependente do Keycloak por operação financeira**, até que um cache com revogação comprovada seja adotado.
- Protocolos padronizados possibilitam substituir o provedor com menos acoplamento, desde que contratos e claims sejam controlados.

## Riscos e mitigação

| Risco | Impacto | Mitigação |
| --- | --- | --- |
| Keycloak self-hosted sem operação madura | Indisponibilidade do login, patches/backup inadequados | Usar imagens suportadas/fixadas, hardening, backup, observabilidade, avaliar serviço gerenciado em produção |
| Cache JWKS desatualizado | Falha de validação após rotação de chave | Política de refresh, sobreposição de chaves, teste de rotação |
| Permissões no token ficarem antigas | Usuário revogado pode manter acesso até expiração | Tokens curtos e política explícita de revogação |
| Autorizar só por role | Vazamento de dados entre comerciantes | Testes de autorização por `merchant_id` e vínculo do sujeito |
| Validar apenas no Gateway | Bypass por acesso direto a API | Validar tokens e autorização nos próprios serviços |
| IAM indisponível | Novos logins/refresh e validação de vínculos financeiros falham | Rejeitar operações financeiras cujo vínculo não possa ser confirmado; testar indisponibilidade |
| Infra IAM aumentar o MVP | Consumo de recursos e atraso na entrega | IAM completo é capacidade da arquitetura-alvo; priorizar escopo exigido sem apresentar segurança de demonstração como produção |

## Critérios de validação

1. Tokens expirados, assinatura inválida, `iss` e `aud` incorretos recebem HTTP 401.
2. Usuário autenticado sem permissão ou vínculo com comerciante recebe HTTP 403.
3. Usuário de um comerciante não pode consultar nem registrar lançamentos para outro comerciante sem vínculo autorizado.
4. Serviços validam tokens independentemente do Gateway.
5. Validar JWT com chave pública em cache durante falha do IAM, mas **negar** operação financeira quando o vínculo atual não puder ser comprovado.
6. Testar rotação/atualização de chaves e política de expiração/revogação.
7. Documentar configuração local reproduzível se Keycloak fizer parte da demonstração do MVP; **não aceitar autenticação fictícia como garantia de produção**.

## Pontos pendentes

- Confirmar se o desafio requer implementação efetiva de login/gestão de usuários ou somente desenho-alvo (o enunciado prioriza os dois serviços financeiros).
- a escolha do Keycloak está decidida; detalhar versão, hardening, operação, backup, HA e eventual migração para um serviço gerenciado conforme implantação e custo.
- **Decidido:** vínculos no Keycloak Groups; implementar gateway seguro por comerciante com consulta atual e fail closed, avaliar dependência síncrona e medir impacto em latência.
- Refinar administração de usuários, MFA, SSO, políticas de senha, revogação, auditoria e LGPD na Issue #9.
- O C4 Nível 2 e a sequência de autenticação foram atualizados para nomear o Keycloak; atualizar as imagens PNG após renderização.

## Referências

- [ASP.NET Core: JWT bearer authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0)
- [Keycloak: documentação](https://www.keycloak.org/documentation)
- [Microsoft Entra External ID para clientes](https://learn.microsoft.com/en-us/entra/external-id/customers/overview-customers-ciam)
- [Building Block View](../building-block-view.md)
