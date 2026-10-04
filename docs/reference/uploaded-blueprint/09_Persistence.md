<!--
Document ID: ID-09
Title: SmartCore Identity Platform Blueprint - Persistence (Repository & Storage Model)
Version: 1.0.10
Status: READY_FOR_GENERATION

Purpose:
Define the Repository pattern, logical persistent entity schema, and
cross-Aggregate transaction/consistency model for the Identity
Platform. This is the document 03_Aggregates.md §10 explicitly defers
to: "Persistence layer design (Repository patterns, storage
strategies, consistency mechanisms) will be defined in blueprint
document 09_Persistence.md" and "Each Aggregate Root will have exactly
one owning Repository, to be specified in the Persistence blueprint."

Dependencies (Required):
- 01_Domain_Model.md
- 03_Aggregates.md
- 04_Commands.md
- 14_MVP.md
- 057_SmartCore_Tenancy_and_Ownership_Model.md
- 059_SmartCore_Identity_Platform.md
- ADR-0002_Identity_Foundation_Clarifications.md

Referenced For Consistency (informative, not a hard Dependency):
- 05_Queries.md (§2.3 — cross-Aggregate read projections)
- 06_Domain_Events.md (§5.1 — Publish-After-Commit presupposes this
  document's commit boundary, but this document does not depend on 06
  for any of its own definitions)

Explicitly NOT a Dependency:
- 07_Contracts.md
- 08_API.md
Persistence is independent of REST Transport and Wire Contract
concerns, the same way 06_Domain_Events.md is independent of them.

Change Log:
  - Version 1.0.10 (2026-07-19): Correction pass — §8.3's
    "Person.Email unique across all Persons" row cited "01_Domain_Model
    §6 Rule-001 equivalent" as a source. Verified against
    01_Domain_Model.md v1.1.0 directly: §6 Domain Invariants and §9
    Domain Rules do not state Email uniqueness anywhere; Rule-001 is
    about Organization membership ("Every Person SHALL belong to at
    least one Organization"), unrelated to Email. This is the same
    inaccurate citation 12_Validation.md's v1.0.5 correction pass
    already identified and removed from its own §5.1 table
    (2026-07-18), which flagged this document's §8.3 as carrying the
    identical unverifiable citation and out of that document's own
    scope to fix. Removed the "01_Domain_Model §6 Rule-001 equivalent"
    citation from §8.3's Email-uniqueness row; the row now cites only
    04_Commands §4.1, §4.3, which do state the constraint. No new
    Aggregates, Commands, Queries, Events, or business rules
    introduced; no MVP scope, constraint, or enforcement mechanism
    changed — this is a citation-accuracy fix only.
  - Version 1.0.9 (2026-07-15): Ninth pass — first cross-check against
    the actual content of 02_Use_Cases.md, 04_Commands.md, and
    05_Queries.md, all previously Required/Referenced Dependencies of
    this document that were not directly available for verification
    until now. No new Aggregates, Commands, Queries, Events, or
    business rules introduced; no MVP scope changed. (1) §10.1's two
    Pending Verification items are resolved: `GetSessionsForPerson`
    (05_Queries §4.4) confirmed as the Query justifying
    `SessionRepository.GetActiveSessionsByPersonId` (§3.1) — corrected
    from the `GetActiveSessionsForPerson` name this document had
    speculatively guessed in v1.0.3–v1.0.8; `GetOrganizationsForPerson`
    (05_Queries §4.2) and `GetMembershipsForPerson` (05_Queries §4.3)
    confirmed as justifying `MembershipRepository.GetByPersonId`.
    02_Use_Cases.md UC-008's "One membership per Person per
    Organization in MVP" citation was also directly verified as
    accurate. (2) **Correction**: §5.2's RefreshSession note previously
    asserted `RefreshSession` rotates both `AccessTokenId` and
    `RefreshTokenId`. Direct cross-check against 04_Commands.md §4.5
    Postconditions shows only `AccessTokenId` reissuance is guaranteed
    ("New AccessTokenId is issued"); `RefreshTokenId` handling is
    explicitly left implementation-specific there ("preserved or
    refreshed according to implementation"). This document's earlier
    wording overstated what 04_Commands.md actually guarantees; §5.2
    is corrected to reflect only what 04_Commands.md commits to,
    without independently deciding the RefreshTokenId question.
  - Version 1.0.8 (2026-07-15): Eighth clarification pass, following
    an eighth architecture review. No new Aggregates, Commands,
    Queries, Events, or business rules introduced; no MVP scope
    changed. (1) §3: added an explicit statement of what `Update`
    means in the Repository Catalog table — persisting an Aggregate's
    already-changed state after the Command Handler has applied
    business intent inside the Aggregate, not an undifferentiated
    write path — addressing review feedback that a single generic
    `Update` method reads as CRUD-flavored without this being stated.
    (2) New §8.4.1: states the minimum requirement for optimistic
    concurrency that §8.4 assumed but never made explicit — some
    Version marker (row-version number, compare-and-swap timestamp, or
    database-native row-version/ETag mechanism) SHALL exist per
    persistent entity, without naming a specific field, column, or
    mechanism, consistent with §1.2's exclusion of *how* concurrency
    control works. Also notes that a future Event-Sourced model would
    redefine "Version" as event-stream position, deferred to
    03_Aggregates.md. (3) Split the historical Change Log: entries for
    versions 1.0.0–1.0.4 are moved to the new archival file
    `09_Persistence_CHANGELOG.md` (informative only, not a Dependency
    of this or any other document); §11 below now retains only
    versions 1.0.5 and later, addressing review feedback that the
    Change Log had grown to roughly one-third of the document's total
    length by v1.0.7. Two other review points from this round were
    considered and intentionally not changed: a Save()-over-Update()
    rename was declined as a naming preference rather than a defect,
    since intent is already applied inside the Aggregate before the
    Repository is called (§3's new clarification addresses the
    underlying concern without the rename); and a request for an
    explicit multi-database/Persistence-Provider pattern was declined
    as scope creep against §1.2's existing, deliberate exclusion of
    physical storage technology choices.
  - Version 1.0.7 (2026-07-15): Seventh clarification pass, following
    a seventh architecture review focused on Future Extensibility
    (Repository Evolution, Schema Evolution, Aggregate Evolution,
    Future Authentication Models). No new Aggregates, Commands,
    Queries, Events, or business rules introduced; no MVP scope,
    schema field, or constraint changed — every addition is a
    non-binding Forward Note or a generalization of reasoning already
    applied locally elsewhere in this document. (1) §3: added a
    Forward Note distinguishing Repository *interface* (method list,
    minimized to MVP Commands/Queries) from Repository *boundary*
    (ownership, fixed by §2.1) — future Commands expand the former
    without touching the latter. (2) §4.4: added a second Forward Note
    stating `AccessTokenId`/`RefreshTokenId` represent Session identity
    under MVP's Password/token model specifically, not permanently —
    future authentication models (API keys, OAuth sessions, device
    trust, Passkeys) may represent Session identity differently
    without changing Session's Repository boundary. (3) §8.3: added a
    Forward Note stating the "at most one Active Credential per
    Person" constraint is scoped to MVP's single-Credential-Type
    (Password) model and would require explicit re-evaluation, not
    silent carry-over, if multiple concurrent Credential Types with
    independent lifecycles are introduced — directly extending §4.5
    v1.0.6's Credential Types Forward Note to its storage-constraint
    consequence. (4) New §5.1.2 closing paragraph: clarified that §5.1
    authorizes exactly one Unit-of-Work exception *for MVP*
    (RegisterPerson), not a claim that Registration is the only
    cross-Aggregate transaction this platform will ever need — any
    future exception, registration-related or not, requires its own
    ADR and document revision under the same standard, per §5.3. (5)
    New §7.0: generalized the promotion-condition reasoning already
    applied locally to RefreshToken in §7.1 (independent lifecycle /
    invariants / transactional-consistency requirements as the
    triggering condition for Aggregate-boundary re-evaluation) into a
    named, standalone Rule — Promotion to Aggregate Boundary — stated
    once so it classifies any future Supporting Structure not yet
    listed in 059 §12, rather than requiring a bespoke forward note
    per structure.
  - Version 1.0.6 (2026-07-15): Sixth clarification pass, following a
    sixth architecture review focused on Future Extensibility. No new
    Aggregates, Commands, Queries, Events, or business rules
    introduced; no MVP scope, schema field, or constraint changed. (1)
    Added seven non-binding Forward Notes flagging future-extensibility
    seams without committing this document to any future design: §4.5
    (Credential is not assumed password-only forever), §4.4 (Session
    may later gain telemetry/security attributes without an ownership-
    boundary change), §4.3 (Membership.Role is not storage-constrained
    to `Owner` only), §7.1 (strengthened the existing RefreshToken
    Aggregate-boundary forward note to name the deciding factor — new
    independent invariants/lifecycle vs. more fields), new §5.1.2
    (future registration-adjacent capabilities do not automatically
    join the §5.1 Unit of Work; each requires its own architectural
    review), §3.1 (made explicit, as a standing Rule rather than an
    implicit pattern, that future Repository read methods are added
    only on demonstrated Command/Query demand), and §3 (explained why
    `OrganizationRepository` has no `Update` in MVP and flagged it as
    the most likely first Repository surface to change). None of these
    notes introduces a new Aggregate, Command, Query, Event, Rule, or
    schema field — each is scoped as informative/non-binding and
    explicitly defers the actual design decision to
    01_Domain_Model.md/03_Aggregates.md/04_Commands.md and a future
    ADR, consistent with this document's existing scope boundary (§1.2,
    §2.2).
  - Version 1.0.5 (2026-07-15): Fifth clarification pass, following a
    fifth architecture review of v1.0.4. No new Aggregates, Commands,
    Queries, Events, or business rules introduced. (1) Added §5.1.1,
    a normative statement that the §5.1 Unit of Work — not any
    individual Repository — owns the transaction boundary for the
    Core Ownership Transaction, and that Repositories SHALL NOT
    independently open/commit their own transaction while
    participating in it; this closes a real implementation-risk gap
    identified by review (a naive per-Repository SaveChanges() pattern
    would silently break the atomicity ADR-0002 Decision 1 requires).
    (2) §7.5's "none is planned to be derived from the Replaced rows"
    softened to "no such capability is defined for MVP," avoiding an
    unwarranted long-term commitment in a v1.0 document. (3) Added
    §10.1, a Pending Verification checklist flagging that this
    document's citations for `GetActiveSessionsByPersonId` and
    `MembershipRepository.GetByPersonId` depend on 05_Queries.md
    content not directly available for cross-check, to be re-verified
    before Freeze.
  - Version 1.0.4 (2026-07-15): Fourth clarification pass, following a
    fourth architecture review of v1.0.3. No new Aggregates, Commands,
    Queries, Events, or business rules introduced. (1) §3.1's
    `GetActiveSessionsByPersonId` justification restructured so the
    citation leads with the expected 05_Queries.md Query rather than
    the `GET /sessions` API endpoint (059 §11 is now cited only as
    background motivation, per this document's own Repository Pattern
    Boundary in §2.1, which does not recognize an API endpoint as a
    valid Repository-method justification). (2) §3.1's `GetById`
    self-justification restated as an explicit named Rule rather than
    an aside. (3) Added a non-binding forward note to §7.1 flagging
    RefreshToken as a candidate for future Aggregate re-evaluation if
    token-family revocation / reuse-detection behavior is introduced,
    explicitly deferring that decision to 03_Aggregates.md and a future
    ADR. Two reviewer points were considered and intentionally NOT
    incorporated as document changes, since they are out of this
    document's scope: whether Credential should be modeled as an
    independent Aggregate vs. a Person child entity is already decided
    and rationalized in 03_Aggregates.md §8, not a Persistence concern;
    and the semantic distinction between Session Status values
    `Authenticated` and `Active` is a Domain Model definition question
    (01_Domain_Model.md), which this document's §4.4 correctly mirrors
    without redefining.
  - Version 1.0.3 (2026-07-15): Third clarification pass. No new
    Aggregates, Commands, Queries, Events, or business rules
    introduced. (1) §4.5 and §5.2 now explicitly state that
    ChangePassword's new Credential instance receives a new
    CredentialId while the previous instance keeps its own CredentialId
    and only transitions Status to `Replaced` — directly answering
    "does CredentialId change?" and confirming the "two Aggregate
    instances" framing from v1.0.1/1.0.2 is correct. (2) §7.5 reworded:
    "no PasswordHistory structure" now explicitly means no separate
    *queryable history capability*, not physical deletion of Replaced
    rows — Replaced Credential rows remain persisted as ordinary
    Credential records. (3) §3.1's justification for
    `GetActiveSessionsByPersonId` reworded to acknowledge it presupposes
    a corresponding Query in 05_Queries.md rather than citing the
    `GET /sessions` API endpoint alone as sufficient justification;
    flagged as provisional pending that Query's confirmed existence.
    (4) Added the previously-missing justification for
    `MembershipRepository.GetByPersonId`, and added a blanket
    self-justification statement for `GetById` methods, closing the
    inconsistency where some catalog methods lacked an explicit
    Command/Query citation.
  - Version 1.0.2 (2026-07-15): Correction and clarification pass
    following a second review. No new Aggregates, Commands, Queries,
    Events, or business rules introduced.
    (1) CORRECTION: §3.1.1 in v1.0.1 incorrectly asserted that
    `SessionRepository.GetActiveByPersonId` returns zero-or-one. This
    contradicted 059 §8 ("A Person may own multiple active Sessions").
    The method is renamed `GetActiveSessionsByPersonId`, its cardinality
    corrected to zero-or-more, its "Active" filter defined explicitly,
    and its Command/Query justification (059 §11 GET /sessions) added
    to §3.1 where it had been missing. `CredentialRepository.GetActiveByPersonId`'s
    zero-or-one cardinality is unaffected and remains correct.
    (2) Added an explicit note to §5.2 stating that RefreshSession
    updates the existing Session instance in place (token rotation)
    rather than closing and recreating it, with a fallback instruction
    if 04_Commands.md is later found to specify otherwise.
    (3) Added §6.3, cross-referencing 059 §6's Consistency Guarantee to
    state explicitly that the Query layer must tolerate a temporarily
    absent Credential/Session for an otherwise-valid Person during the
    Post-Commit window.
  - Version 1.0.1 (2026-07-15): Editorial clarification pass — no new
    Aggregates, Commands, Queries, Events, or business rules
    introduced. (1) §3.1.1 added zero-or-one query cardinality for
    CredentialRepository.GetActiveByPersonId and
    SessionRepository.GetActiveByPersonId, consistent with the
    already-existing §8.3 "at most one" storage constraints. **This
    entry's SessionRepository claim was incorrect and was corrected in
    v1.0.2 — see above.** (2) §5.2 reworded to distinguish Aggregate
    *type* from Aggregate *instance* for ChangePassword's two-row write,
    removing a possible DDD ambiguity; the underlying conclusion (no
    cross-Aggregate exception required) is unchanged.
  - Version 1.0.0 (2026-07-14): Initial Persistence Blueprint. Defines
    one Repository per Aggregate Root (03_Aggregates §10), the logical
    persistent entity schema per Aggregate, the Core Ownership
    Transaction as a Unit of Work spanning exactly three Repositories
    (the sole approved exception, per ADR-0002 Decision 7 / 057 §8),
    Post-Commit transaction isolation, classification of 059 §12's
    Supporting Persistence Structures for MVP, and the uniqueness/
    concurrency constraints required by existing Domain Rules and
    Command Invariants. No new Aggregates, Commands, Events, or
    business rules introduced.
-->

# 1. Overview

This document defines the **Persistence Contract** of the Identity
Platform: how each Aggregate is stored, which component owns that
storage, and — critically — how the one approved cross-Aggregate
transaction (Registration) is actually realized at the persistence
layer without either violating Aggregate encapsulation or introducing
a general precedent for multi-Aggregate transactions.

This document answers:

> "Where does an Aggregate's state live, who is allowed to read/write
> it, and what transactional guarantees hold when a Command touches
> more than one Aggregate?"

## 1.1 What This Document Defines

- The Repository pattern: exactly one Repository per Aggregate Root
  (§3)
- The logical persistent entity schema per Aggregate (§4)
- The Core Ownership Transaction realized as a Unit of Work (§5)
- Post-Commit transaction isolation (§6)
- Classification of 059 §12's "Supporting persistence structures" for
  MVP — which are required, which are excluded, and which are
  reclassified as internal storage detail rather than new Aggregates
  (§7)
- Consistency, concurrency, and uniqueness constraints required by
  Domain Rules and Command Invariants already declared elsewhere (§8)

## 1.2 What This Document Does NOT Define

- Physical database technology or engine choice (relational,
  document, key-value, etc.) — per 057 §5 Principle 5, "Ownership
  SHALL remain independent from implementation technology," which this
  document extends to Persistence generally
- DDL, column types, physical indexing syntax, partitioning, or
  migration strategy
- Specific concurrency-control mechanism (optimistic version tokens vs.
  pessimistic locking) — this document states *where* a concurrency
  guarantee is required, not *how* it is implemented
- Connection pooling, replication, backup/restore, or disaster
  recovery
- Event Store, Outbox pattern, or any Domain Event delivery mechanism
  — that boundary is set by 06_Domain_Events.md §1.2 and is not
  reopened here
- ORM or data-mapping framework choice

This document introduces no new:

- Aggregates
- Commands
- Queries
- Events
- Business Rules

---

# 2. Scope Boundary

## 2.1 Repository Pattern Boundary

Per 03_Aggregates.md §10 and §9 ("Cross-Aggregate Consistency:
Maintained by Domain Services, not aggregates"): each Aggregate Root
has exactly one owning Repository. A Repository SHALL NOT expose
write operations on any Aggregate other than the one it owns.

## 2.2 MVP Boundary Protection

No persistence structure introduced by this document SHALL imply a
new Aggregate, a new Command, or a new cross-Aggregate coordination
path beyond the single exception already authorized by ADR-0002
Decision 7 (§5). Where 059 §12 lists a "Supporting persistence
structure" without a corresponding Aggregate in 01_Domain_Model.md,
this document classifies it as internal storage detail owned by an
existing Repository, never as an implicit new Aggregate (§7).

---

# 3. Repository Catalog

Per 01_Domain_Model.md §1 / §4 and 03_Aggregates.md §1, there are
exactly five Aggregate Roots. Each has exactly one owning Repository.

| Repository | Owns Aggregate | Write Operations (MVP) | Read Operations (MVP) |
|---|---|---|---|
| PersonRepository | Person | Create, Update | GetById, GetByEmail |
| OrganizationRepository | Organization | Create | GetById |
| MembershipRepository | Membership | Create | GetById, GetByPersonId, GetByPersonIdAndOrganizationId |
| SessionRepository | Session | Create, Update | GetById, GetByAccessTokenId, GetByRefreshTokenId, GetActiveSessionsByPersonId |
| CredentialRepository | Credential | Create, Update (status transition) | GetActiveByPersonId |

**What `Update` means in this table**: `Update` denotes persisting an
Aggregate instance's already-changed state — it is a state-persistence
operation, not a business operation in its own right. The business
intent (which field changed, and under what rule) is applied inside
the Aggregate by the Command Handler *before* the Repository is
called, per 03_Aggregates.md's Aggregate-owns-its-invariants boundary;
`Update` is intentionally a single generic method rather than one
method per Command (`UpdateProfile`, `ChangeEmail`, etc.) because the
Repository's role stops at persisting whatever valid state the
Aggregate now holds. This table's use of `Update` does not imply an
undifferentiated write path with no business rule behind it — the rule
already ran, one layer up.

No Repository exposes a Delete operation in MVP — no Command in
04_Commands.md removes an Aggregate instance (14_MVP §2/§3: no
Suspend/Archive/Revoke Commands exist in Version 1.0).

**Forward note (non-binding) — Interface vs. Boundary**: the Write/
Read Operations columns above are intentionally minimized to exactly
what MVP's Commands and Queries require, not to what the underlying
Aggregate could eventually support. Future Commands — e.g.
`RenameOrganization`, `SuspendOrganization`, `ChangeOrganizationSettings`
— will expand a Repository's *contract* (its list of methods) without
changing that Repository's *ownership boundary* (§2.1: still exactly
one Repository per Aggregate Root, still no cross-Aggregate writes).
Readers of this catalog should treat the columns as a current snapshot
tied to 14_MVP.md's Command set, not as a ceiling on what
`OrganizationRepository` (or any Repository here) is architecturally
capable of.

**Note on `OrganizationRepository`'s narrower surface**: unlike
`PersonRepository` and `SessionRepository`, `OrganizationRepository`
exposes no `Update` in MVP. This is not an oversight — 14_MVP.md §1/§3
defines no Command that modifies an existing Organization (no Rename,
no Update-Organization-Profile, no lifecycle transition), so there is
nothing for an `Update` method to serve, consistent with the read-
method citation Rule above. This is expected to be the first
Repository surface to change in a future version: the moment
04_Commands.md defines any Organization-modifying Command, this
Repository gains the corresponding `Update` method and this table is
revised — that dependency is called out here explicitly so it is not
mistaken for a gap in the current MVP scope.

## 3.1 Why These Read Methods, Specifically

Each read method above exists because a specific Command or Query in
04_Commands.md / 05_Queries.md requires it:

- `PersonRepository.GetByEmail` — required by RegisterPerson's
  uniqueness precondition (04_Commands §4.1), AuthenticatePerson's
  Person resolution (04_Commands §4.2 "Resolve Person by email"), and
  UpdatePersonProfile's email-conflict check (04_Commands §4.3)
- `MembershipRepository.GetByPersonIdAndOrganizationId` — required to
  enforce "One membership per Person per Organization in MVP"
  (02_Use_Cases UC-008 Constraints)
- `SessionRepository.GetByRefreshTokenId` — required by RefreshSession
  (04_Commands §4.5)
- `SessionRepository.GetByAccessTokenId` — required to resolve the
  calling Session's identity for every Session-ownership-validated
  Command (UpdatePersonProfile, ChangePassword, LogoutSession)
- `SessionRepository.GetActiveSessionsByPersonId` — required to serve
  `GetSessionsForPerson` (05_Queries §4.4), whose Purpose is stated
  there as "Retrieve active Sessions belonging to a Person," matching
  this Repository method's "Active" filter (§3.1.1). This closes the
  provisional status this citation carried in v1.0.5–v1.0.7 (§10.1):
  05_Queries.md does define the corresponding Query, under the name
  `GetSessionsForPerson` rather than the `GetActiveSessionsForPerson`
  name this document had speculatively anticipated before
  05_Queries.md was available for direct cross-check. Note also that
  `GetSessionsForPerson`'s output (05_Queries §4.4) explicitly excludes
  `AccessTokenId` and `RefreshTokenId` from its projection; this
  Repository method still returns the full Session entity (§4.4) — the
  narrower Query-level projection is applied by the Query layer, not
  by this Repository method, consistent with §2.1's boundary.
- `MembershipRepository.GetByPersonId` — required to serve
  `GetOrganizationsForPerson` (05_Queries §4.2) and
  `GetMembershipsForPerson` (05_Queries §4.3, referenced in §3.2
  below), both confirmed to exist in 05_Queries.md; they compose this
  method with `OrganizationRepository` reads as a read projection, not
  a write dependency. This closes the provisional status this citation
  carried in v1.0.5–v1.0.7 (§10.1).
- `CredentialRepository.GetActiveByPersonId` — required by
  AuthenticatePerson (Credential validation) and ChangePassword
  (current-password validation)

Every read method above is justified by a Command in 04_Commands.md, a
Constraint in 02_Use_Cases.md, or a Query in 05_Queries.md — this
document does not speculatively add query capability.

**Rule — read methods grow only from demand, not in anticipation of
it**: this applies going forward, not only to the catalog as it
stands today. If a future lookup need arises — for example locating a
Person by Username, PhoneNumber, or ExternalIdentity instead of
Email — the corresponding Repository method SHALL be added only once
a Command or Query in 04_Commands.md / 05_Queries.md actually requires
it, following the same citation discipline as every method in §3.1
above. This document does not pre-declare such methods now on the
expectation that they will eventually be needed.

**Rule — Repository identity retrieval is self-justifying**: `GetById`
methods (one per Repository, omitted from the list above) are an
intrinsic Repository capability, not an exception to the
Command/Query-justification rule above. Loading an Aggregate instance
by its own primary key is a precondition for essentially every Command
that operates on an existing instance, rather than a capability tied
to one specific Command; requiring a per-Command citation for it would
add no traceability value. No other read method in this catalog is
exempt from citation.

### 3.1.1 Query Cardinality: `GetActiveByPersonId` / `GetActiveSessionsByPersonId`

These two methods have **different cardinalities**, and the naming
intentionally reflects that difference.

**`CredentialRepository.GetActiveByPersonId` returns zero or one**
result, never a collection. This follows directly from the §8.3
storage-level constraint ("at most one Credential with Status =
`Active` per PersonId") — "at most one" at the storage layer implies
"zero or one" at the query layer. Zero results is a valid outcome, not
an error: for example, a Person exists with no Active Credential
during the window between the Core Ownership Transaction committing
and the Post-Commit Credential creation completing (§6). Callers SHALL
treat an empty result as "not yet present," not as a fault.

**`SessionRepository.GetActiveSessionsByPersonId` returns zero or
more** results (a collection). Per 059 §8 ("A Person may own multiple
active Sessions" — Web Browser, Mobile App, Telegram Bot, Desktop
Application, each independent): there is no "at most one Active
Session per Person" constraint anywhere in 01_Domain_Model.md,
03_Aggregates.md, or 059, and this document introduces none. A
singular, zero-or-one method name here would misrepresent the
Session Aggregate's multi-device design; the method is named in the
plural for that reason.

**Definition of "Active" for this query**: a Session is included in
`GetActiveSessionsByPersonId`'s result set if and only if its `Status`
field (§4.4) equals literal value `Active`. Sessions in `Created`,
`Authenticated`, `Suspended`, `Expired`, or `Closed` status are
excluded. This is a query-time filter on the existing Status field
(01_Domain_Model §5 Session Lifecycle); it introduces no new status
value and no new Aggregate rule.

## 3.2 Cross-Repository Reads Remain a Query Concern, Not a Repository Concern

`GetOrganizationsForPerson` and `GetMembershipsForPerson`
(05_Queries §4.2, §4.3) require joining Membership and Organization
data. Per 05_Queries §2.3: "Queries MAY read across Aggregates... this
is a read projection, not a transactional coordination." This document
does not add a `GetOrganizationsForPerson` method to any single
Repository; that join is a read-model/query-handler concern that MAY
compose `MembershipRepository` and `OrganizationRepository` reads (or
a dedicated read projection), without either Repository gaining a
write dependency on the other. This preserves §2.1's boundary — reads
may cross Aggregates for projection purposes; writes never do outside
§5's single exception.

---

# 4. Persistent Entity Schema

Each Aggregate's persistent entity mirrors its attributes in
01_Domain_Model.md §2 exactly — this document adds no business field.
Field names here follow the same PascalCase convention as
01_Domain_Model.md and 06_Domain_Events.md (persistence, like Domain
Events, is independent of 07_Contracts.md's REST-specific camelCase
convention — see header).

## 4.1 Person

| Field | Notes |
|---|---|
| PersonId | Primary key. Immutable (01_Domain_Model §6 Invariants) |
| Email | Unique across all Persons (§8.3) |
| DisplayName | |
| Status | MVP: always `Active` (14_MVP §3) |
| CreatedAt | |
| UpdatedAt | |

## 4.2 Organization

| Field | Notes |
|---|---|
| OrganizationId | Primary key |
| Name | |
| Category | MVP: always `Personal` |
| Status | MVP: always `Active` |
| CreatedAt | |

## 4.3 Membership

| Field | Notes |
|---|---|
| MembershipId | Primary key |
| PersonId | References Person. Uniqueness: (PersonId, OrganizationId) pair unique in MVP (§8.3) |
| OrganizationId | References Organization |
| Role | MVP: always `Owner` |
| Status | MVP: always `Active` |
| CreatedAt | |

**Forward note (non-binding)**: `Role` is a plain field, not an
enumerated storage constraint — this document does not encode "Owner
is the only legal value" as a database-level check. Additional
Membership roles are future scope (01_Domain_Model.md does not rule
them out; 14_MVP.md simply does not implement Commands/Use Cases for
them yet); when introduced, they extend this field's legal value set
without requiring a schema or Repository change here.

## 4.4 Session

| Field | Notes |
|---|---|
| SessionId | Primary key |
| PersonId | References Person |
| AccessTokenId | Unique across all Sessions (§8.3) |
| RefreshTokenId | Unique across all Sessions (§8.3) |
| DeviceInfo | Optional |
| IpAddress | |
| ExpiresAt | |
| Status | `Created` → `Authenticated` → `Active` → `Suspended` (optional) → `Expired` → `Closed` |
| CreatedAt | |

**Forward note (non-binding)**: this schema deliberately holds close
to the minimum fields required by MVP's Session Lifecycle and
Commands. Additional security and telemetry attributes commonly added
to Session persistence in later versions — for example
`LastActivityAt`, `RevokedAt`, `RevokedBy`, `SessionType`, or
`ClientId` — may be introduced without altering Session's ownership
boundary (still exactly one `SessionRepository`, still one Aggregate
Root) or its Repository Pattern Boundary (§2.1). Such additions are a
`01_Domain_Model.md`/`03_Aggregates.md` schema change to make first;
this document would then mirror it in §4.4, per §4's "adds no business
field" rule.

**Forward note (non-binding) — token-based identity is an MVP
authentication-model detail, not a permanent Session definition**:
this schema currently represents a Session's identity via
`AccessTokenId` and `RefreshTokenId`, matching MVP's Password/token
authentication model (01_Domain_Model.md §3). Future authentication
models — API keys, service accounts, OAuth-issued sessions, device-
trust sessions, Passkey-originated logins — may represent session
identity differently than a rotating access/refresh token pair. This
document does not assume `AccessTokenId`/`RefreshTokenId` are the only
possible shape of Session identity; a future change to how Session
identity is represented is a `01_Domain_Model.md`/`03_Aggregates.md`
schema decision, and would not by itself imply a change to Session's
Repository boundary (§2.1) or ownership (still one Session Aggregate,
still one `SessionRepository`).

## 4.5 Credential

| Field | Notes |
|---|---|
| CredentialId | Primary key. Each Credential Aggregate instance has its own, distinct CredentialId — `ChangePassword` (§5.2) assigns a **new** CredentialId to the newly-Active Credential; it does not reuse the previous instance's CredentialId. The previous instance keeps its original CredentialId and simply transitions its own Status to `Replaced`. |
| PersonId | References Person. Uniqueness: at most one row with Status = `Active` per PersonId (§8.3, Invariant-005) |
| PasswordHash | Never exposed outside the Credential Repository boundary — no Repository, Command, Query, Event, or Contract in this Blueprint set reads this field back out (01_Domain_Model §2; 06_Domain_Events §2.5) |
| PasswordVersion | |
| Status | `Created` → `Active` → `Replaced` → `Revoked` |
| CreatedAt | |

**Forward note (non-binding)**: this schema, and every Credential
reference elsewhere in this document, describes the MVP's sole
Credential Type (Password) without asserting that Credential *means*
Password permanently. `CredentialRepository` is scoped to whatever
`01_Domain_Model.md` and `03_Aggregates.md` define as the Credential
Aggregate's shape at a given version — this document does not assume
Credential is password-only forever, and introduces no MVP field,
Command, or constraint premised on that assumption. Should a future
version introduce additional Credential Types (e.g. Passkey, WebAuthn,
TOTP, Recovery Code, OAuth Identity), the resulting schema and Storage
constraint changes are a `01_Domain_Model.md`/`03_Aggregates.md`
decision to make first; this document would then extend §4.5 and §8.3
to match, not originate the change.

---

# 5. Core Ownership Transaction as a Unit of Work

## 5.1 The Single Approved Exception

Per ADR-0002 Decision 7 and 057 §8 "Command Model Coordination
Exception," `RegisterPerson` is the **only** operation in the Identity
Platform whose persistence step SHALL span more than one Repository
within a single atomic transaction.

Concretely: `RegistrationApplicationService` (01_Domain_Model §8)
SHALL execute the Core Ownership Transaction as one Unit of Work
comprising exactly three Repository writes:

```text
BEGIN Unit of Work
  PersonRepository.Create(Person)
  OrganizationRepository.Create(Organization)
  MembershipRepository.Create(Membership)
COMMIT Unit of Work
```

If any of the three writes fails, the entire Unit of Work SHALL roll
back — no partial ownership state may persist (ADR-0002 Decision 1;
057 §8: "Partial ownership registration states are prohibited").

### 5.1.1 Transaction Ownership: the Unit of Work Owns the Transaction, Repositories Do Not

The atomicity guaranteed above holds only if a single physical
transaction is shared by all three Repository writes. This document
therefore states, as a normative constraint on any implementation:

- The **Unit of Work** (not any individual Repository) owns the
  transaction boundary — it opens the transaction, passes/shares it
  with `PersonRepository`, `OrganizationRepository`, and
  `MembershipRepository` for the duration of the three writes, and
  alone commits or rolls it back.
- Individual Repositories SHALL NOT open or commit their own
  transaction (e.g. an independent `SaveChanges()`/commit call per
  Repository) when operating inside this Unit of Work. A sequence of
  three independently-committed Repository writes is **not** a Unit of
  Work, even if the three writes happen to succeed in immediate
  succession — it reintroduces exactly the partial-ownership-state
  risk (a Person committed with no Organization, for example) that
  ADR-0002 Decision 1 and 057 §8 prohibit, because a failure on the
  second or third write can no longer roll back the first.
- Outside this Unit of Work (i.e. every other Command in §5.2, and the
  Post-Commit writes in §6), each Repository's `Create`/`Update` call
  legitimately owns its own transaction, since those operate on
  exactly one Aggregate.

This is a persistence-layer constraint, not an infrastructure/ORM
choice: *how* a shared transaction is technically propagated across
three Repository instances (shared connection/session, ambient
transaction, explicit transaction object passed to each Repository
call, etc.) remains implementation-specific per §1.2 — but *that* the
transaction is shared and singly-owned by the Unit of Work is not
optional.

### 5.1.2 Future Registration-Related Capabilities Do Not Automatically Join This Unit of Work

Per §5.3's Scope Limitation, this document additionally states, as a
guard against **Transaction Creep**: if a future capability related to
registration is introduced — for example an Invitation flow,
Organization Template, Initial Settings, or a Billing Account created
alongside a new Person — that capability's persistence step SHALL NOT
be assumed to join the §5.1 Unit of Work merely because it happens
during registration. Whether such a future write belongs inside the
Core Ownership Transaction, becomes an additional Post-Commit
Operation (§6), or is modeled as its own Aggregate with its own
Repository is a separate architectural decision requiring explicit
review against ADR-0002 Decision 7 / 057 §8 at the time it is
proposed — it is not granted membership in this Unit of Work by
default. This keeps the three-Repository scope of §5.1 a closed,
enumerated list rather than an implicitly growing one.

**Scope of this section's authorization**: §5.1 authorizes exactly one
Unit-of-Work exception *for MVP* — RegisterPerson. This is not the
same as asserting Registration is the only cross-Aggregate transaction
this platform will ever need. If a future capability, registration-
related or otherwise, genuinely requires its own multi-Repository
atomic transaction, that is a **new** exception, not an extension of
this one — it requires its own explicit ADR and its own corresponding
revision to 03_Aggregates.md and this document, following the same
justification standard ADR-0002 Decision 7 already set. §5.3's Scope
Limitation governs both cases identically: no exception, present or
future, is self-authorizing.

## 5.2 No Other Command Requires This

Every other Command in 04_Commands.md's catalog (§3) operates within
exactly one Repository's transaction:

| Command | Repository (single, per §2.1) |
|---|---|
| AuthenticatePerson | SessionRepository (Person, Credential are read-only inputs, not written) |
| UpdatePersonProfile | PersonRepository |
| LogoutSession | SessionRepository |
| RefreshSession | SessionRepository |
| ChangePassword | CredentialRepository |

`ChangePassword` deserves a specific note: it writes **two Credential
Aggregate instances** — the previous Credential (Status → `Replaced`)
and a new Credential (Status = `Active`) — within one transaction, both
managed by the same `CredentialRepository`. These are two distinct
Aggregate *instances* of the same Aggregate *type* (`Credential`), not
one Aggregate split across two records — each row is independently a
complete, valid Credential. This is **not** a cross-Aggregate-type
transaction: both instances belong to the `Credential` Aggregate type,
both writes go through `CredentialRepository` alone, and no other
Repository participates. It therefore requires no exception under
ADR-0002 Decision 7 / 057 §8, which govern coordination *across
distinct Aggregate types*, not multiple instance writes within one
Aggregate type's own Repository.

**Aggregate identity criterion**: the two instances are distinguished
by CredentialId — the new Credential receives a newly-generated
CredentialId (§4.5); the previous Credential keeps its own original
CredentialId and only its Status field changes, to `Replaced`. This is
what makes them two instances rather than one: each has its own
persistent identity that outlives this transaction, consistent with
the Credential Lifecycle `Created → Active → Replaced → Revoked`
(01_Domain_Model §5; 03_Aggregates §6) describing the lifecycle of a
single Credential instance, not a lifecycle shared across password
changes.

`RefreshSession` also deserves a specific note, since the Repository
Catalog (§3) lists it under `SessionRepository.Update` rather than
`Create`: `RefreshSession` SHALL update the **same** Session Aggregate
instance in place — rather than closing the existing Session and
creating a new one. The Session Lifecycle (01_Domain_Model §5;
03_Aggregates §5) shows a single Session progressing through
`Created → Authenticated → Active → ... → Closed`; token rotation is a
change to a Session's attributes, not a change to its identity, so it
does not create a second Session instance. This "same instance,
in-place update" reading is confirmed by 04_Commands.md §4.5's
Postconditions ("Session continues as authenticated... Session
lifecycle state remains unchanged").

**Correction (v1.0.9)**: earlier versions of this note stated that
`RefreshSession` rotates *both* `AccessTokenId` and `RefreshTokenId`.
Now that 04_Commands.md is available for direct cross-check, its §4.5
Postconditions state this more precisely than this document previously
assumed: `AccessTokenId` is definitively reissued ("New AccessTokenId
is issued"), but `RefreshTokenId` is explicitly left
implementation-specific ("RefreshTokenId is preserved or refreshed
according to implementation"). This document does not narrow that
implementation-specific choice — doing so in a Persistence Blueprint
would exceed this document's role, since 04_Commands.md is the
authoritative source for what a Command's Postconditions guarantee,
and 04_Commands.md itself declined to guarantee unconditional
RefreshTokenId rotation. The corrected statement is: `RefreshSession`
updates the existing Session instance in place, always reissuing
`AccessTokenId` and extending `ExpiresAt`; whether `RefreshTokenId` is
also reissued on a given call is an implementation choice
04_Commands.md leaves open, not a fact this document should assert
either way. This document treats "same instance, in-place update" as
the intended reading of 04_Commands.md's RefreshSession Postconditions
regardless of which token-rotation choice is made; if a future revision
of 04_Commands.md instead defines `RefreshSession` as issuing a new
Session, §3's Write Operations column for `SessionRepository` SHALL be
revised from `Update` to `Create, Update` accordingly.

## 5.3 Scope Limitation Carries Through

Per ADR-0002's Scope Limitation and 057 §8: this Unit-of-Work exception
"SHALL NOT be interpreted as a general precedent for multi-Aggregate
transactional Commands elsewhere in the platform." This document
confirms that no other Repository combination is ever opened within a
single transaction in MVP scope.

---

# 6. Post-Commit Transaction Isolation

## 6.1 Separate Transactions, Started Only After Commit

Per 059 §6 and ADR-0002 Decision 1, Credential creation and initial
Session creation are Post-Commit Operations. At the persistence layer,
this means:

```text
[Unit of Work from §5.1 COMMITTED]
        ↓
BEGIN separate transaction
  CredentialRepository.Create(Credential)
COMMIT (or fail independently)
        ↓
BEGIN separate transaction
  SessionRepository.Create(Session)
COMMIT (or fail independently)
```

Each Post-Commit write is its own transaction, opened only after §5.1's
Unit of Work has already committed. A failure in either SHALL NOT roll
back, retry-into, or otherwise reopen the §5.1 Unit of Work — the
ownership Aggregates (Person, Organization, Membership) are already
durably committed and remain valid regardless of what happens next
(059 §6 Non-Invalidating Policy).

## 6.2 Recovery Is Implementation-Specific

Per 04_Commands §9.3 and 059 §6: how a failed Post-Commit write is
retried, compensated, or surfaced to an operator is implementation-
specific and out of scope for this document, consistent with every
other Blueprint document's treatment of this same topic.

## 6.3 Effect on the Query Layer

During the interval between §5.1's Unit of Work committing and §6.1's
Post-Commit writes completing, `PersonRepository.GetById` (and any
Query composing it, e.g. `GetPersonById` in 05_Queries.md) SHALL
return a fully valid Person, Organization, and Membership — per 059 §6
"Consistency Guarantee," ownership is complete and durable at that
point regardless of Credential or Session state. Callers reading
Person/Organization/Membership data during this window observe
complete data; only `CredentialRepository.GetActiveByPersonId` and
`SessionRepository.GetActiveSessionsByPersonId` may legitimately
return empty results for that Person during this same interval (per
§3.1.1). No Query in this Blueprint set SHALL treat a temporarily
absent Credential or Session as a fault when resolving an otherwise
valid Person.

---

# 7. Supporting Persistence Structures (059 §12)

059 §12 lists five "Supporting persistence structures" that "may
include" beyond the five minimum Aggregate tables. This section
classifies each for MVP, resolving which are required, which are
internal storage detail (not new Aggregates), and which are excluded.

## 7.0 General Rule — Promotion to Aggregate Boundary

Each classification in §7.1–§7.5 below applies one general rule,
stated here once rather than re-derived per structure:

> **Rule — Promotion to Aggregate Boundary.** A supporting persistence
> structure SHALL remain an internal implementation detail, owned
> exclusively by its parent Aggregate's Repository, unless and until it
> acquires:
>
> - independent lifecycle rules,
> - independent invariants, or
> - transactional consistency requirements
>
> that cannot be fully owned by its current Aggregate Root. Only once
> one of these three conditions is met may the structure be considered
> a candidate for promotion to a child entity or independent Aggregate
> — and even then, promotion itself requires revision of
> `01_Domain_Model.md`, `03_Aggregates.md`, and, where it introduces a
> new cross-Aggregate transaction, a future ADR (per §5.1.2/§5.3). This
> document classifies structures against this Rule; it does not
> perform promotions itself.

This is the same reasoning §7.1 already applied specifically to
RefreshTokens (token-family revocation / reuse detection as the
triggering condition); it is stated here as the general test so that a
future Supporting Structure not yet listed in 059 §12 can be classified
consistently without waiting for a document revision to spell out its
own bespoke forward note.

## 7.1 RefreshTokens — Internal to SessionRepository

`RefreshTokenId` is already a Session attribute (01_Domain_Model §2,
§3). A supporting `RefreshTokens` structure, if implemented (e.g. to
optimize lookup or support explicit revocation lists), is **internal
storage detail owned exclusively by `SessionRepository`** — it is not
a separate Aggregate and gains no independent Repository. This
resolves the apparent tension between 059 §12 listing it as a
"structure" and 03_Aggregates.md never listing it as an Aggregate:
it is the same Session data, potentially normalized into a supporting
table, still behind one Repository boundary.

**Forward note (non-binding)**: if a future capability requires
token-family revocation, reuse detection, compromise tracking, or
device-trust behavior for RefreshTokens, that behavior would
constitute new domain rules attached to token data — not merely new
fields on the existing Session record. At that point RefreshToken
would need re-evaluation as a candidate child entity or independent
Aggregate boundary decision, which belongs to 03_Aggregates.md and a
future ADR, not to this document. This document does not currently
take a position on which outcome is more likely; it only flags that
"internal storage detail" (the classification above) and "independent
Aggregate" are the two live candidates, and that the deciding factor
is whether the future behavior requires its own invariants and
lifecycle independent of Session — not merely additional storage. For
MVP, no such behavior exists, and the internal-storage-detail
classification above stands.

## 7.2 LoginHistory — Optional, Non-Transactional

A `LoginHistory` structure, if implemented, records each authentication
attempt for audit/analytics purposes. It is **not required for MVP
functional correctness** — `LoginSucceeded`/`LoginFailed` Domain
Events (06_Domain_Events §4.5, §4.6) already carry the audit-relevant
facts. If implemented, `LoginHistory` is a best-effort, eventually-
consistent write derived from those same facts (e.g. a projection),
not a write inside the Core transaction or any Command's transaction
boundary — its failure SHALL NOT affect `AuthenticatePerson`'s outcome.

## 7.3 IdentityEvents — Explicitly Deferred

An `IdentityEvents` table would be an Event Store. Per
06_Domain_Events.md §1.2, Event Bus/Outbox/store mechanics are
explicitly out of scope for that document, and this document does not
reopen that boundary. Whether Domain Events are persisted at all
(and if so, how) is deferred to a future Messaging/Integration
document.

## 7.4 AuditLogs — Explicitly Deferred

Per 06_Domain_Events.md §2.6, field-level sensitivity/audit
classification is deferred to a future Security/Data Classification
document. A general-purpose `AuditLogs` structure is likewise deferred
— this document defines Aggregate persistence, not a general audit
subsystem.

## 7.5 PasswordHistory — Explicitly Excluded from MVP

Per 01_Domain_Model.md §2 ("Do NOT introduce Credential History in
MVP. Credential History belongs to future extensibility") and
03_Aggregates.md §8 ("Credential History is future scope"): **no
separate `PasswordHistory` structure exists** in Version 1.0.

This does not mean previous Credential rows are physically deleted.
Per §5.2, `ChangePassword` (04_Commands §4.6) marks the previous
Credential instance's Status `Replaced` and that row remains persisted
in the same `Credential` table/collection — it is simply no longer the
Active Credential. What is excluded is a *second, separate* structure
(e.g. a `PasswordHistory` table keyed by PersonId, listing prior
PasswordHash values for lookup or reuse-prevention purposes) that
would let application code query "all past passwords for this
Person." No such capability is defined for MVP; whether one is
introduced later, reading from `Replaced` rows or from a future
dedicated structure, is a decision for a future ADR and out of scope
here. This is a firmer position than §7.1–§7.4
above (which are optional/deferred): `PasswordHistory` **as a queryable
capability** is excluded from MVP, not merely deferred, consistent with
the Domain Model's explicit prohibition — while the `Replaced` rows
themselves persist as ordinary Credential records, not as history
data.

---

# 8. Consistency, Concurrency, and Uniqueness

## 8.1 Intra-Aggregate Consistency

Per 03_Aggregates.md §9: each Aggregate's internal state is consistent
without external coordination, and all changes to one Aggregate
instance occur within a single transaction scoped to its own
Repository. This document adds no exception to that rule beyond §5.1.

## 8.2 Cross-Aggregate Consistency

Outside the §5.1 Unit of Work, no transaction spans more than one
Repository. Where a Command's Postconditions imply effects on more
than one Aggregate's data (there are none beyond RegisterPerson in
MVP — see §5.2's table), no such Command exists in this catalog.

## 8.3 Required Uniqueness Constraints

The persistence layer SHALL enforce the following as hard storage-level
constraints, not merely application-level checks (application-level
validation alone is insufficient to prevent race conditions under
concurrent requests):

| Constraint | Source |
|---|---|
| Person.Email unique across all Persons | 04_Commands §4.1, §4.3 |
| (Membership.PersonId, Membership.OrganizationId) unique | 02_Use_Cases UC-008 Constraints: "One membership per Person per Organization in MVP" |
| Session.AccessTokenId unique across all Sessions | Required for `SessionRepository.GetByAccessTokenId` to resolve unambiguously |
| Session.RefreshTokenId unique across all Sessions | Required for `SessionRepository.GetByRefreshTokenId` to resolve unambiguously; 04_Commands §4.5 |
| At most one Credential with Status = `Active` per PersonId | 04_Commands Invariant-005: "A Person SHALL have at most one Active Credential" |

The specific mechanism (unique index, application-level lock,
serializable isolation, etc.) is implementation-specific (§1.2); this
document only mandates that the constraint SHALL hold under
concurrent access, not how.

**Forward note (non-binding) — the single-Active-Credential constraint
is an MVP Password-model constraint, not a permanent one**: "at most
one Active Credential per Person" is correct for MVP because MVP has
exactly one Credential Type (Password) and one Person can meaningfully
have only one current password. This constraint would require explicit
re-evaluation — not silent carry-over — if a future version introduces
multiple concurrent Credential Types with independent lifecycles (e.g.
a Password and a Passkey and a TOTP secret, each simultaneously
`Active` for the same Person, per §4.5's Credential Types forward
note). Whether that future model relaxes this constraint to "at most
one Active Credential per (Person, CredentialType)" or replaces it
with something else entirely is a `01_Domain_Model.md` decision this
document would then reflect, not decide here.

## 8.4 Concurrency Control

Where two concurrent requests could race on the same Aggregate
instance (e.g. two concurrent `UpdatePersonProfile` calls for the same
Person, or a `RefreshSession` racing a `LogoutSession` for the same
Session), the Repository SHALL detect and reject a write based on
stale state (optimistic concurrency) or serialize access (pessimistic
locking). Which mechanism is used is implementation-specific; that
*some* mechanism prevents a silently-lost update is required for every
Repository's `Update` operation listed in §3.

### 8.4.1 Minimum Requirement for Optimistic Concurrency

If a Repository implements optimistic concurrency (the more common
choice for the single-instance updates in §3's catalog), each
persistent entity in §4 SHALL carry **some** form of Version marker
that a write can be conditioned on — this document does not name a
field in §4's schemas because it is a concurrency-control mechanism,
not a business field, and adding it as a named schema column would
violate §4's "adds no business field beyond 01_Domain_Model.md" rule.
Concretely, at least one of the following SHALL exist, whichever an
implementation chooses:

- a monotonically-incrementing version number (e.g. `RowVersion`,
  `AggregateVersion`),
- a server-managed last-write timestamp usable as a compare-and-swap
  token, or
- a database-native row-version mechanism (e.g. a native `rowversion`/
  `xmin` column, an ETag derived from one).

This document mandates only that *one* such marker exists and that
every `Update` write is conditioned on it — not which of the three
forms is used, nor its column name or type. This is consistent with
§1.2's exclusion of "specific concurrency-control mechanism" from this
document's scope: §1.2 already declines to choose *how* concurrency is
enforced; this subsection closes the narrower gap of confirming *that*
a comparable marker must exist for optimistic concurrency to be
possible at all, which §8.4 assumed but did not state.

**Relationship to a possible future Event-Sourced model**: if a future
version of this platform adopts Event Sourcing for any Aggregate, that
Aggregate's "Version" would be redefined as the Aggregate's event
stream position rather than a row-level marker, and that redefinition
is a `03_Aggregates.md`/architecture decision this document would
follow, not one it makes preemptively here. This subsection describes
only the current, non-event-sourced persistence model already assumed
throughout this document (§4, §5, §6).

---

# 9. Cross-Document Alignment

## 9.1 Against 03_Aggregates.md

✅ §10's forward reference ("09_Persistence.md," "exactly one owning
Repository") is fulfilled by §3 of this document.

✅ §9's Aggregate Consistency Boundaries (intra-Aggregate transaction
scope, Domain-Service-mediated cross-Aggregate consistency, and the
RegisterPerson exception) are carried through unchanged into §5, §8.1.

## 9.2 Against 01_Domain_Model.md

✅ Every persistent entity field in §4 traces to an attribute in
01_Domain_Model.md §2, with no additions.

✅ The explicit MVP exclusion of Credential History (§2 Relationships
note) is carried through as §7.5.

## 9.3 Against 04_Commands.md

✅ Every Command's Aggregate Interaction table (§4.1–§4.6 of
04_Commands.md) matches the Repository involvement described in §3.1
and §5.2 of this document.

✅ Invariant-005 (at most one Active Credential per Person) is
enforced as a storage-level constraint in §8.3.

## 9.4 Against 057_SmartCore_Tenancy_and_Ownership_Model.md

✅ §8's Registration Model and Command Model Coordination Exception are
realized concretely as the §5.1 Unit of Work — this document is the
persistence-layer implementation of what 057 §8 describes at the
architectural-principle level.

✅ Principle 5 ("Ownership SHALL remain independent from implementation
technology") is honored throughout §1.2's explicit non-goals.

## 9.5 Against 059_SmartCore_Identity_Platform.md §12

✅ All five minimum persistent entities (Persons, Organizations,
Memberships, Credentials, Sessions) are specified in §4.

✅ All five Supporting persistence structures are individually
classified in §7 (one internal-detail reclassification, one optional/
non-transactional, two explicitly deferred, one explicitly excluded)
— none left ambiguous.

## 9.6 Against ADR-0002_Identity_Foundation_Clarifications.md

✅ Decision 1 (Registration Boundary) and Decision 7 (Command Model
Coordination Exception) are both realized concretely in §5, with the
Scope Limitation explicitly carried through in §5.3.

---

# 10. MVP Readiness Checklist

Persistence Blueprint Version 1.0 is complete when:

✓ Every Aggregate Root has exactly one owning Repository, with no
  Repository writing another's Aggregate (§3)

✓ Every persistent entity field traces to 01_Domain_Model.md with no
  additions (§4)

✓ The Core Ownership Transaction is defined as a Unit of Work spanning
  exactly three Repositories, and is confirmed to be the only such
  exception in MVP (§5)

✓ Post-Commit Operations are confirmed to execute in separate
  transactions that cannot invalidate the already-committed Core
  Ownership Transaction (§6)

✓ Every Supporting Persistence Structure named in 059 §12 has an
  explicit MVP classification — required, internal detail, optional,
  deferred, or excluded (§7)

✓ Every uniqueness constraint required by an existing Domain Rule or
  Command Invariant is enforced at the storage level, not merely in
  application code (§8.3)

✓ No new Aggregates, Commands, Queries, Events, or Business Rules
  introduced

✓ Alignment verified against 03_Aggregates, 01_Domain_Model,
  04_Commands, 057, 059, and ADR-0002 (§9)

## 10.1 Verification Status Against 05_Queries.md and 02_Use_Cases.md (Resolved at v1.0.9)

The two items previously listed here as Pending Verification (v1.0.5–
v1.0.7) have been directly cross-checked against 05_Queries.md v1.0.2
and 02_Use_Cases.md v1.2.0, both now available, and are resolved:

✓ **`SessionRepository.GetActiveSessionsByPersonId`** (§3.1) —
  confirmed. 05_Queries.md §4.4 defines `GetSessionsForPerson`, whose
  stated Purpose ("Retrieve active Sessions belonging to a Person")
  matches this Repository method's Active-status filter. The Query's
  actual name differs from the `GetActiveSessionsForPerson` name this
  document speculatively anticipated in earlier versions; §3.1's
  citation has been corrected accordingly.

✓ **`MembershipRepository.GetByPersonId`** (§3.1) — confirmed.
  05_Queries.md §4.2 (`GetOrganizationsForPerson`) and §4.3
  (`GetMembershipsForPerson`) both exist as described and compose this
  method as a read projection with `OrganizationRepository`, per §3.2.

✓ **Bonus verification**: `MembershipRepository.GetByPersonIdAndOrganizationId`'s
  citation ("One membership per Person per Organization in MVP,"
  02_Use_Cases UC-008 Constraints) is confirmed verbatim at
  02_Use_Cases.md's UC-008 Constraints section.

No further Pending Verification items remain in this document as of
v1.0.9.

---

# 11. Change Log

## Version 1.0.9 (2026-07-15)

Ninth pass. First revision written with direct access to the actual
content of 02_Use_Cases.md, 04_Commands.md, and 05_Queries.md — until
now, several citations to these Required/Referenced Dependencies had
been written provisionally, without the ability to cross-check them
directly. No new Aggregates, Commands, Queries, Events, or business
rules introduced; no MVP scope changed.

- **§10.1 Pending Verification items resolved**: both items flagged in
  v1.0.5 are now confirmed against 05_Queries.md v1.0.2. §4.4's
  `GetSessionsForPerson` justifies `SessionRepository
  .GetActiveSessionsByPersonId` (§3.1) — this document's earlier guess
  at the Query's name, `GetActiveSessionsForPerson`, was incorrect and
  is now corrected. §4.2's `GetOrganizationsForPerson` and §4.3's
  `GetMembershipsForPerson` are confirmed to justify
  `MembershipRepository.GetByPersonId` as described since v1.0.0. Also
  noted: `GetSessionsForPerson`'s output explicitly excludes
  `AccessTokenId`/`RefreshTokenId` (05_Queries §4.4); this document's
  Repository method still returns the full Session entity, with the
  narrower projection applied at the Query layer, per §2.1.
  02_Use_Cases.md UC-008's Constraint citation was independently
  verified as accurate.
- **Correction — RefreshSession token rotation** (§5.2): this
  document's note previously stated `RefreshSession` rotates both
  `AccessTokenId` and `RefreshTokenId`. Direct cross-check against
  04_Commands.md §4.5 Postconditions shows this overstated what
  04_Commands.md actually guarantees: `AccessTokenId` reissuance is
  unconditional, but `RefreshTokenId` handling is explicitly left
  implementation-specific by 04_Commands.md itself. §5.2 is corrected
  to state only what 04_Commands.md commits to — this document does
  not independently resolve an ambiguity 04_Commands.md left open, as
  doing so would exceed a Persistence Blueprint's role relative to its
  upstream Command contract.

**Verification note**: this pass found no other discrepancy between
this document's existing citations and the actual content of
02_Use_Cases.md, 04_Commands.md, or 05_Queries.md — the RefreshSession
token-rotation overstatement above was the only correction required
beyond closing the two already-flagged Pending Verification items.

## Version 1.0.8 (2026-07-15)

Eighth clarification pass, following an eighth architecture review
(DDD, Repository Design, Persistence Model, Transaction Model, Future
Extensibility, AI Code Generation). No new Aggregates, Commands,
Queries, Events, or business rules introduced; no existing MVP scope,
schema field, or constraint changed.

- **§3 — `Update` semantics made explicit**: added a short statement
  clarifying that `Update` in the Repository Catalog denotes
  persisting an Aggregate's already-changed state, with business
  intent (which field, under what rule) already applied inside the
  Aggregate by the Command Handler before the Repository is called —
  per 03_Aggregates.md's Aggregate-owns-its-invariants boundary. This
  addresses review feedback that a single generic `Update` reads as
  undifferentiated CRUD, without adopting the alternative proposal of
  renaming it to `Save` (see "considered and not changed" below).
- **New §8.4.1 — Minimum requirement for optimistic concurrency**:
  §8.4 required *some* concurrency-control mechanism but never stated
  what a Version marker requires at minimum. §8.4.1 now states that if
  optimistic concurrency is used, each persistent entity SHALL carry
  *some* Version marker (a monotonic version number, a compare-and-
  swap timestamp, or a database-native row-version/ETag mechanism) —
  without naming a specific field, column, or mechanism, since that
  remains implementation-specific per §1.2. Also notes, non-bindingly,
  that a future Event-Sourced Aggregate would redefine "Version" as
  event-stream position rather than a row-level marker, deferred to
  03_Aggregates.md.
- **Change Log split**: entries for versions 1.0.0 through 1.0.4 are
  moved verbatim to a new archival file, `09_Persistence_CHANGELOG.md`
  — informative only, not a Dependency of this or any other Blueprint
  document. This section now retains versions 1.0.5 and later. This
  addresses review feedback that the Change Log had grown to roughly
  one-third of this document's total length by v1.0.7.

**Reviewer points considered and intentionally not changed:**
(1) renaming `Update` to `Save` — declined as a naming preference, not
a defect; the underlying concern (undifferentiated write path) is
addressed by this version's §3 clarification without changing the
method name or Repository contract. (2) An explicit multi-database /
Persistence-Provider pattern (e.g. for a future Postgres + Redis +
Elastic + S3 combination) — declined as scope creep against this
document's own, deliberate exclusion of physical storage technology
choices (§1.2); adding such a pattern here would reopen a boundary
this document has held consistently since v1.0.0. (3) A more explicit
`Repository → ReadModel → DTO` diagram for cross-Aggregate reads — not
treated as a defect, since §3.2 and §6.3 already state this document's
position (reads may cross Aggregates for projection purposes via the
Query layer; Repositories never gain a write dependency on each
other); no document change was made, as the existing wording already
covers the substance of this suggestion.

## Version 1.0.7 (2026-07-15)

Seventh clarification pass, following a seventh architecture review
focused specifically on Future Extensibility (scored across Repository
Evolution, Schema Evolution, Aggregate Evolution, and Future
Authentication Models). No new Aggregates, Commands, Queries, Events,
or business rules introduced; no existing MVP scope, schema field, or
constraint changed. Every addition below is either a non-binding
Forward Note or a generalization of a Forward Note pattern already
applied locally elsewhere in this document.

- **§3 — Repository interface vs. boundary**: added a Forward Note
  making explicit that the Write/Read Operations columns are a
  snapshot of what 14_MVP.md's Commands/Queries currently require, not
  a ceiling on what a Repository can architecturally support. Future
  Commands (e.g. `RenameOrganization`, `SuspendOrganization`) expand a
  Repository's method list without changing its ownership boundary
  (§2.1). This complements, rather than replaces, the v1.0.6 note
  specific to `OrganizationRepository`'s missing `Update`.
- **§4.4 — Session identity is a token-model detail, not a
  definition**: added a second Forward Note (alongside v1.0.6's
  telemetry-fields note) stating that `AccessTokenId`/`RefreshTokenId`
  represent Session identity under MVP's Password/token authentication
  model specifically. Future models — API keys, service accounts,
  OAuth-issued sessions, device-trust sessions, Passkey-originated
  logins — may represent session identity differently; such a change
  is a `01_Domain_Model.md`/`03_Aggregates.md` schema decision and
  would not by itself imply a Repository-boundary change.
- **§8.3 — Single-Active-Credential constraint scoped to MVP's
  single-Credential-Type model**: added a Forward Note stating this
  constraint is correct because MVP has exactly one Credential Type
  (Password), and would require explicit re-evaluation — not silent
  carry-over — if a future version introduces multiple concurrent
  Credential Types with independent lifecycles (e.g. Password + Passkey
  + TOTP simultaneously Active for one Person). This closes a gap in
  v1.0.6's Credential Types Forward Note (§4.5), which named future
  Credential Types but did not address their storage-constraint
  consequence.
- **§5.1.2 — Registration is not asserted to be the only future
  cross-Aggregate exception**: added a closing paragraph clarifying
  that §5.1 authorizes exactly one Unit-of-Work exception *for MVP*.
  This is not a claim that Registration is the only multi-Aggregate
  transaction this platform will ever require; any future exception
  requires its own ADR and its own revision to 03_Aggregates.md and
  this document, under the same justification standard ADR-0002
  Decision 7 already set. §5.3's Scope Limitation is stated to govern
  present and future exceptions identically.
- **New §7.0 — Promotion to Aggregate Boundary (general Rule)**: the
  promotion-condition reasoning already applied locally to RefreshToken
  in §7.1 (independent lifecycle rules / invariants / transactional-
  consistency requirements as the trigger for Aggregate-boundary
  re-evaluation) is generalized into a single named Rule stated once
  at the top of §7, so that a future Supporting Structure not yet
  listed in 059 §12 can be classified against the same standard without
  requiring a bespoke forward note to be authored for it first.

**Reviewer points considered and intentionally not changed:** none
raised in this round required a scope, field, or constraint change —
all five accepted points were additive, non-binding Forward Notes or
Rule generalizations, consistent with this document's stated
non-goals (§1.2) and MVP Boundary Protection (§2.2).

## Version 1.0.6 (2026-07-15)

Sixth clarification pass, following a sixth architecture review
focused specifically on Future Extensibility. No new Aggregates,
Commands, Queries, Events, or business rules introduced; no existing
MVP scope, schema field, or constraint changed — every addition in
this pass is a non-binding Forward Note.

- **§4.5 (Credential)**: added a Forward Note stating this document
  does not assume Credential is password-only forever; future
  Credential Types (Passkey, WebAuthn, TOTP, Recovery Code, OAuth
  Identity) are a `01_Domain_Model.md`/`03_Aggregates.md` decision this
  document would follow, not originate.
- **§4.4 (Session)**: added a Forward Note naming likely future
  telemetry/security attributes (`LastActivityAt`, `RevokedAt`,
  `RevokedBy`, `SessionType`, `ClientId`) and stating their addition
  would not alter Session's ownership boundary or Repository count.
- **§4.3 (Membership)**: added a Forward Note clarifying `Role` is a
  plain field, not a storage-level enum constrained to `Owner` — future
  roles extend its legal value set without a schema change here.
- **§7.1 (RefreshToken)**: strengthened the existing forward note to
  name the concrete deciding factor for a future Aggregate-boundary
  re-evaluation (new independent invariants/lifecycle vs. simply more
  fields on the existing Session record), without taking a position on
  the outcome.
- **New §5.1.2 (Registration Unit of Work)**: added an explicit
  Transaction Creep guard — future registration-adjacent capabilities
  (Invitation, Organization Template, Initial Settings, Billing
  Account, etc.) do not automatically join the §5.1 Unit of Work;
  each requires its own explicit review against ADR-0002 Decision 7 /
  057 §8 when proposed.
- **§3.1 (Repository read methods)**: promoted the existing implicit
  pattern ("this document does not speculatively add query capability")
  to an explicit standing Rule, so future lookup methods (e.g. by
  Username, PhoneNumber, ExternalIdentity) are understood to require
  the same Command/Query citation discipline as the current catalog.
- **§3 (`OrganizationRepository`)**: added a note explaining the
  absence of an `Update` method is a direct consequence of 14_MVP.md
  defining no Organization-modifying Command, and flagging this
  Repository as the most likely first surface to change in a future
  version — closing an open point from review rather than leaving it
  implicit.

**Reviewer points considered and intentionally not changed:** the
Session `Authenticated` vs. `Active` Status-semantics question was
raised again across both review inputs for this pass. Per v1.0.4's
Change Log, this remains a 01_Domain_Model.md definition question that
§4.4 correctly mirrors without redefining; no new information in this
round changes that assessment, so no document change was made for it.

## Version 1.0.5 (2026-07-15)

Fifth clarification pass, following a fifth architecture review of
v1.0.4. No new Aggregates, Commands, Queries, Events, or business rules
introduced.

- **Transaction ownership made explicit** (new §5.1.1): the most
  substantive gap identified in this round. The document previously
  described the Core Ownership Transaction as "one Unit of Work
  comprising exactly three Repository writes" without stating who
  owns the transaction boundary. An implementation that called
  `PersonRepository.Create`, `OrganizationRepository.Create`, and
  `MembershipRepository.Create` each inside its own independently-
  committed transaction would satisfy the old wording literally while
  completely breaking the atomicity ADR-0002 Decision 1 requires — a
  failure on the second or third write could no longer roll back the
  first. §5.1.1 now states normatively that the Unit of Work owns the
  transaction and Repositories participate in a shared one; the
  specific technical propagation mechanism remains implementation-
  specific per §1.2.
- **Softened an unwarranted future commitment** (§7.5): "none is
  planned to be derived from the Replaced rows" → "no such capability
  is defined for MVP," since a Persistence Blueprint should not make
  claims about future product roadmap (password reuse prevention,
  security audit, credential age policy are all plausible future
  needs that would read the same `Replaced` rows this document already
  keeps).
- **Added §10.1 Pending Verification**: explicitly lists the two
  Repository methods (`GetActiveSessionsByPersonId`,
  `MembershipRepository.GetByPersonId`) whose justification depends on
  05_Queries.md content this document could not directly verify, with
  concrete pass/fail criteria for final review before Freeze — rather
  than leaving that dependency implicit in prose.

**Reviewer points considered and intentionally not changed:** the
Credential-as-independent-Aggregate question and the
Authenticated-vs-Active Session Status distinction were both raised
again this round; the reviewer's own review correctly identifies both
as upstream Domain Model / 03_Aggregates concerns that this document
appropriately reflects without re-deciding, consistent with v1.0.4's
Change Log. No further action taken on either in this document.

## Earlier Versions (1.0.0–1.0.4)

Change Log entries for versions 1.0.0 through 1.0.4 have been moved to
`09_Persistence_CHANGELOG.md` (split out at v1.0.8, per the entry
above, to keep this section readable — it had grown to roughly
one-third of this document's length by v1.0.7). That file is archival
only and is not a Dependency of this document or any other Blueprint
document; nothing in §1–§10 above relies on it.

---

**END OF DOCUMENT**