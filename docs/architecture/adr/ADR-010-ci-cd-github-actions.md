# ADR-010: GitHub Actions para integração e entrega contínuas

> **Status:** decidida  
> **Data:** 2026-11-15  
> **Issue:** [#7, Documentar ADRs e trade-offs](https://github.com/wolwerr/CashFlow-Architecture/issues/7)  
> **Relacionadas:** [ADR-001](ADR-001-language-framework.md), [ADR-002](ADR-002-architecture-style-clean-architecture.md), [ADR-007](ADR-007-clients-api-gateway.md), [ADR-008](ADR-008-observability.md), [ADR-009](ADR-009-deployment-cloud.md)

## Contexto e problema

O CashFlow mantém o **backlog (GitHub Issues)** e o **código (GitHub)** no mesmo repositório. A ADR-002 definiu **monorepo** com **Transactions** e **Consolidation** independentes, e a ADR-009 definiu LOCAL com Docker Compose, DEV/HOM em clusters AKS separados e econômicos, e PROD em AKS Standard com PostgreSQL gerenciado.

Precisamos automatizar validação de mudanças, criação de artefatos, publicação de imagens e implantação por ambiente, sem forçar a publicação simultânea dos dois microsserviços. A integração de CI/CD também deverá permitir rastrear de uma issue e pull request até o commit, a imagem publicada e a implantação.

A estratégia detalhada de **testes, ferramentas de teste e meta de cobertura** pertence ao planejamento de desenvolvimento e aos requisitos de qualidade, **não constitui uma ADR separada**.

## Alternativas avaliadas

| Alternativa | Benefícios | Limitações e trade-offs |
| --- | --- | --- |
| **GitHub Actions** | Integração direta com código, pull requests, issues, checks e permissões do GitHub; workflows versionados em YAML | Dependência do ecossistema GitHub, quotas/minutos de execução, manutenção de runners e segurança de workflows |
| **Azure DevOps Pipelines** | Integração madura com Azure, pipelines e controles de release | Exige mais uma plataforma e integração para rastrear código, backlog e políticas já centralizados no GitHub |
| **Jenkins** | Flexibilidade e extenso ecossistema de plugins, possibilidade de hospedagem própria | Operação, manutenção, atualizações, segurança do servidor/plugins e maior esforço de governança |
| **Scripts manuais de build/deploy** | Simplicidade para protótipos | Baixa rastreabilidade, erro humano e falta de verificações consistentes |

## Decisão registrada

**Adotar GitHub Actions como plataforma de CI/CD do CashFlow.** Os workflows serão versionados em `.github/workflows/` no mesmo repositório, mas com **caminhos de build, publicação e implantação separados** para Transactions, Consolidation e frontend quando necessário.

### Fluxo previsto

1. **Pull request:** executar build e verificações automatizadas pertinentes aos caminhos alterados; apresentar resultados como checks antes do merge. O conjunto concreto de testes e os limiares serão definidos na documentação de desenvolvimento.
2. **Build:** compilar .NET 10 e React/TypeScript conforme a alteração, produzindo artefatos e imagens Docker identificadas por commit SHA.
3. **Registry:** publicar imagens no **Azure Container Registry (ACR)** definido como SHARED na ADR-009. Usar tags imutáveis/identificáveis e manter vínculo com o commit, evitando depender da tag `latest`.
4. **Deploy DEV:** promover automaticamente ou sob gatilho definido as imagens correspondentes aos serviços alterados para o AKS de DEV, sujeito aos checks.
5. **Deploy HOM:** promover **a mesma imagem** já produzida, sem rebuild por ambiente, para o cluster isolado de HOM, com controles e verificações adequados. HOM mantém seu porte econômico definido na ADR-009.
6. **Deploy PROD:** promover imagem previamente validada em HOM para o AKS de produção, com **aprovação humana obrigatória via GitHub Environments**, conforme decisão do MVP. Validar antes da implantação se as regras de proteção estão disponíveis no plano/repositório; **não liberar PROD sem a proteção efetiva**, ajustando o plano/repositório se necessário.
7. **Rollback:** manter versões anteriores das imagens/manifests e procedimento de rollback do Deployment/Helm, sem presumir que rollback de schema de banco seja sempre seguro; migrations exigem compatibilidade e estratégia separada.

### Independência entre os serviços

- **Transactions** e **Consolidation** terão pipelines ou jobs distintos com filtros de caminho, para que alterar um serviço não implique automaticamente reconstruir e implantar o outro.
- Componentes compartilhados, contratos ou bibliotecas comuns exigirão uma regra explícita para selecionar quais serviços reconstruir e testar.
- A alteração de infraestrutura ou configurações comuns poderá disparar verificações integradas, sem eliminar a independência do deployment.
- A configuração de cada ambiente será mantida separada, com namespaces, identidade, credenciais e bancos próprios conforme a ADR-009.

### Identidade, permissões e segurança

- **Preferir GitHub Actions OpenID Connect (OIDC) com Microsoft Entra ID** para acesso ao Azure, evitando segredos de cliente de longa duração. Usar credenciais federadas e RBAC de menor privilégio por ambiente.
- Definir permissões explícitas em `permissions:`, incluindo `id-token: write` **somente** nos jobs que precisam obter token OIDC. Restringir `GITHUB_TOKEN` e revisar actions de terceiros, preferencialmente com versões/SHAs fixados conforme política.
- Isolar **GitHub Environments** de DEV, HOM e PROD. Proteger segredos, variáveis e aprovações de produção; verificar disponibilidade de regras de proteção no plano e no tipo de repositório.
- Evitar credenciais persistidas em imagens, artefatos e logs. ACR, Key Vault e recursos AKS precisam ter identidades e acessos apropriados; o registry pode ser compartilhado, mas as autorizações de deploy não.
- Restringir permissões de workflows disparados por código não confiável, principalmente em pull requests de forks.

## Consequências positivas

- Integração natural com GitHub Issues, pull requests, commits e revisões já utilizados pelo projeto.
- Fonte dos pipelines versionada junto ao código, com rastreabilidade da revisão até a imagem e ao ambiente.
- Reutilização do mesmo artefato nos ambientes e implantação independente dos microsserviços.
- Menos ferramentas de gestão necessárias em comparação com introduzir Jenkins ou Azure DevOps Pipelines.
- Possibilidade de integração com Azure por OIDC sem armazenar credenciais de serviço de longa duração.

## Riscos e mitigação

| Risco | Consequência | Mitigação |
| --- | --- | --- |
| Acoplamento ao GitHub Actions | Dependência de um fornecedor no pipeline | Builds em `dotnet`, Docker e scripts portáveis, infraestrutura versionada |
| Quotas, minutos e runners | Custos ou filas de execução | Verificar franquias do plano, configurar cache e filtros, dimensionar runners depois de medir |
| Workflows com permissões excessivas | Risco de comprometimento do registry ou cluster | OIDC, RBAC mínimo, environments separados, revisão de workflows |
| Pipeline único para todo o monorepo | Deploy acoplado de Transactions e Consolidation | Workflows/jobs independentes e filtros de caminho, observando dependências compartilhadas |
| Rebuild entre ambientes | Imagens diferentes em HOM e PROD | Promover o mesmo digest de imagem entre ambientes |
| Promoção de PROD sem controle | Mudança não revisada em produção | Verificação/approval manual e política de branch/environment |
| Migrations incompatíveis | Rollback da aplicação não restaura compatibilidade do banco | Migrations compatíveis com versões adjacentes, backups e plano de rollout |
| Dependência de runner | Problemas de rede, segurança ou custo de acesso ao AKS | Avaliar runners GitHub-hosted versus self-hosted com políticas e segurança antes da implantação |

## Critérios de validação

1. Um pull request de Transactions executa os checks pertinentes e não promove automaticamente Consolidation.
2. Commits e imagens publicadas no ACR são rastreáveis, com identificador imutável.
3. DEV, HOM e PROD usam identidades e permissões segregadas para deploy.
4. A imagem promovida a PROD é a mesma (por digest) validada nos ambientes anteriores.
5. Deploy de PROD só ocorre com controles de autorização e aprovação definidos.
6. Uma falha de build, testes exigidos ou deploy interrompe o pipeline e preserva evidências de diagnóstico.
7. Existe procedimento documentado para reversão de versão, observando as limitações de migrations.

## Complemento de governança, 15/11/2026

O gate de produção via **GitHub Environments com aprovação manual** é obrigatório na arquitetura aprovada. Disponibilidade/configuração do recurso ainda não foram verificadas, portanto o deploy PROD permanece bloqueado até que o ambiente protegido funcione e seja testado. A liberação exige **backups automáticos e execução documentada de teste de restauração** dos dados críticos. [Decisões do MVP](../mvp-decisions.md).

## Detalhes de implantação ainda pendentes

- Definir os nomes concretos dos workflows, estrutura das pastas, gatilhos e filtros de dependências compartilhadas.
- Selecionar scripts ou Helm/Kustomize/manifests para implantação no AKS e estratégia para migrations.
- Configurar a federação OIDC entre GitHub e Microsoft Entra ID, permissões ACR/AKS e proteções do ambiente PROD.
- Definir a política de branches, pull requests e aprovações em PROD.
- Avaliar runners GitHub-hosted ou self-hosted, suas necessidades de rede e orçamento.
- Configurar checks e suítes de testes de acordo com o plano de desenvolvimento. O objetivo de cobertura e testes de carga/resiliência **não foi decidido nesta ADR**.
- Implementar os workflows e testar promoções/rollback. Esta ADR registra a escolha da ferramenta, não atesta execução.

## Referências

- [ADR-002: dois microsserviços independentes](ADR-002-architecture-style-clean-architecture.md)
- [ADR-009: ambientes AKS e orçamento](ADR-009-deployment-cloud.md)
- [GitHub Actions](https://docs.github.com/en/actions)
- [GitHub Actions com OIDC no Azure](https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-azure)
- [Microsoft Entra: federação de identidades](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation)

## Sequenciamento de entrega: LOCAL primeiro

**Fase atual aprovada:** desenvolvimento, integração e testes **somente em Docker Compose no computador local**, sem deploy para Azure. Os workflows GitHub Actions de build/teste podem ser preparados para qualidade do repositório, mas a promoção entre DEV/HOM/PROD e o gate de aprovação manual em GitHub Environments são **fase futura**. Antes de qualquer release de PROD, manter as exigências já decididas: aprovação humana efetiva, backups e teste documentado de restauração. Essa postergação não altera a escolha de GitHub Actions como plataforma CI/CD.
