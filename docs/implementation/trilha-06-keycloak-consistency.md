# Trilha 06 | Segurança e consistência operacional do Keycloak

**Escopo:** desenvolvimento local com Keycloak como fonte de identidade, grupos e papéis. Não há novo banco lógico ou serviço de autorização. Nenhuma permissão é inferida do frontend ou somente do JWT.

## Proteções implementadas

1. **Concorrência na criação de grupos:** quando o Keycloak responde 409 a uma criação, a API relê o caminho hierárquico exato e somente aceita o grupo se sua identidade corresponder ao nome e caminho esperados. 409 sem grupo identificável é falha fechada.
2. **Concorrência no username:** HTTP 409 na criação de usuário retorna conflito controlado, sem associar a conta criada por outro processo. A verificação prévia ainda não é uma reserva transacional do username.
3. **Compensação de novo usuário:** se a API criar uma identidade e a atribuição ao grupo falhar com erro HTTP, tenta removê-la usando o UUID resolvido e um timeout independente de 10 segundos. Não exclui usuários já existentes. Falha de compensação retorna erro e exige reconciliação manual.
4. **Conflitos de grupos:** múltiplos grupos `ADMIN`, `OPERATOR` ou `INACTIVE` para o mesmo merchant não são considerados uma única associação válida. Gestão de cargos retorna erro, permissão de administrador é negada e listagem não concede permissões ambíguas.
5. **Autorização financeira compartilhada:** Transactions e Consolidation agora percorrem todas as páginas de grupos atuais do Keycloak antes de autorizar. Um vínculo `INACTIVE`, ou `ADMIN + OPERATOR` simultâneos, nega acesso mesmo quando outra página contém papel ativo.\n6. **Proteção pré-existente:** usuário bootstrap não pode ser desativado ou rebaixado; revogação e reativação continuam consultando Keycloak sem cache positivo, e associações não são deletadas fisicamente no soft delete.

## Limitações e riscos residuais

Keycloak não fornece transação atômica multi-recurso para criação de grupo + identidade + vínculos. A compensação é *best effort* e depende de confirmação de UUID. Se o processo cair, a rede ficar indisponível durante o rollback ou uma criação composta falhar antes da identificação de usuário, pode haver objetos parciais. Não é seguro apagar automaticamente um usuário preexistente ou um grupo que possa conter vínculos concorrentes. **Esses casos exigem inspeção e reconciliação auditável**, não limpeza cega.

A primeira criação de merchant ainda precisa de uma estratégia operacional de reconciliação, retries com idempotency key e registros persistentes de saga caso o volume justifique. Não afirmar garantias de *exactly once* ou de criação transacional do realm.

## Verificações locais para o aceite

- Criar um comerciante por conta bootstrap e confirmar grupos `ADMIN`, `OPERATOR`, `INACTIVE`.
- Repetir o cadastro de username já existente: retornar conflito, sem acesso concedido.
- Simular conflito entre duas requisições de criação e verificar que somente o resultado verificado no Keycloak pode ser usado.
- Bloquear `ADMIN + INACTIVE` simultâneos, sem acesso administrativo nem financeiro até reparo.
- Desativar e reativar uma conta, confirmando preservação da identidade e histórico.
- Desligar Keycloak e confirmar que operações e mutações protegidas não retornam sucesso.
- Conferir os testes de API, build e Sonar no GitHub Actions; não marcar fluxo E2E real como executado sem evidências.

**Próxima evolução de segurança:** auditoria administrativa persistente, workflow de reconciliação de objetos incompletos, prevenção de corrida entre alterações de papel e políticas mais fortes de senhas/MFA antes de produção. Azure fica para fase futura.
