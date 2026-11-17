# Homologação manual local: OIDC no navegador e gestão de comerciantes

**Data:** 17/11/2026. **Origem:** confirmação direta do responsável pelo projeto no navegador local, após execução do checklist funcional. **Resultado informado:** os sete itens passaram. Não houve automação do navegador nesta validação e não foram anexadas capturas de tela. A confirmação do operador é evidência declarativa, não auditoria independente.

## Escopo confirmado

1. Login via Keycloak e retorno à aplicação CashFlow.
2. Identificação correta do usuário e do papel após autenticação.
3. Listagem de comerciantes cadastrados.
4. Cadastro de loja descartável com usuário próprio.
5. Edição da loja descartável e visualização da alteração.
6. Soft delete da loja e ausência de acesso operacional comum após a exclusão lógica.
7. Logout e exigência de autenticação novamente para a área protegida.

**Cenários do manifesto:** `browser_oidc` e `merchant_management`.

A aprovação local não é uma medição de segurança do OIDC (PKCE, armazenamento dos tokens ou revogação), não demonstra todas as permissões administrativas, concorrência de cadastro nem integridade histórica do soft delete por consulta SQL. O isolamento entre comerciantes e fail-closed do IAM foram validados separadamente.

## Evidência local e privacidade

Registrar `evidence/browser-oidc-manual.json` e `evidence/merchant-management-manual.json` no manifesto `local-acceptance.json` usando o script `.github/scripts/record_manual_ui_acceptance.py --confirm-seven-checks`, **somente porque o operador confirmou os sete passos**. Esses arquivos permanecem locais e ignorados no Git; não versionar JWT, senha, cookies, fotos de documentos, nomes reais ou dados pessoais de contas.

O commit de cada execução histórica não foi rastreado no momento do teste. Não substituir `NOT_RECORDED` por um commit posterior por conveniência.
