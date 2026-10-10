# Password reset: recovery proof decision

Status: selected by Amir (@amirfassadi), project/architecture owner, on 2026-10-10. This selects the proof policy for email and telephone accounts; it is not production release acceptance. Enrollment and initiation are implemented as opt-in development adapters; completion remains pending the event contract disposition below. Reset remains required before real-user registration. Architecture A and the existing receipt-backed reconciliation worker remain selected.

The [transaction decision](AUTH_TRANSACTION_DECISION.md) requires reset proof bound to the observed Person epoch and explicitly says contact possession must not silently become sufficient to take over an existing mobile account. An OTP sent to a reassigned phone proves its current holder, not continuity with the account's original owner. Current Person storage has a single primary contact and no enrolled recovery factor. The existing ResetPassword intent discriminator is storage groundwork, not authorization.

## Owner-selected choice

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

No reset completion route is added while its truthful event contract is unresolved. The selected proof policy is no longer an open decision. Future Person eligibility transitions must advance epoch/revoke Sessions through the same serialized mutation boundary before any administrative deactivation feature is exposed.

## Conditions attached by the owner

- Reset verification needs a shared failure budget per Person, independent of new challenge creation, plus the challenge attempt cap. Wrong OTP and wrong recovery code must return the same failure shape and never identify the failing factor. Completion candidate limits are five failures per Person per fifteen-minute window and five presentations per challenge; initiation/replay cannot reset those counters. This proof verification is not implemented in the initiation-only adapter.
- Codes have 128 random bits. MAC verifier binds Person and monotonically increasing enrollment version. Code registration/replacement requires current-password proof and current online Session. Reserve the code at admitted reset so competing proofs cannot use it; consume after authoritative Applied reconciliation. RejectedNotApplied releases the reservation only after the terminal receipt, allowing a new proof against the advanced epoch. Unknown outcomes never release it on a timer. After Applied, replacement enrollment requires fresh successful login with the new password and one-time presentation; no unauthenticated result lookup returns a replacement code. BFF must enforce recovery enrollment before normal use.
- Queue notifications for primary contact on initial enrollment, replacement, and confirmed reset completion. Current implementation atomically queues enrollment/replacement tasks only; provider delivery and completion notification remain outstanding. Notifications contain no password, code, verifier or token.
- Show this explicit product statement before enrollment: «اگر رمز عبور و کد بازیابی را هر دو گم کنید، بازنشانی خودکار حساب ممکن نیست.» The API requires acceptLossRisk=true; the real BFF must display the statement and collect the user's acknowledgement.
- A new holder of an occupied telephone number can neither register another account nor reset the existing account without its recovery code. Contact-release/reassignment policy is a documented release condition; no automatic release or operator proof bypass is introduced for MVP.

## Current delivery and next dependency

See [enrollment and initiation](RECOVERY_ENROLLMENT.md). The pinned Platform PasswordChanged schema requires SessionReference even though reset proof is sessionless. The [event disposition candidate](RESET_EVENT_DECISION.md) and executable schema proposal make this narrow contract change reviewable. Enrollment/initiation do not change the public event schema and cannot replace any Credential.
