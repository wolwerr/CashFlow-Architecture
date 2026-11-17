FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app
COPY Backend/src/Shared/CashFlow.Security/ ./src/Shared/CashFlow.Security/
COPY Backend/src/Services/Transactions/ ./src/Services/Transactions/
RUN dotnet restore src/Services/Transactions/CashFlow.Transactions.Api/CashFlow.Transactions.Api.csproj
RUN dotnet publish src/Services/Transactions/CashFlow.Transactions.Api/CashFlow.Transactions.Api.csproj -c Release -o /out --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet","CashFlow.Transactions.Api.dll"]
