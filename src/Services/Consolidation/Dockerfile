FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app
COPY src/Services/Consolidation/ ./src/Services/Consolidation/
RUN dotnet restore src/Services/Consolidation/CashFlow.Consolidation.Api/CashFlow.Consolidation.Api.csproj
RUN dotnet publish src/Services/Consolidation/CashFlow.Consolidation.Api/CashFlow.Consolidation.Api.csproj -c Release -o /out --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet","CashFlow.Consolidation.Api.dll"]
