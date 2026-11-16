# CI unificado | Build, testes e SonarQube Cloud

## Arquivo único

A esteira de CI do CashFlow usa apenas **`.github/workflows/local-build.yml`**. O workflow antigo `sonarqube-cloud.yml` foi removido para evitar compilações e análises duplicadas.

O job `Build and SonarQube` é executado nos pushes de `main` e `feat/**` e em pull requests:

1. Prepara .NET 10, Node.js 22, JDK 21 e instala dependências npm.
2. Inicia SonarScanner for .NET antes do build, quando as credenciais estão disponíveis.
3. Restaura e compila toda a solução backend `CashFlow.slnx`.
4. Executa `npm run build` em `web` (TypeScript + Vite).
5. Roda testes arquiteturais (NetArchTest) e testes de Transactions com cobertura mínima de 80% **separadamente** para Domain e Application.
6. Gera relatório `OpenCover` com Coverlet, para importação de cobertura C# no Sonar.
7. Executa `npm run test:coverage` com Vitest para validar o frontend e produzir `web/coverage/lcov.info`.
8. Publica a análise C# + JS/TS em um único projeto SonarQube Cloud e aguarda o Quality Gate.

O SonarScanner for .NET suporta análise multilíngue por padrão (`sonar.scanner.scanAll=true`). O frontend é analisado pelo mesmo scanner; **não** se cria um segundo projeto Sonar nem se executa um segundo scanner.

## Configuração GitHub

Em `Settings > Secrets and variables > Actions`, cadastrar os três **Repository secrets**:

- `SONAR_TOKEN`: token de análise do Sonar, sem inserir no chat ou no repositório.
- `SONAR_PROJECT_KEY`: Project Key exata.
- `SONAR_ORGANIZATION`: Organization Key exata.

Para habilitar análise por CI, desativar `Automatic Analysis` em `Administration > Analysis Method` no SonarQube Cloud.

O CI verifica que os três secrets estão presentes e falha se faltar algum. Em PRs de forks, secrets não são disponibilizados pelo GitHub: build e testes seguem obrigatórios, mas os passos Sonar são omitidos. Se o Quality Gate Sonar for obrigatório para merge, PRs externos precisarão de política específica.

## Cobertura e gates

- **Backend**: Coverlet produz OpenCover (`**/coverage.opencover.xml`) para Sonar e mantém gates separados de **80% line coverage** para Domain e Application.
- **Frontend**: Vitest com `@vitest/coverage-v8` produz `web/coverage/lcov.info`. A primeira validação real é um smoke test renderizando a tela atual via ReactDOMServer. O relatório inclui arquivos de `web/src`; não concluir que a cobertura do frontend já é >=80% sem ver o resultado.
- **Sonar**: inspeção de issues/code smells/vulnerabilidades/duplicação e ingestão dos dois formatos de cobertura; Quality Gate definido pela política da organização, e `sonar.qualitygate.wait=true` propaga falhas.
- **Outros serviços**: ao adicionar código testável em Consolidation/Infrastructure, incluir testes e thresholds apropriados na mesma esteira. Não mascarar cobertura ausente com exclusões artificiais.

O Sonar não executa os testes sozinho; ele importa os relatórios gerados. A análise multilíngue, os relatórios OpenCover/LCOV e o Quality Gate precisam ser comprovados em uma execução real do GitHub Actions.

## Validação local

```powershell
dotnet restore CashFlow.slnx
dotnet build CashFlow.slnx -c Release
dotnet test tests/CashFlow.ArchitectureTests/CashFlow.ArchitectureTests.csproj -c Release
dotnet test tests/CashFlow.Transactions.UnitTests/CashFlow.Transactions.UnitTests.csproj -c Release
cd web
npm install
npm run build
npm run test:coverage
```

Sem VM dedicada, sem deploy ou provisionamento Azure.
