# SonarQube Cloud | CashFlow

## Objetivo

Adicionar análise estática de C#/.NET 10 em GitHub Actions para bugs, vulnerabilidades, code smells, duplicação e cobertura de testes. A execução usa **GitHub-hosted runner**, sem VM ou Azure. O SonarQube Cloud não executa os testes: o pipeline produz **OpenCover** com Coverlet e o scanner importa o relatório.

## Habilitação manual obrigatória

1. Projeto SonarQube Cloud: em **Administration > Analysis Method**, desabilitar **Automatic Analysis** para evitar conflito com CI-based analysis; manter análise via GitHub Actions.
2. No projeto SonarQube Cloud, copiar os valores exatos de **Project Key** e **Organization Key**; **não adivinhar a partir do nome exibido**.
3. GitHub: em **Settings > Secrets and variables > Actions**, criar os três **Repository secrets**:
   - `SONAR_PROJECT_KEY`: Project Key exata do SonarQube Cloud.
   - `SONAR_ORGANIZATION`: Organization Key exata do SonarQube Cloud.
   - `SONAR_TOKEN`: token de análise gerado no SonarQube Cloud. **Não salvar nem transmitir o token em mensagens ou commits.**
5. Concluir merge do PR contendo `.github/workflows/sonarqube-cloud.yml`. A análise roda para push na `main` e PRs internos do mesmo repositório. Para PRs provenientes de forks, a job é ignorada por segurança, porque segredos não são expostos pelo GitHub.
6. Validar uma execução real e verificar se o Sonar recebeu análise **CI-based** e o relatório de **Coverage**. Depois disso, configurar o check Quality Gate como obrigatório no GitHub Ruleset/Branch Protection se desejado.

**Importante:** o workflow verifica explicitamente a presença dos três secrets e falha se algum não estiver configurado. A job de PR originado de fork é ignorada por segurança. Isso evita inserir segredos no código.

## Execução

Workflow dedicado `.github/workflows/sonarqube-cloud.yml`:

- checkout completo (`fetch-depth: 0`) para histórico/PRs;
- .NET SDK 10 e JDK 21;
- instalar `dotnet-sonarscanner`;
- `begin` antes do `dotnet build` para análise Roslyn;
- `dotnet test` em Transactions com `coverlet.msbuild` para gerar `coverage.opencover.xml`;
- `end` para publicar o resultado, com espera pelo Quality Gate (`sonar.qualitygate.wait=true`).

O workflow de testes `.github/workflows/local-build.yml` continua responsável pelos thresholds de **80% de cobertura de linhas por módulo** e testes arquiteturais. O Sonar não substitui essa exigência.

## Cobertura e limitações

- O Sonar importa o relatório **OpenCover** dos testes de Transactions. Cobertura do React/TypeScript **não será enviada** até termos testes de frontend e relatórios LCOV. Não interpretar a cobertura C# como cobertura completa de todo o produto.
- Não foi criada VM, não houve provisionamento Azure e não foi conectado banco real.
- Status de ativação: **pendente das três configurações no GitHub, da desativação de Automatic Analysis e de uma análise CI executada**.
- Configurar regra de cobertura no novo código no Quality Gate no Sonar; a interface pode ignorar certas condições para mudanças pequenas.
- Se o Sonar estiver usando a região US, verificar o parâmetro `sonar.region=us` e o endpoint recomendado para a organização. O workflow assume a região padrão apontada por `https://sonarcloud.io`.
