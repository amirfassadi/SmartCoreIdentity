# B1 browser infrastructure

Status: tasks 2 and 3 started after the 0.1.1 browser contract review. No production
launch, account handlers, generated client, real HTTPS ingress or completed B1 claim.

## Delivered

- [Next shell](../../web/identity/package.json): pinned Next.js/React/TypeScript,
  next-intl, Tailwind and npm lockfile; `/fa` RTL and `/en` LTR; client account shell
  with no Identity calls, credentials or token access. No remote fonts/CDN dependency.
- Dynamic HTML, fresh nonce forwarded to Next rendering, CSP denying inline style
  attributes, no-store, frame/referrer/permissions policy. Production HTTP smoke
  checks all emitted script nonces; this is not browser hydration evidence.
- Locale cookie is explicitly `smartcore-locale`, Secure/Lax/Path=/, bounded age,
  no Domain. Next's defence-in-depth `/bff` rejection produces JSON instead of HTML;
  the ingress must still route **all** `/bff` traffic to .NET. The real proxy cookie
  allowlist is task 4 and is not implemented here.
- [.NET host](../../src/SmartCore.Identity.Bff.Host/Program.cs) registers existing
  BffStore, refresh/logout adapter, BffRefresh, BffCircuit and BffMaintenance;
  runs existing PostgreSQL migrations before readiness (PostgreSQL 17 minimum).
- Both hosts use the same application name and shared Data Protection directory,
  with an explicit separate RSA wrapping certificate, ephemeral private-key load
  and no unprotected-key fallback. Production, public bind, missing certificate
  and equal client/receipt keys are rejected at startup. Certificate/key-ring
  filesystem permissions, secret mounts and rotation remain operator obligations.
- Antiforgery cookie/header options are registered. **No authentication principal
  handler or CSRF validation has been delivered yet.** The five known browser
  methods deliberately return `503 BFF_UNAVAILABLE`, without session cookies or
  upstream work. Other methods return route-specific `405`/Allow; undeveloped
  B2/B3 and unknown paths return JSON `404 NOT_FOUND`; responses are no-store.
  These scaffold responses are not the final task-5 guard pipeline.

## Configuration and local checks

Development/Testing only. Supply configuration through an untracked environment or
secret mount; nothing here generates production secrets. Both hosts need:

| Setting | Meaning |
| --- | --- |
| `ASPNETCORE_URLS` | Separate loopback listen URLs, for example ports 5171 and 5172 |
| `ConnectionStrings__Bff` | Shared disposable PostgreSQL 17 database for acceptance |
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

## Next slice

Task 3 still needs seeded Identity account integration through the real login
handler (task 6). Task 4 adds HTTPS ingress with locale-only cookies to Next and
trusted-header overwrites. Task 5 establishes the opaque-cookie principal and
session-bound CSRF guards. The [raw request fixtures](../../tests/browser/b1-guard-precedence.json)
cover each adjacent competing error pair plus cross-site/malformed JSON. Their
current Python evaluator injects a CSRF result; they must be executed against the
live host with zero upstream calls asserted once task 5 lands. The 15 older
controls are a precedence model, not 15 live requests.

B2 must review OTP/register Retry-After again; B3 must review reset cooldowns.
Do not silently clamp longer upstream cooldowns into the B1 60-second schema.
