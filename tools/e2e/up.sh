#!/usr/bin/env bash
# Starts the throw-away backend: a fresh database (auth stub + every migration + seeds), PostgREST on it, and the auth/proxy/static shim.
# Needs: psql + node; the `postgrest` binary on PATH (or $POSTGREST); PGHOST/PGUSER/PGPASSWORD for a Postgres server.
# Optional: E2E_BUILD_DIR=<WebGL build> so the shim also serves the game. Writes /tmp/aro-e2e.env (source it), pids in /tmp/aro-e2e.pids.
set -euo pipefail
cd "$(dirname "$0")/../.."
export PGHOST="${PGHOST:-localhost}" PGUSER="${PGUSER:-postgres}" PGPASSWORD="${PGPASSWORD:-postgres}"
DB=aro_e2e; PORT="${E2E_PORT:-54321}"; REST_PORT="${E2E_REST_PORT:-3000}"
SECRET="${PGRST_JWT_SECRET:-$(head -c 48 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c 48)}"
POSTGREST="${POSTGREST:-postgrest}"
psql -q -d postgres -c "drop database if exists $DB" -c "create database $DB"
for f in supabase/tests/00_auth_stub.sql supabase/migrations/*.sql supabase/seed.sql supabase/seed_world.sql supabase/seed_content.sql supabase/tests/01_testkit.sql; do
  psql -q -v ON_ERROR_STOP=1 -d $DB -f "$f" >/dev/null
done
DB_URL="postgresql://$PGUSER:$PGPASSWORD@$PGHOST/$DB"
PGRST_DB_URI="$DB_URL" PGRST_DB_SCHEMAS=public PGRST_DB_ANON_ROLE=anon PGRST_JWT_SECRET="$SECRET" PGRST_SERVER_HOST=127.0.0.1 PGRST_SERVER_PORT="$REST_PORT" PGRST_DB_POOL=10 \
  nohup "$POSTGREST" > /tmp/aro-e2e-postgrest.log 2>&1 & echo $! > /tmp/aro-e2e.pids
E2E_PORT="$PORT" E2E_REST="http://127.0.0.1:$REST_PORT" PGRST_JWT_SECRET="$SECRET" E2E_DB_URL="$DB_URL" E2E_BUILD_DIR="${E2E_BUILD_DIR:-}" \
  nohup node tools/e2e/server.js > /tmp/aro-e2e-shim.log 2>&1 & echo $! >> /tmp/aro-e2e.pids
for i in $(seq 1 60); do
  if curl -fsS "http://127.0.0.1:$PORT/e2e/health" >/dev/null 2>&1 && curl -sS -o /dev/null -w '%{http_code}' "http://127.0.0.1:$REST_PORT/" 2>/dev/null | grep -qE '^(200|401|404)$'; then break; fi
  sleep 1
  if [ "$i" = 60 ]; then echo "backend did not start"; tail -n 30 /tmp/aro-e2e-postgrest.log /tmp/aro-e2e-shim.log; exit 1; fi
done
ANON=$(curl -fsS "http://127.0.0.1:$PORT/e2e/health" | python3 -c 'import json,sys; print(json.load(sys.stdin)["anon_key"])')
cat > /tmp/aro-e2e.env <<EOF
export E2E_URL=http://127.0.0.1:$PORT
export E2E_ANON_KEY=$ANON
export PGRST_JWT_SECRET=$SECRET
export E2E_DB_URL=$DB_URL
EOF
echo "backend up: http://127.0.0.1:$PORT (source /tmp/aro-e2e.env)"
