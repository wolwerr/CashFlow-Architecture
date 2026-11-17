# Trilha 07 | Resiliência, observabilidade e desempenho local

**Escopo:** consolidar ferramentas e evidências do MVP local, preservando as restrições de segurança das Trilhas 05 e 06. Azure/AKS não é utilizado. **Código instrumentado não significa SLO homologado.**

## Inventário operacional, sem mutações

O script `.github/scripts/local_operational_snapshot.py` fornece uma fotografia somente de leitura do projeto Docker Compose. Por padrão, roda em **preview**, sem executar Docker. A execução explícita `--execute` verifica os serviços esperados, lista containers em execução e utiliza `rabbitmqctl list_queues` somente para contar mensagens prontas e ainda não confirmadas. Não lê credenciais, não expõe nomes de filas, não consome eventos e não altera banco ou volume.

```bash
python3 .github/scripts/local_operational_snapshot.py
python3 .github/scripts/local_operational_snapshot.py --execute
```

Pré-condições: executar **na raiz do repositório** com Docker Compose apontando para instância local isolada. `outcome=OBSERVED` significa que containers esperados estão em execução e as métricas foram lidas; **não comprova readiness, saúde dos workers nem consistência de saldos**. `DEGRADED` identifica serviço ausente. O comando encerra com código 1 nesse caso ou quando não pode ler o estado da fila. Não versionar relatórios de infraestrutura real.

## Sequência de ensaios de resiliência

1. **Baseline:** inicializar a stack local descartável, executar `docker compose ps`, a fotografia read-only e uma consulta autorizada de saldo. Anotar commit, recursos do host e dados de teste.
2. **Broker fora:** usar o [drill local](local-recovery-drills.md) com **preview** seguido de `--execute` somente com autorização do operador. Durante a interrupção, registrar lançamento descartável via fluxo autorizado. Após recuperação, recolher snapshot e evidências de Outbox pendente/processada, histórico e saldo único. **Não interpretar `RESTORED` como prova financeira.**
3. **Consumer fora:** repetir isoladamente para `consolidation`, verificando que Transactions persiste o lançamento e que a projeção converge sem duplicidade quando o consumer volta.
4. **Keycloak fora:** confirmar comportamento *fail closed* das operações financeiras e administrativas, inclusive um JWT já emitido. Após retornar, confirmar autorização corrente. Observar que o fluxo de gestão não deve retornar HTTP 2xx durante falha de consulta de grupos.
5. **Backup:** executar o [verificador de backup/restauração PostgreSQL](local-postgres-backup-verification.md) apenas na instância local descartável; conferir registros e saldos restaurados sem sobrescrever a base original.
6. **Carga e projeção:** executar o [probe de carga](local-hardening-load.md), no máximo 50 RPS/5 min, e o [medidor de projeção](local-slo-observability.md), com no mínimo 20 amostras, em cenário isolado e explicitamente autorizado. Não executar carga/POST em CI ou contra dados de usuário.

## Ajustes adicionais de gestão de comerciantes

- A tabela agrupa visualmente os usuários de uma mesma loja por célula `rowSpan`. A seleção representa **a loja inteira**, enquanto cada linha mantém ações independentes de usuário. Não são três comerciantes diferentes quando três usernames aparecem sob `Teste`.
- A coluna **Status** apresenta `Ativo` para ADMIN/OPERATOR e `Inativo` para INACTIVE, mantendo a coluna Cargo separada.
- Mensagens de atualização de usuário aparecem na mesma linha da ação. Mensagens gerais aparecem junto ao cabeçalho da listagem, não depois dos formulários.
- A edição de username depende da configuração do realm Keycloak. Para **novos ambientes descartáveis**, o import `infra/local/keycloak/cashflow-realm.json` define `editUsernameAllowed=true`. Para um **realm local já inicializado**, alterar manualmente no console administrativo: `Realm settings > Login > Edit username` (a posição pode mudar conforme a versão). Salvar e repetir a edição. **Não remover volumes e não importar novamente sobrescrevendo identidades.**
- A API distingue o retorno HTTP 400 do Keycloak durante alteração de username como conflito de configuração (`409` com mensagem orientativa). Outros erros, inclusive ausência de permissão do service account e indisponibilidade, ainda precisam de análise pelos logs do container `transactions`, sem expor credenciais.
- O registro `Teste5` do print é uma **loja diferente**, enquanto `teste`, `teste3` e `teste4` são usuários da loja `Teste`. O banco financeiro PostgreSQL **não** é a fonte desses cadastros, que estão nos grupos e usuários do Keycloak. Nenhuma exclusão automática é feita, para não quebrar vínculos ou histórico. Conferir UUID, vínculos e existência de transações antes de planejar qualquer limpeza de dados descartáveis.

## Critérios a comprovar

| Dimensão | Critério | Evidência necessária |
| --- | --- | --- |
| Throughput/latência | 50 RPS durante 300 s; p95 de consultas bem-sucedidas inferior a 500 ms; falhas até 5% | Relatório do probe real, infraestrutura, tamanho de base e sessão válida |
| Projeção assíncrona | 95% dos créditos observáveis em até 30 s | No mínimo 20 amostras, timestamps, ausência de escritores concorrentes e resultado real |
| Recuperação RabbitMQ | Outbox mantém eventos; replay após retorno e saldo sem duplicidade | Estado antes/depois, evento descartável, persistência e projection |
| Recuperação Consolidation | Registrar enquanto consumer está fora, convergir no retorno | POST 201, histórico, saldo antes/depois e verificação de event_id único |
| Recuperação Keycloak | Negar sem vínculo atual ou serviço indisponível | HTTP 403/503 e sucessos autorizados após recuperação |
| Backup/restauração | Base de verificação independente, consistência e RPO/RTO observados | Dump/restore local, contagens, checks e durações medidos |
| Observabilidade | Logs com trace interno, status, duração; fila pronta e não confirmada | Evidências sanitizadas do logger e snapshot local |

**Segurança das evidências:** nunca incluir bearer token, segredo de serviço, senha, payload financeiro pessoal, nome de usuário real, IDs privados ou conteúdo de backups nos logs compartilhados. Os probes existentes permanecem *opt-in* e são testados na CI sem executar efeitos reais.

## Qualidade e avisos corrigidos nesta trilha

- Constantes internas para grupos `ADMIN`, `OPERATOR`, `INACTIVE` e rota de membros.
- Regex de username com limite de tempo explícito e remoção de operadores null-forgiving redundantes.
- Actions do workflow atualizadas para versões com runtime Node.js 24 (checkout v5, setup-dotnet v5, setup-node v5, setup-java v5, upload/download-artifact v6). Os três jobs ficam fixados em `ubuntu-24.04` para evitar migração implícita de `ubuntu-latest`; atualizar mediante teste dedicado quando houver necessidade.
- Testes unitários offline do snapshot incorporados à mesma política de CI e Sonar.

**Pendente para considerar a Trilha 07 homologada:** relatórios locais reais dos drills, da carga, de projeção, da restauração e da integridade financeira. O CI verde valida os scripts e o build, não a recuperação e os SLOs.
