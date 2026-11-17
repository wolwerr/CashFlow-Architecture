# CashFlow | Desafio de Arquitetura de Soluções

[![GitHub Actions](https://github.com/wolwerr/CashFlow-Architecture/actions/workflows/local-build.yml/badge.svg)](https://github.com/wolwerr/CashFlow-Architecture/actions/workflows/local-build.yml)

Aplicação de fluxo de caixa multicomerciante: registro de **créditos e débitos** e consulta do **saldo diário consolidado**. Este repositório contém código, testes, decisões, diagramas e procedimentos locais. O desafio exige independência entre lançamentos e consolidado, além de **50 consultas/segundo** com **no máximo 5% de falhas**. A conformidade operacional desses limites ainda depende de evidências de homologação, não apenas da CI.

> **Ambiente desta entrega:** Docker Compose em máquina local. A arquitetura Azure AKS é **plano futuro**, não infraestrutura provisionada. Recursos opcionais de observabilidade estão decididos na arquitetura, mas não fazem parte dos seis serviços do Compose abaixo.

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
Copy-Item .env.example .env
notepad .env
```

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
5. Informe **Username: `cashflow.admin`**. Mantenha **Enabled: ON**. Salve em **Create**; os demais campos de perfil não são obrigatórios para o bootstrap local salvo alguma exigência adicional configurada no realm.
6. Na página do usuário criado, abra a aba **Credentials** e clique em **Set password**. Defina uma senha forte diferente da senha do admin do `master`, confirme-a, deixe **Temporary: OFF** para permitir o primeiro login local sem troca forçada e salve a alteração.
7. Confira novamente em **Users** que `cashflow.admin` existe e está **Enabled** dentro do realm **cashflow**. Não atribua a ele papéis de administração do `master`: a capacidade de bootstrap do MVP é reconhecida pela API Transactions apenas em Development, pela configuração `CASHFLOW_BOOTSTRAP_USERNAME=cashflow.admin`.

**Se `cashflow` não aparecer no seletor:** verifique `docker compose logs --tail=100 keycloak`, procure `Realm 'cashflow' imported` e aguarde a inicialização. Também é possível conferir a página de configuração OIDC em **http://localhost:8081/realms/cashflow/.well-known/openid-configuration**; ela deve responder JSON quando o realm existir. Não crie outro realm com nome parecido e não apague volumes apenas para tentar corrigir o seletor.

**Importante:** existem duas contas diferentes: `admin` no realm `master` (administra Keycloak) e `cashflow.admin` no realm `cashflow` (usuário da aplicação com bootstrap local). A senha da primeira vem do `.env`; a segunda é criada nesta etapa e fica armazenada no Keycloak.

**B. Cliente de autorização, apenas leitura.** Em **Clients > cashflow-authorization-service > Credentials**, copie o **Client secret**. Em **Service account roles**, atribua no cliente `realm-management` somente **`view-users`** (e acesso de consulta estritamente necessário caso sua versão do Keycloak o exija). Esse cliente não pode gerenciar usuários ou grupos.

**C. Cliente de gestão.** Em **Clients > cashflow-management-service > Credentials**, copie o **Client secret**. Em **Service account roles**, atribua, no cliente `realm-management`, **`manage-realm`**, **`manage-users`**, **`view-users`** e **`query-users`** conforme o contrato local de provisionamento. Essas permissões são amplas e **apenas de desenvolvimento**, nunca devem ser dadas à SPA.

**D. Completar o `.env`.** Sem incluir aspas e sem publicar valores:

```dotenv
KEYCLOAK_AUTHZ_CLIENT_SECRET=<secret_do_cliente_de_autorizacao>
KEYCLOAK_MGMT_CLIENT_SECRET=<secret_do_cliente_de_gestao>
CASHFLOW_BOOTSTRAP_USERNAME=cashflow.admin
```

**Dica:** as duas variáveis `KEYCLOAK_AUTHZ_CLIENT_SECRET` e `KEYCLOAK_MGMT_CLIENT_SECRET` devem estar preenchidas antes de iniciar as APIs. Como os clientes de serviço são confidenciais, obtenha cada segredo em **Clients > [cliente] > Credentials**. Não altere o valor do `Client ID` nem use o segredo de outro cliente.

**E. Subir os serviços da aplicação somente agora:**

```powershell
docker compose up -d --build transactions consolidation web
docker compose ps -a
docker compose logs --tail=80 transactions consolidation web
```

Os seis serviços devem aparecer ativos. O NGINX é iniciado junto com as APIs. Confirme as verificações HTTP pelo PowerShell:

```powershell
curl.exe -i http://localhost:8080/nginx-health
docker compose exec transactions printenv KeycloakManagement__BootstrapUsername
```

Se uma API encerrar com `Keycloak authorization service credentials and realm are required`, confira se salvou `.env` na raiz e se `KEYCLOAK_AUTHZ_CLIENT_SECRET` contém o segredo do cliente de autorização. **Não publique seu `.env` nem imprima os segredos no terminal**. Após corrigir, recrie apenas as APIs:

```powershell
docker compose up -d --force-recreate --no-deps transactions consolidation
```

Se a API iniciar, mas retornar `503` em operações autorizadas, confira as **Service account roles** dos dois clientes no Keycloak; segredo presente não garante permissão de consulta.

**F. Validar o login.** Abra `http://localhost:8080` em janela anônima, autentique como `cashflow.admin` do realm `cashflow` e confirme a área administrativa. Se não aparecer, use `docker compose logs --tail=100 transactions` e confira `GET /api/v1/merchants/permissions` no DevTools, sem expor tokens. Se o login exigir configuração de usuário, confira Enabled, senha, ações obrigatórias e realm selecionado.

A opção **Edit username** do realm novo está habilitada no JSON de importação. Em realms já existentes, o import não sobrescreve essa preferência; altere manualmente nas configurações de Login do realm.

Para mais detalhes e diagnóstico: [bootstrap de comerciantes e permissões](docs/implementation/merchant-onboarding.md).

## 8. Roteiro de uso e demonstração

1. Como `cashflow.admin`, cadastrar **Loja A** e **Loja B** com usernames diferentes, senhas iniciais distintas de 12 a 128 caracteres e nomes visíveis. O username é único no realm, não por loja.
2. Conferir a lista administrativa com loja, usuário, cargo e status. Editar username/cargo, desativar e reativar usuário de teste. Soft delete usa o grupo `INACTIVE`, sem exclusão física de histórico.
3. Sair e entrar como usuário OPERATOR da Loja A; ele deve ver apenas **Novo lançamento, Saldo Diário, Histórico**, sem administração. Confirmar que não enxerga dados da Loja B.
4. Na Loja A sem saldo anterior, registrar **CREDIT R$ 100,00** e **DEBIT R$ 25,00** para a mesma data. A confirmação da transação precede a consolidação: aguardar o processamento normal do RabbitMQ. Conferir duas linhas de histórico, crédito total R$ 100,00, débito total R$ 25,00, movimento líquido e saldo acumulado **R$ 75,00**.
5. Conferir competência anterior e dia sem lançamentos, histórico paginado, bloqueio de data futura, valor zero/negativo, autorização e idempotência. Não refazer POST por atraso do saldo.

Todos os cenários de homologação, incluindo dois tokens e isolamento real, estão no [roteiro da Trilha 08](docs/implementation/trilha-08-local-acceptance.md).

## 9. Testes automatizados e evidência de requisitos não funcionais

**CI:** [GitHub Actions](https://github.com/wolwerr/CashFlow-Architecture/actions) executa build, testes e Sonar. Para executar o teste offline do aceite, com Python 3 instalado:

```powershell
python .github/scripts/local_mvp_acceptance.py --init local-acceptance.json
python .github/scripts/local_mvp_acceptance.py --manifest local-acceptance.json
```

A primeira execução cria um manifesto com 14 cenários `NOT_RUN`; a segunda retorna `NOT_ACCEPTED` até haver evidências reais. Execute `--init` **uma única vez**, pois ele não sobrescreve arquivos existentes. Os relatórios locais são ignorados pelo Git.

**Diagnóstico somente de leitura:**

```powershell
python .github/scripts/local_operational_snapshot.py --execute
```

**Resiliência e carga:** o desafio impõe que uma falha apenas de Consolidation **não indisponibilize** o registro em Transactions e que o consolidado suporte **50 requisições/segundo com até 5% sem resposta útil**. Para testar esses requisitos, consulte:

- [Drills locais, broker e consumer](docs/implementation/local-recovery-drills.md)
- [Teste de carga de 50 RPS por 5 minutos](docs/implementation/local-hardening-load.md)
- [SLO de projeção 95% em até 30 segundos](docs/implementation/local-slo-observability.md)
- [Auditoria, integridade e recuperação](docs/implementation/local-audit-e2e-validation.md)
- [Backup e restauração PostgreSQL](docs/implementation/local-postgres-backup-verification.md)

Esses ensaios são **opt-in**, usam dados descartáveis e podem interromper temporariamente serviços. Não são executados automaticamente ao subir o Compose ou pela CI. Para 50 RPS, interpretar a perda como consultas válidas sem resposta útil é uma **premissa explícita do projeto**. O p95 inferior a 500 ms é um **SLO adicional do MVP**, não uma exigência textual do desafio. Nenhuma métrica é considerada comprovada sem relatório real.

## 10. Documentação do desafio

A [matriz de rastreabilidade do enunciado](docs/requirements/challenge-compliance-matrix.md) relaciona cada requisito do PDF à implementação, às decisões e às evidências que ainda precisam ser coletadas.

| Assunto | Documento |
| --- | --- |
| Capacidades e domínios | [Context map](docs/domain/business-capabilities-and-context-map.md) |
| Requisitos funcionais | [RF-01 a RF-07](docs/requirements/functional-requirements.md) |
| Requisitos não funcionais | [RNF e SLIs/SLOs](docs/requirements/non-functional-requirements.md) |
| Arquitetura alvo e C4 | [Building Block View](docs/architecture/building-block-view.md) |
| Integrações e decisões | [ADRs](docs/architecture/adr/README.md) e [sequências](docs/architecture/sequence-diagrams.md) |
| Custos estimados, não provisionados | [Planejamento Azure](docs/architecture/four-environment-total-cost-plan.md) |
| Homologação do MVP local | [Trilha 08](docs/implementation/trilha-08-local-acceptance.md) e [fechamento técnico](docs/implementation/trilha-09-mvp-closeout.md) |

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

## Status do projeto

**Código e documentação entregues**, com homologação final dos requisitos do desafio **pendente de execução real local**, sobretudo falha de Consolidation, pico de 50 RPS, recuperação e integridade. Veja a [matriz de atendimento](docs/requirements/challenge-compliance-matrix.md) para distinguir decisão arquitetural, implementação e evidência comprovada.
