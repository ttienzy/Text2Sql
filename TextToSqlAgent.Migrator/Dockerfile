FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["TextToSqlAgent.Migrator/TextToSqlAgent.Migrator.csproj", "TextToSqlAgent.Migrator/"]
COPY ["TextToSqlAgent.Infrastructure/TextToSqlAgent.Infrastructure.csproj", "TextToSqlAgent.Infrastructure/"]
COPY ["TextToSqlAgent.Core/TextToSqlAgent.Core.csproj", "TextToSqlAgent.Core/"]

RUN dotnet restore "TextToSqlAgent.Migrator/TextToSqlAgent.Migrator.csproj"
COPY . .

RUN dotnet publish "TextToSqlAgent.Migrator/TextToSqlAgent.Migrator.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "TextToSqlAgent.Migrator.dll"]
