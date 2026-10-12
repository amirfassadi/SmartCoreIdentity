# Local preflight before VPS work

`bash preflight-local.sh` runs the **implemented private B1** checks without VPS
access. It uses current tracked/non-ignored untracked source, copied to a private
temporary directory. Builds, npm installs, Python packages, caches, browser binaries,
logs and disposable backup files live there; cleanup removes them and its owned
PostgreSQL container. It never reads ignored .env files or accepts an existing DB.
Remote Docker endpoints and external/public BASE_URL probes are not accepted.

Prerequisites: Linux/Bash, Git, Python with venv, .NET 10 SDK, Node 24/npm, OpenSSL,
Docker with a local Unix-socket daemon, Chromium OS dependencies and Gitleaks with
`git`/`dir` commands (CI pins 8.28.0). The script installs no OS packages. Dependency
package/image/browser downloads still need connectivity. `SMARTCORE_DOTNET`,
`SMARTCORE_PYTHON` and `SMARTCORE_GITLEAKS` can select explicit executable paths.

```sh
bash preflight-local.sh                 # full isolated local B1 verification
bash preflight-local.sh --static-only   # intentionally incomplete; exits 2
BASE_URL=https://localhost:7443 LOCAL_CA_FILE=/absolute/path/local.crt \
  bash preflight-local.sh --probe-only  # strict local TLS probe only; exits 2
```

Exit **0**: requested full local B1 checks passed. Exit **1**: failure. Exit **2**:
incomplete due to any skip, including an intentionally limited mode. A zero fail
count does not turn skipped tests into evidence. No exit code authorizes production.

## Corrections to the generic template

- Secrets: current tracked/untracked source and available-ref Git history are separate
  scans. Shallow history is incomplete. Missing Gitleaks cannot be replaced by a full
  PASS from grep. No source line, token, cookie, connection string or raw failure tail
  is printed. Private logs are deleted. The exact four legacy refresh-UUID sample
  occurrences in the byte-pinned archived Blueprint are allowlisted by one path **and**
  exact literal values; no blanket documentation/test-folder exclusion is used.
- Browser identifiers: schema/property checks recognize personId casing; prose saying
  "do not expose personId" is not a DTO leak. Actual response isolation remains the
  responsibility of the real B1 HTTP/Chromium tests.
- Backend: this project uses an executable native PostgreSQL harness, not a test-SDK
  solution. `dotnet test` is not evidence that its 303 existing controls ran.
- Frontend: use actual `check`, `typecheck`, `build`, `smoke` scripts; nonexistent
  `lint`/`test` commands cannot silently count as tests. All build writes are isolated.
- Database: an owned postgres:17 container uses a random password and ephemeral
  loopback port. Forward migrations are reapplied to the same DB. After the real
  native harness, pg_dump/pg_restore into a second disposable DB must preserve
  migration/person/session counts. There is no Down migration and no up/down claim.
  This database-only local restore does not certify VPS/WAL/key-ring disaster recovery.
- Required configuration: a healthy Testing startup is the positive control; each
  of 11 missing BFF settings must independently reject before database/network work.
  Production refusal is a separate control, not proof of individual variable checks.
- HTTPS: full mode invokes the real owned two-host/TLS/Next/Chromium suites. Explicit
  probe-only mode verifies certificate trust (no -k), exact headers and every
  antiforgery-cookie attribute including HttpOnly, Path=/ and no Domain. Missing
  cookies fail. Missing Origin/missing CSRF/media rejection require exact status/code;
  400/401/404 router/business failures cannot masquerade as CSRF evidence. A CSRF
  bootstrap alone says nothing about session-cookie attributes or account E2E.
- Fixture ports are checked before any local HTTP tests. No existing listener is
  stopped. Interruption cleans only the owned command process group and container.

B2 registration/OTP and B3 recovery/reset/change browser flows are not implemented
by this runner. RELEASE_GATES.md, canonical Platform provenance, real delivery,
VPS configuration and operator-controlled backups remain independent requirements.

Gitleaks command reference: https://github.com/gitleaks/gitleaks#usage .
