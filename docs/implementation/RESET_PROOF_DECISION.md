# Password reset: recovery proof decision

Status: proposed owner decision, not an accepted policy or an implemented endpoint. Reset remains required before real-user registration. Architecture A and the existing receipt-backed reconciliation worker remain selected.

The [transaction decision](AUTH_TRANSACTION_DECISION.md) requires reset proof bound to the observed Person epoch and explicitly says contact possession must not silently become sufficient to take over an existing mobile account. An OTP sent to a reassigned phone proves its current holder, not continuity with the account's original owner. Current Person storage has a single primary contact and no enrolled recovery factor. The existing ResetPassword intent discriminator is storage groundwork, not authorization.

## Proposed choice

Require both primary-contact OTP and an independently enrolled, high-entropy recovery code for password reset. Apply the same rule to email and phone accounts for a consistent MVP. Enrollment and replacement require a current online Session and current-password proof, obeying Person backoff and the issuance gate. Reveal a newly generated recovery code once; store only a purpose-bound verifier, never the code in audit/outbox. A stolen access token or ownership of a reassigned phone alone cannot enroll or reset.

The UX cost is concrete: users must save a recovery code. An account without an enrolled code, or with both password and code lost, has no automatic reset. MVP provides no operator bypass of recovery proof. Internal existing accounts remain disposable test data; new real-user onboarding must finish recovery enrollment before being opened. A real BFF must implement secure one-time presentation and acknowledgement.

An alternative is a previously verified secondary email for phone accounts, combined with primary OTP. This adds contact enrollment and lifecycle rules and does not protect accounts where both contacts are compromised. Introducing it as an automatic fallback would require a separate decision; it is not implemented or selected here.

## Implementation after disposition

1. Add recovery enrollment and immutable challenge bindings, versioning enrollment alongside the Person epoch. Enrollment races with password change/reset must invalidate stale proofs, never revive a superseded factor.
2. Reset initiation returns uniform accepted responses for eligible, unknown and unsupported accounts. Rate-limit challenge/delivery allocation. Initiation, delivery and invalid attempts cannot fence, advance epoch or close Sessions. Use a distinct OTP purpose, capped attempts and a fixed expiry; resend never extends proof life or resets attempts.
3. Verify both factors outside issuance/Credential locks, enforce new-password policy and bounded KDF, and bind proof to Person, authenticated BFF, operation, observed epoch and credential identity. Persist atomic proof consumption with admission; two competing uses cannot admit two changes.
4. Under the Person gate, revalidate eligibility, proof freshness and Credential evidence; persist ResetPassword intent and sealed material, advance epoch and close all Sessions/families with PasswordReset. Credential replacement, immutable receipt, PasswordChanged fact, worker and private operator recovery keep separate commits and existing lock rules.
5. Repeated accepted operations use a bounded recovery-proof/result capability and sealed Argon2 new-password binding; no raw-password MAC or arbitrary Person lookup. No tokens on reset completion. Terminal unknown outcomes remain fenced. Consuming a recovery code requires a documented way to enroll a replacement after successful fresh login; a replacement is not exposed by unauthenticated status lookup.

Acceptance cases include reassigned-phone OTP without recovery code, unknown-account response shape, attempt/expiry/resend limits, stale epoch and enrollment races, duplicate/concurrent proof use, password-change/reset competition, lost admission/receipt responses, worker recovery, operator effective outcomes, and absence of a reset-triggered fence before valid dual proof. Load tests and real delivery/BFF release acceptance remain separate.

No reset route is added while this proof decision is unresolved. Future Person eligibility transitions must advance epoch/revoke Sessions through the same serialized mutation boundary before any administrative deactivation feature is exposed.
