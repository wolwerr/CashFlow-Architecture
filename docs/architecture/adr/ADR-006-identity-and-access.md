# ADR-006: Identidade, autenticação e autorização por comerciante

> **Status:** decidida  
> **Data:** 2026-11-14  
> **Issue:** [#7, Documentar ADRs e trade-offs](https://github.com/wolwerr/CashFlow-Architecture/issues/7)  
> **Relacionadas:** [ADR-002](ADR-002-architecture-style-clean-architecture.md), [Building Block View](../building-block-view.md) e Issue #9 (segurança)

## Contexto

A arquitetura do CashFlow contempla cliente Web responsivo, Keycloak para identidade e, NGINX como reverse proxy adotado no MVP. Um API Gateway dedicado e o BFF ficam para evolução futura conforme a ADR-007. Usuários podem estar vinculados a comerciantes distintos; autenticar um usuário não lhe concede autorização sobre qualquer `merchant_id`. O MVP de demonstração prioriza os dois serviços financeiros, mas a arquitetura deve evitar desenvolvimento de autenticação caseira e impedir dependência síncrona do IAM em toda operação de lançamento.

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
3. **IAM no C4:** representar explicitamente o **Keycloak** e o **Banco do Keycloak**; este banco armazena identidade, credenciais e configuração OIDC. **Não criar um microsserviço próprio emissor de tokens nem armazenar cópia de senhas no CashFlow**. Os vínculos usuário/comerciante são regras de negócio do CashFlow. **Decisão do MVP em 15/11/2026:** banco lógico próprio de autorização e consulta com cache curto e invalidação; sua interface e propriedade de execução serão especificadas na Issue #9.
4. **JWT access tokens:** os serviços Transactions e Consolidation devem validar assinatura e algoritmo permitido, emissor (`iss`), audiência (`aud`), validade (`exp`/`nbf`) e autorização necessária, usando mecanismos suportados por ASP.NET Core. **ID token não é access token** e não deve ser aceito para chamar APIs financeiras.
5. **JWKS:** obter chaves públicas de forma segura por metadata OIDC/JWKS, mantendo cache e política explícita de renovação/rotação. Com chaves em cache, as APIs podem validar tokens localmente sem chamada ao IAM por requisição.
6. **Autorização de negócio:** verificar `merchant_id`, associação do sujeito ao comerciante e permissões exigidas no serviço de negócio, não apenas no Gateway. O MVP adota **ADMIN e OPERATOR por comerciante**; VIEWER não integra o MVP. A matriz de permissões por ação será especificada.
7. **Dados:** identidades e segredos de autenticação ficam sob responsabilidade do provedor. Vínculos de usuários com comerciantes e regras de domínio pertencem à camada de autorização do CashFlow, com esquema e ciclo de vida específicos a definir, evitando acessos diretos entre os bancos financeiros.

## Complemento de autorização, 15/11/2026

- Multi-comerciante obrigatório no MVP: dados e comandos segregados por `merchant_id`, com checagem de vínculo **servidor-side** e papel ADMIN/OPERATOR em cada contexto financeiro.
- Banco lógico **próprio de autorização CashFlow**, isolado de Keycloak e bancos financeiros. Consulta com cache curto **e invalidação em alteração/revogação**; TTL, implementação de revogação e ownership da interface de autorização a detalhar. Em situação de dúvida sobre vínculo, **falhar fechado**. Não pressupor nova API financeira nem joins cross-domain.
- Autenticação com JWT validado em cada serviço, JWKS em cache. A indisponibilidade do banco/cache de autorização pode comprometer operações não autorizáveis, e não deve ser mascarada como disponibilidade garantida.
- Trilha de criação financeira confirmada contém ator/merchant/time/correlation, retenção inicial de 365 dias, sujeita a revisão legal e controles de acesso.

[Registro de decisões](../mvp-decisions.md).

## Fluxos e decisões de segurança

**Login:** cliente inicia fluxo OIDC com o provedor, obtém token de acesso para a API; desenho detalhado do cliente e armazenamento de tokens pertencem à ADR-007 e Issue #9.

**Operação financeira:** o cliente Web chama as APIs através do NGINX como reverse proxy, e **cada API** valida access token. A autorização do comerciante deve derivar de associação confiável, com verificação servidor-side. Não aceitar `merchant_id` enviado pelo usuário como prova de autorização.

**Falha de IAM:** se um token estiver válido e as chaves em cache forem suficientes, o serviço financeiro pode continuar autorizando operações até os limites previstos na política. Login, refresh, revogação imediata e atualização de associações podem ficar indisponíveis; **não aceitar tokens expirados, assinatura desconhecida ou falha aberta (fail-open)**.

**Permissões revogadas:** a validação local de JWT não proporciona revogação instantânea. Definir duração curta dos tokens de acesso, rotação e estratégia de autorização/atualização de permissões. Se o negócio exigir revogação imediata, o trade-off pode requerer consulta/controle adicional com impacto na disponibilidade.

**Segurança em trânsito e repouso:** TLS, armazenamento seguro de segredos, MFA quando aplicável, proteção a brute force, auditoria de acessos, minimização de claims e cuidados LGPD. Não registrar credenciais nem tokens completos nos logs.

## Consequências positivas

- Não desenvolver fluxos sensíveis de identidade do zero.
- Os serviços financeiros mantêm responsabilidade pela autorização de negócio, permitindo segregação por comerciante.
- Validação local reduz dependência síncrona de um componente central no caminho de lançamento.
- Protocolos padronizados possibilitam substituir o provedor com menos acoplamento, desde que contratos e claims sejam controlados.

## Riscos e mitigação

| Risco | Impacto | Mitigação |
| --- | --- | --- |
| Keycloak self-hosted sem operação madura | Indisponibilidade do login, patches/backup inadequados | Usar imagens suportadas/fixadas, hardening, backup, observabilidade, avaliar serviço gerenciado em produção |
| Cache JWKS desatualizado | Falha de validação após rotação de chave | Política de refresh, sobreposição de chaves, teste de rotação |
| Permissões no token ficarem antigas | Usuário revogado pode manter acesso até expiração | Tokens curtos e política explícita de revogação |
| Autorizar só por role | Vazamento de dados entre comerciantes | Testes de autorização por `merchant_id` e vínculo do sujeito |
| Validar apenas no Gateway | Bypass por acesso direto a API | Validar tokens e autorização nos próprios serviços |
| IAM indisponível | Novos logins/refresh falham | Definir comportamento seguro de tokens válidos em cache e teste de indisponibilidade |
| Infra IAM aumentar o MVP | Consumo de recursos e atraso na entrega | IAM completo é capacidade da arquitetura-alvo; priorizar escopo exigido sem apresentar segurança de demonstração como produção |

## Critérios de validação

1. Tokens expirados, assinatura inválida, `iss` e `aud` incorretos recebem HTTP 401.
2. Usuário autenticado sem permissão ou vínculo com comerciante recebe HTTP 403.
3. Usuário de um comerciante não pode consultar nem registrar lançamentos para outro comerciante sem vínculo autorizado.
4. Serviços validam tokens independentemente do Gateway.
5. Validar continuidade de operações com token válido e chave pública em cache durante falha do IAM, dentro da política de segurança.
6. Testar rotação/atualização de chaves e política de expiração/revogação.
7. Documentar configuração local reproduzível se Keycloak fizer parte da demonstração do MVP; **não aceitar autenticação fictícia como garantia de produção**.

## Pontos pendentes

- Confirmar se o desafio requer implementação efetiva de login/gestão de usuários ou somente desenho-alvo (o enunciado prioriza os dois serviços financeiros).
- a escolha do Keycloak está decidida; detalhar versão, hardening, operação, backup, HA e eventual migração para um serviço gerenciado conforme implantação e custo.
- Decidir onde persistir vínculos usuário/comerciante e como refletir a autorização com baixa dependência síncrona.
- Refinar administração de usuários, MFA, SSO, políticas de senha, revogação, auditoria e LGPD na Issue #9.
- O C4 Nível 2 e a sequência de autenticação foram atualizados para nomear o Keycloak; atualizar as imagens PNG após renderização.

## Referências

- [ASP.NET Core: JWT bearer authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0)
- [Keycloak: documentação](https://www.keycloak.org/documentation)
- [Microsoft Entra External ID para clientes](https://learn.microsoft.com/en-us/entra/external-id/customers/overview-customers-ciam)
- [Building Block View](../building-block-view.md)
