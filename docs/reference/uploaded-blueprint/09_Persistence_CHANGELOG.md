<!--
Document ID: ID-09-CHANGELOG
Title: SmartCore Identity Platform Blueprint - Persistence — Historical Change Log (v1.0.0–v1.0.4)
Purpose: Archived version history for 09_Persistence.md, split out
from the primary document to keep its active Change Log concise. This
split is purely archival — carries no normative content of its own,
changes no scope, and is not itself a Dependency of any other
Blueprint document. For the active Change Log, see the current
revision of 09_Persistence.md §11.
Dependencies: None (informative/archival only)
-->

# Historical Change Log — 09_Persistence.md (Versions 1.0.0–1.0.4)

This file is informational only and contains archived Change Log
entries for 09_Persistence.md versions 1.0.0 through 1.0.4, moved out
of the primary document to improve readability once the Change Log
there had grown to roughly one-third of the document's total length.
The active Change Log — covering this file's later versions and all
versions after them — remains in the current revision of
09_Persistence.md §11.

No content below was altered during the move; each entry is
reproduced verbatim from its original location in 09_Persistence.md.

---

## Version 1.0.4 (2026-07-15)

Fourth clarification pass, following a fourth architecture review of
v1.0.3. No new Aggregates, Commands, Queries, Events, or business rules
introduced.

- **Reverse-dependency smell removed from `GetActiveSessionsByPersonId`
  justification** (§3.1): the citation previously led with the
  `GET /sessions` API endpoint (059 §11); it now leads with the
  expected 05_Queries.md Query, with the endpoint retained only as
  background context for *why* such a Query is expected to exist. This
  aligns the wording with §2.1's Repository Pattern Boundary, under
  which only a Command or Query justifies a Repository method — never
  an API endpoint directly. The method remains provisional pending
  confirmation in 05_Queries.md, unchanged from v1.0.3.
- **`GetById` self-justification promoted to an explicit Rule** (§3.1):
  restated as a named rule ("Repository identity retrieval is
  self-justifying") rather than a parenthetical aside, so it reads as
  a stated exception to the citation rule rather than an unstated one.
- **Forward note added to §7.1**: flags that if RefreshToken later
  needs token-family revocation, reuse detection, or similar
  multi-device security behavior, that would introduce new domain
  rules requiring re-evaluation of RefreshToken as a candidate
  Aggregate — explicitly marked as a future 03_Aggregates.md / ADR
  decision, not something this document resolves now.

**Reviewer points considered and intentionally not changed:**
whether `Credential` should be an independent Aggregate vs. a `Person`
child entity is a decision already made and rationalized in
03_Aggregates.md §8 ("Credential Aggregate Rationale") — restating or
re-justifying it here would duplicate, not clarify, an upstream
document. The semantic difference between Session Status values
`Authenticated` and `Active` is a Domain Model definition question
belonging to 01_Domain_Model.md, not this document, which mirrors that
Status field (§4.4) without adding business meaning to it, consistent
with §1.1/§4's "adds no business field" scope discipline.

## Version 1.0.3 (2026-07-15)

Third clarification pass, following a third architecture review of
v1.0.2. No new Aggregates, Commands, Queries, Events, or business
rules introduced.

- **Credential Aggregate identity clarified** (§4.5, §5.2): explicitly
  answers whether `CredentialId` changes across `ChangePassword`. It
  does, for the new instance: the new Credential receives a
  newly-generated `CredentialId`; the previous Credential retains its
  original `CredentialId` and only its `Status` changes to `Replaced`.
  This confirms — rather than changes — the "two Credential Aggregate
  instances" conclusion already stated in v1.0.1/v1.0.2.
- **PasswordHistory wording clarified** (§7.5): "no PasswordHistory
  structure" is now stated to mean no separate *queryable history
  capability*, not physical deletion of superseded Credential rows.
  Replaced Credential instances remain persisted as ordinary
  `Credential` records with `Status = Replaced`; what is excluded is a
  second structure exposing "all past passwords for this Person" as a
  query capability.
- **`GetActiveSessionsByPersonId` justification softened** (§3.1): no
  longer cites the `GET /sessions` API endpoint (059 §11) alone as
  sufficient justification for a Repository read method, since this
  document's own stated rule ties read methods to Commands/Queries, not
  API endpoints, and 07_Contracts.md/08_API.md are explicitly not
  Dependencies of this document (see header). The method is now marked
  provisional, pending confirmation that 05_Queries.md defines a
  corresponding Query.
- **Missing justification added** (§3.1): `MembershipRepository.GetByPersonId`
  now has an explicit Command/Query citation (composed by
  `GetOrganizationsForPerson`/`GetMembershipsForPerson` per §3.2), and
  a blanket statement now covers why `GetById` methods are
  self-justifying and were intentionally omitted from the per-method
  list, closing an inconsistency where some catalog methods had
  citations and others did not.

## Version 1.0.2 (2026-07-15)

Correction and clarification pass, following a second architecture
review of v1.0.1. No new Aggregates, Commands, Queries, Events, or
business rules introduced.

- **Correction**: v1.0.1's §3.1.1 incorrectly claimed
  `SessionRepository.GetActiveByPersonId` returns zero-or-one. This
  contradicted 059 §8's multi-Session-per-Person design (a Person may
  have simultaneous Web/Mobile/Telegram/Desktop Sessions). The method
  is renamed to `GetActiveSessionsByPersonId`, corrected to zero-or-
  more cardinality, given an explicit "Active" filter definition
  (Status = `Active` exactly), and given the Command/Query
  justification (059 §11 `GET /sessions`) that was missing from §3.1
  in both v1.0.0 and v1.0.1. `CredentialRepository.GetActiveByPersonId`
  is unaffected — its zero-or-one cardinality was and remains correct.
- Added a clarifying note to §5.2 stating that `RefreshSession`
  updates the existing Session instance in place (token rotation) and
  does not create a second Session, consistent with the Session
  Lifecycle in 01_Domain_Model §5 / 03_Aggregates §5, with an explicit
  fallback instruction should 04_Commands.md specify otherwise.
- Added §6.3, cross-referencing 059 §6's Consistency Guarantee to state
  explicitly that the Query layer SHALL tolerate a temporarily absent
  Credential/Session for an otherwise-valid Person during the
  Post-Commit window (§6.1), and SHALL NOT treat that absence as a
  fault.

## Version 1.0.1 (2026-07-15)

Editorial clarification pass. **Note**: this version's §3.1.1 Session
cardinality claim was incorrect and was corrected in v1.0.2 above.

- Added §3.1.1: query cardinality contract for
  `CredentialRepository.GetActiveByPersonId` and (incorrectly, at the
  time) `SessionRepository.GetActiveByPersonId`.
- Reworded §5.2's ChangePassword note to distinguish Aggregate *type*
  from Aggregate *instance* explicitly ("two Credential Aggregate
  instances," not "two rows of the same Aggregate type"), removing a
  possible DDD ambiguity. The conclusion — no ADR-0002 Decision 7 / 057
  §8 exception required — is unchanged.

## Version 1.0.0 (2026-07-14)

Initial Persistence Blueprint.

- Defined the Repository Catalog: one Repository per Aggregate Root,
  with MVP-justified read/write methods traced to specific Command/
  Query needs (§3)
- Defined the logical persistent entity schema per Aggregate, adding
  no field beyond 01_Domain_Model.md (§4)
- Defined the Core Ownership Transaction as a concrete Unit of Work
  spanning exactly three Repositories — the sole approved exception
  per ADR-0002 Decision 7 / 057 §8 — and confirmed no other Command
  requires cross-Repository coordination, including a specific
  clarification that ChangePassword's two-row write is single-
  Aggregate-type and requires no exception (§5)
- Defined Post-Commit transaction isolation: Credential and Session
  creation as separate transactions opened only after the Core
  Ownership Transaction commits, whose failure cannot invalidate it
  (§6)
- Classified all five 059 §12 Supporting Persistence Structures for
  MVP: RefreshTokens (internal SessionRepository detail), LoginHistory
  (optional, non-transactional), IdentityEvents and AuditLogs
  (explicitly deferred, consistent with 06_Domain_Events.md's own
  scope boundary), and PasswordHistory (explicitly excluded, per
  01_Domain_Model.md's existing prohibition) (§7)
- Defined required storage-level uniqueness constraints (Person.Email,
  Membership person/organization pair, Session token uniqueness,
  single Active Credential per Person) and a general concurrency-
  control requirement, without mandating a specific mechanism (§8)
- Verified alignment against 03_Aggregates, 01_Domain_Model,
  04_Commands, 057, 059, and ADR-0002 (§9)

---

**END OF ARCHIVED CHANGE LOG**