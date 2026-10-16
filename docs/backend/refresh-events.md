# Internal refresh security events

Status: candidate propagation of owner direction reported on 2026-10-09; not an accepted replacement for the active SESSION contract. [Source status](status-and-next-steps.md). Candidate schema: [refresh-events.schema.json](../../contracts/internal/refresh-events.schema.json).

## Event scope and payload

RefreshTokenRotated and RefreshTokenReuseDetected are real internal Session security facts. They are separate from the ten public Identity events and the T16 Person stream. No Person position, token, verifier, password, digest/hash, IP, contact, device snapshot or arbitrary metadata is permitted in their payload.

Event envelope carries a stable eventId, schemaVersion and type. Payload carries only sessionId, familyId, generation, occurredAt and reason. generation is the newly committed generation for rotation; for reuse it identifies the previously issued generation presented again. This is a candidate wire encoding; formal propagation/version adoption remains pending.

| Event | Reason | Committed meaning |
|---|---|---|
| RefreshTokenRotated | RefreshAccepted | Current authorized generation was consumed and replaced with generation + 1 |
| RefreshTokenReuseDetected | PreviouslyConsumedGeneration | A valid previously issued/consumed generation was presented; the entire family was invalidated in the same transaction |

Reuse does not need a separate SessionRevoked event to carry family invalidation in this direction. An unknown or forged token is a generic authentication failure, not evidence of previously consumed generation and not an authorization to revoke an arbitrary family. Family identity must be established by authenticated protected token evidence.

## Concurrency, delivery and response

Current-generation consumption and generation increment use storage CAS/locking. A concurrent second refresh that presents the just-consumed valid generation follows reuse handling, invalidating the family. There is no retry grace or cached-success bypass. Clients serialize refresh requests; a lost successful response cannot safely be retried as though it were idempotent.

Mutation and journal insertion commit or roll back together. eventId is stable across delivery retries; at-least-once publication does not create a second fact. Internal delivery and access controls must be specified before release, independently of T16. Public responses must not expose internal event payloads or contact existence.

Rotation must not extend absolute Session ExpiresAt. Existing Access may remain valid for up to the adopted 900-second TTL after revocation; do not promise immediate cutoff unless a separately adopted online validation mechanism provides it. Password change closes all Sessions. Foreground idle of 30 minutes is owner-selected direction pending active-contract reconciliation.

## Retention gate

Retention is still OPEN. Choose finite journal/audit retention and delivery/replay horizon, access grants and purge behavior before enabling cleanup or production publication. No arbitrary default, infinite-retention promise or deletion job is introduced here. Schema validity does not close SESSION/064/065 or prove runtime safety.
