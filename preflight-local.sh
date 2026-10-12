#!/usr/bin/env bash
# SmartCoreIdentity local B1 verification. Never connects to the VPS or uses an existing DB.
# Build/test writes go to an isolated current-worktree copy. Full mode owns one disposable PG17 container.
set +x
set -uo pipefail
umask 077
ROOT=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P) || exit 1
MODE=${1:---full}
case "$MODE" in --full|--static-only|--probe-only) ;; *) echo 'Usage: bash preflight-local.sh [--full|--static-only|--probe-only]'; exit 1;; esac
PASS=0; FAIL=0; SKIP=0; CID=''; CHILD=''; TEMP=''
ok() { printf '[PASS] %s\n' "$1"; PASS=$((PASS+1)); }
bad() { printf '[FAIL] %s\n' "$1"; FAIL=$((FAIL+1)); }
skip() { printf '[SKIP] %s\n' "$1"; SKIP=$((SKIP+1)); }
cleanup() {
  if [[ -n "$CHILD" ]]; then kill -TERM -- "-$CHILD" 2>/dev/null || true; fi
  if [[ "$CID" =~ ^[0-9a-f]{12,64}$ ]]; then docker rm -f "$CID" >/dev/null 2>&1 || true; fi
  if [[ -n "$TEMP" && -d "$TEMP" ]]; then rm -rf -- "$TEMP"; fi
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
PYTHON_BIN=${SMARTCORE_PYTHON:-python3}
DOTNET_BIN=${SMARTCORE_DOTNET:-dotnet}
GITLEAKS_BIN=${SMARTCORE_GITLEAKS:-gitleaks}
for binary in git "$PYTHON_BIN" mktemp timeout setsid; do
  if ! command -v "$binary" >/dev/null 2>&1; then printf '[FAIL] Required utility unavailable\n'; exit 1; fi
done
if ! git -C "$ROOT" rev-parse --is-inside-work-tree >/dev/null 2>&1; then echo '[FAIL] Git working copy required'; exit 1; fi
TMPBASE=${TMPDIR:-/tmp}
if ! "$PYTHON_BIN" -c 'import pathlib,sys; sys.exit(pathlib.Path(sys.argv[1]).resolve().is_relative_to(pathlib.Path(sys.argv[2]).resolve()))' "$TMPBASE" "$ROOT"; then echo '[FAIL] TMPDIR must be outside the repository'; exit 1; fi
TEMP=$(mktemp -d "$TMPBASE/smartcore-local-preflight.XXXXXXXX") || exit 1
LOG_INDEX=0
run_check() {
  local label=$1 budget=$2 result
  shift 2
  LOG_INDEX=$((LOG_INDEX+1))
  # Own a process group so interruption cleans this command and its test-host children only.
  setsid timeout --kill-after=10 "$budget" "$@" >"$TEMP/check-$LOG_INDEX.log" 2>&1 < /dev/null &
  CHILD=$!
  wait "$CHILD"; result=$?
  CHILD=''
  if [[ "$result" -eq 0 ]]; then ok "$label"; return 0; fi
  bad "$label (exit $result; logs suppressed and removed, no secret-bearing tail)"; return 1
}
summary() {
  printf '\nSummary: %s passed, %s failed, %s skipped\n' "$PASS" "$FAIL" "$SKIP"
  if [[ "$FAIL" -gt 0 ]]; then echo 'RESULT: FAILED'; return 1; fi
  if [[ "$SKIP" -gt 0 ]]; then echo 'RESULT: INCOMPLETE — skipped checks are not PASS'; return 2; fi
  echo 'RESULT: requested local B1 checks passed — no VPS/public-release approval'; return 0
}
if [[ "$MODE" == --probe-only ]]; then
  if [[ -z "${BASE_URL:-}" ]]; then bad 'BASE_URL required for explicit local probe';
  else run_check 'Local HTTPS/header/cookie/guard probe' 80 "$PYTHON_BIN" "$ROOT/tools/preflight_local.py" probe --origin "$BASE_URL" --ca "${LOCAL_CA_FILE:-}" || true; fi
  skip 'Repository, backend, DB and browser journey were not run in probe-only mode'
  summary; exit $?
fi
# No build/restore/npm write is permitted in ROOT.
if ! run_check 'Isolated current-worktree copy' 60 "$PYTHON_BIN" "$ROOT/tools/preflight_local.py" copy --root "$ROOT" --target "$TEMP/source"; then summary; exit $?; fi
run_check 'No tracked non-template .env' 30 "$PYTHON_BIN" "$ROOT/tools/preflight_local.py" env --root "$ROOT" || true
if command -v "$GITLEAKS_BIN" >/dev/null 2>&1; then
  # dir scans the current copy, including untracked files; git scans history separately.
  run_check 'Gitleaks current tracked/untracked source (redacted)' 180 "$GITLEAKS_BIN" dir "$TEMP/source" --config "$ROOT/deploy/preflight/gitleaks.toml" --no-banner --redact --exit-code 1 || true
  if [[ "$(git -C "$ROOT" rev-parse --is-shallow-repository)" == false ]]; then
    run_check 'Gitleaks available Git history/all refs (redacted)' 300 "$GITLEAKS_BIN" git "$ROOT" --log-opts=--all --config "$ROOT/deploy/preflight/gitleaks.toml" --no-banner --redact --exit-code 1 || true
  else skip 'Shallow checkout: complete available-ref history scan not possible'; fi
else
  run_check 'Limited key-marker heuristic (not a full secret scan)' 60 "$PYTHON_BIN" "$ROOT/tools/preflight_local.py" heuristic --root "$ROOT" || true
  skip 'Gitleaks unavailable: full working-tree/history secret scan not performed'
fi
if "$PYTHON_BIN" -c 'import yaml' >/dev/null 2>&1; then
  run_check 'Browser personId schema/property boundary' 60 "$PYTHON_BIN" "$TEMP/source/tools/preflight_local.py" browser --root "$TEMP/source" || true
else skip 'PyYAML unavailable for structural browser-contract check'; fi
if [[ "$MODE" == --static-only ]]; then
  skip 'Builds, actual native DB harness, migration/reapply/restore, host/config and Chromium not run in static-only mode'
  summary; exit $?
fi
# Do not inherit any existing deployment/test DB, secret, host bind or diagnostic configuration.
for variable in $(compgen -e); do
  case "$variable" in ConnectionStrings__*|DATABASE_URL|SMARTCORE_TEST_*|Identity__*|Bff__*|Kestrel__*|ASPNETCORE_*|DOTNET_URLS|SMARTCORE_INGRESS_*|SMARTCORE_TLS_*|SMARTCORE_BFF_*|SMARTCORE_NEXT_*|BASE_URL)
    unset "$variable";;
  esac
done
export DOTNET_CLI_HOME="$TEMP/dotnet-home" NUGET_PACKAGES="$TEMP/nuget" npm_config_cache="$TEMP/npm-cache"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 PYTHONDONTWRITEBYTECODE=1
export PLAYWRIGHT_BROWSERS_PATH="$TEMP/browsers" SMARTCORE_DOTNET="$DOTNET_BIN"
DOTNET_READY=0; NODE_READY=0; PY_READY=0; DB_READY=0; FRONT_READY=0; BUILD_READY=0; PORTS_READY=0
if command -v "$DOTNET_BIN" >/dev/null 2>&1 && [[ "$("$DOTNET_BIN" --version 2>/dev/null)" == 10.* ]]; then DOTNET_READY=1; else skip '.NET 10 SDK unavailable'; fi
if command -v node >/dev/null 2>&1 && command -v npm >/dev/null 2>&1 && [[ "$(node --version)" == v24.* ]]; then NODE_READY=1; else skip 'Pinned Node 24/npm unavailable'; fi
if run_check 'Isolated Python environment' 60 "$PYTHON_BIN" -m venv "$TEMP/python"; then
  PYTHON_BIN="$TEMP/python/bin/python"
  if run_check 'Pinned Python contract dependencies' 180 "$PYTHON_BIN" -m pip install --cache-dir "$TEMP/pip-cache" -r "$TEMP/source/requirements-dev.txt"; then PY_READY=1; fi
fi
cd -- "$TEMP/source" || exit 1
if [[ "$PY_READY" -eq 1 ]]; then
  run_check 'Browser OpenAPI/schema/precedence candidate checks' 60 "$PYTHON_BIN" tools/check_bff_browser_contract.py || true
  run_check 'Generated browser client contract drift' 30 "$PYTHON_BIN" tools/generate_bff_browser_client.py --check || true
else skip 'Python contract/dependency checks not available'; fi
if [[ "$DOTNET_READY" -eq 1 ]]; then
  if run_check 'Locked Identity restore' 240 "$DOTNET_BIN" restore src/SmartCore.Identity.Api --locked-mode \
    && run_check 'Release browser host build' 240 "$DOTNET_BIN" build src/SmartCore.Identity.Bff.Host -c Release -m:1 \
    && run_check 'Release executable-test-harness build' 240 "$DOTNET_BIN" build tests/SmartCore.Identity.Tests -c Release -m:1; then BUILD_READY=1; fi
else skip 'Backend build/config/harness prerequisites incomplete'; fi
if run_check 'Private HTTP fixture ports are free' 15 "$PYTHON_BIN" -c 'import socket; peers=[]
for port in (3181,5157,5171,5172,5271,5272,7443):
 s=socket.socket(); s.bind(("127.0.0.1",port)); peers.append(s)'; then PORTS_READY=1; fi
if [[ "$NODE_READY" -eq 1 ]]; then
  export NEXT_PUBLIC_BFF_ACCOUNTS_ENABLED=true
  if run_check 'Pinned frontend install (no lifecycle hooks)' 240 npm ci --ignore-scripts --prefix web/identity \
    && run_check 'fa/en parity and Node timezone checks' 60 npm run check --prefix web/identity \
    && run_check 'Frontend TypeScript' 120 npm run typecheck --prefix web/identity \
    && run_check 'Production frontend build in temporary copy' 240 npm run build --prefix web/identity; then FRONT_READY=1; fi
  if [[ "$FRONT_READY" -eq 1 && "$PORTS_READY" -eq 1 ]]; then
    run_check 'Production Next/TLS HTTP checks' 90 npm run smoke --prefix web/identity || true
    run_check 'TLS ingress cookie/header/no-retry boundary' 60 node tests/ingress/https-ingress.test.mjs || true
  fi
else skip 'Frontend checks not available'; fi
if [[ "$BUILD_READY" -eq 1 && "$PY_READY" -eq 1 ]]; then
  run_check 'Healthy Testing control, missing configuration and independent Production rejection' 90 "$PYTHON_BIN" scripts/bff-host-smoke.py --config-only || true
fi
DOCKER_LOCAL=0
if command -v docker >/dev/null 2>&1; then
  DOCKER_ENDPOINT=${DOCKER_HOST:-$(docker context inspect --format '{{.Endpoints.docker.Host}}' 2>/dev/null)}
  if [[ "$DOCKER_ENDPOINT" == unix://* ]] && docker info >/dev/null 2>&1; then DOCKER_LOCAL=1; fi
fi
if [[ "$BUILD_READY" -eq 1 && "$PY_READY" -eq 1 && "$NODE_READY" -eq 1 && "$DOCKER_LOCAL" -eq 1 ]]; then
  PASSWORD=$("$PYTHON_BIN" -c 'import secrets; print(secrets.token_urlsafe(32))')
  if docker run -d --rm -e "POSTGRES_PASSWORD=$PASSWORD" -e POSTGRES_DB=smartcore_preflight -p 127.0.0.1::5432 postgres:17 >"$TEMP/cid" 2>"$TEMP/docker.log"; then
    CID=$(cat "$TEMP/cid")
    if [[ "$CID" =~ ^[0-9a-f]{12,64}$ ]]; then
      VERSION=''
      # The image's temporary initialization server accepts Unix sockets before the
      # requested database exists. Require a successful query over the final TCP listener.
      for ((attempt=0; attempt<40; attempt++)); do
        if VERSION=$(docker exec -e "PGPASSWORD=$PASSWORD" "$CID" psql -X -h 127.0.0.1 -U postgres -d smartcore_preflight -At -v ON_ERROR_STOP=1 -c 'SHOW server_version_num;' 2>"$TEMP/readiness.log"); then DB_READY=1; break; fi
        sleep 0.25
      done
      if [[ "$DB_READY" -eq 1 ]]; then
        PORT=$(docker port "$CID" 5432/tcp); PORT=${PORT##*:}
        if [[ ! "$PORT" =~ ^[0-9]+$ || ! "$VERSION" =~ ^17[0-9]{4}$ ]]; then DB_READY=0; bad 'Disposable PostgreSQL 17 version/loopback port check';
        else
          export ConnectionStrings__Identity="Host=127.0.0.1;Port=$PORT;Database=smartcore_preflight;Username=postgres;Password=$PASSWORD"
          export ConnectionStrings__Bff="$ConnectionStrings__Identity" SMARTCORE_TEST_DB="$ConnectionStrings__Identity"
          export SMARTCORE_TEST_ALLOW_RESET=yes SMARTCORE_TEST_ENGINE='PostgreSQL 17' SMARTCORE_EVENT_FIXTURES="$TEMP/identity-events.json"
          ok 'Owned disposable PostgreSQL 17 ready on an ephemeral loopback port'
        fi
      else bad 'Owned PostgreSQL container did not become ready'; fi
    else bad 'Docker returned no valid owned container identifier'; fi
  else bad 'Could not create the disposable PostgreSQL container'; fi
else skip 'Native PostgreSQL phase unavailable: Docker/daemon or build/dependency prerequisite missing'; fi
if [[ "$DB_READY" -eq 1 ]]; then
  API=src/SmartCore.Identity.Api/bin/Release/net10.0/SmartCore.Identity.Api.dll
  run_check 'Forward migrations to disposable DB' 60 "$DOTNET_BIN" "$API" --migrate || true
  run_check 'Reapplying forward migrations to the same DB' 60 "$DOTNET_BIN" "$API" --migrate || true
  if run_check 'Actual executable native backend harness (not dotnet test)' 600 "$DOTNET_BIN" tests/SmartCore.Identity.Tests/bin/Release/net10.0/SmartCore.Identity.Tests.dll; then
    run_check 'Actual emitted-event contract validation' 60 "$PYTHON_BIN" scripts/check-events.py "$TEMP/identity-events.json" || true
    run_check 'Actual reset-event proposal validation' 60 "$PYTHON_BIN" scripts/check-reset-event-proposal.py "$TEMP/identity-events.json" || true
    SQL='SELECT count(*) FROM schema_versions; SELECT count(*) FROM bff_schema_versions; SELECT count(*) FROM persons; SELECT count(*) FROM auth_sessions; SELECT count(*) FROM bff_sessions;'
    if docker exec "$CID" pg_dump -U postgres -d smartcore_preflight -Fc >"$TEMP/backup.dump" 2>"$TEMP/dump.log" \
      && docker exec "$CID" createdb -U postgres smartcore_restore >"$TEMP/createdb.log" 2>&1 \
      && docker exec -i "$CID" pg_restore -U postgres -d smartcore_restore --exit-on-error --no-owner <"$TEMP/backup.dump" >"$TEMP/restore.log" 2>&1; then
      if BEFORE=$(docker exec "$CID" psql -X -U postgres -d smartcore_preflight -At -v ON_ERROR_STOP=1 -c "$SQL")         && AFTER=$(docker exec "$CID" psql -X -U postgres -d smartcore_restore -At -v ON_ERROR_STOP=1 -c "$SQL")         && [[ -n "$BEFORE" && "$BEFORE" == "$AFTER" ]]; then ok 'Disposable DB dump/restore and migration/person/session counts match'; else bad 'Disposable restore counts mismatch'; fi
    else bad 'Disposable pg_dump/pg_restore failed'; fi
  fi
  if [[ "$PORTS_READY" -eq 1 ]]; then
    for runner in api setup auth password recovery; do
      run_check "Existing $runner HTTP smoke" 180 "$PYTHON_BIN" "scripts/$runner-smoke.py" || true
    done
    run_check 'Real two-host/TLS session-CSRF guard checks' 180 "$PYTHON_BIN" scripts/bff-host-smoke.py || true
    if [[ "$FRONT_READY" -eq 1 ]]; then
      if run_check 'Pinned Playwright fixture install' 120 npm ci --ignore-scripts --prefix web/identity/e2e \
        && run_check 'Chromium download to temporary cache (no OS package install)' 240 npx --prefix web/identity/e2e playwright install chromium; then
        export SMARTCORE_BROWSER_E2E=yes
        run_check 'Actual B1 Chromium/Identity/two-host account journey' 240 "$PYTHON_BIN" scripts/bff-account-smoke.py || true
      fi
    else skip 'Real browser journey prerequisites incomplete'; fi
  fi
fi
printf '\nScope: implemented private B1 only. No Down migration exists; no up/down rollback is claimed.\n'
printf 'B2/B3 browser flows, production gates and VPS/key-ring restore remain outside this result.\n'
summary; exit $?
