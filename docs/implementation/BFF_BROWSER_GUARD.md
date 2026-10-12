# B1 task 5 — opaque-session and CSRF guard

Status: implemented for Development/Testing; full account handlers and browser
login remain disconnected. This is not registration/login/logout/reset browser E2E.

The explicit [BffOpaque handler](../../src/SmartCore.Identity.Bff.Host/OpaqueSessionHandler.cs)
resolves the MAC of the opaque cookie against BffStore and places only the BFF row
UUID in ClaimTypes.NameIdentifier. It neither decrypts tokens nor writes, locks,
refreshes or calls Identity. Closed/expired/unknown cookies stay anonymous.
LogoutRequested has the original identity only on the exact POST /bff/logout route;
ordinary routes deny it. Final account handlers must still recheck core state when
lookup races a logout or credential mutation.

Minimal apps may auto-insert authentication middleware. The documented
[SuppressAutoDefaultScheme switch](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/7/default-authentication-scheme?view=aspnetcore-10.0)
and no default scheme prevent premature cookie lookup. Startup asserts that no
default authenticate scheme exists. The guard explicitly invokes BffOpaque only
at the intended point. Lookup failure returns unavailable and preserves cookies;
it must not silently become anonymous or a terminal 401.

## Request order and response boundary

Routing first: unknown B2/B3 paths remain JSON 404; wrong methods get exact 405/Allow.
For a declared POST, [BrowserGuard](../../src/SmartCore.Identity.Bff.Host/BrowserGuard.cs)
performs raw size (16384), media type (JSON/UTF-8), fixed HTTPS transport plus exact
Origin/optional Fetch-Site, principal resolution and antiforgery validation, then
JSON/DTO validation, then session/business disposition. JSON is never parsed before
CSRF. Duplicate JSON members, unknown fields, null/bad types and passwords over
128 UTF-16 units are rejected. Phone digits/separators follow the Iran B1 policy;
passwords are never normalized. The future handler must use the same validated
contact normalization when dispatching to Identity.

GET /bff/csrf now returns a real antiforgery request token for the resolved current
principal. Bootstrap never authenticates anonymous users or refreshes. Antiforgery
cookie options remain Secure/HttpOnly/Lax/Path=/, no Domain; both hosts share the
certificate-encrypted ring and app name. Header tokens are session-bound, and a new
BFF session UUID rejects the old session/anonymous header even with the same underlying
Identity-session fixture metadata. No BFF UUID or personId is exposed in JSON.

Anonymous requests with valid anonymous CSRF to authenticated paths return terminal
401 and clear both cookies. An already-active principal attempting login gets 409
without replacement. Successful guard passes still end in BFF_UNAVAILABLE for the
unconnected task-6 account handlers; no session login cookie is minted. Clients must
obtain fresh CSRF after future actual login/logout transitions.

## Acceptance evidence boundary

The [live guard runner](../../scripts/bff-guard-checks.py) starts with the committed
five competing-error fixtures (including all four adjacent pairs) through the real
HTTPS proxy. It also checks host/Kestrel size precedence directly over a separately
verified TLS endpoint. Other checks cover cross-host antiforgery, anonymous/session
binding, same underlying fixture identity/new BFF UUID, closed/expired cookies,
LogoutRequested scope, stable UUID across stored version advance, strict DTOs and
UTF-16 bounds. Cookie clear/keep behavior and every response are checked against the
exact browser OpenAPI contract. An Identity trap asserts zero dispatches.

A private mode in the disposable test executable seeds synthetic BFF session rows;
there is no host diagnostic/seed/operator endpoint. The runner captures cookies over
stdin/stdout IPC and never logs them. A tightly scoped column-rename fault probe in
the disposable DB verifies that media/Origin errors precede lookup and a lookup
failure does not log users out. It restores the column in finally. These are synthetic
session state tests, not actual Identity login/logout/password/reset acceptance.
Fresh PostgreSQL 17 CI must pass before live-guard evidence is claimed.
