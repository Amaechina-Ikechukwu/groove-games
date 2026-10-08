# Multi-stage build for TriviaSync All-In-One Container (.NET 8 + PostgreSQL)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project definitions
COPY src/TriviaSync.Api/TriviaSync.Api.csproj src/TriviaSync.Api/
COPY tests/TriviaSync.Tests/TriviaSync.Tests.csproj tests/TriviaSync.Tests/

# Restore dependencies
RUN dotnet restore src/TriviaSync.Api/TriviaSync.Api.csproj
RUN dotnet restore tests/TriviaSync.Tests/TriviaSync.Tests.csproj

# Copy full source tree and static assets
COPY . ./

# Build and test
RUN dotnet test tests/TriviaSync.Tests/TriviaSync.Tests.csproj --no-restore -c Release
RUN dotnet publish src/TriviaSync.Api/TriviaSync.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime Image with ASP.NET Core Runtime and PostgreSQL
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Environment configuration
ENV ASPNETCORE_URLS=http://+:5000 \
    ASPNETCORE_ENVIRONMENT=Production \
    DEBIAN_FRONTEND=noninteractive \
    PGDATA=/var/lib/postgresql/data

# Install PostgreSQL server, client, curl, and dos2unix
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    postgresql \
    postgresql-contrib \
    curl \
    dos2unix && \
    rm -rf /var/lib/apt/lists/*

# Symlink all postgres binaries (initdb, pg_ctl, psql, pg_isready) into /usr/local/bin
RUN for bin in /usr/lib/postgresql/*/bin/*; do \
      if [ -f "$bin" ]; then \
        ln -sf "$bin" "/usr/local/bin/$(basename "$bin")"; \
      fi \
    done

# Copy compiled application
COPY --from=build /app/publish .

# Copy and prepare entrypoint script
COPY entrypoint.sh /app/entrypoint.sh
RUN sed -i 's/\r$//' /app/entrypoint.sh && chmod +x /app/entrypoint.sh

# Web API and SignalR port
EXPOSE 5000

# Persistent PostgreSQL storage directory
VOLUME ["/var/lib/postgresql/data"]

# Health check
HEALTHCHECK --interval=15s --timeout=5s --start-period=12s --retries=3 \
  CMD curl -f http://localhost:5000/health || exit 1

ENTRYPOINT ["/app/entrypoint.sh"]
