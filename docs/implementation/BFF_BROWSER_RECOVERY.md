# B3 browser recovery contract candidate

The additive contract defines enrollment, password change, uniform reset initiation
and two-factor reset completion. This is a private Development/Testing candidate,
not implemented runtime acceptance or release approval yet. B1 and B2 contract
paths/components must stay unchanged. Runtime requires explicit recovery opt-in;
public/Production startup gates remain enforced.

Enrollment requires the authenticated opaque BFF session, current password and
explicit loss-risk acceptance. Issued returns the one-time 128-bit code in volatile
UI only. AlreadyIssued never returns it again. Acknowledgment only hides the local
code; it cannot prove the user saved it. An ambiguous enrollment must not be
silently replaced/replayed. Explicitly enrolling a replacement invalidates the
previous recovery code and requires new consent. No storage, SSR, telemetry,
clipboard automation, downloads, screenshots or traces retain the code.

Change requires current password and a 15–128-unit new password. The existing
Identity command first fences issuance and closes all sessions. Browser Accepted
means durable intent; it does not claim Credential replacement completed. BFF must
commit local logout before clearing session/antiforgery cookies and acknowledging.
A failure after upstream acceptance stays ambiguous; no automatic command replay
or success claim. Fresh independent sign-in resolves pending work, with admission
limits and manual actions instead of polling credential commands.

Reset initiation is anonymous and uniform for enrolled/unenrolled/unknown contacts.
A server-owned encrypted HttpOnly __Host proof cookie carries the private challenge,
binding and absolute expiry. Follow-up tab flow ID must match before upstream work.
Both OTP (new challenges eight digits; bounded legacy six-digit acceptance) and a
pre-enrolled 32-hex recovery code are required. Normalize only OTP/recovery spelling,
never passwords. Completion consumes/reserves that recovery verifier according to
the existing authoritative workflow; it creates no login session. After new-password
login, enrollment is required again. Operators and private status routes never cross
ingress or become browser APIs.

The native reset service intentionally uses a uniform unauthorized failure during
its persisted 15/30/60-minute proof cooldowns. It does not supply the exact cooldown
or eligibility in response metadata. BFF must preserve that nondisclosure rather
than inventing or clamping a Retry-After. Existing generic admission 429/503 retains
the B1 60-second contract. Wrong current password/invalid reset factors map to
browser proof errors and preserve cookies; they cannot be mistaken for terminal
session access rejection. Only an independently recognized private /me access 401
may terminalize the matching BFF session. Backend cooldown controls remain covered
by the native recovery suite, not a fabricated browser countdown.

Acceptance still to implement: real TLS/two-host HTTP proof/cookie controls and the
full Chromium journey from B2 registration through login, one-time enrollment,
logout, both-factor reset, pending/new-password login, replacement enrollment and
password change with old-session rejection. Protected inbox and private CLI worker
fixtures stay outside browser origin. Secret-bearing diagnostics remain suppressed.
