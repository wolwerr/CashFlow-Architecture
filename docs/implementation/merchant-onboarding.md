# Merchant onboarding local: cadastro e administradores no CashFlow

## Escopo

O usuário não precisa mais cadastrar grupos manualmente para cada comerciante. Na SPA, em **Meus comerciantes**, informa somente o nome da loja. A API Transactions cria um UUID, provisiona os grupos `/merchants/{uuid}/ADMIN` e `/merchants/{uuid}/OPERATOR`, grava o nome como atributo `displayName` do grupo do comerciante e vincula o criador ao ADMIN. A listagem mostra apenas comerciantes aos quais o usuário autenticado pertence, com o papel vigente obtido do Keycloak.

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
