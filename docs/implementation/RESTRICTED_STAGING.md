# Restricted staging transport (accounts disabled)

This is an opt-in transport acceptance slice, not production approval. Existing
Development/Testing loopback hosting remains available. Production and ordinary
Staging environments remain rejected. No live VPS installation is performed by
these source changes.

## Implemented boundary

The BFF accepts a remote browser origin only in `RestrictedStaging`, with literal
`Bff:RestrictedStagingEnabled=true`, the same canonical HTTPS origin explicitly
confirmed in `Bff:StagingBrowserOrigin`, and a dedicated gateway proof. Wildcards,
IP browser origins, credentials, paths, query strings and local browser origins
are rejected. The Identity origin and BFF listening URLs still must be loopback.

Before forwarded headers are processed, the BFF requires a loopback peer and one
valid `X-SmartCore-Bff-Gateway-Proof` header, compared in constant time. Only the
loopback `/health/ready` probe is exempt. The proof is removed before account
handlers execute. The gateway reconstructs this header; caller-supplied proof,
Identity credentials and forwarded headers are never trusted.

The internal proof is the unpadded base64url HMAC-SHA256 of the ASCII context
`SmartCore.Bff.Gateway.v1`, keyed by the dedicated gateway credential. This is not
a browser session, CSRF token or Identity credential. Keep both the gateway key
and its derived proof out of client configuration, arguments, logs and source.

## Runner and bundle

The package now includes `staging.py`, the gateway and its shared cookie/route
policy, a systemd template, this guide and a TLS vhost template. The manifest
declares `restricted-staging-v1`. Merely copying staging.py into an older bundle
does not enable staging and is rejected.

`staging.py` uses the existing bundle's private configuration, database ownership
marker, session keys, encrypted key ring and separate wrapping certificate. It
never initializes/provisions a database or regenerates keys. The existing `run.py`
private runner still behaves as before and still does not read the dedicated env.

The staging runner parses the dedicated env as plain data without sourcing shell
code. Its file and immediate parent must belong to the application user, must
have no group/other permissions and cannot be symlinks. Duplicate settings, shell
expressions and overlarge files are rejected. Configured origins must be canonical.

`check` verifies bundle hashes/capability, private state, host configuration and
the existing PostgreSQL 18 marker without starting services or applying migrations:

```sh
python3 staging.py check --settings /home/amir/.config/smartcore-identity/.env
```

It can validate complete settings while the bridge flag remains false. The old
placeholder gateway hostname must first be replaced with the actual IP TLS origin.

`run` additionally requires literal bridge activation, acquires the existing
private-runner lock, refuses occupied ports before database work, verifies the
existing database, applies the application's normal migrations and launches:

- Identity on 127.0.0.1:5157 (existing private provider/inbox behavior).
- Two restricted BFF hosts on 127.0.0.1:5171 and :5172 with the shared encrypted ring.
- Authenticated gateway on 127.0.0.1:5181, selecting exactly one BFF per request.

No local Next server or development HTTPS ingress is started. Account,
registration and recovery flags are **forced false** in this runner. The four
public browser flags in the dedicated env must also be false. This runner does
not accept an arbitrary flag to turn on real-user account traffic.

Startup probes actual CSRF bootstrap through the authenticated gateway. Child
stdout/stderr are suppressed. A child failure or stop signal shuts down and reaps
only owned process groups, preserving the database and private state.

## Future VPS installation checkpoint

Do not run these steps until the current bundle inventory and a newly built
artifact's source/checksum have been reviewed. No commands below have been run on
the owner's VPS as part of this change.

1. Record the current bundle source/runtime and service/listener inventory with
   `deploy/preflight/identity-bridge-readonly.py`. Obtain the new bundle and verify
   its separately supplied archive digest before extraction.
2. Stage the new bundle separately. Keep the complete existing private workspace
   and rollback bundle. Preserve `dev-secrets/private-stack` permissions and keys;
   do not run init/provision-db or merge newly generated state into it.
3. Prepare the same private env with the stable Vercel browser origin,
   `SMARTCORE_BFF_GATEWAY_ORIGIN=https://87.107.54.244:8443`, its existing dedicated
   gateway key, and all account flags false. Do not put keys in NEXT_PUBLIC vars.
4. Run `staging.py check`. Before switching, stop only the existing owned private
   runner/service in an operator-controlled session. Keep a rollback path; the new
   runner rejects an active old runner rather than killing it.
5. The supplied `smartcore-identity-staging.service` is a template for user amir
   and the known bundle path. Review its paths and verify with systemd-analyze
   before installing. Its Conflicts directive stops the old owned private unit
   when the new staging unit is started. It is not installed/enabled automatically.
6. Enable transport only when ready to start the reviewed service. Confirm the
   loopback gateway's authenticated CSRF probe before changing the TLS vhost.
7. Back up only `/etc/nginx/conf.d/smartcore-identity-ip-tls.conf`; review the
   supplied `deploy/gateway/nginx-ip-tls.conf` as its replacement. Do not add it as
   a second duplicate 8443 vhost. Test with `nginx -t` before reload. Keep the
   separate port-80 IP ACME vhost and the existing certificate renewal hook.
8. Confirm verified HTTPS from outside, reject a missing/wrong gateway key with
   zero account dispatch, then provision the same key as a server-only Vercel
   secret. Vercel project authorization is still unresolved in this session.
9. Verify a real Vercel route -> IP TLS -> gateway -> restricted BFF bootstrap,
   cookies, CSRF rejection and restart/key-ring continuity. The healthz endpoint
   reports TLS transport only; it is not account or database readiness.

The Nginx template preserves the raw proxy URI, strips forged forwarding/internal
proof headers, disables proxy retries, caps bodies and disables access logging.
Nginx syntax on the target VPS, the actual private bundle upgrade and Vercel
connectivity are still pending. IP certificates are short-lived: retain the
already tested renewal timer and reload hook, and monitor expiry after installation.

## Validation and remaining account work

```sh
dotnet run --project tests/SmartCore.Identity.Transport.Tests -c Release
python -m unittest discover -s tests/private-stack -v
node --test tests/ingress/gateway.test.mjs
python scripts/bff-host-smoke.py --config-only
```

The independent .NET suite exercises environment/origin/proof policy and actual
middleware dispatch. Python tests cover private configuration, key preservation,
disabled account flags, occupied ports, legacy-bundle rejection and owned process
cleanup. The gateway socket suite includes forged proof replacement and no retry.
The prebuilt-bundle CI also checks disabled activation, real gateway bootstrap,
restart/port cleanup and byte-for-byte preservation of prior keys and key-ring
files on Ubuntu and Debian. Until that CI completes, the new bundle is a candidate.

With a disposable PostgreSQL database, `scripts/bff-host-smoke.py` additionally
runs native verified HTTPS fetch through the real Next transport function,
authenticated gateway and two actual restricted BFF processes. It checks shared
CSRF/cookie protection and zero Identity calls while accounts are disabled. Its
TLS terminator is an explicit local fixture; this is not a claim of target Nginx,
live VPS or Vercel acceptance. Existing private B1/B2/B3 account suites still run
separately. No production-release gate, real-user account acceptance or OTP
provider delivery is closed by these transport tests.
