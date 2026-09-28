# Three-source registration review

Version: 0.1.0 — DRAFT — 2026-09-28. Source identities and hashes: source-manifest.json.

## Review scope and evidence limits

Reviewed the registration-related flows, contracts, persistence boundaries, security/configuration, validation and tests/examples in the uploaded package against ADR-0002 Decisions 8–9 and ADR-0004, plus the integrated platform registration API/service proposal. This is not a claim that every historical changelog, unrelated Query/extension rule or all 00–16 content has been fully reconciled. A preserved byte copy proves provenance, not technical approval. No runtime implementation exists in the examined Identity main snapshot.

The active scoped contract is workflow-contract.md and validation-and-tests.md. Original detailed documents remain intact in ../reference/uploaded-blueprint; none of their old readiness labels authorizes generation. This package is a scoped registration replacement, NOT a declaration that a complete consolidated active 00–16 Blueprint is finished.

## Section disposition

| Uploaded source | Finding | Registration disposition |
|---|---|---|
| 00 §6; 01 §8; 02 UC-001/002 | Two-phase flow, initial Session, implementation-specific recovery | Replace with precommit verification, ownership/Pending, guarded provisioning, Ready/ack and independent login |
| 01 Person/EmailAddress and relationships; 03 | Email-only, exactly one active Credential assumption | Verified email OR mobile; 0..1 active before Ready; active Credential may precede Ready; five Aggregates retained |
| 04 §4.1/§5.1/§9.2–9.3 | Early duplicate-email rejection, success despite Credential failure | Non-enumerating initiation; proof before conflict; atomic workflow/material/Outbox; Pending is not complete |
| 04 §4.2; 02 UC-003/004 | Active Credential sufficient for Session | Require Ready AND current active Credential and valid authentication |
| 05 integration queries; 09 §6.3 | Ownership visible before Credential | Preserve read visibility but do not equate it with login; self queries still require Session |
| 06 §4.1/4.2/5.4 | Aggregate-commit timestamp, required Email, optional initial Session reference | Ready OccurredAt plus OwnershipCommittedAt; Email optional; no SessionReference; actual Ready actor |
| 07 §5.1–5.2; 08 §3 | POST register immediate 201/full DTO; email login | Proposed multi-step 202/201/200 state protocol; explicit login; exact encoding via pinned upstream schema |
| 09 §§3–8 | Recovery unspecified; three repositories only; supporting-record rule too narrow | Preserve UoW ownership/no independent repository commits; explicitly include workflow/bind/Outbox; separate Credential guard and Ready transaction |
| 10 §§2/4 | Numeric policy and production rotation conflict with shorter proposal | Preserve source values; open security/operations choice, no silent weaker replacement |
| 11 §§3/4/5/9 | MFA excluded broadly; auth-only Session ownership; email lookup; optional throttling | Operator MFA/scoped recovery added; selected verified contact; mandatory bounded abuse/proof controls; business authorization still external |
| 12 §§2–4 | Useful checkpoint taxonomy but email-only/no cross-field registration | Preserve taxonomy; add exactly-one contact, proof/binding and storage guards as separate checkpoints |
| 13 §§5.1/6.3/7.3 | Tests expect PersonRegistered even when Credential fails | Replace those expectations with delayed Ready event and mandatory recovery/ack race tests |
| 14 MVP | General identity scope | Registration-first delivery milestone; broader Commands retained, not silently deleted |
| 15 extensions | Verification/recovery historically future | Registration-specific proof/setup/admin are current design; general lost-contact/password reset remains future |
| 16 §2/§12.1 | Direct 201 with optional Session, camelCase event example, early conflict | Replace registration examples with state-aware flow; event encoding follows pinned schema rather than obsolete examples |
| capability_machine.yaml 1.0.7 | Historical VALIDATION_READY but generation BLOCKED, old routes/fields | Preserve as non-active source; do not rename/adopt as current machine spec |

## Valuable detail preserved, not discarded

UoW versus repository commit ownership; Credential instance replacement versus history capability; optimistic-version requirements; read-only cross-repository query composition; encryption/key-rotation requirements; Argon2id algorithm/cost configuration; validation checkpoint taxonomy; detailed concurrency/rollback tests; future Aggregate promotion rationale. These remain inspectable byte-for-byte. Registration replacements do not certify all non-registration clauses as compatible.

## Open issues and acceptance implications

- **GOV:** ADR-0002/applicable ADR-0003 remain Proposed; accepted ADR-0004 is scoped architecture approval only.
- **T16:** ordering remains open; no commercial subscriber or workload is invented because Kimia is a future product.
- **SESSION:** local production refresh rotation/sliding expiry versus platform nonrotation/absolute lifetime. Requires explicit security/architecture disposition before runnable login/refresh rollout.
- **POLICY:** password thresholds, KDF costs, Session lifetimes and retry defaults differ; select and benchmark under approved policy, not by document length or newest timestamp alone.
- **MATERIAL:** local “only PasswordHash persisted” versus platform protected staged material. Concrete secret representation/access/expiry implementation must satisfy no durable raw password, atomic reference binding and bounded disposal; no plaintext staging is authorized.
- **CONSOLIDATION:** active full 00–16/machine integration and applicable 065 validation remain open; this registration package and archived baseline do not pretend to finish them.
- **MISSING SOURCE:** capability.validation_notes.md is named by uploaded YAML but absent from the ZIP. Request the original if available; do not invent its validation evidence. This does not block recording these corrections, but old validation-complete claims cannot be relied upon.
- **DEPLOYMENT:** delivery provider, database/runtime, KMS/service trust, finite recovery settings, real operator grants and runtime tests remain undecided/unverified here.

## Change control

Preserve the source hash manifest; revise current docs rather than editing imported source evidence. Review candidate/merged SHAs explicitly. No source history is rewritten as if it contained the newer protocol. This patch modifies documentation only and creates no implementation or acceptance record.
