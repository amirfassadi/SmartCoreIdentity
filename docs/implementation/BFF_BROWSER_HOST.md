Current continuation: private account handlers and fa/en client are implemented in
[BFF_BROWSER_ACCOUNTS.md](BFF_BROWSER_ACCOUNTS.md); their exact verified private CI revisions
are recorded there. Historical infrastructure scope below
remains tied to its recorded revisions.

# B1 browser infrastructure

Status: tasks 2 and 3 started after the 0.1.1 browser contract review. No production
launch, account handlers, generated client or completed B1 claim. Development HTTPS
ingress is now present; see HTTPS_INGRESS.md for its acceptance evidence boundaries.

## Delivered

- [Next shell](../../web/identity/package.json): pinned Next.js/React/TypeScript,
  next-intl, Tailwind and npm lockfile; `/fa` RTL and `/en` LTR; client account shell
  with no Identity calls, credentials or token access. No remote fonts/CDN dependency.
- Dynamic HTML, fresh nonce forwarded to Next rendering, CSP denying inline style
  attributes, no-store, frame/referrer/permissions policy. Production HTTP smoke
  checks all emitted script nonces; this is not browser hydration evidence.
- Locale cookie is explicitly `smartcore-locale`, Secure/Lax/Path=/, bounded age,
  no Domain. Next's defence-in-depth `/bff` rejection produces JSON instead of HTML;
  the development HTTPS ingress now routes **all** `/bff` traffic to .NET with
  independent request/response cookie allowlists; see HTTPS_INGRESS.md.
- [.NET host](../../src/SmartCore.Identity.Bff.Host/Program.cs) registers existing
  BffStore, refresh/logout adapter, BffRefresh, BffCircuit and BffMaintenance;
  runs existing PostgreSQL migrations before readiness (PostgreSQL 17 minimum).
- Both hosts use the same application name and shared Data Protection directory,
  with an explicit separate RSA wrapping certificate, ephemeral private-key load
  and no unprotected-key fallback. Production, public bind, missing certificate
  and equal client/receipt keys are rejected at startup. Certificate/key-ring
  filesystem permissions, secret mounts and rotation remain operator obligations.
- Antiforgery cookie/header options are registered. The opaque-session principal and CSRF validation are now implemented;
  see [BFF_BROWSER_GUARD.md](BFF_BROWSER_GUARD.md). GET /bff/csrf issues an
  antiforgery request token. Account handlers remain disconnected: guard-passing
  account commands return unavailable without minting login session cookies or
  upstream work. Other methods return route-specific `405`/Allow; undeveloped
  B2/B3 and unknown paths return JSON `404 NOT_FOUND`; responses are no-store.
  Task-6 account commands still terminate in unavailable; actual account
  transitions and browser acceptance remain open.

## Configuration and local checks

Development/Testing only. Supply configuration through an untracked environment or
secret mount; nothing here generates production secrets. Both hosts need:

| Setting | Meaning |
| --- | --- |
| `ASPNETCORE_URLS` | Separate loopback listen URLs, for example ports 5171 and 5172 |
| `ConnectionStrings__Bff` | Shared disposable PostgreSQL 17 database for acceptance |
| `Bff__ExternalOrigin` | Fixed HTTPS browser origin, e.g. https://localhost:7443 |
| `Bff__IdentityOrigin` | Private loopback Identity root URL; no path/query/credentials |
| `Bff__ClientKey`, `Bff__ReceiptKey` | Separate existing Identity client and receipt keys |
| `Bff__SessionMacKey`, `Bff__SessionMaterialKey` | Two distinct base64-encoded 32-byte BFF session keys |
| `Bff__DataProtection__KeyRing` | Absolute existing shared directory, access restricted |
| `Bff__DataProtection__CertificatePath` | Absolute PFX file outside the key ring |
| `Bff__DataProtection__CertificatePassword` | Password from the secret mount |

```sh
npm ci --ignore-scripts --prefix web/identity
npm run check --prefix web/identity
npm run typecheck --prefix web/identity
npm run build --prefix web/identity
npm run smoke --prefix web/identity
dotnet build src/SmartCore.Identity.Bff.Host -c Release -m:1
python scripts/bff-host-smoke.py --config-only
# With ConnectionStrings__Bff pointing to a disposable PostgreSQL 17 database:
python scripts/bff-host-smoke.py
```

The smoke runner provisions temporary test-only wrapping material and two hosts,
checks encrypted XML and cross-process protection/decryption, and checks JSON
routing responses against the exact browser contract. It never logs tokens or
connection strings. There is no diagnostic HTTP endpoint for key-ring proof.
The new Browser B1 infrastructure workflow executes the Next production checks
and the two-host PostgreSQL checks in isolated jobs.

## Current continuation

Private task-6 account handlers and task-7 fa/en forms are implemented with explicit
acceptance opt-ins. The separate account suite covers real Identity, two BFF hosts,
PostgreSQL 17, HTTPS and production Next/Chromium; see BFF_BROWSER_ACCOUNTS.md.
Task 5's zero-dispatch guard suite deliberately keeps account handlers disabled.
Private B1 account/browser acceptance is verified at the revisions recorded there. B2 registration/OTP
and B3 recovery/reset/password-change UI remain open. Their Retry-After contracts
must be reviewed separately; do not truncate longer cooldowns to B1's 60 seconds.

## Verified infrastructure CI

Code revision `d3bae2be0ac4289af724cadbd3b561664f1e3ee9` passed
[Browser B1 infrastructure run 38142922769](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38142922769).
Both job logs were inspected: production Next HTTP checks and actual two-host
PostgreSQL 17 routing/shared encrypted key-ring checks passed. Integration revision
`135bdafcef525d7f872d7da8c12067660e72ada9` has the same complete code tree and passed
run 38142923724. This is not a completed login/CSRF/browser acceptance result.
