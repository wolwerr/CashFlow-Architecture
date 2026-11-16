# Merchant onboarding local: cadastro e administradores no CashFlow

## Perfis de acesso do MVP

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
| GET | `/api/v1/merchants/` | `200`, lista `[{id,name,role}]` autorizada |
| POST | `/api/v1/merchants/` | `201`, `{id,name,role}`, com `{name}` no corpo |
| POST | `/api/v1/merchants/{merchantId}/admins` | `204` ao associar usuário existente ou criar com `{username,temporaryPassword}` |

Qualquer endpoint exige JWT do realm cashflow com a audiência `cashflow-transactions-api`. O cadastro exige conta bootstrap permitida, e a associação exige ADMIN no comerciante. Falha de Keycloak retorna `503` e não autoriza a ação.

## Limitações e testes seguintes

- É necessário criar o cliente de gestão e atribuir suas roles uma única vez em Keycloak com volume persistente. A importação do arquivo de realm não altera realms já existentes.
- O cadastro usa as APIs administrativas do Keycloak em operações sequenciais. Uma indisponibilidade intermediária pode deixar um grupo incompleto; idempotência de provisionamento, compensação e auditoria administrativa precisam ser endurecidas antes de produção.
- A listagem consulta os grupos atuais, sem cache positivo. Atributos de nome são metadados de demonstração, não cadastro fiscal/comercial.
- Depois do merge, validar cadastro, segundo ADMIN, login do segundo usuário, permissões 403 de não membros, revogação e fluxo crédito/débito/consolidação E2E. Azure permanece futuro.
