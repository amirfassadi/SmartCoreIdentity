# Standalone authenticated VPS gateway

Status: implemented and tested with controlled BFF peers on loopback sockets.
Not installed on the owner's VPS. Public TLS and real ASP.NET account acceptance
are not claimed. Do not enable the owner's bridge configuration yet.

## Topology

Vercel's server-only Next bridge calls the fixed HTTPS backend hostname. Nginx
terminates TLS on its existing 443 listener in a separate server_name vhost and
forwards only `/bff` routes to `http://127.0.0.1:5181`. Identity, BFF and PostgreSQL
remain bound to loopback. Existing Nginx sites/listeners must stay intact.

The gateway has no request/response logging. It requires its dedicated key before
dispatching, using SHA-256 digests and constant-time comparison; raw browser trust
headers and the gateway key never reach ASP.NET. Forwarded host/proto are rebuilt
from the configured browser origin. Forwarded-for is conservatively 127.0.0.1;
no remote client-IP claim is accepted from Vercel/browser headers. IP-based budgets
will therefore aggregate until a separately accepted trusted-client scheme exists.

Only one BFF dispatch is attempted. Two explicitly configured loopback BFF peers
can be round-robin selected; errors never cause retry/failover. Exact routes,
method checks, 16 KiB request/1 MiB response limits, a 45 second upstream deadline,
origin/media checks, cookie filtering and no-store apply. Redirects, unexpected
content types and unsafe cookies become code-only unavailable responses.

## Private environment file

The CLI reads `/home/amir/.config/smartcore-identity/.env` by default. A file path
may be provided as the first CLI argument. It never sources shell code, expands
variables or prints values. The file and immediate parent must be owned by the
running user, have no group/other permissions and not be symlinks. Maximum file
size is 16 KiB. Syntax is plain `UPPER_CASE_KEY=value`, blank lines and comments;
duplicates, quotes, shell expansions and commands are rejected.

It uses the four `SMARTCORE_*` connection variables listed in VERCEL_BFF_BRIDGE.md.
The existing private `run.py` still reads its original configuration.json. This
gateway does not migrate, replace or regenerate those database/session keys.
The template's false flag and placeholder hostname intentionally prevent startup.

## Source invocation (only after activation acceptance)

```sh
node deploy/gateway/server.mjs /home/amir/.config/smartcore-identity/.env
```

It binds **127.0.0.1:5181**, not a public interface. Node 24 is the project runtime.
The CLI defaults to one BFF peer on 127.0.0.1:5171. Keep the source module's
relative pure-policy dependency (`web/identity/src/lib/bff/bridge.mjs`) present;
this source checkout is not a self-contained replacement for the existing bundle.

## Remaining prerequisites

- Backend hostname under the owner's DNS account, pointing to the verified VPS IP.
- Dedicated Nginx vhost and trusted TLS certificate, tested without replacing
  current sites or stopping services. Do not publish an AAAA record unless IPv6
  routing and TLS for this hostname are verified too.
- Explicit ASP.NET restricted-staging policy for the real external browser origin.
  Current HostConfiguration still rejects non-loopback external origins and
  Production. Do not bypass that by forging Origin into localhost.
- Service/runner wiring, ownership, persistent encrypted key ring and safe restart.
- Real two-host account E2E through the TLS gateway, not just synthetic peers.
- Secret provisioning in Vercel matching the VPS gateway key; it must never enter
  NEXT_PUBLIC variables, chat, source control, command-line arguments or logs.
- Applicable release gates before real-user accounts or OTP delivery.

## Tests

```sh
node --test tests/ingress/gateway.test.mjs
npm run test:bridge --prefix web/identity
```

The gateway suite exercises real Node HTTP sockets, two controlled peers,
authorization failures with zero dispatch, reconstructed headers, cookie fields,
body limits, timeouts, ambiguity and the actual Next bridge transport through an
explicit test adapter. That adapter substitutes only the HTTPS network hop;
it is not evidence of public TLS or real ASP.NET account readiness.
