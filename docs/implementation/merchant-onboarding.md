# Merchant onboarding local: cadastro e administradores no CashFlow

## Primeiro ADMIN da plataforma: bootstrap local (uma única vez)

Há **três conceitos diferentes** no Keycloak e no CashFlow:

| Conta ou grupo | Onde existe | Função |
| --- | --- | --- |
| `admin` do realm **master** | Administração do Keycloak | Configurar o IAM e criar o primeiro usuário do CashFlow; **não** é o administrador financeiro |
| `cashflow.admin` no realm **cashflow** | Usuário do CashFlow criado manualmente no bootstrap inicial | Administrador **da plataforma** no MVP local, autorizado pelo `CASHFLOW_BOOTSTRAP_USERNAME` assinado no token |
| `/merchants/{uuid}/ADMIN` ou `OPERATOR` | Grupos do realm cashflow | Permissões **dentro de uma loja**, distintas do papel de administrador da plataforma |

**Como criar o primeiro ADMIN em um ambiente novo, sem acesso ADMIN prévio:**

1. Em um ambiente do zero, iniciar **somente** `docker compose up -d postgres rabbitmq keycloak` (não executar `docker compose up -d --build` ainda). Acessar `http://localhost:8081/admin/` e fazer login na conta administrativa do realm **master**. O motivo é que Transactions e Consolidation exigem `KEYCLOAK_AUTHZ_CLIENT_SECRET` já configurado para iniciar.
2. Selecionar o realm **cashflow**, entrar em **Users > Add user** e criar o usuário `cashflow.admin`, com **Enabled** ligado e email verificado quando aplicável. A conta do Keycloak no realm master não é reutilizada como login do CashFlow.
3. Em **Credentials > Set password**, definir a senha inicial do administrador, evitar as senhas de exemplo em ambientes compartilhados e, para o bootstrap local de teste, ajustar a flag Temporary conforme a política escolhida.
4. Em `.env`, configurar `CASHFLOW_BOOTSTRAP_USERNAME=cashflow.admin`. Esta variável já tem esse valor como padrão em `compose.yaml`, mas deve corresponder exatamente ao username criado.
5. Configurar uma única vez o `cashflow-management-service`, a sua Service Account e `KEYCLOAK_MGMT_CLIENT_SECRET`, segundo a seção abaixo. Manter o cliente de autorização somente leitura separado.
6. Na primeira inicialização após configurar os dois secrets, executar `docker compose up -d --build transactions consolidation web`. Em atualizações posteriores do `.env`, recriar Transactions e Consolidation com `docker compose up -d --force-recreate --no-deps transactions consolidation`.
7. Entrar em `http://localhost:8080` com **cashflow.admin**. A interface deve mostrar **Administrador** e o username, com gestão de comerciantes. O endpoint autenticado `GET /api/v1/merchants/permissions` deve retornar `{"can_manage_merchants":true}`.
8. Daí em diante, cadastrar cada **comerciante** (nome, username exclusivo e senha inicial) pela tela de ADMIN; o Keycloak cria a conta OPERATOR, sem voltar ao painel de administração.

**Por que não há cadastro público do primeiro ADMIN?** Permitir que o primeiro visitante se autodeclare administrador criaria um risco de *privilege escalation*. O bootstrap restrito é proposital. Em produção, substituir a comparação por username por um vínculo administrativo explícito, processo de provisionamento auditável e controles fortes de acesso.

**Se `cashflow.admin` aparecer como COMERCIANTE:** a UI deve mostrar o **username autenticado**. Conferir se a sessão realmente é `cashflow.admin` e se `CASHFLOW_BOOTSTRAP_USERNAME` foi aplicada ao container Transactions. Para verificar o valor configurado sem expor segredos: `docker compose exec transactions printenv KeycloakManagement__BootstrapUsername`. Se estiver correto, verificar no Network o retorno `GET /api/v1/merchants/permissions`. Falhas nessa consulta devem exibir **perfil indisponível**, sem classificar o usuário automaticamente como comerciante.

> **Como trocar de realm no console Keycloak 26.2:** após fazer login em `http://localhost:8081/admin/`, clique no seletor **Current realm** no canto superior esquerdo da navegação, escolha **Manage realms**, clique em **cashflow** na lista de realms e confirme o realm selecionado antes de abrir **Users > Add user**. Se o realm não aparecer, confira `docker compose logs --tail=100 keycloak` e a importação do realm. Para as telas e o bootstrap completo, siga o [README da raiz](../../README.md#7-bootstrap-obrigatório-no-keycloak-ambiente-zerado). Não use **Create realm**, pois o realm `cashflow` é importado automaticamente.\n\n## Perfis de acesso do MVP

**ADMIN de plataforma (bootstrap local)**: entra com a conta `cashflow.admin` no Keycloak e acessa gestão de comerciantes, cadastro de contas e as três áreas financeiras (Novo lançamento, Saldo Diário, Histórico). O backend verifica o `preferred_username` assinado contra `CASHFLOW_BOOTSTRAP_USERNAME`, em Development. Não basta escolher ADMIN na interface.

**COMERCIANTE (OPERATOR)**: o administrador cadastra **nome da loja, username único e senha inicial**, a API cria o usuário no Keycloak e associa exclusivamente ao grupo `/merchants/{uuid}/OPERATOR`. O comerciante entra usando essas próprias credenciais no mesmo provedor OIDC e vê **somente Novo lançamento, Saldo Diário e Histórico**. Não pode criar comerciantes nem gerenciar contas. As APIs validam o vínculo atual com o merchant, inclusive contra tentativas de chamada direta sem permissão.

O Keycloak mantém as senhas; o CashFlow não as armazena. O username é único em todo o realm `cashflow`, não apenas na loja. Uma tentativa de cadastrar username já existente retorna HTTP 409 e não cria novo comerciante. Para este MVP local, a senha inicial tem **mínimo de 12 e máximo de 128 caracteres**, sem exigência de troca imediata, para permitir login no teste.

### Evoluções de segurança e domínio documentadas

- Endurecer senhas antes de produção com comprimento maior conforme política, bloqueio de credenciais comprometidas, prevenção de reutilização, MFA e recuperação segura, sem armazenar senhas em serviços CashFlow.
- Implementar **unicidade transacional de usernames**, idempotência e rollback/compensação do cadastro composto, além da checagem prévia de username no Keycloak; evitar comerciantes órfãos após falha intermediária.
- Expandir os campos de comerciante com dados fiscais, razão social, CNPJ, contato e endereço **quando houver necessidade**, em modelo de domínio próprio. Por ora, apenas nome.
- Tratar convites, troca obrigatória da senha de bootstrap e auditoria de cadastro/acessos, e desenho de papéis para produção.
- A visão de ADMIN é protegida também no backend. Ocultar botões no React não é controle de autorização.

### Critérios de aceite local

1. ADMIN cadastra Loja Campinas, `loja.campinas`, senha inicial; novo username aparece no Keycloak, apenas OPERATOR na loja.
2. Login do comerciante exibe as três áreas financeiras e oculta gestão e inclusão de administradores.
3. COMERCIANTE não consegue POST em `/api/v1/merchants/`, nem gerenciar ADMINs.
4. Usuário sem merchant não pode criar lançamentos ou ler saldos de comerciantes não atribuídos.
5. Crédito `100` e débito `25,00` geram histórico de dois eventos e saldo **R$ 75,00** depois da projeção assíncrona. Nunca repetir POST para corrigir saldo atrasado.
6. Consultas entre comerciantes, autorização revogada e indisponibilidade do Keycloak devem negar acesso.


## Escopo

O usuário não precisa mais cadastrar grupos manualmente para cada comerciante. Na SPA administrativa, informa o nome da loja e o username/senha inicial do comerciante. A API Transactions cria um UUID, provisiona os grupos `/merchants/{uuid}/ADMIN` e `/merchants/{uuid}/OPERATOR`, grava o nome como atributo `displayName` do grupo do comerciante e vincula o criador ao ADMIN. A listagem mostra apenas comerciantes aos quais o usuário autenticado pertence, com o papel vigente obtido do Keycloak. O usuário OPERATOR tem visão financeira restrita.

O ADMIN pode adicionar outro usuário existente como ADMIN digitando seu username. Para criar um usuário ainda inexistente, preenche também uma **senha temporária** (mínimo 12 caracteres), que o Keycloak exige trocar no primeiro login. Não há armazenamento de senha, membership ou role nas bases financeiras.

## Configuração única, após inicializar Keycloak local

A importação automática de `infra/local/keycloak/cashflow-realm.json` só ocorre quando o realm não existe. Para volumes existentes, criar manualmente **uma única vez** o novo cliente, sem apagar volumes:

1. Em `http://localhost:8081/admin/`, selecionar realm `cashflow`, depois **Clients > Create client**.
2. Client ID: `cashflow-management-service`, OpenID Connect, **Client authentication ON**, **Service accounts roles ON**, Standard flow OFF.
3. Em **Service account roles**, selecionar cliente `realm-management` e atribuir `manage-realm`, `manage-users`, `view-users`, `query-users`. Atribuir somente nesse cliente de gestão, nunca ao cliente SPA nem a todos os usuários.
4. Em **Credentials**, copiar o client secret e definir no arquivo `.env` local (não versionado):

   ```dotenv
   KEYCLOAK_MGMT_CLIENT_SECRET=<secret_do_cliente_de_gestao>
   CASHFLOW_BOOTSTRAP_USERNAME=cashflow.admin
   ```

5. Manter `KEYCLOAK_AUTHZ_CLIENT_SECRET` para o cliente somente leitura `cashflow-authorization-service`, utilizado nas verificações das transações.
6. Recriar as imagens alteradas: `docker compose build transactions web && docker compose up -d --force-recreate transactions web`.

## Bootstrap restrito

**Somente em ambiente Development**, o usuário autenticado cujo claim assinado `preferred_username` corresponde exatamente a `CASHFLOW_BOOTSTRAP_USERNAME` pode cadastrar comerciantes. Esta é uma política restrita de bootstrap local, não autoatendimento global. O padrão local no compose é `cashflow.admin`; customize para seu usuário administrador. Em produção, o cadastro fica negado até que uma política administrativa de provisionamento seja definida. O backend nunca confia no username enviado pelo navegador.

Depois que o comerciante existe, apenas membros de seu grupo ADMIN podem adicionar administradores. Não existe endpoint anônimo de criação, nem atribuição de ADMIN pelo próprio cliente.

## Contratos

| Método | Rota | Resposta |
| --- | --- | --- |
| GET | `/api/v1/merchants/permissions` | `200`, `{can_manage_merchants}`, apenas para usuários autenticados |\n| GET | `/api/v1/merchants/` | `200`, lista `[{id,name,role}]` autorizada |
| POST | `/api/v1/merchants/` | `201`, `{id,name,role}`, com `{name,username,password}` no corpo |
| POST | `/api/v1/merchants/{merchantId}/admins` | `204` ao associar usuário existente ou criar com `{username,temporaryPassword}` |

Qualquer endpoint exige JWT do realm cashflow com a audiência `cashflow-transactions-api`. O cadastro exige conta bootstrap permitida, e a associação exige ADMIN no comerciante. Falha de Keycloak retorna `503` e não autoriza a ação.

## Limitações e testes seguintes

- É necessário criar o cliente de gestão e atribuir suas roles uma única vez em Keycloak com volume persistente. A importação do arquivo de realm não altera realms já existentes.
- O cadastro usa as APIs administrativas do Keycloak em operações sequenciais. Uma indisponibilidade intermediária pode deixar um grupo incompleto; idempotência de provisionamento, compensação e auditoria administrativa precisam ser endurecidas antes de produção.
- A listagem consulta os grupos atuais, sem cache positivo. Atributos de nome são metadados de demonstração, não cadastro fiscal/comercial.
- Depois do merge, validar cadastro, segundo ADMIN, login do segundo usuário, permissões 403 de não membros, revogação e fluxo crédito/débito/consolidação E2E. Azure permanece futuro.

## Gestão de lojas e usuários (MVP local)

A visão ADMIN mostra somente **usuários efetivos das lojas**, e oculta a conta técnica `cashflow.admin` na tabela de operação. Ela **continua** em Keycloak como ADMIN dos comerciantes criados: ocultar não modifica permissões.

Com uma loja selecionada, o administrador pode:
- **Adicionar comerciante:** cria usuário novo com username exclusivo no realm e senha inicial (12 a 128 caracteres), e vincula ao grupo `OPERATOR` da loja existente.
- **Editar cargo:** alterna entre `Comerciante` (`OPERATOR`) e `ADMIN`. O serviço atribui primeiro o novo grupo e depois remove o anterior.
- **Desativar (soft delete):** move o usuário do grupo de permissões `ADMIN` ou `OPERATOR` da loja para o grupo `INACTIVE` na mesma loja. A identidade, o vínculo histórico recuperável, a senha e as transações continuam preservados. Os endpoints financeiros verificam a associação ativa no Keycloak e deixam de autorizar essa conta na loja.
- **Reativar:** move um membro `INACTIVE` de volta para `OPERATOR` sem recriar conta ou perder lançamentos. O ADMIN pode então promover o cargo novamente.
- **Atualizar nome da loja:** altera apenas o atributo `displayName` do grupo no Keycloak; o identificador UUID e a movimentação permanecem os mesmos.

Os endpoints exigem JWT válido, vínculo atual **ADMIN da loja**, identificador UUID validado (`X-Merchant-ID`) e autorização do próprio backend. O username de bootstrap **não pode ser desativado nem rebaixado** pelo serviço. Os endpoints de escrita usam rotas fixas: `POST /api/v1/merchants/members`, `PUT /api/v1/merchants/members`, `DELETE /api/v1/merchants/members` e `PUT /api/v1/merchants/details`.

**Exclusão integral de loja** permanece fora deste MVP. Exige decisão específica sobre conservação de dados financeiros, auditoria, vínculos órfãos e recuperação, não deve ser confundida com a remoção de acesso de um usuário. Alterar username e senha de identidade compartilhada também permanece no Keycloak até existir um fluxo dedicado e auditável.


### Soft delete e recuperação

A conta desativada permanece visível na tabela como **Inativo**, com botão **Reativar**. O grupo Keycloak `/merchants/{uuid}/INACTIVE` é criado automaticamente em novas lojas e sob demanda nas lojas antigas. Esse grupo não confere os direitos de `ADMIN` ou `OPERATOR`. A operação de desativação usa o endpoint `DELETE /api/v1/merchants/members`, mas **não executa exclusão definitiva**: remove apenas a permissão ativa após atribuir o grupo `INACTIVE`. O endpoint `PUT /api/v1/merchants/members` com cargo `OPERATOR` realiza a recuperação. O username global permanece reservado, portanto criar outra conta com o mesmo nome não é o mecanismo de recuperação.
