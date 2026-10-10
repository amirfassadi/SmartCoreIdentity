# Pending-registration setup and completion

Revision: 0.2.0, 2026-10-10. Implemented development surface; no public release approval.

An expired initial proof or failed provisioning must not require a second Person or permit the new verification password to replace a committed Credential. This flow completes the existing registration using a separately delivered setup proof.

| Step | Authority and result |
|---|---|
| Fresh registration verification | Exactly one canonical contact, fresh binding and proof; proven conflict disposes the new attempt's password |
| Request setup | Original proven conflict mapping, original code/binding and original expiry/attempt budget; one distinct challenge/delivery |
| Accept completion | Distinct setup code/binding, unexpired challenge, shared attempt budget, immutable key/request binding; encrypted KDF candidate and worker redrive commit together |
| Commit Credential | Storage serialization, one immutable winner; automatic and setup candidates cannot overwrite each other |
| Ready and acknowledgment | Existing separate guarded transactions; stable Ready fact/event/ack; completion reports the actual candidate outcome |
| Replay or recovery | Identical authorized replay while proof remains valid; accepted work survives network/process interruption; worker uses separately bounded material after proof expiry |

Challenges and delivery records are application support records, not new Aggregates or public events. Contact destination derives from the stored Person associated with the registration. The original verification password is never used as setup input. All checks are server-side; an identifier, idempotency key or client timer alone grants no authority.

ExistingWinner explicitly means the supplied setup password was not installed. Ready never grants tokens. Ready accounts cannot be reset by starting this flow. Password recovery/reset still needs its own security/Session contract before real-user onboarding.

`002_registration_setup.sql` preserves 001 and adds the conflict target, challenge and delivery tables. Candidate acceptance is durable before provisioning. A live accepted candidate remains independent of original verification cleanup. Wrong proofs, wrong bindings and replay conflicts consume the shared five-attempt budget; expiry is absolute and no replay extends it. Setup code is six digits, generated independently and different from the original code; its verifier uses a separate purpose.

Encrypted candidate output is cleared on winner or material expiry; proof/request verifiers and undelivered codes are cleared on their own expiry. Physical backup/WAL erasure, managed keys and production provider/retention controls remain release work.

The tests in `tests/SmartCore.Identity.Tests/Program.cs` inject a real Credential INSERT failure and verify recovery of the committed candidate after proof expires, in addition to concurrent candidate/winner tests. `scripts/setup-smoke.py` verifies HTTP shapes, safe fake inbox authorization, malformed inputs, truthful outcome and bound replay. Native PostgreSQL CI remains the authoritative concurrency check; PGlite is preliminary.
