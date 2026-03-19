# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files (từ root context)
COPY ["TextToSqlAgent.API/TextToSqlAgent.API.csproj", "TextToSqlAgent.API/"]
COPY ["TextToSqlAgent.Application/TextToSqlAgent.Application.csproj", "TextToSqlAgent.Application/"]
COPY ["TextToSqlAgent.Infrastructure/TextToSqlAgent.Infrastructure.csproj", "TextToSqlAgent.Infrastructure/"]
COPY ["TextToSqlAgent.Core/TextToSqlAgent.Core.csproj", "TextToSqlAgent.Core/"]
COPY ["TextToSqlAgent.Plugins/TextToSqlAgent.Plugins.csproj", "TextToSqlAgent.Plugins/"]

# Restore packages với clean cache
RUN dotnet nuget locals all --clear
RUN dotnet restore "TextToSqlAgent.API/TextToSqlAgent.API.csproj" --no-cache

# Copy toàn bộ source code
COPY . .

# Build và publish với full restore
WORKDIR "/src/TextToSqlAgent.API"
RUN dotnet publish "TextToSqlAgent.API.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl cho health check
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Copy published files
COPY --from=build /app/publish .

# Expose port
EXPOSE 5251

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
  CMD curl -f http://localhost:5251/health || exit 1

# Entry point
ENTRYPOINT ["dotnet", "TextToSqlAgent.API.dll"]