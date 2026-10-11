# Selected reset event context

Owner disposition, 2026-10-10: retain PasswordChanged. Authenticated password change supplies SessionReference. Verified dual-factor reset supplies ExecutionContext.RecoveryProofReference instead, identifying the accepted ResetPassword operation/intent UUID, never the challenge or raw proof. Exactly one context is required. ActorIdentity remains the verified Person and the Credential/Person payload is unchanged.

Platform was updated first: schema 1.2.2, source commit `49ea40a50854c447b18afbdd7c2f3116dbee3136`, `SmartCore_Platform_Docs_v1/Identity/events.schema.json`, [draft PR 11](https://github.com/amirfassadi/SmartCorePlatform/pull/11). Identity imports those exact bytes into contracts/events.input.schema.json and records source/version in events.input.provenance.json. The draft is not a main merge or production approval.

PasswordChangedEvents guards both context branches at runtime and rejects both/neither. CI validates actual Credential outbox envelopes against the full pinned Platform schema and tests missing, ambiguous and secret-bearing contexts. RecoveryProofReference is written only by the Credential owner when the accepted operation is applied; no synthetic Session is created. Receipt-backed reset reconciliation consumes the reserved recovery code and queues a private completion notification.

Release dependency: merge Platform PR 11 and verify its canonical contract/source version before merging/releasing the Identity consumer. The branch pin is valid review provenance, not a claim that the contract is on Platform main.
