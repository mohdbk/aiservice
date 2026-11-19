#!/usr/bin/env bash
# Starts the whole stack for the browser tests (Ministry of Economy & Tourism), as it runs on IIS: SQL Server is a CI
# service; the HRMS app (API, assistant and staff app from wwwroot) on :5080; the careers portal on :8080; and a
# stand-in model on :9000. Then loads the demo data.
# Settings (environment): DB_NAME, MODEL_SCRIPT (default e2e/fake-model.mjs), RUN_WORKERS (default 0), and API_LLM_MODEL
# to give the API's own AI features a model as well (the assistant always uses the stand-in model "fake").
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT=$PWD
API_BIN=$ROOT/backend/src/Moet.Hrms.Api/bin/Release/net10.0
PORTAL_BIN=$ROOT/portal/host/Moet.Careers.Portal/bin/Release/net10.0
rm -rf "$API_BIN/wwwroot" "$PORTAL_BIN/wwwroot"
cp -r hrms/web/dist "$API_BIN/wwwroot"
cp -r portal/web/dist "$PORTAL_BIN/wwwroot"

node "${MODEL_SCRIPT:-e2e/fake-model.mjs}" > model.log 2>&1 &
API_LLM=()
if [ -n "${API_LLM_MODEL:-}" ]; then API_LLM=(LLM_BASE_URL=http://localhost:9000/v1 LLM_MODEL="$API_LLM_MODEL" LLM_API_KEY=none); fi
(cd "$API_BIN" && env DB_CONNECTION="Server=localhost,1433;Database=${DB_NAME:-MoetE2e};User Id=sa;Password=Ci-Strong-Password-123;TrustServerCertificate=True;Encrypt=False" \
  PORTAL_API_KEY=e2e-portal-key PORTAL_API_SECRET=e2e-secret-0123456789 PORTAL_URL=http://127.0.0.1:8080 COOKIE_SECURE=0 RUN_WORKERS="${RUN_WORKERS:-0}" \
  AGENT_LLM_BASE_URL=http://localhost:9000/v1 AGENT_LLM_MODEL=fake AGENT_LLM_API_KEY=none "${API_LLM[@]}" \
  dotnet Moet.Hrms.Api.dll --urls http://localhost:5080 > "$ROOT/api.log" 2>&1 &)
(cd "$PORTAL_BIN" && env HRMS_API_URL=http://localhost:5080 CAREERS_PORTAL_API_KEY=e2e-portal-key CAREERS_PORTAL_API_SECRET=e2e-secret-0123456789 \
  SESSION_SECRET=e2e-session-secret-0123456789abcdef COOKIE_SECURE=0 PORT=8080 \
  dotnet Moet.Careers.Portal.dll > "$ROOT/portal.log" 2>&1 &)
for i in $(seq 1 90); do
  curl -sf http://localhost:5080/healthz >/dev/null && curl -sf http://localhost:5080/ >/dev/null && curl -sf http://127.0.0.1:8080/ar >/dev/null && break
  sleep 2
done
curl -sf http://localhost:5080/ | grep -q 'id="root"' || { echo "HRMS app not served"; tail -60 api.log; exit 1; }
API=http://localhost:5080 PORTAL_KEY=e2e-portal-key:e2e-secret-0123456789 node hrms/scripts/seed-demo.mjs
