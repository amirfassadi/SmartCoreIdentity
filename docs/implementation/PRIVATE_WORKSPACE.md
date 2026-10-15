# Persistent private workspace without SMS

Owner direction, 2026-10-15: postpone Kavenegar integration and continue the
remaining Identity work. The earlier ten-message ceiling still applies whenever
the provider trial resumes. This runner configures no provider and makes no SMS,
email, voice or fallback delivery calls. Codes remain in the existing encrypted
development outboxes. Contact verification is still exercised; it is not bypassed.

## Run locally

Prerequisites: Linux, Python 3.12+, OpenSSL, Git, .NET 10 SDK, Node 24/npm and a
local Docker daemon with Compose v2 (`up --wait`). All commands run from the repo
root. Dependencies/images need download access. No OS packages are installed.

```sh
python tools/private_stack.py init
python tools/private_stack.py build
python tools/private_stack.py run
```

Open `https://localhost:7443/fa` (or `/en`). Trust the generated
`dev-secrets/private-stack/tls.crt` in your local test browser/OS certificate store.
The runner's own HTTPS readiness probe verifies that certificate; it never uses
an insecure TLS bypass. Browser trust is a local setup step, not an automated
system-wide certificate installation.

The complete private registration, sign-in, recovery enrollment/reset and password
change UI is built with the existing feature flags. Use synthetic test accounts,
for example an address ending in `@example.test`, and a personal test password of
at least 15 characters. No shared/default account or password is provisioned.

For the registration code, in another terminal:

```sh
python tools/private_stack.py inbox
```

Enter the test contact when prompted. The command prints the latest unexpired
development code only in that operator terminal. For unfinished-account setup or
password reset, use `inbox --kind setup` or `inbox --kind reset`. Do not paste these
codes into logs/support reports. Reset still requires a previously enrolled
recovery code as well as the contact code. Save the recovery code when the UI
shows it once; there is no administrative password bypass.

After registration becomes Ready, sign in explicitly with the account email or
mobile and its password. Routine login makes no OTP or provider request.

Ctrl+C stops only the application's owned child process groups. PostgreSQL remains
running. To stop that private database, without deleting its volume:

```sh
python tools/private_stack.py db-stop
```

Run `run` again to resume. Existing configuration is preserved by `init`; both
BFF processes use the same persistent encrypted Data Protection ring and session
keys. `build` is required after source edits; stale builds are rejected. A second
runner/build/db-stop cannot take the active runner's lock. Existing app listeners
are rejected before migrations; the tool never kills another listener.

## On a private VPS via SSH

The same prerequisites and commands can run in a private checkout on a Linux host.
The operator must first review that host; this change makes no VPS modifications.
All app listeners and the database stay on loopback. The operator socket is in a
fresh private temporary directory so long repository paths do not exceed Unix
socket path limits. A private local browser can reach the HTTPS origin using:

```sh
ssh -N -L 7443:127.0.0.1:7443 USER@HOST
```

Use `https://localhost:7443/fa`, preserving the configured origin. Transfer only
the public `tls.crt` to the browser machine for trust. Never transfer `tls.key`,
wrapping keys or configuration through chat. This is a foreground developer
workspace: no systemd install, public domain, reverse proxy reconfiguration or
production-mode startup is introduced.

## Persistent state and isolation

Ignored `dev-secrets/private-stack` holds 0600 configuration/key files in 0700
directories. Initialization refuses an incomplete existing directory rather than
replacing keys that may protect stored data. The separate Compose project gets a
random durable identity; its `private-data` volume does not use the root Compose
registration database. PostgreSQL binds only `127.0.0.1:55437`. The app ports are
5157, 5171, 5172, 3181 and HTTPS 7443. This is a separate test database, not an
adapter for a pre-existing VPS database.

Runtime environments discard inherited Identity/BFF/ASP.NET/connection-string
overrides before applying the fixed local configuration. Build and child output
are suppressed, and failures report a phase and exception type only. No raw
request, secret, SQL result, password, cookie or proof-bearing failure is logged.
The explicit `inbox` command is the only code-display operation. The existing
ingress blocks `/dev`, `/auth` and `/operator`; no diagnostic key enters Next.js.

Do not remove or regenerate this directory to upgrade/restart. It contains keys
required to decrypt stored material and maintain sessions. This private runner
does not implement a coordinated encrypted database/key-ring backup or disaster
recovery procedure. Existing disposable restore tests do not establish that claim.

## Verification and remaining work

`tests/private-stack` checks actual key generation/preservation, secret file
permissions and symlink rejection, partial initialization, environment isolation,
exclusive runner locks and failure-output redaction. The new
`private-workspace.yml` workflow builds the real source, starts its own PostgreSQL
17 volume, starts Identity/two BFFs/Next/verified TLS, registers and signs in through
the BFF, reads across both peers, restarts all app processes, checks the original
session still works and logs out. CI results must be reported against the exact
commit; adding this workflow is not proof it passed.

All earlier release gates remain independently tracked in
[RELEASE_GATES.md](RELEASE_GATES.md): canonical contract integration, delivered
alerts, retention/contact-release policy, accepted load/failover/backup evidence,
production configuration and legal notices. SMS delivery/spend/ambiguity controls
are deferred by the owner, not considered complete. This runner completes a
private repeatable operating path; it does not authorize public users or close
Stage C.
