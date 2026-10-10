# Run the first backend delivery

This service handles test registration only. It has no frontend, login or public deployment. Start at [the implementation baseline](BASELINE.md).

## Docker: local database and backend

Requirements: Docker Engine with Compose. From repository root:

```bash
bash scripts/init-dev.sh
docker compose up --build
```

The API binds to `127.0.0.1:8080`. PostgreSQL has no published host port. The migration service must succeed before the API starts. `.env` holds generated local keys, is ignored by git, and must be retained with the test database. Do not regenerate keys while retaining encrypted work. Compose is development-only; do not expose it to the Internet.

`GET /health/live` checks the process; `GET /health/ready` checks migration availability. Neither is a claim of product/release readiness.

## Host development

Install .NET 10 SDK and provide PostgreSQL 17. Set:

```text
ConnectionStrings__Identity=Host=localhost;Database=smartcore;Username=...;Password=...
ASPNETCORE_ENVIRONMENT=Development
Identity__MacKey=<base64 of 32 random bytes>
Identity__MaterialKey=<different base64 of 32 random bytes>
```

```bash
dotnet run --project src/SmartCore.Identity.Api -- --migrate
dotnet run --project src/SmartCore.Identity.Api
```

Migration is explicit and repeatable, serialized by an advisory lock. This initial migration bootstraps a new database; do not edit it after deployment. Add forward migrations for future schema changes. HTTP startup never runs DDL.

For a test OTP inbox, additionally set a random `Identity__DevInboxKey` of at least 32 characters, then read `/dev/inbox/{verificationSessionId}` with header `X-Dev-Inbox-Key`. This route requires Development, opt-in key and a loopback caller. Compose does not enable it; use host development for the local smoke demo. Never use a real contact/password in test fixtures. Codes are not printed in application logs.

## Registration API

The executable subset is [OpenAPI](../../contracts/registration.openapi.yaml).

1. Generate `bindingSecret` from 32 cryptographically random bytes, base64url without padding; generate a separate unpredictable `Idempotency-Key`.
2. `POST /auth/register` with JSON `email` **or** `mobile`, `password`, `displayName`, `bindingSecret`, and the idempotency header. Expect 202/AwaitingVerification.
3. Read the test delivery through the authenticated local inbox; real delivery is not connected yet.
4. `POST /auth/register/verify` with `verificationSessionId`, six-digit `code`, and the same `bindingSecret`. First success is 201/PendingCredential; it does not log in.
5. Worker commits Credential, Ready and acknowledgment. Within proof expiry/budget, replay the identical verification to read the current result (200/Ready). Preserve the same request after a lost response.
6. `POST /auth/register/resend` with session and binding secret returns generic 202. It does not reopen a consumed/expired challenge or reset the attempt budget.

## Tests and operations

Use an exclusively disposable test database. The harness deliberately requires reset permission and truncates only its named application tables:

```bash
export SMARTCORE_TEST_DB='Host=localhost;Database=smartcore_test;Username=...;Password=...'
export SMARTCORE_TEST_ALLOW_RESET=yes
dotnet run --project tests/SmartCore.Identity.Tests
```

CI provisions PostgreSQL 17 and runs the same harness. Local PGlite results do not replace native PostgreSQL race verification. Install Python test dependencies with `python -m pip install -r requirements-dev.txt`. `scripts/api-smoke.py` additionally runs HTTP checks against a disposable database provided via `ConnectionStrings__Identity`.

For investigation inspect non-secret `workflow_jobs` status: `recovery_needed`, attempts, next attempt, classified error; inspect counts/age of unpublished Outbox entries. No API exposes these operational tables. Do not dump verification/material/credential tables into tickets. No unrestricted admin mutation endpoint exists. Failed material/proof windows require the next delivery's formal recovery implementation; do not unlock guards or overwrite password hashes manually.

Production startup intentionally fails. Before removing that guard, complete reset, delivery, recovery, Session/BFF, native concurrency/crash tests, secret management, least-privilege roles, HTTPS, external audit and restore/reconciliation gates in the baseline. Deployment destination and provider credentials have not been supplied.
