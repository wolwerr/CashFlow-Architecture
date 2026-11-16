# CashFlow | CI único com jobs separados

## Organização

Existe um único workflow GitHub Actions: `.github/workflows/local-build.yml`, com três **jobs independentes e visíveis** no GitHub:

1. **Backend | Build and tests**: restaura e compila a solução .NET 10, executa testes arquiteturais e xUnit, aplica gates de cobertura de **80% por módulo** (Transactions Domain e Application), gera OpenCover e publica o artifact `backend-opencover`.
2. **Frontend | Build and tests**: instala dependências Node.js, compila React/TypeScript com Vite, roda Vitest com cobertura mínima de 80% (linhas, instruções, ramos e funções), gera LCOV e publica o artifact `frontend-lcov`.
3. **Sonar | Backend and frontend**: aguarda ambos os jobs; baixa OpenCover e LCOV; prepara o scanner .NET com as duas fontes; compila .NET novamente **dentro do contexto do scanner**, que exige Begin/Build/End; verifica arquivos e fontes TypeScript; publica análise C# e TS, aguarda o processamento da análise e aplica a política de severidade do CashFlow no GitHub Actions.

Os três jobs ficam separados no gráfico de execução do GitHub Actions. Os relatórios são transferidos por `upload-artifact` e `download-artifact`. O Sonar fica em um único job porque **um projeto SonarQube Cloud recebe uma análise atômica por execução**. Rodar scanners concorrentes separados para C# e TypeScript com a mesma Project Key poderia produzir análises inconsistentes ou sobrepostas. Para Quality Gates totalmente independentes por linguagem seriam necessários dois projetos Sonar e suas respectivas configurações.

O `sonar.scanner.scanAll=true` analisa JS/TS junto ao .NET. O scanner importa `sonar-reports/backend/coverage.opencover.xml` e `web/coverage/lcov.info`. O Sonar não executa testes; o CI produz os relatórios.

## Política de bloqueio no plano gratuito

O **Sonar way** padrão no SonarQube Cloud continua ativo e apenas informativo para este pipeline. No plano gratuito não é possível editar esse gate; gates customizados pelo painel são exclusivos de Team/Enterprise. O workflow publica a análise normalmente, sem esperar que o `Sonar way` determine o resultado do job.

O script `.github/scripts/sonar_severity_gate.py` aguarda a conclusão da mesma análise no servidor Sonar e decide o check de CI segundo a política CashFlow:

- **Bloquear** por qualquer issue **nova e não resolvida** com impacto `HIGH` ou `BLOCKER` (classificação moderna do Sonar), equivalentes a `CRITICAL` e `BLOCKER` no modelo antigo. Inclui Security, Reliability e Maintainability.
- **Não bloquear** por issues `MEDIUM`, `LOW` ou `INFO`; elas continuam visíveis no Sonar para tratamento conforme prioridade.
- **Preservar** as condições não relacionadas à contagem ou rating das issues, sobretudo **cobertura >=80% no novo código**, **duplicação <=3%** e **revisão de security hotspots**, quando essas condições constarem do gate.
- Falhar fechado se o Sonar não disponibilizar análise, issues ou resultados de métricas corretamente. Testes unitários do script verificam o limiar e a preservação das métricas.

**Atenção:** o painel do Sonar pode exibir `Sonar way: Failed` por uma issue Medium mesmo quando o check GitHub `Sonar | Backend and frontend` passa conforme a política aprovada. Se a proteção da `main` também exigir um check nativo `SonarCloud Code Analysis`/`SonarQube`, ajuste o Ruleset para exigir **os checks dos jobs do nosso workflow**, sem tornar obrigatória a decoração nativa do gate padrão. Isso não desativa a análise nem remove issues; apenas diferencia a condição de bloqueio do GitHub. A alternativa para alterar o gate diretamente no Sonar é um plano Team/Enterprise.


## Segurança das dependências npm

O frontend versiona `web/package-lock.json` (lockfile v3) para fixar a árvore de versões resolvidas. Os jobs Frontend e Sonar utilizam `npm ci --ignore-scripts --no-audit --no-fund`, impedindo a execução de scripts de instalação de terceiros e garantindo que a instalação falhe caso `package.json` e o lockfile estejam divergentes. O lockfile foi gerado com Node.js 22/npm 10 em ambiente GitHub Actions. Não apagar o lockfile nem voltar a `npm install` no CI; atualizar dependências intencionalmente por PR e revisar o diff do lockfile.

`--ignore-scripts` desabilita scripts no momento da instalação, mas `npm run build` e `npm run test:coverage` executam explicitamente as tarefas próprias do projeto. Caso alguma dependência necessite script de pós-instalação, avaliar risco e alternativa antes de flexibilizar essa proteção.

## Secrets e segurança

GitHub `Settings > Secrets and variables > Actions`, em **Repository secrets**:

- `SONAR_TOKEN`
- `SONAR_PROJECT_KEY`
- `SONAR_ORGANIZATION`

Os valores só são passados às etapas que realmente usam as credenciais. Nunca versionar nem publicar tokens. O Sonar deve estar configurado para **CI-based Analysis**, desativando `Automatic Analysis` para evitar conflito. Em PRs vindos de forks, o job Sonar é ignorado porque secrets não são fornecidos pelo GitHub, mas ambos os builds e testes continuam obrigatórios.

## Quality Gate: main vs Pull Request

O status **Passed** exibido no Overview da SonarQube Cloud refere-se à branch selecionada (normalmente `main`), e **não comprova aprovação do PR #26**. O CI do PR mostrou `QUALITY GATE STATUS: FAILED` mesmo com builds, testes, OpenCover e LCOV corretos. Precisamos inspecionar as **condições do Quality Gate para o próprio PR**, sem diminuir ou remover as regras.

O aviso sobre PL/SQL Data Dictionary não configurado é uma limitação das regras PL/SQL aplicáveis e não demonstra, isoladamente, a causa da falha do Quality Gate de C#/TS.

Nos logs do GitHub, URLs com project key que coincide com um secret podem aparecer mascaradas como `id=***`. Copiar essa URL mascarada provoca **project not found / access denied**. Abra diretamente o projeto no Sonar pela interface autenticada e selecione o PR 26 na lista de branches/PRs, ou construa a URL com o valor real da Project Key no seu navegador, nunca compartilhe tokens.

## Qualidade e restrições

- **Backend**: gates independentes de Domain e Application, mínimo 80% de cobertura de linhas. Adicionar gates separados para Infrastructure e Consolidation quando seus códigos reais estiverem integrados à `main`.
- **Frontend**: Vitest mínimo 80% em linhas, statements, branches e functions; LCOV importado.
- **Sonar**: manutenibilidade, segurança, confiabilidade, duplicações, cobertura e Quality Gate calculado para a mesma revisão.

Não há VM própria ou Azure no pipeline. Não declarar Quality Gate aprovado sem conferir a execução mais recente.
