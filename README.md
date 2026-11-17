# CashFlow | Desafio de Arquitetura de Soluções

[![GitHub Actions](https://github.com/wolwerr/CashFlow-Architecture/actions/workflows/local-build.yml/badge.svg)](https://github.com/wolwerr/CashFlow-Architecture/actions/workflows/local-build.yml)

Aplicação de fluxo de caixa multicomerciante: registro de **créditos e débitos** e consulta do **saldo diário consolidado**. Este repositório contém código, testes, decisões, diagramas e procedimentos locais. O desafio exige independência entre lançamentos e consolidado, além de **50 consultas/segundo** com **no máximo 5% de falhas**. Os limites foram avaliados em ensaios locais informados pelo operador; a [auditoria final](docs/validation/final-delivery-evidence.md) documenta os resultados e limitações de rastreabilidade.

> **Ambiente desta entrega:** Docker Compose em máquina local. A arquitetura Azure AKS é **plano futuro**, não infraestrutura provisionada. O Compose inclui os seis serviços principais e cinco serviços de observabilidade. O teste funcional de observabilidade foi confirmado pelo operador localmente, sem SHA contemporâneo registrado.


## Entrega para avaliação

**Enunciado revisado e checklist:** [Auditoria final e evidências](docs/validation/final-delivery-evidence.md) · [Matriz de requisitos](docs/requirements/challenge-compliance-matrix.md) · [Roteiro de apresentação](docs/validation/demo-presentation.md).

**Resultado informado da homologação local de 17/11/2026:** 14/14 cenários `PASS` (sem falhas), incluindo independência de Transactions, 50 RPS por cinco minutos e trace distribuído HTTP → Outbox → Consolidation. **Limite de rastreabilidade:** o manifesto local contém `commit: NOT_RECORDED` e portanto seu estado formal ainda é `NOT_ACCEPTED`; não associar os testes históricos a um SHA posterior. A [CI #577](https://github.com/wolwerr/CashFlow-Architecture/actions/runs/44163593607) aprovou Backend, Frontend, Validation e Sonar para a revisão da PR #82.

## 1. Arquitetura em poucas palavras

```text
Navegador (React + OIDC PKCE) ---> Keycloak (login)
              |
              v
         NGINX :8080
         /           \
Transactions API   Consolidation API
      |                    |
 PostgreSQL            PostgreSQL
      |                    ^
 Transactional Outbox     |
      +----> RabbitMQ ----+ (consumer idempotente)
```

Os bancos lógicos são `cashflow_transactions`, `cashflow_consolidation` e `cashflow_keycloak` no PostgreSQL local. O **Keycloak** é a fonte de usuários, grupos e permissões por comerciante; não existe quarto banco de autorização. O POST de lançamento confirma a transação e a Outbox antes de responder, **sem aguardar** o Consolidation. O saldo é atualizado assincronamente.

### Organização do repositório

```text
Backend/
  CashFlow.slnx       # Solução .NET 10
  src/                # Transactions, Consolidation e segurança compartilhada
  tests/              # Testes unitários, integração, API e arquitetura
Frontend/
  src/                # React + TypeScript
  Dockerfile           # Build do frontend e imagem NGINX
infra/                 # Keycloak, banco local e configuração NGINX
docs/                  # Requisitos, domínios, diagramas e ADRs
compose.yaml           # Inicialização local do sistema completo
```

Os caminhos do código foram reorganizados, mas os seis **nomes originais dos serviços Docker Compose continuam** `postgres`, `rabbitmq`, `keycloak`, `transactions`, `consolidation` e `web`. Os novos serviços `otel-collector`, `prometheus`, `loki`, `tempo` e `grafana` também são iniciados com o mesmo `docker compose up -d --build`. Portanto, os comandos `docker compose up -d ... web` permanecem válidos. Para instruções de testes sem Docker, consulte a [seção 9](#9-testes-automatizados-e-evidências-do-desafio).

## 2. Pré-requisitos

- **Windows:** PowerShell, Git e Docker Desktop com Docker Compose v2 (engine Linux). Mac/Linux: adapte os comandos de terminal.
- Docker em execução, memória/disco disponíveis para construir imagens .NET e frontend. É necessário acesso à internet para baixar imagens e dependências na primeira execução.
- Portas livres em `127.0.0.1`: **8080** (CashFlow), **8081** (Keycloak), **5432** (PostgreSQL) e **15672** (RabbitMQ Management). Outras portas dos serviços não são publicadas.
- **Python 3** somente para os scripts de aceite e diagnóstico; **.NET SDK/Node não são exigidos para executar a aplicação via Docker**.

Confira:

```powershell
git --version
docker version
docker compose version
```

## 3. Clonar ou atualizar

```powershell
git clone https://github.com/wolwerr/CashFlow-Architecture.git
cd CashFlow-Architecture
git checkout main
git pull origin main
```

Se já tiver o repositório, execute apenas `cd`, `git checkout main` e `git pull`.

## 4. Instalação absolutamente limpa (opcional, destrutiva)

**ATENÇÃO: executa a exclusão permanente dos dados de demonstração do projeto local.** Apaga comerciantes, usuários e configuração do realm Keycloak, lançamentos, saldos e filas presentes nos volumes **deste projeto Compose**. Não use se houver dados importantes, nem execute `docker system prune --volumes`, que pode afetar outros projetos.

Na **raiz** do repositório, confirme primeiro o projeto e volumes:

```powershell
docker compose config --services
docker compose ps -a
docker volume ls --filter label=com.docker.compose.project=cashflow-local
```

Se você realmente quer recomeçar do zero:

```powershell
docker compose down --volumes --remove-orphans
docker compose ps -a
docker volume ls --filter label=com.docker.compose.project=cashflow-local
```

`down --volumes` exclui os volumes nomeados deste Compose; os volumes de **outros projetos Docker** não são alvo. Imagens Docker compartilhadas permanecem instaladas, o que é normal. Para reconstruir sem cache, pode usar `docker compose build --no-cache` depois, se necessário.

## 5. Configurar variáveis locais

Crie o arquivo não versionado `.env`:

```powershell
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
notepad .env
```

### Grafana e observabilidade local

No `.env`, defina `CASHFLOW_GRAFANA_PASSWORD` com uma **senha forte própria**, substituindo o marcador de `.env.example`. Sem essa variável válida, o Compose não inicializa. Em instalações existentes, **adicione apenas a variável que falta**, sem substituir o `.env` e as senhas dos demais serviços.

Após concluir o bootstrap do Keycloak na [seção 7](#7-bootstrap-obrigatório-no-keycloak-ambiente-zerado), o Compose completo inicia **11 serviços**, inclusive OpenTelemetry Collector, Prometheus, Loki, Tempo e Grafana. Acesse `http://localhost:3000` como `admin` usando a senha definida; as fontes Prometheus, Loki e Tempo são provisionadas automaticamente. Consultas e verificações detalhadas estão no [runbook de observabilidade](docs/validation/local-observability-stack.md). **Não apague volumes para atualizar os contêineres.**

**Passo obrigatório ANTES de subir o Keycloak:** `.env.example` é um **modelo**, não um arquivo de credenciais funcionais. Os valores como `DEFINA_AQUI_SUA_SENHA_INICIAL_FORTE` são apenas marcadores e precisam ser trocados no `.env` local. Defina senhas próprias para `POSTGRES_PASSWORD`, `RABBITMQ_PASSWORD` e `KEYCLOAK_ADMIN_PASSWORD`. A conta técnica inicial do Keycloak usa `KEYCLOAK_ADMIN=admin` como username no realm `master` e **a senha que você escolher** em `KEYCLOAK_ADMIN_PASSWORD`.

Exemplo ilustrativo, **não copie a senha literal**:

```dotenv
KEYCLOAK_ADMIN=admin
KEYCLOAK_ADMIN_PASSWORD=<sua_senha_forte_criada_agora>
```

O Keycloak usa essa senha para criar o **administrador temporário do bootstrap** somente quando o realm `master` ainda não existe. **Não há troca automática de senha no primeiro login.** Depois de terminar a configuração inicial, crie um administrador permanente no realm `master`, confirme o acesso com ele e remova o administrador temporário. Não deixe uma senha padrão compartilhada em um repositório público nem interprete a troca de senha do usuário `cashflow.admin` como equivalente à remoção da conta temporária do `master`.

Mantenha `KEYCLOAK_AUTHZ_CLIENT_SECRET` e `KEYCLOAK_MGMT_CLIENT_SECRET` vazios **somente até concluir a etapa 7**. `.env` é ignorado pelo Git. Não cole segredos em issues, capturas ou logs.

**Não confunda:** `KEYCLOAK_ADMIN` é o administrador técnico do realm `master`; `cashflow.admin` é o usuário da plataforma no realm `cashflow`.

## 6. Inicializar PRIMEIRO PostgreSQL, RabbitMQ e Keycloak

**Não execute `docker compose up -d --build` neste momento.** Em um ambiente recém-criado, `KEYCLOAK_AUTHZ_CLIENT_SECRET` e `KEYCLOAK_MGMT_CLIENT_SECRET` ainda estão vazios. Transactions e Consolidation validam as credenciais **na inicialização** e encerram imediatamente se faltarem. O bootstrap do Keycloak precisa acontecer **antes** de subir as APIs.

Inicie somente as dependências, na raiz do repositório:

```powershell
docker compose up -d postgres rabbitmq keycloak
docker compose ps -a
docker compose logs --tail=60 keycloak
```

Aguarde o Keycloak concluir a inicialização e importar o realm `cashflow`. Um retorno normal registra `Realm 'cashflow' imported` e `Keycloak ... started`. Acesse **http://localhost:8081/admin/**, usando o username `admin` (ou o valor de `KEYCLOAK_ADMIN`) e **a senha escolhida por você** em `KEYCLOAK_ADMIN_PASSWORD` no `.env`. O Keycloak não mostrará nem exigirá que você substitua a senha automaticamente no primeiro acesso. Se o realm ainda não aparecer, confira os logs e aguarde sua inicialização em vez de recriar volumes.

Neste estágio, **somente** `postgres`, `rabbitmq` e `keycloak` precisam estar ativos. O CashFlow em http://localhost:8080 ainda não estará disponível. Configure a identidade na etapa 7 antes de iniciar o restante da aplicação.

| Aplicação | URL | Disponível nesta etapa? |
| --- | --- | --- |
| Keycloak Admin | http://localhost:8081/admin/ | Sim, após inicialização |
| RabbitMQ Management | http://localhost:15672 | Sim; usuário `cashflow` e senha `RABBITMQ_PASSWORD` |
| PostgreSQL | localhost:5432 | Sim; usuário `cashflow` e senha `POSTGRES_PASSWORD` |
| CashFlow/NGINX | http://localhost:8080 | **Ainda não**, só depois da etapa 7 |

## 7. Bootstrap obrigatório no Keycloak, ambiente zerado

A importação automática de `infra/local/keycloak/cashflow-realm.json` cria o realm `cashflow` e os clientes, **mas não fornece as credenciais e permissões administrativas necessárias**. Complete uma vez:

**A. Criar o administrador CashFlow no realm correto (Keycloak 26.2).**

1. Acesse **http://localhost:8081/admin/** e faça login com o usuário `admin` e a senha escolhida em `KEYCLOAK_ADMIN_PASSWORD`. Essa é a conta de administração do **realm master**, não um usuário do CashFlow.
2. No **canto superior esquerdo** do painel, procure **Current realm**, próximo ao nome Keycloak. Clique nele para abrir o menu. Na versão 26.2 mostrada na instalação local, o menu apresenta a opção **Manage realms**. Não confunda esse comando com **Create realm**.
3. No menu aberto, clique em **Manage realms**. Na tela com a lista de realms, clique em **cashflow**. **Não use Create realm**: o arquivo `infra/local/keycloak/cashflow-realm.json` já deve ter importado esse realm automaticamente. Após selecioná-lo, confirme que o seletor agora mostra **cashflow**. A URL de administração normalmente terá `/admin/master/console/#/cashflow/`; `master` nesse prefixo representa a sessão de administração e `cashflow` após `#/` indica o realm selecionado.
4. No menu à esquerda, com **cashflow** selecionado, abra **Users** e clique em **Create new user**.
5. Informe **Username: `cashflow.admin`**. Salve em **Create**; os demais campos de perfil não são obrigatórios para o bootstrap local salvo alguma exigência adicional configurada no realm.
6. Na página do usuário criado, abra a aba **Credentials** e clique em **Set password**. Defina uma senha forte diferente da senha do admin do `master`, confirme-a, deixe **Temporary: OFF** para permitir o primeiro login local sem troca forçada e salve a alteração.
7. Confira novamente em **Users** que `cashflow.admin` existe dentro do realm **cashflow**. Não atribua a ele papéis de administração do `master`: a capacidade de bootstrap do MVP é reconhecida pela API Transactions apenas em Development, pela configuração `CASHFLOW_BOOTSTRAP_USERNAME=cashflow.admin`.

**Se `cashflow` não aparecer no seletor:** verifique `docker compose logs --tail=100 keycloak`, procure `Realm 'cashflow' imported` e aguarde a inicialização. Também é possível conferir a página de configuração OIDC em **http://localhost:8081/realms/cashflow/.well-known/openid-configuration**; ela deve responder JSON quando o realm existir. Não crie outro realm com nome parecido e não apague volumes apenas para tentar corrigir o seletor.

**Importante:** existem duas contas diferentes: `admin` no realm `master` (administra Keycloak) e `cashflow.admin` no realm `cashflow` (usuário da aplicação com bootstrap local). A senha da primeira vem do `.env`; a segunda é criada nesta etapa e fica armazenada no Keycloak.

**B. Abrir o cliente e copiar seu secret**

1. Confirme que o realm selecionado no Keycloak é **cashflow**.
2. No menu esquerdo, abra **Clients** e clique em **cashflow-authorization-service**. O cliente já deve existir após a importação do realm; não crie outro.
3. Abra a aba **Credentials**, localize **Client secret** e copie o valor usando o botão de copiar.
4. Abra o arquivo `.env` na raiz do repositório e preencha **somente** esta variável com o secret copiado:

   ```dotenv
   KEYCLOAK_AUTHZ_CLIENT_SECRET=COLE_AQUI_O_SECRET_DE_AUTORIZACAO
   ```

5. Salve o arquivo. Substitua o marcador pelo valor real, sem aspas. Não divulgue o secret nem versione o `.env`.

**B.2. Atribuir a permissão de leitura**

1. Volte à página de **Clients > cashflow-authorization-service**.
2. Abra a aba **Service account roles**. Se essa aba não aparecer, confira em **Settings** se **Client authentication** e **Service accounts roles** estão habilitados para o cliente confidencial.
3. Clique em **Assign role** e, na janela aberta, escolha o filtro **Filter by clients** (a descrição pode variar conforme a interface).
4. Localize as roles pertencentes ao cliente **view-users** e clique em **Assign**.
5. Confira que `realm-management / view-users` aparece na lista de permissões da Service Account. Caso alguma consulta de usuários/grupos seja negada na homologação, investigue o retorno do Keycloak antes de ampliar privilégios.

**Resultado esperado:** o cliente de autorização possui um secret próprio no `.env` e apenas as permissões de consulta necessárias. **Não atribua `manage-users` ou `manage-realm` a esse cliente.**

**C. Configurar o cliente de gerenciamento (`cashflow-management-service`).**

Esse cliente é usado pela API **Transactions** para cadastrar lojas, criar usuários e administrar grupos, cargos e desativações no Keycloak. Por isso tem permissões administrativas **somente para o ambiente local de desenvolvimento**.

**C.1. Abrir o cliente e copiar seu secret**

1. Ainda no realm **cashflow**, abra **Clients > cashflow-management-service**.
2. Abra **Credentials** e copie o **Client secret** desse cliente, que é **diferente** do cliente de autorização.
3. No mesmo arquivo `.env`, preencha:

   ```dotenv
   KEYCLOAK_MGMT_CLIENT_SECRET=COLE_AQUI_O_SECRET_DE_GESTAO
   ```

4. Salve o `.env`. Não reutilize o secret de `cashflow-authorization-service`.

**C.2. Atribuir as permissões de gerenciamento**

1. Na página **Clients > cashflow-management-service**, abra **Service account roles**.
2. Clique em **Assign role** e escolha **Filter by clients**.
3. Localize as roles do cliente **realm-management**, selecione as quatro abaixo e confirme em **Assign**:

   | Role | Para que serve no bootstrap local |
   | --- | --- |
   | `manage-realm` | Operações administrativas de configuração e gerenciamento de grupos do realm |
   | `manage-users` | Criar e atualizar usuários e administrar seus vínculos |
   | `view-users`, na segunda tela | Consultar usuários existentes |
   | `query-users`, na segunda tela | Pesquisar e localizar usuários |

4. Volte à lista **Service account roles** e confirme que as quatro roles estão atribuídas ao cliente correto.

**Resultado esperado:** `cashflow-management-service` possui seu próprio secret no `.env` e as quatro roles listadas. Essas permissões **não** devem ser atribuídas ao cliente público `cashflow-spa` nem aos usuários comerciantes.

**Antes de continuar:** confirme que copiou um secret diferente para cada variável e que ambos foram salvos no `.env`. Não inicie Transactions e Consolidation até concluir esses passos.

**D. Completar o `.env`.** Sem incluir aspas e sem publicar valores:

```dotenv
CASHFLOW_BOOTSTRAP_USERNAME=cashflow.admin
KEYCLOAK_AUTHZ_CLIENT_SECRET=<secret_do_cliente_de_autorizacao>
KEYCLOAK_MGMT_CLIENT_SECRET=<secret_do_cliente_de_gestao>
```

**Dica:** as duas variáveis `KEYCLOAK_AUTHZ_CLIENT_SECRET` e `KEYCLOAK_MGMT_CLIENT_SECRET` devem estar preenchidas antes de iniciar as APIs. Como os clientes de serviço são confidenciais, obtenha cada segredo em **Clients > [cliente] > Credentials**. Não altere o valor do `Client ID` nem use o segredo de outro cliente.

**E. Subir a aplicação e a observabilidade somente agora:**

```powershell
docker compose up -d --build
docker compose ps -a
docker compose logs --tail=80 transactions consolidation web
```

Os **11 serviços** devem iniciar no Compose (incluindo os cinco de observabilidade). O NGINX serve a interface e encaminha as chamadas às APIs. Confirme as verificações HTTP pelo PowerShell:

```powershell
curl.exe -i http://localhost:8080/nginx-health
docker compose exec transactions printenv KeycloakManagement__BootstrapUsername
```

Se uma API encerrar com `Keycloak authorization service credentials and realm are required`, confira se salvou `.env` na raiz e se `KEYCLOAK_AUTHZ_CLIENT_SECRET` contém o segredo do cliente de autorização. **Não publique seu `.env` nem imprima os segredos no terminal**. Após corrigir, recrie apenas as APIs:

```powershell
docker compose up -d --force-recreate --no-deps transactions consolidation
```

Se a API iniciar, mas retornar `503` em operações autorizadas, confira as **Service account roles** dos dois clientes no Keycloak; segredo presente não garante permissão de consulta.

**F. Fazer o primeiro login com `cashflow.admin` e concluir o cadastro do perfil.**

1. Depois de confirmar que `transactions`, `consolidation` e `web` estão ativos em `docker compose ps`, abra **http://localhost:8080** em uma janela anônima do navegador.
2. Clique para entrar, se a tela de autenticação não abrir automaticamente. Você será direcionado ao **Keycloak**, no realm **cashflow**.
3. Preencha o formulário de login com:
   - **Username:** `cashflow.admin`
   - **Password:** a senha que **você definiu em Users > cashflow.admin > Credentials > Set password** quando criou esse usuário na etapa **A**. **Não** é a senha `KEYCLOAK_ADMIN_PASSWORD` do arquivo `.env`, pois essa senha pertence ao administrador técnico do realm `master`.
4. **No primeiro acesso**, conforme o comportamento observado nesta instalação do Keycloak, será exibida a tela **Update Account Information**. Antes de entrar na aplicação, preencha todos os campos obrigatórios marcados com `*`:
   
   | Campo na tela | O que informar |
   | --- | --- |
   | **Username** | `cashflow.admin` (já preenchido; mantenha esse valor) |
   | **Email** | Um endereço de e-mail válido para a conta administrativa local |
   | **First name** | Nome do administrador de teste |
   | **Last name** | Sobrenome do administrador de teste |
   
   Clique em **Submit** para concluir a atualização do perfil. Se algum campo ficar vazio, o Keycloak mostrará **Please specify this field** e não permitirá continuar.
5. Após enviar o formulário, o Keycloak deve redirecionar o navegador de volta ao **CashFlow** em `http://localhost:8080`. Confira que está autenticado como `cashflow.admin` e que a área de **administração de comerciantes** aparece.
6. Se houver erro de login ou o redirecionamento não funcionar, confira se o usuário está **Enabled** no realm `cashflow`, se a senha foi definida e se o perfil foi preenchido. Consulte `docker compose logs --tail=100 keycloak transactions web` e, no DevTools do navegador, verifique a resposta de `GET /api/v1/merchants/permissions` sem copiar tokens nem segredos.

**Observação:** a tela `Update Account Information` é uma etapa de conclusão de perfil exigida pela configuração atual do Keycloak. Não confunda com a troca de senha: quando o usuário foi criado com **Temporary: OFF**, a senha definida na etapa A continua sendo usada no login. A solicitação de preenchimento do perfil pode variar se os campos já tiverem sido informados previamente.

A opção **Edit username** do realm novo está habilitada no JSON de importação. Em realms já existentes, o import não sobrescreve essa preferência; altere manualmente nas configurações de Login do realm.

Para configurar o Keycloak do zero e diagnosticar falhas de permissões, siga as etapas 5 a 7 deste README e consulte a [ADR-006](docs/architecture/adr/ADR-006-identity-and-access.md).

## 8. Roteiro de uso e demonstração

1. Como `cashflow.admin`, acesse **Gestão de lojas** e clique em **Cadastrar nova loja** para abrir o formulário de cadastro. Ele começa recolhido para simplificar a tela. Cadastre duas lojas. Neste MVP, **comerciante é a loja**, não a pessoa que fará login. No campo **Nome da nova loja**, digite `Loja A`. Em **Usuário de acesso da loja**, informe um login exclusivo, por exemplo `loja.a`. Em **Senha inicial do usuário da loja**, defina uma senha de 12 a 128 caracteres e clique em **Cadastrar loja e usuário**. Repita com `Loja B`, username `loja.b` e outra senha. O username é único no realm inteiro, e o cadastro cria a loja e sua conta de acesso. A opção **Adicionar administrador** também abre um formulário apenas quando clicada, desde que uma loja administrável esteja selecionada. A lista **Lojas cadastradas** e os seletores ficam em ordem alfabética.
2. Conferir a lista administrativa com loja, usuário, cargo e status. Editar username/cargo, desativar e reativar usuário de teste. Soft delete usa o grupo `INACTIVE`, sem exclusão física de histórico.
3. Sair e entrar como usuário OPERATOR da Loja A; ele deve ver apenas **Novo lançamento, Saldo Diário, Histórico**, sem administração. Confirmar que não enxerga dados da Loja B.
4. Na Loja A sem saldo anterior, registrar **CREDIT R$ 100,00** e **DEBIT R$ 25,00** para a mesma data. A confirmação da transação precede a consolidação: aguardar o processamento normal do RabbitMQ. Conferir duas linhas de histórico, crédito total R$ 100,00, débito total R$ 25,00, movimento líquido e saldo acumulado **R$ 75,00**.
5. Conferir competência anterior e dia sem lançamentos, histórico paginado, bloqueio de data futura, valor zero/negativo, autorização e idempotência. Não refazer POST por atraso do saldo.

Para homologar, teste dois usuários de lojas diferentes, confirme o isolamento de dados e registre os resultados. Os requisitos estão na [matriz de atendimento](docs/requirements/challenge-compliance-matrix.md).

## 9. Testes automatizados e evidências do desafio

### 9.1. Resultado automático da CI

Confira os resultados mais recentes no [GitHub Actions](https://github.com/wolwerr/CashFlow-Architecture/actions/workflows/local-build.yml); a execução citada na [auditoria final](docs/validation/final-delivery-evidence.md) é um registro histórico e não garante que a `main` atual esteja verde.

O selo acima reflete o **estado atual da pipeline da branch `main`**, não um resultado de teste manual. Clique nele para abrir o GitHub Actions, selecione a execução mais recente e confira os jobs **Backend | Build and tests**, **Frontend | Build and tests**, **Validation | Offline contracts and Compose** e **Sonar | Backend and frontend**. É possível verificar cada passo, os relatórios de testes e a geração dos relatórios de cobertura na própria execução, sem necessidade de capturas estáticas no README. Um selo verde indica sucesso da execução, mas não comprova sozinho os cenários de falha e carga descritos abaixo.

### Cobertura de código, evidência por execução

A pipeline apresenta **um único percentual consolidado de cobertura de linhas para todo o backend .NET**, unindo os seis relatórios OpenCover (testes unitários, de integração e de API de Transactions e Consolidation). O cálculo considera cada linha instrumentada uma única vez por assembly/arquivo e a marca como coberta se **qualquer** suíte a executou. Não é média de percentuais.

O resumo do job **Backend | Build and tests** exibe esse percentual global, junto da quantidade de linhas cobertas e instrumentadas. O job **Frontend | Build and tests** também exibe **uma cobertura de linhas** calculada do LCOV do Vitest.

**Onde consultar o valor atualizado:** abra [GitHub Actions](https://github.com/wolwerr/CashFlow-Architecture/actions/workflows/local-build.yml), selecione a execução e consulte os resumos de **Backend .NET Coverage Report** e **Frontend Coverage Report**. Os relatórios originais continuam disponíveis como artifacts temporários.

**Meta de 80%:** a CI ainda exige 80% de cobertura em camadas selecionadas do backend; a **cobertura global consolidada é uma métrica adicional** e pode ficar abaixo de 80% sem que esses limites específicos tenham falhado. Não confundir aprovação dos testes com percentual de cobertura, nem atribuir a uma execução futura o valor histórico de outra.

### 9.2. Resultado documentado de execução local do backend

Na execução local de **17/11/2026**, em Windows PowerShell com **.NET 10**, o terminal apresentou os seguintes resultados para `dotnet test Backend/CashFlow.slnx -c Release --no-build` (após configurar bancos PostgreSQL exclusivos de teste):

| Projeto de testes | Aprovados | Falhas |
| --- | ---: | ---: |
| CashFlow.Transactions.ApiTests | 103 | 0 |
| CashFlow.Transactions.UnitTests | 38 | 0 |
| CashFlow.Transactions.IntegrationTests | 26 | 0 |
| CashFlow.Consolidation.ApiTests | 5 | 0 |
| CashFlow.Consolidation.UnitTests | 12 | 0 |
| CashFlow.Consolidation.IntegrationTests | 23 | 0 |
| CashFlow.ArchitectureTests | 16 | 0 |
| **Total** | **223** | **0** |

**Resultado:** 223 testes executados e aprovados, 0 falhas, 0 ignorados, código de saída 0, conforme saída do terminal compartilhada pelo responsável pelo projeto. Esta tabela é um **registro de execução local informado**, não um relatório de CI vinculado a commit e não uma medição de cobertura de código. Para evidência auditável, associe a captura real, o log da execução e o SHA do commit testado.

### 9.3. Resultado documentado de execução local do frontend

Na execução local de **17/11/2026**, em Windows PowerShell, Node.js 24.21.0/npm e Vitest 5.1.0, o comando `npm run test:coverage` em `Frontend/` apresentou:

| Indicador | Resultado |
| --- | ---: |
| Arquivos de teste | 9 aprovados |
| Testes | **169 aprovados, 0 falhas** |
| Cobertura de linhas | **88,30%** |
| Cobertura de instruções | 84,51% |
| Cobertura de ramificações | 84,22% |
| Cobertura de funções | 80,83% |

O build `tsc -b && vite build` também concluiu com sucesso em **Vite 6.4.6**. O `npm audit` e `npm audit --omit=dev` informaram **0 vulnerabilidades** no ambiente local após a atualização das dependências.

**Total backend + frontend:** **392 testes aprovados**, 0 falhas, em execuções locais separadas. Os 223 testes .NET e os 169 testes Vitest passaram, mas **100% de aprovação de testes não significa 100% de cobertura de código**. Estes números são provenientes das saídas de terminal compartilhadas pelo responsável pelo projeto; para evidência auditável, incluir a captura real, os relatórios e o SHA correspondente.

**Pontos de atenção:** `src/features/app/useCashFlowController.ts` apresentou **76,07% de cobertura de linhas** após a migração do Vitest 3 para 5, abaixo de 80% individualmente, embora as quatro métricas globais estejam acima de 80%. A atualização também alterou os percentuais de cobertura (antes eram 95,48% de linhas); isso não significa, por si só, perda de testes. O `npm audit` anterior apontava 7 vulnerabilidades (5 moderadas e 2 críticas), mas a nova verificação local informou 0. **As porcentagens locais registradas devem ser distinguidas das medições produzidas por cada execução da CI.**

### 9.4. Rodar os testes automatizados localmente

Para executar os testes .NET, com o SDK .NET 10 instalado e os serviços de integração necessários disponíveis:

```powershell
dotnet restore Backend/CashFlow.slnx
dotnet build Backend/CashFlow.slnx -c Release
dotnet test Backend/CashFlow.slnx -c Release --no-build
```

Para executar os testes de frontend, com Node.js 22 e npm instalados:

```powershell
cd Frontend
npm ci
npm run test:coverage
cd ..
```

**Alternativa sem SDK .NET e Node.js instalados localmente:** acompanhe o build, os testes, a cobertura e os Docker builds pelo [GitHub Actions](https://github.com/wolwerr/CashFlow-Architecture/actions/workflows/local-build.yml). O Docker Compose serve para executar o sistema, não substitui automaticamente os testes da CI. Testes de integração podem exigir PostgreSQL e RabbitMQ, que são provisionados especificamente no workflow de CI.

### 9.5. Aceite funcional, falha e desempenho

O script `local_mvp_acceptance.py` **não executa testes**, não acessa Docker e não valida as APIs. Ele apenas cria ou lê um **manifesto de evidências** para 14 cenários que deverão ser executados e comprovados separadamente. Para criar o manifesto na primeira utilização (Python 3):

```powershell
python .github/scripts/local_mvp_acceptance.py --init local-acceptance.json
```

**Se `local-acceptance.json` já existir, pule o comando acima.** Ele usa criação exclusiva e retorna `Acceptance report failed: FileExistsError` se o arquivo já existe. Isso evita sobrescrever registros e **não indica falha dos testes da aplicação**. Para apenas consultar o manifesto existente:

```powershell
python .github/scripts/local_mvp_acceptance.py --manifest local-acceptance.json
```

Em um manifesto recém-criado, o resultado esperado é `outcome: NOT_ACCEPTED`, `NOT_RUN: 14` e `PASS: 0`; o script retorna código 1 até que os 14 cenários estejam marcados como `PASS`, tenham referências de evidência e o manifesto registre o SHA do commit. **Não marque PASS sem executar o cenário correspondente e guardar evidência real.** O manifesto não inspeciona a veracidade do conteúdo das evidências, apenas sua estrutura e, quando aplicável, a existência de arquivos. Os relatórios locais são ignorados pelo Git.

Para verificar apenas os containers e filas, execute o diagnóstico abaixo. O resultado `outcome: OBSERVED` significa que os serviços foram encontrados em execução e as filas puderam ser consultadas, **não que os testes funcionais tenham passado**.

**Diagnóstico somente de leitura:**

```powershell
python .github/scripts/local_operational_snapshot.py --execute
```

**Teste ponta a ponta de resiliência financeira (LOCAL, opt-in):** o script `.github/scripts/local_resilience_e2e.py` combina uma interrupção real do container Consolidation com um registro financeiro autenticado em Transactions e a verificação da projeção após a restauração. **Ele grava um crédito real de R$ 1,00, permanente no histórico financeiro**, portanto use apenas uma loja de testes descartável, nunca dados reais/produção. O teste exige um JWT válido para esse comerciante, definido somente na variável local `CASHFLOW_RESILIENCE_TOKEN`. Não cole o token em issues, prints ou commits. A execução não usa Azure.

Na raiz do repositório, em PowerShell, substitua o UUID pelo de um **comerciante de teste já cadastrado** e a data por hoje ou outra data válida:

```powershell
# Visualização segura: não exige token, não altera containers nem banco.
python .github/scripts/local_resilience_e2e.py --merchant-id "UUID-DO-COMERCIANTE-DE-TESTE" --date "2026-11-17"

# Na mesma janela PowerShell, configure o JWT do usuário autorizado de teste sem salvá-lo no histórico:
$env:CASHFLOW_RESILIENCE_TOKEN = Read-Host "Cole o JWT do usuário de teste"
python .github/scripts/local_resilience_e2e.py --merchant-id "UUID-DO-COMERCIANTE-DE-TESTE" --date "2026-11-17" --outage-seconds 15 --recovery-timeout 90 --execute
Remove-Item Env:CASHFLOW_RESILIENCE_TOKEN
```

A execução confirma (1) membership e saldo antes da interrupção, (2) Consolidation realmente parado, (3) HTTP `201` em Transactions durante a falha, (4) restauração no bloco `finally`, (5) uma única transação no histórico, e (6) incremento exato de R$ 1,00 no saldo após a recuperação. O relatório local é salvo em `reports/resilience-local.json`, sem token ou IDs; execute **apenas no projeto Compose local**. O status `PASS` só é emitido após essas verificações. Este teste não substitui o teste de carga nem prova disponibilidade contínua por períodos maiores.

**Evidência local relatada em 17/11/2026 (UTC 14:55:28):** execução com `--execute` retornou código `0` e `outcome: PASS`. O relatório exibido confirmou saldo inicial consultado, Consolidation parado durante um `POST` que retornou HTTP `201`, restauração do container, unicidade da transação no histórico e incremento exato do saldo. Janela solicitada: 15 segundos. Trata-se de resultado observado e fornecido pelo executor local, **não de um teste destrutivo realizado pela CI**; o JSON original permanece no ambiente local em `reports/resilience-local.json`.

**Resiliência e carga:** o desafio impõe que uma falha apenas de Consolidation **não indisponibilize** o registro em Transactions e que o consolidado suporte **50 requisições/segundo com até 5% sem resposta útil**. Para testar esses requisitos, consulte:

- Simulação de falha de Consolidation e recuperação do RabbitMQ: scripts `.github/scripts/local_recovery_drill.py` (somente com `--execute` e dados locais descartáveis).
- Carga de 50 RPS durante cinco minutos: script `.github/scripts/local_load_probe.py` (exige execução explícita e coleta dos resultados).
- Frescor do consolidado: `.github/scripts/local_projection_slo.py` (meta adicional: 95% até 30 segundos).
- Inventário de auditoria, backup e restauração: scripts locais em `.github/scripts/`. Evite executar ações destrutivas sem revisar as opções de cada script.
- Requisitos e evidências exigidas para apresentar o desafio: [matriz de atendimento](docs/requirements/challenge-compliance-matrix.md).

Esses ensaios são **opt-in**, usam dados descartáveis e podem interromper temporariamente serviços. Não são executados automaticamente ao subir o Compose ou pela CI. Para 50 RPS, interpretar a perda como consultas válidas sem resposta útil é uma **premissa explícita do projeto**. O p95 inferior a 500 ms é um **SLO adicional do MVP**, não uma exigência textual do desafio. Nenhuma métrica é considerada comprovada sem relatório real.

## 10. Documentação do desafio

A [matriz de rastreabilidade do enunciado](docs/requirements/challenge-compliance-matrix.md) relaciona os requisitos do PDF à implementação, às decisões arquiteturais e às evidências disponíveis, indicando limitações quando existirem.

| Assunto | Documento |
| --- | --- |
| Capacidades e domínios | [Context map](docs/domain/business-capabilities-and-context-map.md) |
| Requisitos funcionais | [RF-01 a RF-07](docs/requirements/functional-requirements.md) |
| Requisitos não funcionais | [RNF e SLIs/SLOs](docs/requirements/non-functional-requirements.md) |
| Arquitetura alvo e C4 | [Building Block View](docs/architecture/building-block-view.md) |
| Integrações e decisões | [ADRs](docs/architecture/adr/README.md) e [sequências](docs/architecture/sequence-diagrams.md) |
| Custos estimados, não provisionados | [Planejamento Azure](docs/architecture/four-environment-total-cost-plan.md) |
| Homologação do MVP local | [Matriz de atendimento e critérios de comprovação](docs/requirements/challenge-compliance-matrix.md) |

**Arquitetura de transição:** o desafio não informa sistema legado; documentamos que não há migração identificada a desenhar, em vez de inventar um estado de origem.

## 11. Parar, atualizar e reiniciar

```powershell
# Parar containers preservando os dados
docker compose down

# Iniciar novamente sem apagar volumes
docker compose up -d --build

# Conferir estado e logs
docker compose ps
docker compose logs --tail=100 transactions consolidation
```

Para **apagar novamente toda a instalação local**, consulte a etapa 4 e confirme que não precisa dos dados. Não use `docker compose down --volumes` como rotina de atualização. Nunca publique o `.env` ou senhas de teste.


