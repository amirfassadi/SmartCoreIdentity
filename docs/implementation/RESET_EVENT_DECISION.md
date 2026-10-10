# Reset completion event: narrow owner disposition

Status: prepared candidate, not adopted. The OTP plus enrolled recovery-code policy is already selected; this is a separate public event contract dependency.

The pinned Platform input `contracts/events.input.schema.json` requires SessionReference in PasswordChanged. That is valid for authenticated password change but not for recovery-proof reset: the owner can be verified without an existing login Session. Creating a synthetic login Session, borrowing a revoked Session, putting a challenge ID in SessionReference or suppressing the Credential fact would misstate the operation.

## Recommended disposition

Retain PasswordChanged and its existing Credential/Person payload. For authenticated change, keep the existing SessionReference and no recovery proof reference. For verified reset, omit SessionReference and require ExecutionContext.RecoveryProofReference, the non-secret admitted ResetPassword intent UUID bound to the verified dual proof. ActorIdentity remains the verified Person; CorrelationId remains required. No OTP, recovery code, token, verifier or hash enters this fact. Exactly one authentication context is permitted, never both or neither.

The complete standalone candidate is [password-changed-reset.schema.json](../../contracts/proposals/password-changed-reset.schema.json). The original pinned Platform input is unchanged. The candidate validates existing password-change fixtures and a sessionless recovery fixture, and rejects missing/ambiguous authentication context. It is not the runtime event validator and does not declare a new Platform approval.

After owner selection, propagate the same change to the Platform contract/governance candidate and its Identity input with explicit version/source provenance. Then implement dual-proof acceptance, code reservation, independent Credential receipt/outbox, code consumption and completion notification on confirmed Applied reconciliation. New native tests must validate both event branches and actual reset/recovery races before release acceptance.

An alternative is a separate PasswordResetCompleted public event. That expands the Identity event vocabulary and consumer contract rather than preserving PasswordChanged; it needs an explicit owner choice. The recommended candidate above is the smaller semantic extension.
