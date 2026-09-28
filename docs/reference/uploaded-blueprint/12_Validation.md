<!--
Document ID: ID-12
Title: SmartCore Identity Platform Blueprint - Validation
Version: 1.0.8
Status: READY_FOR_GENERATION

Purpose:
Consolidates the correctness rules already established across the
Identity Platform Blueprint set — Input Validation, Business
Validation, Cross-field Validation, and Cross-Aggregate Validation —
into the single authoritative location required by
064_SmartCore_Blueprint_Standard.md §8.13. Distinguishes Domain Rules,
Policy Rules, and Infrastructure Constraints per that same section.
This document introduces no new validation rule; it organizes and
cross-references rules already normative in 01_Domain_Model.md,
02_Use_Cases.md, 03_Aggregates.md, 04_Commands.md, 05_Queries.md,
09_Persistence.md, and 11_Security.md.

Dependencies (Required):
- 00_Overview.md
- 01_Domain_Model.md
- 02_Use_Cases.md
- 03_Aggregates.md
- 04_Commands.md
- 05_Queries.md
- 09_Persistence.md
- 11_Security.md
- 057_SmartCore_Tenancy_and_Ownership_Model.md
- 059_SmartCore_Identity_Platform.md
- 064_SmartCore_Blueprint_Standard.md
- ADR-0002_Identity_Foundation_Clarifications.md
- ADR-0003_Organization_and_Membership_Lifecycle_Standardization.md

Explicitly NOT a Dependency:
- 07_Contracts.md
- 08_API.md
- 10_Configuration.md
Validation defines *that* a rule holds and *which layer* enforces it;
it does not define wire-format error codes (07/08) or configurable
threshold values (10). See §1.2.

Change Log:
  - Version 1.0.8 (2026-07-18): Correction pass — reworded §9.3's
    Domain Model alignment claim, which read as if every Domain Rule
    classification in this document originates from 01_Domain_Model.md;
    now scoped to "where applicable" with an explicit pointer to the
    Email-uniqueness finding (v1.0.5) as the reason for the
    qualification. Governance note: reviewer flagged that this
    document's Change Log (§11) is now roughly a third of the
    document's length and recommended trimming to the last 3-5 versions
    or moving full history to a separate Revision History document
    after Freeze — this is recorded as a post-Freeze housekeeping
    recommendation, not applied in this pass, since full traceability
    remains valuable until Freeze is declared. No new Aggregates,
    Commands, Queries, Events, or business rules introduced. See §11
    for full detail. This review found no remaining Blocker or Major
    issues and assessed the document as READY_FOR_FREEZE pending this
    correction.
  - Version 1.0.7 (2026-07-18): Correction pass — fixed §2.4's stale
    reference to a "row lists both Domain Rule + Infrastructure
    Constraint" pattern, which no longer exists in any table since
    v1.0.3's row-split fix; now references the actual current pattern
    (paired 5a/5b rows, §5.1's two columns). Renamed §5.1/§5.2's
    "Normative Source" column to "Application-Level Rule Source" —
    "Normative" was ambiguous given "Infrastructure Enforcement" is
    also normative; the new name states precisely what the column
    identifies (the application-layer/Domain Rule checkpoint's source).
    Six additional observations from this review (Query Validation
    Implicit/Explicit wording, Temporary/Infrastructure Failure
    phrasing, §9.3's Domain Rules generality, Change Log length,
    Membership Aggregate independence, §8 ordering rationale) were
    reviewed and explicitly not applied — the reviewer identified them
    as non-blocking for Freeze and did not recommend acting on them in
    this pass. No new Aggregates, Commands, Queries, Events, or
    business rules introduced. See §11 for full detail.
  - Version 1.0.6 (2026-07-18): Correction pass — renamed the §5.1/§5.2
    "Domain Rule Source" column to "Normative Source": every row in
    both tables actually cites 04_Commands, 02_Use_Cases, 09_Persistence,
    ADR-0002, or 057 — none cites 01_Domain_Model.md — so the old name
    wrongly implied a Domain-document source. Refined §5.1's opening
    definition to distinguish global uniqueness (Email, AccessTokenId,
    RefreshTokenId — no partition key) from partition-scoped uniqueness
    (Membership per (PersonId, OrganizationId); Credential per PersonId
    among Active instances), since "same Aggregate type" alone did not
    capture that distinction. No new Aggregates, Commands, Queries,
    Events, or business rules introduced. See §11 for full detail.
  - Version 1.0.5 (2026-07-18): Correction pass — with 01_Domain_Model.md
    and 02_Use_Cases.md now available for direct verification, found
    that §5.1's "Email unique across all Persons" row cited
    "01_Domain_Model §6 (Rule-001 equivalent)" as a source, but
    01_Domain_Model.md v1.1.0 does not state Email uniqueness anywhere
    (checked §6 Domain Invariants, §9 Domain Rules, and the EmailAddress
    Value Object section) — Rule-001 is actually about Organization
    membership, unrelated to Email. Removed the unverifiable Domain
    Model citation; the row now cites only 04_Commands §4.1/§4.3 and
    09_Persistence §8.3, which do state it. This also resolves the
    "Email vs. Membership uniqueness sourcing inconsistency" item left
    open in v1.0.4: both rows now correctly cite only their actual
    source documents, with no Domain Model claim for either. Verified
    the three other 01_Domain_Model.md citations added in prior
    versions (§6.1 GetCurrentPerson, GetOrganizationsForPerson,
    GetMembershipsForPerson rows) against the actual document — all
    three are accurate. Note: 09_Persistence.md §8.3 carries the same
    inaccurate "01_Domain_Model §6 Rule-001 equivalent" citation for
    Email uniqueness; this is out of this document's scope to correct
    but is flagged for a separate correction pass on 09_Persistence.md.
    No new Aggregates, Commands, Queries, Events, or business rules
    introduced. See §11 for full detail.
  - Version 1.0.4 (2026-07-18): Correction pass — resolved the
    "Precondition" taxonomy gap flagged as open in v1.0.3: all eight
    rows across §4.1, §4.3, §4.4, and §4.6 reclassified from bare
    "Precondition" to "Business (Precondition)," bringing every
    Category value into 064 §8.13's four required categories; §7.1's
    Not-Found Error Source Validation Category updated to match. Added
    a proper document citation to §5.1's AccessTokenId uniqueness row,
    which previously gave a rationale ("Required for unambiguous
    resolution") instead of a source reference. No new Aggregates,
    Commands, Queries, Events, or business rules introduced. See §11
    for full detail.
  - Version 1.0.3 (2026-07-18): Correction pass — split every table
    row that combined two Classifications into one cell (§4.1, §4.3,
    §4.6) into two single-classification rows, satisfying §2's
    "exactly one classification per checkpoint" rule at the table
    level, not just in prose; fixed an undefined "Cross-Instance
    Lookup" Category value in §4.5 (reclassified as Business); made
    all Cross-Aggregate subtype labels consistently formatted as
    "Cross-Aggregate (<subtype>)" instead of bare subtype names;
    added an explicit Source Validation Category column to §7.1's
    Error Categories table; flagged (but did not silently resolve) that
    "Precondition," used as a Category value in several §4 rows, is not
    one of 064 §8.13's four required categories. No new Aggregates,
    Commands, Queries, Events, or business rules introduced. See §11
    for full detail.
  - Version 1.0.2 (2026-07-18): Correction pass — resolved an internal
    contradiction where §4.2 denied Cross-Aggregate Validation applies
    to Person→Credential resolution while §5.2 classified the identical
    rule as True Cross-Aggregate Validation; §4.2 now aligns with §5.2
    and re-tags its rows 4-5 accordingly. Grounded §6.1's "every
    registered Person has at least one Organization/Membership"
    Existence Validation claims in 057 §5 Principle 1 / §12's governance
    invariant (rather than an unqualified permanent fact), and flagged
    that a future Membership Revocation ADR (ADR-0003 §2, currently
    future scope) would need to explicitly preserve this invariant. No
    new Aggregates, Commands, Queries, Events, or business rules
    introduced. See §11 for full detail.
  - Version 1.0.1 (2026-07-18): Correction pass — clarified that
    classification (§2) applies per enforcement checkpoint rather than
    per Business Requirement, resolving an internal inconsistency for
    multi-layer rules; fixed a broken cross-reference (§2.3 → §8.1);
    reframed §8 as non-binding "Recommended Validation Processing
    Order" guidance rather than a mandated sequence; expanded §6 Query
    Validation with explicit Authentication/Ownership/Existence columns;
    added an explicit Cross-field Validation scope note (§4); noted
    Policy Rule values remain implementation-defined pending
    10_Configuration.md (§1.2). See §11 for full detail. No new
    Aggregates, Commands, Queries, Events, or business rules introduced.
  - Version 1.0.0 (2026-07-18): Initial Validation Blueprint. Classifies
    every existing correctness rule from 01, 02, 03, 04, 05, 09, and 11
    into Input / Business / Cross-field / Cross-Aggregate categories
    (064 §8.13), and into Domain Rule / Policy Rule / Infrastructure
    Constraint classification (064 §8.13's "distinguish" requirement).
    Establishes a canonical six-step Validation Order applicable across
    all six Public Commands. No new Aggregates, Commands, Queries,
    Events, or business rules introduced.
-->

# 1. Overview

This document defines the **Validation Contract** of the Identity
Platform: which correctness rules exist, at what layer each is
checked, and in what order checks execute.

This document answers:

> "What makes a given Command or Query input correct, and where —
> application logic or storage — is that correctness actually
> guaranteed?"

## 1.1 What This Document Defines

- A three-way classification framework — Domain Rule, Policy Rule,
  Infrastructure Constraint — applied consistently to every rule below
  (§2)
- Input Validation: field-level correctness rules for every Command
  input (§3)
- Business Validation, organized per-Command (§4)
- Cross-field Validation: rules that depend on the relationship between
  two or more fields on the same request (§4)
- Cross-Aggregate Validation: rules that require reading beyond the
  single Aggregate instance under validation, split into Cross-Instance
  Uniqueness (same Aggregate type) and True Cross-Aggregate validation
  (different Aggregate types) (§5)
- Query-side validation (§6)
- Error classification principles (§7) — not wire-format error codes
- A recommended Validation Processing Order applicable to every Command,
  with the two constraints that are actually binding named explicitly
  (§8)

## 1.2 What This Document Does NOT Define

- **Exact threshold values** (minimum password length, DisplayName
  character limits, throttling thresholds) — these are Policy Rules
  whose *existence* is stated here but whose *value* is owned by
  10_Configuration.md (11_Security.md §12; 064 §8.11: "Configuration
  SHALL NOT redefine domain rules" — the inverse also holds: this
  document SHALL NOT freeze a Configuration-owned value). **Until
  10_Configuration.md exists as an accepted Blueprint document, every
  Policy Rule threshold referenced in this document remains
  implementation-defined.** This document's Policy Rule classification
  states only that a check SHALL exist and SHALL execute at the
  Business Validation step (§8); it does not depend on
  10_Configuration.md's existence to be internally complete.
- **Wire-format error codes, HTTP status codes, or response body shape**
  — these belong to 07_Contracts.md / 08_API.md, consistent with how
  06_Domain_Events.md and 09_Persistence.md already exclude those two
  documents from their own dependency sets
- **The storage mechanism** that enforces a constraint (unique index,
  serializable isolation, optimistic concurrency marker) — defined in
  09_Persistence.md §8; this document states *that* a constraint SHALL
  hold under concurrent access and *at which layer* it is
  authoritative, not the physical mechanism

This document introduces no new:

- Aggregates
- Commands
- Queries
- Events
- Business Rules
- Validation Rules beyond what 01, 02, 03, 04, 05, 09, and 11 already
  state

---

# 2. Validation Classification Framework

Per 064 §8.13 ("Validation rules SHALL distinguish between: Domain
Rules, Policy Rules, Infrastructure Constraints"), this document
classifies at the level of the **enforcement checkpoint** — a single
place, at a single layer, where a condition is actually checked. Every
enforcement checkpoint in this document is tagged with exactly one of
the three classifications below. A **Business Requirement** (the
underlying fact that must hold, e.g. "Email SHALL be unique across all
Persons") is a separate, higher-level concept from a checkpoint: one
Business Requirement MAY be realized by more than one checkpoint, each
independently classified (§2.4). The "exactly one classification" rule
applies to each checkpoint, never to the Business Requirement as a
whole.

## 2.1 Domain Rule

A correctness condition, checked at the application/business-logic
layer, that is intrinsic to the Identity domain and holds regardless of
deployment, configuration, or environment. A Domain Rule cannot be
relaxed by configuration (11_Security §12: "Configuration SHALL NOT
weaken mandatory security invariants").

Examples: PersonId immutability, the application-layer check that
rejects a duplicate Email before attempting a write, at-most-one-Active-
Credential-per-Person, Membership uniqueness per (Person, Organization).

## 2.2 Policy Rule

A correctness condition whose *existence* is mandated by this Blueprint
but whose *specific value* is externally configurable and owned by
10_Configuration.md. A Policy Rule's threshold MAY differ across
deployments without violating this Blueprint.

Examples: minimum password strength, DisplayName length bounds, Session
expiration duration, authentication-attempt throttling thresholds.

## 2.3 Infrastructure Constraint

A correctness condition enforced by the persistence or transport layer
rather than application business logic — necessary because
application-level checks alone cannot prevent a race condition under
concurrent requests (09_Persistence.md §8.3).

Examples: the storage-level uniqueness index on Person.Email, optimistic
concurrency version marker (09_Persistence §8.4.1), TLS-encrypted
transport (11_Security §6.1).

## 2.4 Business Requirements Enforced at Multiple Layers

Several Business Requirements in this document — most visibly the
Cross-Instance Uniqueness rules in §5.1 — are realized by **two
independent checkpoints**: a Domain Rule checkpoint at the application
layer (fast, pre-write rejection) and an Infrastructure Constraint
checkpoint at the storage layer (the race-condition-safe authority).
Where one Business Requirement is represented by two checkpoints — for
example, the paired `5a`/`5b` rows in §4.1, §4.3, and §4.6, or the two
enforcement columns in §5.1 — this denotes two separate checkpoints for
one Business Requirement, not one checkpoint carrying two
classifications. §8.1 explains why both checkpoints are required and
are not duplicative.

Per §4 and §5.1, every multi-layer Business Requirement is represented
as **two separate table rows** (or two separate columns, in §5.1's
case) — one row per checkpoint, each with exactly one classification.
No table row in this document displays more than one classification;
the "checkpoint 1 of 2 / checkpoint 2 of 2 (§2.4)" annotation in the
Source column is what links the two rows back to the single underlying
Business Requirement.

---

# 3. Input Validation

Input Validation checks the format, presence, and type of a single
field, independent of any other field or any stored state.

| Field | Rule | Classification | Source |
|---|---|---|---|
| Email | Required (RegisterPerson, AuthenticatePerson) | Domain Rule | 04_Commands §4.1, §4.2 |
| Email | Valid format | Domain Rule | 01_Domain_Model §3 (EmailAddress VO) |
| Email | Case-insensitive comparison | Domain Rule | 01_Domain_Model §3 |
| Email | Normalized before storage/comparison | Domain Rule | 057 §8 (registration flow); 04_Commands §4.1, §4.3 |
| Password (registration) | Required | Domain Rule | 04_Commands §4.1 |
| Password (registration) | SHALL satisfy credential strength requirements | Policy Rule (threshold owned by 10_Configuration) | 04_Commands §4.1; 11_Security §12 |
| Password (authentication) | Required | Domain Rule | 04_Commands §4.2 |
| CurrentPassword (ChangePassword) | Required | Domain Rule | 04_Commands §4.6 |
| NewPassword (ChangePassword) | Required | Domain Rule | 04_Commands §4.6 |
| NewPassword (ChangePassword) | SHALL satisfy credential strength requirements | Policy Rule (threshold owned by 10_Configuration) | 04_Commands §4.6 |
| DisplayName | Required (RegisterPerson) | Domain Rule | 04_Commands §4.1 |
| DisplayName | SHALL satisfy length and format constraints | Policy Rule (bounds owned by 10_Configuration) | 04_Commands §4.1, §4.3 |
| SessionId / AccessTokenId (LogoutSession identifier) | One of the two SHALL be provided | Domain Rule | 04_Commands §4.4 |
| RefreshTokenId (RefreshSession) | Required | Domain Rule | 04_Commands §4.5 |
| DeviceInfo (AuthenticatePerson) | Optional | Domain Rule | 04_Commands §4.2 |
| PersonId (GetPersonById) | Required | Domain Rule | 05_Queries §5.1 |

**Note on Password/DisplayName Policy Rules**: this document confirms
that a strength/length check SHALL exist and SHALL be evaluated before
any write; it does not state a minimum length or a disallowed-character
set, since fixing those values here would violate 10_Configuration.md's
exclusive ownership of Policy Rule thresholds (11_Security §12; 064
§8.11).

---

# 4. Business, Cross-field, and Cross-Aggregate Validation Per Command

Each subsection below restates that Command's rules from 04_Commands.md
§4, organized into the four categories 064 §8.13 requires, plus the
Domain/Policy/Infrastructure classification from §2. This is a
re-organization for validation-layer clarity, not a new rule set —
every rule below already exists in 04_Commands.md, 02_Use_Cases.md, or
09_Persistence.md.

**Resolved taxonomy item — "Precondition" reclassified into 064
§8.13's four required categories**: rows previously tagged bare
"Precondition" (inherited from 04_Commands.md's own Preconditions →
Validation Rules → Aggregate Interaction → Failure Conditions
structure, §8) now read "Business (Precondition)" throughout §4.1,
§4.3, §4.4, and §4.6. This keeps the top-level Category value within
064 §8.13's mandated set (Input, Business, Cross-field, Cross-Aggregate)
while preserving, as a parenthetical, the same descriptive distinction
this document already uses for Cross-Aggregate subtypes (e.g.
"Cross-Aggregate (Session ↔ Person)"). The classification is Business
because Preconditions are checked at the application/business-logic
layer per §2.1, consistent with every other Business-category row.

**Note on Cross-field Validation scope**: MVP intentionally defines
very few Cross-field Validation rules. The only genuine instance in
this Blueprint set is UpdatePersonProfile's "at least one mutable
attribute SHALL be provided" (§4.3, row 3) — a constraint between two
fields (Email, DisplayName) on the same request. No Command in
04_Commands.md defines a rule comparing two field *values* against
each other (e.g. a NewPassword-vs-CurrentPassword equality/inequality
check is never stated in 04_Commands.md §4.6 and is therefore not
listed below — inventing one here would violate 064 §6.5's Explicit
Scope principle). This document does not fill that gap; it reports it
as a scope observation, not a defect requiring correction before
Freeze.

## 4.1 RegisterPerson

| Step | Rule | Category | Classification | Source |
|---|---|---|---|---|
| 1 | No existing Person exists with the provided email | Business (Precondition) / Cross-Aggregate (Cross-Instance Uniqueness) | Domain Rule | 04_Commands §4.1 Preconditions; UC-001 |
| 2 | Email required, valid, normalized | Input | Domain Rule | §3 above |
| 3 | Password satisfies strength requirements | Business | Policy Rule | §3 above |
| 4 | DisplayName satisfies length/format | Business | Policy Rule | §3 above |
| 5a | Email uniqueness — application-level pre-write check | Cross-Aggregate (Cross-Instance Uniqueness) | Domain Rule | 09_Persistence §8.3 (checkpoint 1 of 2 — §2.4) |
| 5b | Email uniqueness — storage-level constraint (authoritative under concurrency) | Cross-Aggregate (Cross-Instance Uniqueness) | Infrastructure Constraint | 09_Persistence §8.3 (checkpoint 2 of 2 — §2.4) |
| 6 | Person + Organization + Membership creation is atomic; partial ownership prohibited | Cross-Aggregate | Domain Rule | ADR-0002 Decision 1; 057 §8 |

No Cross-field Validation applies — RegisterPerson's three inputs
(Email, Password, DisplayName) do not constrain one another.

## 4.2 AuthenticatePerson

| Step | Rule | Category | Classification | Source |
|---|---|---|---|---|
| 1 | Email required | Input | Domain Rule | §3 above |
| 2 | Password required | Input | Domain Rule | §3 above |
| 3 | Person exists, resolved by Email | Business | Domain Rule | 04_Commands §4.2 Preconditions |
| 4 | Active Credential exists for Person | Cross-Aggregate (Person ↔ Credential) | Domain Rule | 04_Commands §4.2 Preconditions |
| 5 | Password validates against stored PasswordHash | Cross-Aggregate (Person ↔ Credential) | Domain Rule | 04_Commands §4.2 |
| 6 | Person-not-found, no-active-Credential, and password-mismatch SHALL produce the same outward failure response | Business (Policy-driven ordering) | Policy Rule (uniform-response is a Security requirement, not a Domain invariant) | 11_Security §3.1, §11.2 (User Enumeration) |

No Cross-field Validation applies. Steps 4–5 above ARE True
Cross-Aggregate Validation (Person ↔ Credential — consolidated in
§5.2), consistent with the Overview's read-based definition of
Cross-Aggregate Validation (§1.1): resolving and validating against the
Credential requires reading beyond the Person Aggregate instance under
validation. Per §5.2's Scope Note, this is a read-time check rather
than a multi-Aggregate write, so it requires no transactional exception
under ADR-0002 Decision 7 / 057 §8 — those govern coordinated writes,
not reads.

## 4.3 UpdatePersonProfile

| Step | Rule | Category | Classification | Source |
|---|---|---|---|---|
| 1 | Person exists with status = Active | Business (Precondition) | Domain Rule | 04_Commands §4.3 Preconditions |
| 2 | Authenticated Session exists for the Person | Business (Precondition) | Domain Rule | 04_Commands §4.3 Preconditions |
| 3 | At least one mutable attribute (Email or DisplayName) SHALL be provided | Cross-field | Domain Rule | 04_Commands §4.3 Input |
| 4 | If Email provided: valid, normalized | Input | Domain Rule | §3 above |
| 5 | If DisplayName provided: length/format | Business | Policy Rule | §3 above |
| 6 | Authenticated Session SHALL belong to the Person being updated | Cross-Aggregate (Session ↔ Person) | Domain Rule | 04_Commands §4.3 Validation Rules; 11_Security §4.3 |
| 7a | New Email uniqueness — application-level pre-write check | Cross-Aggregate (Cross-Instance Uniqueness) | Domain Rule | 04_Commands §4.3; 09_Persistence §8.3 (checkpoint 1 of 2 — §2.4) |
| 7b | New Email uniqueness — storage-level constraint (authoritative under concurrency) | Cross-Aggregate (Cross-Instance Uniqueness) | Infrastructure Constraint | 04_Commands §4.3; 09_Persistence §8.3 (checkpoint 2 of 2 — §2.4) |

## 4.4 LogoutSession

| Step | Rule | Category | Classification | Source |
|---|---|---|---|---|
| 1 | Session exists | Business (Precondition) | Domain Rule | 04_Commands §4.4 Preconditions |
| 2 | Session is not already Closed | Business (Precondition) | Domain Rule | 04_Commands §4.4 Preconditions |
| 3 | Session SHALL belong to requesting Person | Cross-Aggregate (Session ↔ Person) | Domain Rule | 04_Commands §4.4 Validation Rules |
| 4 | Session SHALL be eligible for closure | Business | Domain Rule | 04_Commands §4.4 |

No Cross-field Validation applies — LogoutSession accepts a single
logical identifier (SessionId or AccessTokenId).

## 4.5 RefreshSession

| Step | Rule | Category | Classification | Source |
|---|---|---|---|---|
| 1 | RefreshTokenId SHALL belong to an existing Session | Business | Domain Rule | 04_Commands §4.5 Validation Rules |
| 2 | RefreshTokenId SHALL NOT be expired (the check itself is mandatory; the expiry *duration* it compares against is a Policy Rule per §2.2, owned by 10_Configuration) | Business | Domain Rule | 04_Commands §4.5; 11_Security §8.4 |
| 3 | RefreshTokenId SHALL NOT be revoked | Business | Domain Rule | 04_Commands §4.5 |
| 4 | Session ownership/permissions verified | Cross-Aggregate (Session ↔ Person) | Domain Rule | UC-009 Main Flow step 3 |

## 4.6 ChangePassword

| Step | Rule | Category | Classification | Source |
|---|---|---|---|---|
| 1 | Person exists with status = Active | Business (Precondition) | Domain Rule | 04_Commands §4.6 Preconditions |
| 2 | Active Credential exists | Business (Precondition) | Domain Rule | 04_Commands §4.6 Preconditions |
| 3 | Authenticated Session exists for Person | Business (Precondition) | Domain Rule | 04_Commands §4.6 Preconditions |
| 4 | Authenticated Session SHALL belong to the Person | Cross-Aggregate (Session ↔ Person) | Domain Rule | 04_Commands §4.6 Validation Rules |
| 5 | CurrentPassword SHALL validate against existing Credential PasswordHash | Business | Domain Rule | 04_Commands §4.6 |
| 6 | NewPassword SHALL satisfy credential strength requirements | Business | Policy Rule | 04_Commands §4.6; 11_Security §12 |
| 7a | Replacement preserves "at most one Active Credential per Person" — application-level pre-write check | Cross-Aggregate (Cross-Instance Uniqueness) | Domain Rule | 04_Commands Invariant-005; 09_Persistence §8.3 (checkpoint 1 of 2 — §2.4) |
| 7b | Replacement preserves "at most one Active Credential per Person" — storage-level constraint (authoritative under concurrency) | Cross-Aggregate (Cross-Instance Uniqueness) | Infrastructure Constraint | 04_Commands Invariant-005; 09_Persistence §8.3 (checkpoint 2 of 2 — §2.4) |

---

# 5. Cross-Aggregate Validation (Consolidated)

This section consolidates every validation rule from §4 that requires
reading beyond the single Aggregate instance being written, split per
064 §8.13's distinct category from Business Validation.

## 5.1 Cross-Instance Uniqueness (Same Aggregate Type)

Validation against other instances of the *same* Aggregate type. This
is not a transactional cross-Aggregate-type concern (09_Persistence
§8.1 — intra-Aggregate consistency still holds per-write) but does
require a read beyond the single instance under validation. Two
scoping patterns both fall under this same category: **global**
uniqueness, checked against every instance of the Aggregate type with
no partition key (Email across all Persons; AccessTokenId and
RefreshTokenId across all Sessions), and **partition-scoped**
uniqueness, checked against the subset of instances sharing a foreign
key or status filter ((PersonId, OrganizationId) uniqueness among
Membership instances; "at most one Active Credential" among Credential
instances sharing a PersonId). Both patterns require reading beyond the
single instance under validation within the same Aggregate collection
— the difference is the scope of that read, not the category. Every
rule below is a Business Requirement enforced at two layers per §2.4 —
the "Application-Level Rule Source" and "Infrastructure Enforcement"
columns name the two separate checkpoints, not a dual classification of
one checkpoint. "Application-Level Rule Source" cites whichever
upstream document
establishes the application-layer (Domain Rule) checkpoint — this is
not necessarily 01_Domain_Model.md specifically; none of the rows below
happen to be stated there (verified in v1.0.5's Change Log), and citing
them as "Domain Rule Source" would have wrongly implied otherwise.

| Rule | Aggregate | Application-Level Rule Source | Infrastructure Enforcement |
|---|---|---|---|
| Email unique across all Persons | Person | 04_Commands §4.1, §4.3 (01_Domain_Model.md does not state this as a Domain Invariant or Domain Rule — verified against §6, §9, and the EmailAddress Value Object section; no Domain Model citation is claimed) | 09_Persistence §8.3 (storage-level unique constraint) |
| (PersonId, OrganizationId) unique per Membership | Membership | UC-008 Constraints: "One membership per Person per Organization in MVP" | 09_Persistence §8.3 |
| At most one Credential with Status = Active per PersonId | Credential | 04_Commands Invariant-005 | 09_Persistence §8.3 |
| AccessTokenId unique across all Sessions | Session | 09_Persistence §8.3 (Required for `SessionRepository.GetByAccessTokenId` to resolve unambiguously) | 09_Persistence §8.3 |
| RefreshTokenId unique across all Sessions | Session | 04_Commands §4.5 | 09_Persistence §8.3 |

## 5.2 True Cross-Aggregate Validation (Different Aggregate Types)

| Rule | Aggregates Involved | Application-Level Rule Source |
|---|---|---|
| Authenticated Session SHALL belong to the Person being acted upon | Session ↔ Person | 04_Commands §4.3 (UpdatePersonProfile), §4.4 (LogoutSession), §4.6 (ChangePassword); 11_Security §4.3 |
| Registration ownership triple (Person + Organization + Membership) SHALL be created atomically or not at all | Person ↔ Organization ↔ Membership | ADR-0002 Decision 1; 057 §8; 09_Persistence §5.1 |
| Password validates against the Credential belonging to the resolved Person | Person ↔ Credential | 04_Commands §4.2, §4.6 |

**Scope note**: per ADR-0002 Decision 7 and 057 §8, only the
Registration ownership triple (row 2 above) is realized as a single
atomic transaction spanning multiple Repositories (09_Persistence
§5.1). Every other True Cross-Aggregate Validation rule above is a
*read-time check* (verify a relationship already holds), not a
multi-Aggregate *write* — it therefore requires no transactional
exception under ADR-0002 Decision 7 / 057 §8, which govern coordinated
writes, not reads.

---

# 6. Query Validation

Queries (05_Queries.md) do not modify state and therefore have no
Business or Cross-field Validation in the Command sense. This section
states, per Query, what is checked under each of three read-time
concerns — Authentication Validation (is the caller who they claim to
be?), Ownership Validation (does the caller have a right to see this
specific data?), and Existence Validation (does the requested record
exist?) — mirroring the depth already given to Commands in §4.

**Note on 05_Queries.md's own terminology**: the User Interaction
Queries below use "Identity Data Ownership Scope" (05_Queries §2.4) for
what this table's Ownership Validation column states — an intrinsic
identity-resolution rule, not Capability Authorization, consistent with
the same distinction 11_Security §4.3 draws for Command-side
Session-ownership checks (§5.2 above).

## 6.1 User Interaction Queries

| Query | Authentication Validation | Ownership Validation | Existence Validation | Source |
|---|---|---|---|---|
| GetCurrentPerson | Valid, non-expired Session SHALL exist for the caller (Domain Rule) | N/A — caller IS the Person resolved from their own Session, by construction | Implicit — a valid Session always resolves to an existing Person (01_Domain_Model §6: identity survives Session expiration, so a Session can only reference a Person that exists) | 05_Queries §4.1 |
| GetOrganizationsForPerson | Valid, non-expired Session SHALL exist for the caller (Domain Rule) | Caller MAY only retrieve Organizations reached through their own Membership (Domain Rule) | Implicit — guaranteed for the governance lifetime of this Blueprint by 057 §5 Principle 1 ("Every Person SHALL belong to at least one Organization") together with 057 §12 (breaking changes to ownership boundaries are prohibited within Version 1.x); this is a platform-wide invariant, not an MVP-only convenience assumption (01_Domain_Model §9 Rule-001 restates the same invariant). Not automatically guaranteed beyond Version 1.x if a future ADR introduces Membership Revocation (ADR-0003 §2, currently future scope) — such an ADR would need to explicitly preserve or re-evaluate this invariant. | 05_Queries §4.2, §2.4; 057 §5, §12 |
| GetMembershipsForPerson | Valid, non-expired Session SHALL exist for the caller (Domain Rule) | Caller MAY only retrieve their own Memberships (Domain Rule) | Implicit — same 057 §5 / §12 ownership invariant as above, since Membership is the mechanism realizing Organization participation (01_Domain_Model §6 restates it). Same future-scope caveat: not automatically guaranteed once Membership Revocation (ADR-0003 §2) moves out of future scope. | 05_Queries §4.3, §2.4; 057 §5, §12 |
| GetSessionsForPerson | Valid, non-expired Session SHALL exist for the caller (Domain Rule) | Caller MAY only retrieve their own Sessions (Domain Rule) | Not applicable as a failure mode — an authenticated caller always has at least their own current Session | 05_Queries §4.4, §2.4 |

## 6.2 Platform Integration Queries

| Query | Authentication Validation | Ownership Validation | Existence Validation | Source |
|---|---|---|---|---|
| GetPersonById | Caller SHALL be a recognized SmartCore Capability Platform — a Transport/infrastructure-layer check (Infrastructure Constraint), not a business-authorization rule (05_Queries §5.1) | Not applicable — this Query is platform-to-platform, not a Person's own data access; no Identity Data Ownership Scope rule applies | PersonId SHALL be provided (Domain Rule) and SHALL resolve to an existing Person, else Not-Found Error (§7.1) (Domain Rule) | 05_Queries §5.1 |

---

# 7. Error Classification Principles

This section states the *taxonomy* of validation failure outcomes
already implied by 04_Commands.md's per-Command Failure Conditions and
02_Use_Cases.md. It does not define wire-format error codes, HTTP
status codes, or response body shape — those belong to 08_API.md
(064 §8.9; §14.3 Contracts vs API distinction).

## 7.1 Error Categories

| Category | Meaning | Source Validation Category | Example |
|---|---|---|---|
| Not-Found Error | A referenced Aggregate instance does not exist | Business (Precondition) (§4 rows tagged "Business (Precondition)") | Person not found (UpdatePersonProfile); RefreshTokenId not found (RefreshSession) |
| Validation Error | Input Validation or Business Validation rule failed before any write | Input or Business (§3, §4) | Invalid email format; password below strength requirement |
| Conflict Error | A Cross-Instance Uniqueness rule (§5.1) would be violated | Cross-Aggregate (Cross-Instance Uniqueness) (§5.1) | Duplicate email at registration; email already in use at profile update |
| Permission Error | A True Cross-Aggregate ownership check (§5.2) failed | Cross-Aggregate (True Cross-Aggregate) (§5.2) | Session does not belong to the requesting Person |
| Authentication-Failure Response | A uniform, intentionally non-specific outcome per 11_Security §11.2 | Business (§4.2 row 6 — Policy-driven uniform-response override) | AuthenticatePerson rejecting Person-not-found, no-Credential, and password-mismatch identically |
| Temporary/Infrastructure Failure | The Command could not be evaluated to a definitive accept/reject outcome | Not applicable — an evaluation failure, not a validation-rule failure; no §3–§6 category produces this outcome | Credential validation system unavailable (04_Commands §4.2) |

## 7.2 Error Content Restrictions

- No error response, log entry, or exception trace SHALL contain
  PasswordHash, a plaintext password, AccessTokenId, or RefreshTokenId
  value (11_Security §10.3, §11.9).
- The Authentication-Failure Response category (§7.1) SHALL NOT
  distinguish its three triggering conditions in its outward form,
  regardless of which specific Business Validation rule in §4.2
  actually failed (11_Security §11.2).
- Validation Error and Conflict Error responses MAY name the offending
  field (e.g. "Email already in use") since these are pre-write,
  non-authentication failures where 11_Security §11.2's Uniform
  Authentication-Failure Response requirement does not apply — this is
  the same accepted trade-off 11_Security §11.2 already documents for
  RegisterPerson's duplicate-email Conflict Error.

---

# 8. Recommended Validation Processing Order

The following six-step order is **documentation guidance consolidating
a pattern already implicit** in every Command's Preconditions →
Validation Rules → Aggregate Interaction → Failure Conditions structure
in 04_Commands.md (§4.1–§4.6). It introduces no new architectural
constraint: no upstream document (01_Domain_Model.md, 02_Use_Cases.md,
03_Aggregates.md, or 04_Commands.md) mandates that validation execute
in exactly six discrete phases, and 064 §8.13 requires only that
validation rules be specified and classified, not sequenced this
precisely.

An implementation MAY interleave, parallelize, or reorder steps 2–5
where doing so produces an equivalent accept/reject outcome for every
rule in §3–§6. The only two ordering constraints that ARE binding,
because they follow directly from rules stated elsewhere in this
Blueprint set rather than from this section itself, are:

- **Step 1 (Precondition Checks) SHALL execute before any write** —
  this follows from 04_Commands.md's own Preconditions-before-execution
  structure, not from this document.
- **Step 6 (Persistence-Level Constraint Enforcement) SHALL remain the
  final authority for every Cross-Instance Uniqueness rule** — this
  follows from 09_Persistence.md §8.3's statement that application-level
  validation alone is insufficient under concurrency, not from this
  document.

Subject to those two constraints, the numbering below is a recommended
processing order, not a mandated one.

```text
1. Precondition Checks
   (Does the referenced Aggregate instance exist and is it in an
   eligible state? — e.g. "Person exists with status = Active")
        ↓
2. Input Validation
   (Is each provided field present, well-formed, and correctly typed?)
        ↓
3. Business Validation
   (Does each field satisfy its domain/policy rule? — e.g. password
   strength, current-password match)
        ↓
4. Cross-field Validation
   (Do the fields on this one request satisfy a joint constraint? —
   e.g. "at least one mutable attribute provided")
        ↓
5. Cross-Aggregate Validation
   (Does this operation satisfy a relationship to another Aggregate
   instance? — e.g. Session-ownership, Cross-Instance Uniqueness
   pre-check)
        ↓
6. Persistence-Level Constraint Enforcement
   (Storage-level uniqueness index / optimistic concurrency, per
   09_Persistence §8.3–§8.4 — the final, race-condition-safe authority)
```

## 8.1 Why Steps 5 and 6 Both Exist for Uniqueness Rules

Every Cross-Instance Uniqueness rule in §5.1 appears at both Step 5
(an early, application-level check against currently-visible data) and
Step 6 (the storage-level constraint). This is intentional, not
duplication:

- **Step 5** gives a fast, friendly failure (Conflict Error, §7.1)
  without attempting a write, for the common case where no race exists.
- **Step 6** is the only layer that is safe under concurrent requests
  (09_Persistence §8.3: "application-level validation alone is
  insufficient to prevent race conditions"). A write that passes Step 5
  but loses a race at Step 6 SHALL still surface as a Conflict Error
  (§7.1) to the caller — the two steps share one error category even
  though they execute at different layers and different times.

## 8.2 Relationship to Command Rollback

Per 04_Commands §4.1 (RegisterPerson) and ADR-0002 Decision 1: if Step
5 or Step 6 fails for any rule inside the Core Ownership Transaction
(Person + Organization + Membership), the entire transaction rolls
back per 09_Persistence §5.1 — no partial ownership state may result
from a Cross-Aggregate Validation failure discovered only at the
persistence layer.

---

# 9. Cross-Document Alignment

## 9.1 Against 064_SmartCore_Blueprint_Standard.md §8.13

✅ Input Validation specified (§3)
✅ Business Validation specified (§4)
✅ Cross-field Validation specified (§4, §8)
✅ Cross-Aggregate Validation specified (§5)
✅ Error Messages — classification principles, not wire format (§7)
✅ Validation Order specified (§8)
✅ Domain Rules / Policy Rules / Infrastructure Constraints distinguished
throughout (§2 framework, applied per rule in §3–§6)

## 9.2 Against 04_Commands.md

✅ Every Validation Rule and Failure Condition in 04_Commands §4.1–§4.6
is represented in §4 of this document, with no addition or omission.

## 9.3 Against 01_Domain_Model.md

✅ Domain Rules explicitly defined by 01_Domain_Model.md (§9 Domain
Rules, §6 Domain Invariants) are carried through as Domain Rule
classifications where applicable throughout §3–§6. Not every Domain
Rule classification in this document originates from
01_Domain_Model.md — some (e.g. Email uniqueness, §5.1) are Domain
Rule-classified per the §2.1 layer-of-enforcement definition while
being sourced from 04_Commands.md or 09_Persistence.md instead; see
v1.0.5's Change Log entry for the specific finding that prompted this
clarification.

## 9.4 Against 09_Persistence.md

✅ Every Required Uniqueness Constraint (09_Persistence §8.3) appears in
§5.1 with matching Infrastructure Constraint classification.

✅ §8.1's rationale for why both application-level and storage-level
checks exist is elaborated in §8.1 of this document.

## 9.5 Against 11_Security.md

✅ Uniform Authentication-Failure Response (11_Security §3.1, §11.2) is
carried through as §4.2's Business Validation rule and §7's Error
Classification principle.

✅ Secret-exclusion requirements (11_Security §10.2, §10.3) are carried
through as §7.2.

✅ Session-ownership as an identity-resolution check, not Capability
Authorization (11_Security §4.3), is preserved in §5.2 and §6.

## 9.6 Against 02_Use_Cases.md

✅ Every Failure Condition cited across UC-001, UC-003, UC-005, UC-006,
UC-007, UC-008, UC-009 is reflected in the corresponding §4 subsection.

## 9.7 Against ADR-0002 and ADR-0003

✅ ADR-0002 Decision 1 (Registration Boundary) is reflected in §4.1 and
§5.2. ADR-0002 Decision 7 (Command Model Coordination Exception) is
reflected in §5.2's Scope Note. ADR-0003's lifecycle constraints
introduce no additional MVP validation rule (Organizations and
Memberships are created directly in Active state; no lifecycle
transition Command exists to validate against in MVP).

---

# 10. MVP Readiness Checklist

Validation Blueprint Version 1.0 is complete when:

✓ Every field accepted by any of the 6 Public Commands has a stated
  Input Validation rule (§3)

✓ Every Command's Business, Cross-field, and Cross-Aggregate Validation
  rules are stated and classified (§4)

✓ Every Cross-Instance Uniqueness and True Cross-Aggregate rule is
  consolidated in one place (§5)

✓ Query-side read-time checks are stated (§6)

✓ Error outcomes are classified into a consistent taxonomy without
  freezing wire-format details (§7)

✓ A recommended Validation Processing Order applies across all
  Commands, with binding vs. non-binding constraints explicitly
  distinguished (§8)

✓ Every rule is classified as Domain Rule, Policy Rule, or
  Infrastructure Constraint (§2, applied throughout)

✓ No new Aggregates, Commands, Queries, Events, or Business Rules
  introduced

✓ Alignment verified against 064, 04_Commands, 01_Domain_Model,
  09_Persistence, 11_Security, 02_Use_Cases, ADR-0002, ADR-0003 (§9)

---

# 11. Change Log

## Version 1.0.8 (2026-07-18)

Correction pass following a seventh architecture review of v1.0.7
(Freeze Readiness / Blueprint Governance focus, scored 9.8/10, no
Blocker or Major issues found). No new Aggregates, Commands, Queries,
Events, or business rules introduced; no MVP scope changed.

- **§9.3 — scoped the Domain Model alignment claim to avoid
  overgeneralization**: "Domain Rules (§9 Domain Rules, §6 Domain
  Invariants) are carried through as Domain Rule classifications
  throughout §3–§6" read as though every Domain Rule-classified row in
  this document traces back to 01_Domain_Model.md. This document's own
  Change Log (v1.0.5) records that Email uniqueness is Domain
  Rule-classified per the §2.1 layer-of-enforcement definition while
  being sourced from 04_Commands.md/09_Persistence.md, not
  01_Domain_Model.md. §9.3 now reads "carried through... where
  applicable" and explicitly notes that not every Domain Rule
  classification originates from 01_Domain_Model.md, pointing to the
  v1.0.5 entry as the specific finding behind the clarification.
- **Recorded, not applied — Change Log size recommendation**: the
  reviewer noted that §11 is now roughly a third of this document's
  total length and will keep growing with each future version (1.0.9,
  1.1.0, ...), and recommended that after Freeze, either only the most
  recent 3-5 versions be retained here with the rest moved to a
  separate Revision History document, or the full history be archived
  elsewhere. This is a post-Freeze housekeeping action, not a
  correctness issue, and is not applied in this pass — full
  traceability of every correction remains valuable while the document
  is still open for review.

## Version 1.0.7 (2026-07-18)

Correction pass following a sixth architecture review of v1.0.6
(Blueprint Governance + Consistency + Freeze Readiness focus). No new
Aggregates, Commands, Queries, Events, or business rules introduced; no
MVP scope changed.

- **§2.4 — fixed a stale example that no longer matches the current
  table structure**: the sentence "Where a table row below lists both —
  e.g. 'Domain Rule + Infrastructure Constraint' — this denotes two
  separate checkpoints..." accurately described the tables as they
  stood through v1.0.2, but v1.0.3 split every such row into separate
  `Na`/`Nb` rows (§4.1, §4.3, §4.6) — so no row combining two
  classifications has existed since v1.0.3, making this example stale.
  Replaced with a reference to the actual current pattern: the paired
  `5a`/`5b` rows and §5.1's two columns.
- **§5.1, §5.2 — renamed "Normative Source" column to "Application-Level
  Rule Source"**: v1.0.6 renamed the column away from "Domain Rule
  Source" (which wrongly implied a 01_Domain_Model.md citation) to
  "Normative Source," but "Normative" was itself ambiguous —
  "Infrastructure Enforcement," the paired column, is equally
  normative. "Application-Level Rule Source" states precisely what the
  column identifies: the source of the application-layer (pre-write)
  checkpoint, as distinct from the storage-layer checkpoint in the
  adjacent column.
- **Reviewed, not applied — six non-blocking observations**: this
  review also raised (a) whether Query Validation's "Implicit" wording
  for GetCurrentPerson reads inconsistently against GetPersonById's
  explicit "SHALL resolve to an existing Person," (b) refining
  Temporary/Infrastructure Failure's description from "no validation
  category produces this outcome" to "does not originate from a
  validation rule" for future Circuit-Breaker/Retry-Policy scenarios,
  (c) whether §9.3's "Domain Rules... are carried through" reads as
  broader than accurate given the Change Log's own Email-uniqueness
  finding, (d) that Membership's status as an independent Aggregate
  Root (assumed from 03_Aggregates.md, not verified against it in this
  document's scope) is a precondition for §5.1's "same Aggregate type"
  framing remaining correct, (e) Change Log length relative to document
  length, and (f) §8's Precondition-before-Input ordering having a
  performance trade-off worth a rationale note. The reviewer explicitly
  scoped only the two items above as worth correcting before Freeze;
  these six are recorded here for traceability but not acted on in this
  pass.

## Version 1.0.6 (2026-07-18)

Correction pass following a fifth architecture review of v1.0.5,
focused on Blueprint Governance and terminology precision. No new
Aggregates, Commands, Queries, Events, or business rules introduced; no
MVP scope changed.

- **§5.1, §5.2 — renamed "Domain Rule Source" column to "Normative
  Source"**: the column name implied citations would come from
  01_Domain_Model.md specifically (per the Blueprint's own 01=Domain,
  02=Use Cases, 04=Commands, 09=Persistence numbering convention). In
  practice, no row in either table cites 01_Domain_Model.md — §5.1's
  rows cite 04_Commands, 02_Use_Cases (UC-008), and 09_Persistence;
  §5.2's rows cite 04_Commands, 11_Security, ADR-0002, and 057. This
  was most visible in v1.0.4's AccessTokenId fix, which added a
  09_Persistence citation directly under a column literally named
  "Domain Rule Source" — a Persistence-layer document under a
  Domain-labeled column. Renamed to "Normative Source" in both tables;
  added a sentence to §5.1's intro clarifying that this column cites
  whichever document establishes the application-layer (Domain Rule)
  checkpoint per the §2 classification, not literally 01_Domain_Model.md.
- **§5.1 — refined the "same Aggregate type" definition to distinguish
  global vs. partition-scoped uniqueness**: the opening sentence
  described all five rules uniformly as validation "against other
  instances of the same Aggregate type," which is accurate but did not
  capture that Email/AccessTokenId/RefreshTokenId are checked globally
  (no partition key) while Membership and Credential uniqueness are
  checked within a partition (PersonId+OrganizationId; PersonId among
  Active-status instances respectively). Added explicit language
  naming both scoping patterns as sub-cases of the same category,
  rather than rewording "same Aggregate type" to a single alternate
  phrase that would not have resolved the imprecision either.

## Version 1.0.5 (2026-07-18)

Correction pass enabled by direct access to 01_Domain_Model.md and
02_Use_Cases.md for the first time, allowing verification of citations
that were previously deferred as unverifiable. No new Aggregates,
Commands, Queries, Events, or business rules introduced; no MVP scope
changed.

- **§5.1 — removed an unverifiable Domain Model citation, resolving
  the deferred "Email vs. Membership uniqueness sourcing" item from
  v1.0.4**: v1.0.4 deferred a question about why "Email unique across
  all Persons" cited 01_Domain_Model.md while "(PersonId,
  OrganizationId) unique per Membership" cited 02_Use_Cases.md
  (UC-008), noting it lacked access to verify. With both documents now
  available: 01_Domain_Model.md v1.1.0 does **not** state Email
  uniqueness anywhere — checked §6 Domain Invariants (Person,
  Organization, Membership, Credential, Session subsections), §9
  Domain Rules (Rule-001 through Rule-006), and the EmailAddress Value
  Object's Rules list (Required, Valid format, Case-insensitive
  comparison — no uniqueness rule). The "01_Domain_Model §6 (Rule-001
  equivalent)" citation was therefore inaccurate — Rule-001 is "Every
  Person SHALL belong to at least one Organization," an unrelated
  invariant. The citation is removed; the row now cites only
  04_Commands §4.1/§4.3 and 09_Persistence §8.3, which do state the
  rule. This resolves the deferred item: both Email and Membership
  uniqueness now correctly cite only their actual source documents
  (04_Commands/09_Persistence and 02_Use_Cases UC-008, respectively) —
  neither claims Domain Model backing, which is the accurate state of
  the source documents, not an inconsistency to reconcile in the
  opposite direction.
- **Verified three other 01_Domain_Model.md citations added in v1.0.1–
  v1.0.4 (§6.1 table)**: GetCurrentPerson's "identity survives Session
  expiration" (§6 Session Invariants — confirmed verbatim),
  GetOrganizationsForPerson's "01_Domain_Model §9 Rule-001 restates the
  same invariant" (confirmed — Rule-001 text matches exactly), and
  GetMembershipsForPerson's "01_Domain_Model §6 restates it" (confirmed
  — §6 Person Invariants states "Person MUST own at least one
  Membership after registration"). All three are accurate; no changes
  needed.
- **Flagged, not corrected — 09_Persistence.md carries the same
  inaccurate citation**: 09_Persistence.md §8.3's own Required
  Uniqueness Constraints table cites "01_Domain_Model §6 Rule-001
  equivalent" for the identical Email uniqueness rule. Since
  09_Persistence.md is outside this document's editable scope, this is
  noted here for a separate correction pass on that document rather
  than corrected in place.

## Version 1.0.4 (2026-07-18)

Correction pass following a fourth architecture review of v1.0.3. No
new Aggregates, Commands, Queries, Events, or business rules
introduced; no MVP scope changed.

- **§4.1, §4.3, §4.4, §4.6 — resolved the "Precondition" taxonomy gap**:
  v1.0.3 flagged, but deliberately did not resolve, that "Precondition"
  was used as a Category value in eight rows despite 064 §8.13
  mandating exactly Input, Business, Cross-field, and Cross-Aggregate.
  All eight rows (§4.1 row 1; §4.3 rows 1–2; §4.4 rows 1–2; §4.6 rows
  1–3) now read "Business (Precondition)" instead of bare
  "Precondition," consistent with the parenthetical-subtype pattern
  already used for Cross-Aggregate rows (e.g. "Cross-Aggregate (Session
  ↔ Person)"). §4's intro note updated from "open item" to "resolved."
  §7.1's Not-Found Error row's Source Validation Category updated from
  "Precondition" to "Business (Precondition)" to match.
- **§5.1 — added missing source citation for AccessTokenId uniqueness**:
  the Domain Rule Source column for "AccessTokenId unique across all
  Sessions" previously read only "Required for unambiguous resolution"
  — a rationale, not a document reference, unlike every other row in
  the table. Verified against 09_Persistence.md §8.3, which states the
  identical rationale for the identical rule (confirming this is not a
  new rule introduced by this document); the cell now cites
  "09_Persistence §8.3" with the original rationale retained as
  parenthetical explanation.
- **Deferred, not resolved — Email vs. Membership uniqueness source
  type inconsistency (§5.1)**: a review noted that "Email unique across
  all Persons" cites 01_Domain_Model.md while "(PersonId,
  OrganizationId) unique per Membership" cites 02_Use_Cases.md
  (UC-008), and asked whether both should cite Domain Model if both are
  Domain Invariants. This document does not have independent read
  access to 01_Domain_Model.md's Membership-related content in this
  pass; adding a Domain Model citation without verifying it exists
  there would risk inventing a reference. Left as an open item pending
  either that document being made available for verification or an
  explicit statement from the document owner that no such Domain Model
  citation exists.

## Version 1.0.3 (2026-07-18)

Correction pass following a third architecture review of v1.0.2 (deep
Blueprint-Reviewer pass focused on taxonomy consistency, not wording).
No new Aggregates, Commands, Queries, Events, or business rules
introduced; no MVP scope changed.

- **§4.1, §4.3, §4.6 — split combined-classification rows**: three
  rows ("Email uniqueness..." in §4.1 row 5 and §4.3 row 7; "Replacement
  preserves at-most-one-Active-Credential..." in §4.6 row 7) each
  carried "Domain Rule + Infrastructure Constraint" in a single
  Classification cell. While §2's prose already reconciled this
  conceptually (one Business Requirement, two checkpoints), the table
  representation itself still displayed two classifications in one row,
  contradicting §2's stated rule at the table level and creating
  ambiguity for machine parsing. Each such row is now split into two
  rows (`Na`/`Nb`), one per checkpoint, mirroring the two-column
  pattern §5.1 already used successfully for the same kind of rule.
  §2.4's closing sentence updated accordingly — it no longer describes
  a row that spans two classifications, since none remain.
- **§4.5 — fixed undefined "Cross-Instance Lookup" Category**: this
  label appeared exactly once, in RefreshSession row 1, and was never
  defined anywhere in this document's taxonomy (only Input, Business,
  Cross-field, and Cross-Aggregate are declared categories; "Precondition"
  is also in use but is separately flagged below). Reclassified to
  Business, consistent with rows 2–3 of the same table, which check the
  same RefreshTokenId under the same Category.
- **Cross-Aggregate subtype labels standardized**: "Cross-Instance
  Uniqueness," used bare as a Category value in three rows (and paired
  with "Precondition" in a fourth), is a subtype of Cross-Aggregate
  Validation per the Overview (§1.1), not a top-level category. All
  such rows now read "Cross-Aggregate (Cross-Instance Uniqueness),"
  consistent with the existing "Cross-Aggregate (Session ↔ Person)" /
  "Cross-Aggregate (Person ↔ Credential)" pattern used elsewhere in §4.
- **§7.1 — added explicit Source Validation Category mapping**: the
  Error Categories table previously left the connection between each
  error outcome and its originating §3–§6 validation category partly
  implicit (Conflict Error and Permission Error already cited §5.1/§5.2
  explicitly; Not-Found Error and Validation Error did not). A new
  "Source Validation Category" column now states this mapping
  explicitly for all six error categories, including that
  Temporary/Infrastructure Failure originates from no validation
  category (it is an evaluation failure, not a rule failure).
- **§4 intro — flagged, did not silently resolve, the "Precondition"
  taxonomy gap**: several rows in §4.3, §4.4, and §4.6 use
  "Precondition" as a Category value. This is inherited from
  04_Commands.md's own Preconditions/Validation Rules structure but is
  not one of 064 §8.13's four required categories (Input, Business,
  Cross-field, Cross-Aggregate). This document does not reclassify
  those rows in this pass — doing so touches multiple existing rows
  and is a taxonomy decision left for explicit confirmation before
  correction, consistent with this Blueprint's governance-first review
  pattern.

## Version 1.0.2 (2026-07-18)

Correction pass following a second architecture review of v1.0.1. No
new Aggregates, Commands, Queries, Events, or business rules
introduced; no MVP scope changed.

- **§4.2 (AuthenticatePerson) — resolved contradiction with §5.2**:
  v1.0.1's closing statement ("No Cross-field or Cross-Aggregate
  Validation applies beyond credential resolution... not a
  multi-Aggregate write") directly contradicted §5.2, which classifies
  the identical rule ("Password validates against the Credential
  belonging to the resolved Person") as True Cross-Aggregate
  Validation. The contradiction traced to §4.2 applying an unstated
  "write vs. read" test that is not part of the Overview's actual
  definition of Cross-Aggregate Validation (§1.1: "rules that require
  reading beyond the single Aggregate instance"). §4.2's closing
  paragraph now affirms Steps 4–5 as True Cross-Aggregate Validation
  (Person ↔ Credential), consistent with §5.2 and its Scope Note that
  read-time checks require no transactional exception. §4.2 table rows
  4–5 re-tagged from "Business" to "Cross-Aggregate (Person ↔
  Credential)" to match the Category labeling convention already used
  in §4.3, §4.4, and §4.6 for Session ↔ Person checks.
- **§6.1 — grounded Existence Validation claims in governance
  invariant, not an unqualified permanent fact**: "every registered
  Person has at least one Organization/Membership" (GetOrganizationsForPerson,
  GetMembershipsForPerson rows) previously cited only
  01_Domain_Model.md. Both rows now cite 057 §5 Principle 1 ("Every
  Person SHALL belong to at least one Organization") and 057 §12
  (breaking changes to ownership boundaries prohibited within Version
  1.x) as the primary source of this guarantee, and note explicitly
  that this is not automatically preserved beyond Version 1.x if a
  future ADR introduces Membership Revocation (ADR-0003 §2, currently
  future scope) — such an ADR would need to explicitly re-evaluate or
  preserve the invariant.

## Version 1.0.1 (2026-07-18)

Correction pass following an architecture review of v1.0.0. No new
Aggregates, Commands, Queries, Events, or business rules introduced; no
MVP scope changed.

- **§2 reworked (classification framework)**: clarified that
  classification applies per *enforcement checkpoint*, not per
  Business Requirement — resolving an internal inconsistency where §2's
  opening statement ("every rule is tagged with exactly one
  classification") contradicted several table rows tagging one rule
  with two classifications (e.g. "Domain Rule / Infrastructure
  Constraint"). Added new §2.4 ("Business Requirements Enforced at
  Multiple Layers") naming this pattern explicitly. Table rows
  representing a genuine single-requirement/multi-layer case (Email
  uniqueness in §4.1, §4.3; Active-Credential uniqueness in §4.6) now
  read "Domain Rule + Infrastructure Constraint (multi-layer — §2.4)"
  instead of the ambiguous "(rule) / (enforcement)" phrasing. Table
  rows that had instead bundled two *distinct* rules into one
  classification cell (NewPassword's Required + strength-threshold in
  §3; RefreshTokenId's expiry-check + expiry-duration in §4.5;
  GetPersonById's PersonId-required + platform-recognition in §6) were
  split into separate rows, each with one rule and one classification.
- **Fixed a broken internal cross-reference**: §2.3's parenthetical
  previously pointed to "§9.4" for the rationale on why both
  enforcement layers exist; that rationale is actually in §8.1. §2.4
  now points to §8.1 correctly.
- **§8 reframed as non-binding guidance**: renamed from "Validation
  Order" to "Recommended Validation Processing Order." Added an
  explicit statement that no upstream document mandates a six-phase
  sequence and that this section introduces no new architectural
  constraint, per 064 §8.13. Named the only two ordering constraints
  that are actually binding (Preconditions before any write;
  Persistence-Level enforcement as final authority for uniqueness) and
  attributed each to the upstream document it actually follows from,
  rather than to this section.
- **§6 (Query Validation) expanded**: restructured from a single thin
  table into explicit Authentication / Ownership / Existence Validation
  columns per Query, matching the depth already given to Commands in
  §4. Split into §6.1 (User Interaction Queries) and §6.2 (Platform
  Integration Queries). The previously bundled GetPersonById row
  (PersonId-required + platform-recognition in one cell) is now
  represented across the three columns without merging distinct rules.
- **§4 intro**: added an explicit note that MVP intentionally defines
  very few Cross-field Validation rules, naming the one genuine
  instance (UpdatePersonProfile's "at least one attribute provided")
  and confirming that no 04_Commands.md rule compares two field
  *values* against each other (e.g. NewPassword vs. CurrentPassword),
  so none is invented here.
- **§1.2**: added an explicit statement that, until 10_Configuration.md
  exists as an accepted Blueprint document, every Policy Rule threshold
  referenced in this document remains implementation-defined, and that
  this document's own completeness does not depend on
  10_Configuration.md's existence.

## Version 1.0.0 (2026-07-18)

Initial Validation Blueprint.

- Established the Domain Rule / Policy Rule / Infrastructure Constraint
  classification framework (§2), applied to every rule in the document
- Consolidated Input Validation for every field across all 6 Public
  Commands and the GetPersonById Query (§3)
- Organized each Command's existing 04_Commands.md rules into Business /
  Cross-field / Cross-Aggregate categories (§4)
- Consolidated Cross-Aggregate Validation into Cross-Instance Uniqueness
  (§5.1) and True Cross-Aggregate (§5.2) subcategories, with an explicit
  Scope Note on why only Registration requires a transactional
  exception (ADR-0002 Decision 7 / 057 §8)
- Specified Query-side read-time validation (§6)
- Established an Error Classification taxonomy (§7) without freezing
  wire-format details, deferred to 08_API.md
- Established a canonical six-step Validation Order (§8), with explicit
  rationale for why application-level and storage-level uniqueness
  checks both exist without being duplicative (§8.1)
- Verified full alignment against 064, 04_Commands, 01_Domain_Model,
  09_Persistence, 11_Security, 02_Use_Cases, ADR-0002, and ADR-0003 (§9)
- No new Aggregates, Commands, Queries, Events, or Business Rules
  introduced

---

**END OF DOCUMENT**
