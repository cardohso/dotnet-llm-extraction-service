# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, with only the project files, so this layer is cached until a dependency changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/DocService.Core/DocService.Core.csproj src/DocService.Core/
COPY src/DocService.Llm/DocService.Llm.csproj src/DocService.Llm/
COPY src/DocService.Api/DocService.Api.csproj src/DocService.Api/
RUN dotnet restore src/DocService.Api/DocService.Api.csproj

COPY src/ src/
RUN dotnet publish src/DocService.Api/DocService.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .

# The base image ships a non-root "app" user; APP_UID is its numeric id.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -fsS http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "DocService.Api.dll"]
