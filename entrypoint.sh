#!/bin/bash
set -e

PGDATA="${PGDATA:-/var/lib/postgresql/data}"
export PGDATA

# Ensure postgres directory ownership and permissions
mkdir -p "$PGDATA"
chown -R postgres:postgres /var/lib/postgresql "$PGDATA"
chmod 700 "$PGDATA"

# Initialize PostgreSQL data directory if not already initialized
if [ ! -s "$PGDATA/PG_VERSION" ]; then
    echo "==> [Groove] Initializing PostgreSQL cluster in $PGDATA..."
    su - postgres -c "initdb -D '$PGDATA' --encoding=UTF8 --locale=C"

    echo "==> [Groove] Configuring local authentication in pg_hba.conf..."
    echo "host all all 127.0.0.1/32 md5" >> "$PGDATA/pg_hba.conf"
    echo "host all all ::1/128 md5" >> "$PGDATA/pg_hba.conf"

    echo "==> [Groove] Starting temporary PostgreSQL instance for initial setup..."
    su - postgres -c "pg_ctl -D '$PGDATA' -w start"

    echo "==> [Groove] Setting 'postgres' password and creating 'groove' database..."
    su - postgres -c "psql -v ON_ERROR_STOP=1 <<-EOSQL
        ALTER USER postgres WITH PASSWORD 'postgres';
        CREATE DATABASE groove;
        GRANT ALL PRIVILEGES ON DATABASE groove TO postgres;
EOSQL"

    echo "==> [Groove] Stopping temporary PostgreSQL instance..."
    su - postgres -c "pg_ctl -D '$PGDATA' -m fast -w stop"
    echo "==> [Groove] PostgreSQL initialization complete."
fi

# Start PostgreSQL server in background
echo "==> [Groove] Starting PostgreSQL server..."
if ! su - postgres -c "pg_ctl -D '$PGDATA' -l /var/lib/postgresql/logfile -w start"; then
    echo "==> [Groove] PostgreSQL failed to start. Its log follows:" >&2
    cat /var/lib/postgresql/logfile >&2 2>/dev/null || echo "(no postgres log was written)" >&2
    if [ -f "$PGDATA/postmaster.pid" ]; then
        echo "==> [Groove] $PGDATA/postmaster.pid exists. If another Groove container is still running" >&2
        echo "    on the same volume, stop it first: in Dokploy set the update order to 'stop-first'." >&2
    fi
    ls -la "$PGDATA" >&2 || true
    exit 1
fi

# Wait for PostgreSQL to be ready
echo "==> [Groove] Verifying PostgreSQL is ready..."
until su - postgres -c "pg_isready -h localhost -p 5432" >/dev/null 2>&1; do
    sleep 0.5
done
echo "==> [Groove] PostgreSQL is accepting connections on localhost:5432."

# Signal handler for clean container shutdown
shutdown() {
    echo "==> [Groove] Shutting down application and PostgreSQL..."
    kill -s TERM "$APP_PID" 2>/dev/null || true
    wait "$APP_PID" 2>/dev/null || true
    su - postgres -c "pg_ctl -D '$PGDATA' -m fast -w stop"
    echo "==> [Groove] Clean shutdown complete."
    exit 0
}
trap shutdown SIGTERM SIGINT

# Start Groove .NET API
echo "==> [Groove] Launching Groove .NET 8 Web API..."
dotnet TriviaSync.Api.dll &
APP_PID=$!

# Wait for app process
wait $APP_PID
