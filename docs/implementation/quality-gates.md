# Gates de qualidade do CashFlow (.NET 10)

## Arquitetura

Projeto: `tests/CashFlow.ArchitectureTests` (xUnit + NetArchTest.Rules).

- Domain: sem PackageReference; sem ProjectReference; sem dependências de outras camadas.
- Application: sem PackageReference; único ProjectReference permitido é Domain do próprio serviço.
- Infrastructure: não pode depender de API.
- Transactions e Consolidation: não podem referenciar um ao outro.
- Referências compiladas do Domain/Application devem se restringir a assemblies da plataforma e aos assemblies próprios permitidos.

```powershell
dotnet test tests/CashFlow.ArchitectureTests/CashFlow.ArchitectureTests.csproj -c Release
```

## Testes e cobertura

Projeto implementado nesta fase: `tests/CashFlow.Transactions.UnitTests`. Para medir o **Domain de Transactions**, sem diluir a métrica com os testes nem com outras camadas:

```powershell
dotnet test tests/CashFlow.Transactions.UnitTests/CashFlow.Transactions.UnitTests.csproj -c Release /p:CollectCoverage=true /p:Threshold=80 /p:ThresholdType=line /p:ThresholdStat=Minimum '/p:Include=[CashFlow.Transactions.Domain]*' /p:CoverletOutputFormat=cobertura
```

O Coverlet retorna código de saída diferente de zero se o módulo instrumentado não atingir **80% das linhas**. O pipeline `.github/workflows/local-build.yml` executa o mesmo teste e falha em violações. Neste estágio **Consolidation ainda não tem cobertura implementada**, portanto não se deve interpretar o gate de Transactions como cobertura total do produto. Ao criar código significativo em Consolidation e Application, adicionar testes e gates próprios em vez de aumentar artificialmente a média global.

**Importante (.NET 10):** o projeto usa xUnit/VSTest com `TestingPlatformDotnetTestSupport=false`, porque `coverlet.msbuild` depende do runner VSTest e não é compatível com execução Microsoft Testing Platform nativa. Não misturar `coverlet.collector` com `coverlet.msbuild` no mesmo projeto.

## Proteção de merge

O workflow falha quando os testes ou o limite de cobertura falham. Para que o merge na branch `main` seja efetivamente impedido, **configurar GitHub Ruleset / Branch Protection** exigindo o check `dotnet` como obrigatório. Isso ainda precisa ser conferido com as permissões e a configuração da conta. O workflow não faz deploy.

## Evidências ainda pendentes

Estes testes e thresholds foram criados no repositório, mas ainda precisam ser executados para produzir relatórios e confirmar que os gates passam. Nenhum resultado de cobertura foi inventado. Azure permanece futuro.
