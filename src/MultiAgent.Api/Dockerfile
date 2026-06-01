# syntax=docker/dockerfile:1
#
# Multi-stage build for MultiAgent.Api. Used only by the docker-compose "full"
# profile; the default dev topology runs the API on the host instead.
# Build context is the repo root:  docker build -f src/MultiAgent.Api/Dockerfile .

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore as its own cached layer: copy only the project files first.
COPY global.json ./
COPY src/MultiAgent.Core/MultiAgent.Core.csproj                     src/MultiAgent.Core/
COPY src/MultiAgent.Agents/MultiAgent.Agents.csproj                 src/MultiAgent.Agents/
COPY src/MultiAgent.Infrastructure/MultiAgent.Infrastructure.csproj src/MultiAgent.Infrastructure/
COPY src/MultiAgent.Api/MultiAgent.Api.csproj                       src/MultiAgent.Api/
RUN dotnet restore src/MultiAgent.Api/MultiAgent.Api.csproj

# Copy the sources + seed data and publish.
COPY src/ src/
COPY data/ data/
RUN dotnet publish src/MultiAgent.Api/MultiAgent.Api.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./

# Writable dirs for SQLite/outbox/notes (volume-mounted at /app/runtime) and
# Serilog logs. A fresh named volume inherits this ownership, so the non-root
# app user can write to it.
RUN mkdir -p /app/runtime /app/logs \
    && chown -R $APP_UID:$APP_UID /app/runtime /app/logs

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "MultiAgent.Api.dll"]
