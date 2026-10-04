<!--
File: 13_Testing.md
Last modified: 2026-07-18

Document ID: ID-13
Title: SmartCore Identity Platform Blueprint - Testing
Version: 1.0.0
Status: READY_FOR_GENERATION

Purpose:
Defines verification requirements for the Identity Platform per
064_SmartCore_Blueprint_Standard.md §8.14: Unit Testing, Aggregate
Testing, Domain Service Testing, Integration Testing, Event Testing,
Security Testing, and Acceptance Criteria. This document does not
introduce new Aggregates, Commands, Queries, Events, or business rules;
every test case defined here verifies behavior already specified in
01_Domain_Model.md, 02_Use_Cases.md, 03_Aggregates.md, 04_Commands.md,
05_Queries.md, 06_Domain_Events.md, 09_Persistence.md, and
11_Security.md.

Dependencies (Required):
- 00_Overview.md
- 01_Domain_Model.md
- 02_Use_Cases.md
- 03_Aggregates.md
- 04_Commands.md
- 05_Queries.md
- 06_Domain_Events.md
- 09_Persistence.md
- 11_Security.md
- 14_MVP.md
- 064_SmartCore_Blueprint_Standard.md
- ADR-0002_Identity_Foundation_Clarifications.md
- ADR-0003_Organization_and_Membership_Lifecycle_Standardization.md

Referenced For Consistency (informative, not a hard Dependency):
- 07_Contracts.md (§8 Error Code Catalog — used for REST Transport
  Integration Testing status-code assertions)
- 08_API.md (§3-§5 Command/Query → REST mapping — used for REST
  Transport Integration Testing)
- 10_Configuration.md (§2, §4 default values — used to parameterize
  timeout/TTL/rotation test cases without hard-coding values this
  document does not own)

Not Yet Available (Forward Reference):
- 12_Validation.md has not yet been authored. Per 064 §6.4 (Single
  Source of Truth), detailed Business/Cross-Field/Cross-Aggregate
  Validation rule *enumeration* (error message catalog, validation
  order) is that document's exclusive responsibility and is not
  anticipated or duplicated here. This document's validation-outcome
  test cases (§3, §6) are sourced only from Validation Rules already
  stated in 04_Commands.md and Failure Conditions already stated in
  02_Use_Cases.md. Once 12_Validation.md exists, a future revision of
  this document SHOULD cross-check §3/§6 against it and extend
  validation-rule-level test case detail accordingly; this is recorded
  as a forward note, not a blocking gap in this document's own MVP
  Readiness Checklist (§13).

Change Log:
  - Version 1.0.0 (2026-07-18): Initial Testing Blueprint. Defines
    Testing Design Principles (§2), Unit Testing (§3), Aggregate
    Testing (§4), Domain Service and Application Service Testing (§5),
    Integration Testing (§6) — including Core Ownership Transaction
    atomicity, Post-Commit isolation, concurrency, and uniqueness
    constraint testing sourced from 09_Persistence.md — Event Testing
    (§7) sourced from 06_Domain_Events.md's envelope, payload,
    publishing, and ordering rules, Security Testing (§8) sourced from
    11_Security.md's threat model (§11.1-§11.10), and Acceptance
    Criteria (§9) sourced from 02_Use_Cases.md's nine Use Cases and
    14_MVP.md §8's Readiness Checklist. No new Aggregates, Commands,
    Queries, Events, or business rules introduced. No MVP scope
    changed. See §2.3 for this document's explicit, non-blocking
    forward reference to 12_Validation.md.
-->

# 1. Overview

This document defines the verification requirements for the Identity
Platform Blueprint, per 064_SmartCore_Blueprint_Standard.md §8.14.

Testing answers:

> "How is it confirmed that the Identity Platform behaves exactly as
> the rest of this Blueprint package specifies — no more, no less?"

This document does not define new behavior. Every test case below
verifies a Precondition, Validation Rule, Aggregate Interaction,
Failure Condition, Postcondition, Event, or Security requirement
already stated in a prior Blueprint document. Where this document
references a rule, it cites the owning document; it does not restate
the rule as its own authority.

## 1.1 What This Document Defines

- Unit Testing expectations (§3)
- Aggregate Testing (§4)
- Domain Service and Application Service Testing (§5)
- Integration Testing (§6), including Core Ownership Transaction
  atomicity, Post-Commit isolation, concurrency, and uniqueness
  constraint verification
- Event Testing (§7)
- Security Testing (§8)
- Acceptance Criteria (§9), derived from 02_Use_Cases.md and
  14_MVP.md §8
- A consolidated Test Coverage Matrix (§10)

## 1.2 What This Document Does NOT Define

- Detailed Business/Cross-Field/Cross-Aggregate Validation rule
  enumeration — deferred to 12_Validation.md (§2.3)
- Test automation framework, tooling, or language choice — an
  implementation detail per 064 §3
- Test data management, environment provisioning, or CI/CD pipeline
  design
- Non-functional performance/load thresholds — not defined elsewhere
  in this Blueprint package and therefore not asserted here
- Penetration testing methodology or specific attack tooling (§8.4
  identifies *what* threat-relevant behavior SHALL be verified, not
  *how* a penetration test is conducted)

This document introduces no new:

- Aggregates
- Commands
- Queries
- Events
- Business Rules

---

# 2. Testing Design Principles

## 2.1 Testing Verifies Already-Approved Behavior

Every test case in this document SHALL trace to one of:

- A Validation Rule or Failure Condition in 04_Commands.md
- A Main Flow, Alternative Flow, Failure Condition, or Postcondition
  in 02_Use_Cases.md
- An Invariant, Lifecycle, or Relationship in 01_Domain_Model.md or
  03_Aggregates.md
- A Publishing Rule, Envelope field, or Payload field in
  06_Domain_Events.md
- A Requirement in 11_Security.md
- A Constraint in 09_Persistence.md

No test case in this document asserts behavior that is not already
required by one of the above. Where a test scenario would require
inventing a new rule, it is out of scope for this document (064 §6.6
Traceability).

## 2.2 Test Levels

This document organizes verification into six levels, matching
064 §8.14 exactly:

| Level | Verifies | Primary Source |
|---|---|---|
| Unit Testing (§3) | Value Objects and Entity attribute rules in isolation | 01_Domain_Model.md §2, §3 |
| Aggregate Testing (§4) | Aggregate invariants and lifecycle transitions | 01_Domain_Model.md §5, §6; 03_Aggregates.md |
| Domain Service / Application Service Testing (§5) | Cross-cutting orchestration logic within a service's own responsibility | 01_Domain_Model.md §7, §8 |
| Integration Testing (§6) | End-to-end behavior across Command Handler → Aggregate → Repository, including cross-Aggregate transaction boundaries | 04_Commands.md; 09_Persistence.md |
| Event Testing (§7) | Envelope, payload, publishing, and ordering correctness | 06_Domain_Events.md |
| Security Testing (§8) | Authentication, secrets, session/token, and threat-specific behavior | 11_Security.md |

Acceptance Criteria (§9) sit above all six levels: they verify that a
complete Use Case succeeds end-to-end, and are the basis for the MVP
Readiness Checklist (14_MVP.md §8).

## 2.3 Relationship to 12_Validation.md (Deferred Item)

12_Validation.md, per 064 §8.13, is the future authoritative source for
detailed Business Validation, Cross-field Validation, Cross-Aggregate
Validation, Error Messages, and Validation Order. It has not yet been
authored (see header, "Not Yet Available").

This document's validation-outcome testing (§3.2, §6.1) is
consequently scoped to the *outcome* level only — asserting that a
Command rejects invalid input and returns the failure already
enumerated in 04_Commands.md's Validation Rules and Failure Conditions
— not to the *rule* level (specific field-format regular expressions,
exact error message text, or cross-field ordering) that 12_Validation.md
will own. This avoids this document anticipating or duplicating content
12_Validation.md has not yet defined, per 064 §6.4 (Single Source of
Truth).

## 2.4 MVP Boundary Protection

Testing SHALL verify only MVP-reachable behavior (14_MVP.md).

Testing SHALL NOT assert behavior for:

- Organization → Suspended, Archived (14_MVP.md §2)
- Membership → Revoked (14_MVP.md §2)
- Person → Suspended, Archived (02_Use_Cases.md UC-F12–UC-F14)
- MFA, OAuth, Enterprise SSO (00_Overview.md §11)
- Any Command, Query, or Event not listed in 04_Commands.md §3,
  05_Queries.md §3, or 06_Domain_Events.md §3

Where a lifecycle state exists in the Domain Model but has no MVP
Command reaching it, this document classifies it as Future Scope and
does not assert a positive test case for it, consistent with
14_MVP.md §4 ("No Unreachable States").

---

# 3. Unit Testing

Unit Testing verifies Value Object and Entity attribute rules in
isolation, without invoking a Repository, Domain Service, or Command
Handler.

## 3.1 Value Object Unit Testing

| Value Object | Source | Test Cases |
|---|---|---|
| EmailAddress | 01_Domain_Model.md §3 | Required; rejects invalid format; comparison is case-insensitive (two emails differing only in case are treated as equal for uniqueness purposes, per 11_Security.md §9) |
| PasswordHash | 01_Domain_Model.md §3 | Immutable once generated (no setter/mutation path exists after construction); generated only via the approved hashing algorithm (10_Configuration.md §2.2: argon2id, RFC 9106) |
| AccessTokenId | 01_Domain_Model.md §3 | Distinct from RefreshTokenId on the same Session (02_Use_Cases.md UC-004 Postconditions: "AccessTokenId and RefreshTokenId exist and are distinct") |
| RefreshToken | 01_Domain_Model.md §3 | Revocable (a revoked RefreshTokenId SHALL fail subsequent RefreshSession validation, 04_Commands.md §4.5) |

## 3.2 Entity Attribute Unit Testing

For each Entity (Person, Organization, Membership, Credential, Session
— 01_Domain_Model.md §2), Unit Testing SHALL verify, independent of
persistence:

- Every attribute listed in 01_Domain_Model.md §2 is present on the
  Entity's in-memory representation
- Immutable attributes (PersonId, OrganizationId, MembershipId,
  CredentialId, SessionId — each is a primary identity per
  09_Persistence.md §4) reject reassignment after construction
- Attribute-level format constraints stated in 04_Commands.md
  Validation Rules (e.g. DisplayName length/format, §4.1, §4.3) are
  enforced at the point the attribute is set, not deferred to
  persistence

**Deferred to 12_Validation.md**: the specific length bounds, format
regular expressions, and complexity rules themselves (per §2.3) —
this level only verifies that *some* enforcement exists at the
attribute-setting point, sourced from what 04_Commands.md already
states (e.g. 10_Configuration.md §2.2: minimum 10 characters, at least
one letter and one digit, for Password).

---

# 4. Aggregate Testing

Aggregate Testing verifies each Aggregate's invariants (01_Domain_Model.md
§6) and lifecycle transitions (01_Domain_Model.md §5; 03_Aggregates.md
§2-§6) in isolation from other Aggregates, using an in-memory or
test-double Repository.

## 4.1 Person Aggregate

Source: 01_Domain_Model.md §2, §5, §6; 03_Aggregates.md §2.

| Test Case | Expected Result |
|---|---|
| PersonId is immutable after construction | Attempted reassignment rejected or structurally impossible |
| Person cannot be constructed without an Email | Construction rejected |
| Person cannot be constructed without a DisplayName | Construction rejected |
| Email may change via profile update; PersonId does not change as a side effect | Email updates in place; PersonId unchanged |
| DisplayName may change via profile update | DisplayName updates in place |
| Lifecycle: Registered → Active reachable via registration | Person is constructed directly in Active status in MVP (14_MVP.md §3: "Persons created in Active state") |
| Lifecycle: Active → Suspended, Active → Archived | Not reachable by any MVP Command; SHALL NOT be asserted as a positive test case (§2.4) |

## 4.2 Organization Aggregate

Source: 01_Domain_Model.md §2, §5, §6; 03_Aggregates.md §3.

| Test Case | Expected Result |
|---|---|
| OrganizationId is immutable after construction | Attempted reassignment rejected |
| Organization Category is fixed to `Personal` in MVP | No construction path accepts a different Category value (01_Domain_Model.md §2: "Category MVP: Personal") |
| Lifecycle: Created → Active reachable via registration | Organization is constructed directly in Active status (14_MVP.md §3) |
| Lifecycle: Active → Suspended, Active → Archived | Not reachable by any MVP Command; SHALL NOT be asserted as a positive test case |
| Organization owns Resources (conceptual invariant) | Out of scope for Identity Platform Aggregate Testing — Resource is owned by a different Capability Platform (00_Overview.md §2) |

## 4.3 Membership Aggregate

Source: 01_Domain_Model.md §2, §5, §6; 03_Aggregates.md §4.

| Test Case | Expected Result |
|---|---|
| MembershipId is immutable after construction | Attempted reassignment rejected |
| Membership cannot be constructed without both a PersonId and an OrganizationId | Construction rejected |
| Role is fixed to `Owner` in MVP | No construction path accepts a different Role value (14_MVP.md §1: "Owner" is the only role) |
| Lifecycle: Created → Active reachable via registration | Membership is constructed directly in Active status (14_MVP.md §3) |
| Lifecycle: Active → Revoked | Not reachable by any MVP Command; SHALL NOT be asserted as a positive test case |

## 4.4 Session Aggregate

Source: 01_Domain_Model.md §2, §5, §6; 03_Aggregates.md §5, §7.

| Test Case | Expected Result |
|---|---|
| SessionId is immutable after construction | Attempted reassignment rejected |
| AccessTokenId and RefreshTokenId are generated independently and are distinct | Two independently-generated, non-equal values (02_Use_Cases.md UC-004 Postconditions) |
| ExpiresAt is always present after construction | No Session can be constructed without a defined expiration (01_Domain_Model.md §2; 11_Security.md §8.3) |
| Session state does not cascade to Person state | Modifying/closing a Session leaves the associated Person Entity's in-memory state unchanged (03_Aggregates.md §7 "Design Consequence") |
| Lifecycle: Created → Authenticated → Active reachable | Verified via AuthenticatePerson flow (§6.1) |
| Lifecycle: Active → Expired reachable (system expiration) | Verified via SessionManagementDomainService (§5.3) |
| Lifecycle: Active → Closed reachable (logout) | Verified via LogoutSession flow (§6.1) |
| Lifecycle: → Suspended (optional state) | Not required in MVP (01_Domain_Model.md §5 note; 14_MVP.md §3: "Optional Suspended state is supported but not required"). If an implementation does not implement Suspended, no test asserts its reachability; if implemented, verify it introduces no MVP Command path in or out of it |
| Expired or Closed Session cannot transition further | Attempted state transition from a terminal status is rejected (02_Use_Cases.md UC-007 Constraints: "Closed Session cannot be reactivated") |

## 4.5 Credential Aggregate

Source: 01_Domain_Model.md §2, §5, §6; 03_Aggregates.md §6, §8.

| Test Case | Expected Result |
|---|---|
| CredentialId is immutable after construction | Attempted reassignment rejected |
| Credential never exposes PasswordHash outside its own boundary | No accessor returns raw PasswordHash to a caller outside CredentialRepository (09_Persistence.md §4.5) |
| Lifecycle: Created → Active reachable | Verified via RegisterPerson Post-Commit and ChangePassword flows (§6.1) |
| Lifecycle: Active → Replaced reachable | Verified via ChangePassword flow: the prior Credential instance transitions to Replaced while retaining its own CredentialId (09_Persistence.md §4.5, §5.2) |
| Lifecycle: → Revoked | No MVP Command produces this transition (04_Commands.md §3 catalog contains no Credential-revocation Command); consistent with §2.4, this document does not assert a positive test case for Credential → Revoked |
| At most one Active Credential per Person, at the Aggregate level | Constructing or activating a second Active Credential for the same Person while one is already Active is rejected (Invariant-005, 04_Commands.md §10) — this is the Aggregate-level assertion; the storage-level enforcement of the same rule is verified separately in §6.5 |

## 4.6 Lifecycle Coverage Cross-Check

This table exists so Aggregate Testing coverage can be verified against
14_MVP.md §4's "No Unreachable States" requirement directly.

| Aggregate | MVP-Reachable States (Tested Positively) | Future-Scope States (Not Tested Positively) |
|---|---|---|
| Person | Registered→Active | Suspended, Archived |
| Organization | Created→Active | Suspended, Archived |
| Membership | Created→Active | Revoked |
| Session | Created→Authenticated→Active→Expired, Active→Closed | Suspended (optional; conditional per implementation) |
| Credential | Created→Active→Replaced | Revoked |

---

# 5. Domain Service and Application Service Testing

Domain Service and Application Service Testing verifies the
orchestration responsibilities each service owns (01_Domain_Model.md
§7, §8), using test-double Repositories for the Aggregates it
coordinates. Per 01_Domain_Model.md's own distinction (§7 preamble),
this level treats RegistrationApplicationService separately from the
four Domain Services, consistent with 064 §14.2 and ADR-0002 Decision
7.1.

## 5.1 RegistrationApplicationService

Source: 01_Domain_Model.md §8; 04_Commands.md §5.1; 09_Persistence.md
§5.

This is an Application Service, not a Domain Service — tests SHOULD be
labeled accordingly so a future reviewer does not mistake this section
for Domain Service Testing of a cross-Aggregate exception (ADR-0002
Decision 7).

| Test Case | Expected Result |
|---|---|
| Core Transaction Phase creates exactly Person, Organization, Membership | No other Aggregate is written during this phase |
| Core Transaction Phase commits atomically | All three Aggregates exist after commit, or none do (see §6.2 for the full transactional test) |
| Post-Commit Phase requests Credential creation via CredentialManagementDomainService | RegistrationApplicationService does not construct Credential directly; it delegates (01_Domain_Model.md §8 "Note") |
| Post-Commit Phase requests Session creation via Authentication lifecycle | RegistrationApplicationService does not construct Session directly; it delegates |
| Post-Commit Phase publishes PersonRegistered, OrganizationCreated, MembershipCreated | All three events are emitted after Core Transaction commit, independent of Post-Commit Phase outcome (verified fully in §7.3) |
| RegistrationApplicationService does not itself publish SessionCreated | SessionCreated is produced by AuthenticationDomainService (01_Domain_Model.md §8 "Note"), not by this service |

## 5.2 AuthenticationDomainService

Source: 01_Domain_Model.md §7; 04_Commands.md §5.2.

| Test Case | Expected Result |
|---|---|
| Resolves Person by Email before validating Credential | Person resolution precedes Credential validation in the observed call sequence |
| Validates supplied password against stored PasswordHash | Correct password → valid; incorrect password → invalid, no Session created |
| Creates a Session only on successful Credential validation | No Session is constructed on any failure path |
| Transitions the created Session into its authenticated lifecycle state | Session status reflects `Authenticated` (or later, per implementation) immediately after creation |
| Produces LoginSucceeded and SessionCreated on success; LoginFailed on failure | Matches 01_Domain_Model.md's Event Producer Mapping exactly |

## 5.3 SessionManagementDomainService

Source: 01_Domain_Model.md §7; 04_Commands.md §5.3.

| Test Case | Expected Result |
|---|---|
| Refresh: issues a new AccessTokenId for a valid, non-expired RefreshTokenId | New AccessTokenId differs from the previous one |
| Refresh: rejects an expired or revoked RefreshTokenId | Refresh fails; no Session mutation occurs |
| Refresh: does not create Person, Organization, Membership, or Credential | No write to any Repository other than SessionRepository (Invariant-004, 04_Commands.md §10) |
| Revoke (via Logout): transitions Session to Closed and revokes both tokens | Both AccessTokenId and RefreshTokenId are unusable for subsequent requests after Logout |
| Expire (system-triggered): transitions Session to Expired independent of user action | Expiration test does not require a Logout request to occur first |
| Produces SessionExpired for system expiration, LogoutCompleted for user logout | These two events are never both produced for the same SessionId (06_Domain_Events.md §6.4) |

## 5.4 PersonManagementDomainService

Source: 01_Domain_Model.md §7; 04_Commands.md §5.

| Test Case | Expected Result |
|---|---|
| Updates only Email and/or DisplayName, never PersonId | PersonId is unchanged before/after the operation |
| Validates Session ownership before applying any change | A Session belonging to a different Person is rejected before Person mutation |
| Produces PersonUpdated on success only | No event is produced if validation fails |
| Does not modify Credential or Session state as a side effect | CredentialRepository and SessionRepository (other than the read/validate already required) receive no write calls during this operation |

## 5.5 CredentialManagementDomainService

Source: 01_Domain_Model.md §7; 04_Commands.md §5.

| Test Case | Expected Result |
|---|---|
| Validates CurrentPassword against the existing Active Credential before creating a new one | New Credential is never created if CurrentPassword validation fails |
| Creates a new Credential with a new CredentialId, distinct from the previous instance's CredentialId | Two distinct CredentialId values exist after a successful change (09_Persistence.md §4.5, §5.2) |
| Transitions the previous Credential's Status to Replaced, not Revoked or deleted | Previous Credential row persists with Status = Replaced |
| Produces PasswordChanged on success only | No event on CurrentPassword validation failure or NewPassword validation failure |
| Does not revoke or otherwise modify existing Sessions | SessionRepository receives no write call as a result of this operation (02_Use_Cases.md UC-006 Constraints: "Credential change does not affect active Sessions") |

---

# 6. Integration Testing

Integration Testing verifies end-to-end behavior across Command
Handler → Domain/Application Service → Aggregate → Repository,
including the transactional and consistency guarantees defined in
09_Persistence.md. Unlike §4 and §5, this level uses a real (or
realistic test-container) persistence layer, not test doubles, because
the guarantees under test — atomicity, isolation, uniqueness,
concurrency — are properties of the persistence layer itself.

## 6.1 Command-Level Integration Tests

For each of the six MVP Commands, Integration Testing SHALL cover: one
success path exercising every Postcondition in 04_Commands.md, and one
scenario per Failure Condition in 04_Commands.md.

| Command | Success Path Assertion (Postconditions, 04_Commands.md) | Failure Scenarios (04_Commands.md) |
|---|---|---|
| RegisterPerson | Person Active; Organization Active/Personal; Membership Active/Owner; Credential exists; Session creation requested; PersonRegistered, OrganizationCreated, MembershipCreated published | Duplicate Person (email); Validation Failure (email/password/displayName); Core Ownership Transaction Failure; Post-Commit Failure |
| AuthenticatePerson | Session created, authenticated state; LoginSucceeded published | Person Not Found; Credential Missing; Password Validation Failure; Authentication Infrastructure Failure |
| UpdatePersonProfile | Person attributes updated; PersonId unchanged; PersonUpdated published; Sessions/Credential unaffected | Person Not Found; Invalid Session Ownership; Email Conflict; Validation Failure |
| LogoutSession | Session Closed; both tokens revoked; LogoutCompleted published | Session Not Found; Session Already Closed; Invalid Session Ownership; Persistence Failure |
| RefreshSession | Session continues authenticated; new AccessTokenId issued; no event produced | Refresh Token Not Found; Refresh Token Invalid (expired/revoked); Session Validation Failure; Persistence Failure |
| ChangePassword | New Credential Active; previous Credential Replaced; PasswordChanged published; Sessions unaffected | Person Not Found; Session Ownership Failure; Current Password Invalid; New Password Validation Failure; Credential Persistence Failure |

## 6.2 Core Ownership Transaction / Unit of Work Testing

Source: 09_Persistence.md §5.1; ADR-0002 Decision 1; 057 §8.

| Test Case | Expected Result |
|---|---|
| All three writes (Person, Organization, Membership) succeed | All three Aggregates are queryable immediately after commit |
| Person write fails; Organization/Membership writes have not yet occurred | No Aggregate is persisted; the transaction is rolled back in full |
| Organization write fails after Person write succeeds within the same Unit of Work | Person write is rolled back along with Organization/Membership; no Person exists after the failed attempt (09_Persistence.md §5.1: "no partial ownership state may persist") |
| Membership write fails after Person and Organization writes succeed within the same Unit of Work | All three writes are rolled back; no Person, Organization, or Membership exists after the failed attempt |
| A simulated per-Repository independent commit (each Repository committing its own write separately) is verified NOT to be how the implementation behaves | This is an implementation-conformance test against 09_Persistence.md §5.1.1's normative constraint that only the Unit of Work — not any individual Repository — owns the transaction boundary |
| No other Command ever spans more than one Repository's transaction | Verified across the five non-Registration Commands (09_Persistence.md §5.2 table); this is the negative-space complement to the RegisterPerson-only exception (057 §8 Scope Limitation) |

## 6.3 Post-Commit Isolation Testing

Source: 09_Persistence.md §6; 059 §6 Non-Invalidating Policy.

| Test Case | Expected Result |
|---|---|
| Core Ownership Transaction commits successfully; Credential creation subsequently fails | Person, Organization, Membership remain valid and queryable; PersonRegistered is still published, with SessionReference absent if Session creation also failed (06_Domain_Events.md §5.4) |
| Core Ownership Transaction commits successfully; Session creation subsequently fails | Person, Organization, Membership remain valid; the Person can still authenticate later via a separate AuthenticatePerson call once a Credential exists |
| A failed Post-Commit write does not reopen or roll back the already-committed Core Ownership Transaction | Attempting to query Person/Organization/Membership immediately after a Post-Commit failure returns the already-committed data, not an error |
| During the window between Core Ownership Transaction commit and Post-Commit completion, GetPersonById-equivalent reads return complete Person/Organization/Membership data | Per 09_Persistence.md §6.3, only Credential/Session reads may legitimately return empty during this window; Person/Organization/Membership reads SHALL NOT |

## 6.4 Concurrency Testing

Source: 09_Persistence.md §8.4, §8.4.1.

| Test Case | Expected Result |
|---|---|
| Two concurrent UpdatePersonProfile requests for the same Person, both based on the same initial Version marker | Exactly one succeeds; the other is rejected due to a stale Version marker, not silently overwritten |
| RefreshSession racing LogoutSession for the same Session | Whichever operation commits first determines final state; the second operation observes the updated Version marker and either fails cleanly or correctly reflects the already-closed Session — no silently lost update occurs |
| Every Repository `Update` operation listed in 09_Persistence.md §3 is verified to reject a write based on stale state | Applies to PersonRepository, SessionRepository, CredentialRepository updates |

## 6.5 Uniqueness Constraint Testing

Source: 09_Persistence.md §8.3.

| Constraint | Test Case |
|---|---|
| Person.Email unique across all Persons | Two concurrent RegisterPerson requests with the same (or case-variant equivalent, per §3.1) email: exactly one succeeds; the other receives Duplicate Person / PERSON_ALREADY_EXISTS |
| (Membership.PersonId, Membership.OrganizationId) unique | Not independently reachable via any MVP Command outside RegisterPerson (only one Membership is ever created per Person in MVP, 02_Use_Cases.md UC-008 Constraints); this constraint is verified as a storage-level guard, exercised via a direct Repository-level test rather than through a Command path, since no MVP Command attempts a duplicate |
| Session.AccessTokenId unique across all Sessions | Concurrent Session creations never produce colliding AccessTokenId values; verified via the token-generation mechanism's contract, not by exhaustive collision testing |
| Session.RefreshTokenId unique across all Sessions | Same treatment as AccessTokenId |
| At most one Credential with Status = Active per PersonId | Concurrent ChangePassword requests for the same Person: exactly one succeeds in producing the new Active Credential; storage-level constraint prevents two simultaneously Active Credential rows for one Person even if both application-level validations passed a race window |

## 6.6 Query Integration Testing

Source: 05_Queries.md §4, §5; 09_Persistence.md §3.2.

| Query | Test Case |
|---|---|
| GetCurrentPerson | Returns only the caller's own Person record, resolved from Session; rejects an invalid/expired Session |
| GetOrganizationsForPerson | Returns exactly one Organization in MVP (the Personal Organization); composes MembershipRepository and OrganizationRepository reads without either Repository gaining a write dependency on the other (09_Persistence.md §3.2) |
| GetMembershipsForPerson | Returns exactly one Membership in MVP |
| GetSessionsForPerson | Returns all of the caller's Sessions with Status = Active, per the "Active" filter defined in 09_Persistence.md §3.1.1; AccessTokenId and RefreshTokenId are absent from the projection (05_Queries.md §4.4) |
| GetPersonById | Returns the minimal (PersonId, DisplayName) projection only; Email and Status are absent (05_Queries.md §5.1) |
| Any Query, invoked during the Post-Commit isolation window (§6.3) | GetOrganizationsForPerson/GetMembershipsForPerson return complete data; a Query composing CredentialRepository or SessionRepository during this same window tolerates a temporarily empty result without treating it as a fault |

## 6.7 REST Transport Integration Testing

Source: 08_API.md §3-§5; 07_Contracts.md §5-§8.

| Test Case | Expected Result |
|---|---|
| Every endpoint in 08_API.md §5's Consolidated Endpoint List invokes exactly the Command/Query it is mapped to | No endpoint invokes a different Contract than the one 08_API.md §3/§4 declares |
| Every success path returns the HTTP status declared in 08_API.md §3/§4 | e.g. RegisterPerson → 201; AuthenticatePerson → 200; GetCurrentPerson → 200 |
| Every failure condition returns the HTTP status declared in 07_Contracts.md §8 Error Code Catalog | e.g. duplicate registration → 409 PERSON_ALREADY_EXISTS; invalid credentials → 401 INVALID_CREDENTIALS; session ownership failure → 403 FORBIDDEN |
| RefreshSession's 401-vs-404 distinction (07_Contracts.md §7) is observably correct | No matching Session/RefreshToken record at all → 404; a record exists but is expired/revoked → 401 |
| LogoutSession's 404-vs-409 distinction (07_Contracts.md §7) is observably correct | No Session record → 404; Session exists but already Closed → 409 |
| GetPersonById has no public REST endpoint | Confirmed absent from the public routing table; exercised only via its internal/service-to-service Contract shape (05_Queries.md §5.1) |

---

# 7. Event Testing

Source: 06_Domain_Events.md, in full.

## 7.1 Envelope Field Testing

For every one of the 10 MVP events, Event Testing SHALL verify the
Common Event Envelope (06_Domain_Events.md §4.1):

| Field | Test Case |
|---|---|
| EventId | Present and unique per event instance |
| EventType | Exact match to the event's declared name (e.g. `PersonRegistered`, not a variant) |
| AggregateType | Matches the enum value declared for that event in §4.2-§4.11 |
| AggregateId | Present, except LoginFailed with Reason = PersonNotFound (§4.6 exception) |
| OccurredAt | Equals commit time of the underlying state change, not request-received time or publish time |
| ActorIdentity | Matches the PersonId responsible, or `System` for SessionExpired, or absent for LoginFailed(PersonNotFound) — the three-way exception set is exhaustive; no other event has an absent or `System` ActorIdentity |
| SessionReference | Present/absent per the per-event rule in §4.2-§4.11 (e.g. present on PersonRegistered only if Post-Commit Session creation succeeded; always absent on OrganizationCreated/MembershipCreated) |
| DelegatedIdentity | Always absent in MVP, for every event, with no exception |
| ExecutionContext.CorrelationId | Present for every event except SessionExpired (System actor), where it is Optional |

## 7.2 Per-Event Payload Testing

For each of the 10 events, Event Testing SHALL verify every payload
field listed in 06_Domain_Events.md §4.2-§4.11 is present with the
correct type, and that no additional, undeclared field is present.

| Event | Payload Fields Under Test |
|---|---|
| PersonRegistered | PersonId, Email, DisplayName, OrganizationId, MembershipId — all Required |
| OrganizationCreated | OrganizationId, Name, Category (`Personal`), Status (`Active`) |
| MembershipCreated | MembershipId, PersonId, OrganizationId, Role (`Owner`), Status (`Active`) |
| LoginSucceeded | PersonId, SessionId |
| LoginFailed | Email, Reason (`PersonNotFound` \| `CredentialMissing` \| `InvalidPassword`) |
| SessionCreated | SessionId, PersonId, ExpiresAt |
| SessionExpired | SessionId, PersonId |
| LogoutCompleted | SessionId, PersonId |
| PersonUpdated | PersonId, Email, DisplayName (full current state, not a delta — §4.10 Design Note) |
| PasswordChanged | PersonId, CredentialId — explicitly verified to NEVER include PasswordHash or any password value (§2.5) |

## 7.3 Publishing Rule Testing

Source: 06_Domain_Events.md §5.

| Rule | Test Case |
|---|---|
| Publish-After-Commit (§5.1) | No event is observable by a consumer before the underlying Aggregate write has durably committed |
| Failed Persistence Never Produces an Event (§5.2) | A rolled-back Core Ownership Transaction produces none of PersonRegistered, OrganizationCreated, MembershipCreated |
| LoginFailed Is Not a State Change (§5.3) | LoginFailed is published on a failed authentication attempt even though no Aggregate state changed, confirming it is governed by the same immutability/audit rules as state-change events |
| Post-Commit Failures Do Not Suppress Already-Valid Events (§5.4) | Killing Session creation after a successful Core Ownership Transaction still results in PersonRegistered, OrganizationCreated, and MembershipCreated being published; only PersonRegistered.SessionReference is absent |

## 7.4 Event Ordering Testing

Source: 06_Domain_Events.md §6.

| Rule | Test Case |
|---|---|
| Per-Aggregate Ordering (§6.1) | For a single Session, SessionCreated is always observed before that same Session's eventual SessionExpired or LogoutCompleted, never the reverse |
| No Cross-Event Ordering Guarantee, LoginSucceeded/SessionCreated (§6.2) | A consumer correlates these two events via shared SessionId/PersonId, not via arrival order; a test SHALL NOT assert a fixed arrival order between them |
| No Cross-Event Ordering Guarantee, Registration Events (§6.2, §6.3) | PersonRegistered, OrganizationCreated, MembershipCreated may arrive in any order; a test SHALL NOT assert a fixed arrival order, and SHALL instead verify that regardless of arrival order, all three refer to already-consistent, already-committed data |
| Mutual Exclusivity of SessionExpired/LogoutCompleted (§6.4) | For any single SessionId across its full lifecycle, at most one of these two events is ever published — never both |

---

# 8. Security Testing

Source: 11_Security.md, in full.

## 8.1 Authentication Security Testing

Source: 11_Security.md §3.

| Test Case | Expected Result |
|---|---|
| Credential is validated against the claimed Person before any Session is created | No Session exists if Credential validation fails |
| Authentication does not modify Person, Organization, or Membership state | Repository write calls to PersonRepository, OrganizationRepository, MembershipRepository are verified absent during an AuthenticatePerson call, regardless of outcome |
| Person-not-found, no-active-Credential, and password-mismatch all produce the same class of outward response | External-facing response body/status is indistinguishable across the three LoginFailed Reason values (§8.2 elaborates the enumeration-specific assertion) |

## 8.2 Secrets Handling Testing

Source: 11_Security.md §5, §10.

| Test Case | Expected Result |
|---|---|
| Plain-text password is never persisted | No Repository write call, at any layer, receives a plaintext password value |
| Only PasswordHash is persisted for Credential | Verified against the Credential persistent entity schema (09_Persistence.md §4.5) |
| PasswordHash is generated only via the approved algorithm | Verified against 10_Configuration.md §2.2 (argon2id, RFC 9106) |
| No Domain Event payload or envelope field contains PasswordHash, plaintext password, AccessTokenId, or RefreshTokenId | Cross-checked against §7.2's per-event payload enumeration; none of the 10 payloads contain a secret field |
| Application/diagnostic logs never contain plaintext passwords, PasswordHash values, or full token values | Log output is inspected (or log-sink assertions applied) across every Command's success and failure paths |
| Error responses and exception traces never contain secret values | Forced-failure scenarios (e.g. a simulated persistence exception during ChangePassword) are inspected to confirm the returned error body and any captured trace exclude PasswordHash/token values (11_Security.md §11.9) |

## 8.3 Session/Token Security Testing

Source: 11_Security.md §8.

| Test Case | Expected Result |
|---|---|
| Session compromise does not compromise Person identity | Closing/expiring a Session leaves the Person Entity's own state unaffected (cross-referenced with §4.4) |
| Revoking one Session does not affect a Person's other concurrent Sessions | A Person with two active Sessions: closing one leaves the other's Status unchanged |
| Logout revokes both AccessTokenId and RefreshTokenId for the terminated Session | Both tokens fail subsequent validation after logout |
| Credential replacement does not revoke existing Sessions | After ChangePassword, a Session created under the old Credential remains valid until its own natural expiration or explicit logout |
| Every Session has a defined, finite ExpiresAt | No Session construction path permits a null/infinite expiration |
| Expired Sessions cannot be reactivated | A Session past ExpiresAt cannot transition back to Active; a new authentication is required |
| Refresh does not create a new Person, Credential, or Membership | Verified as the negative-space complement to §5.3's RefreshSession test |

## 8.4 Threat-Specific Test Cases

Source: 11_Security.md §11.1-§11.10. Each row states the Blueprint-level
mitigation under test and, where the mitigation is explicitly deferred
to implementation (per 11_Security.md's own framing), notes that this
document does not assert a positive test case beyond confirming the
deferral boundary itself.

| Threat | Blueprint-Level Test Case | Deferred to Implementation |
|---|---|---|
| §11.1 Credential Stuffing / Brute Force | Repeated failed authentication attempts each produce the same uniform LoginFailed outward response | Throttling/rate-limiting thresholds (10_Configuration.md) — not tested here |
| §11.2 User Enumeration | AuthenticatePerson's external response is identical whether the cause is PersonNotFound, CredentialMissing, or InvalidPassword; RegisterPerson's duplicate-email conflict response is confirmed as the accepted, narrower, deliberate exception | — |
| §11.3 Session/Token Theft | Covered by §8.3 (independent revocation, logout revokes both tokens) | Anomaly detection, IP/device binding — not tested here (excluded from MVP) |
| §11.4 Refresh Token Replay | Where rotation is enabled (Production default per 10_Configuration.md §2.1), a used RefreshTokenId is rejected on reuse; where rotation is disabled (non-Production), no such rejection is asserted | Reuse detection / token-family revocation — explicitly deferred by 03_Aggregates.md §11.1, not tested here |
| §11.5 Cross-Aggregate Consistency Abuse | Covered fully by §6.2's Core Ownership Transaction tests — no partial ownership state is ever observable | — |
| §11.6 Privilege Escalation via Direct Resource Access | Structural verification only: no Identity Query or Command contract (04_Commands.md §3, 05_Queries.md §3) accepts a direct Person-to-Resource reference as input | Not a runtime test — verified by Contract/schema inspection against 07_Contracts.md |
| §11.7 Session Fixation | Structural verification only: no Command's Input (04_Commands.md §4) accepts a client-supplied SessionId as an authentication input | Not a runtime test — verified by Contract/schema inspection |
| §11.8 Timing Attack | Not asserted by this document — 11_Security.md §11.8 classifies constant-time comparison as an implementation/runtime property, not a Blueprint-mandated behavior | Deferred entirely to implementation-level security testing |
| §11.9 Secret Leakage via Exception/Error Handling | Covered by §8.2's forced-failure/error-response assertions | — |
| §11.10 Replay of Expired Access Token | A request using an AccessTokenId associated with a Session past ExpiresAt is rejected, independent of whether SessionExpired has yet been processed asynchronously | — |

## 8.5 Authorization Boundary Testing

Source: 11_Security.md §4; 05_Queries.md §2.4.

| Test Case | Expected Result |
|---|---|
| No Identity Command or Query evaluates business permission on a Resource | Verified by Contract inspection: no 04_Commands.md or 05_Queries.md Contract accepts a Resource identifier or a permission/role parameter for evaluation |
| Session-ownership validation (UpdatePersonProfile, ChangePassword, LogoutSession) is confirmed as the *only* authorization-adjacent check Identity performs | No other form of permission check exists in any Command Handler under test |
| GetCurrentPerson/GetOrganizationsForPerson/GetMembershipsForPerson/GetSessionsForPerson each return only the caller's own data | Attempting to resolve another Person's data through any of these four Queries, using only a valid Session for a different Person, fails or returns only the caller's own scope — never a foreign Person's data |

---

# 9. Acceptance Criteria

Source: 02_Use_Cases.md §3; 14_MVP.md §8.

## 9.1 Per-Use-Case Acceptance Criteria

Each Use Case's Postconditions (02_Use_Cases.md) constitute its
Acceptance Criteria. A Use Case is accepted only when every listed
Postcondition is observably true after its Main Flow executes, and
every Failure Condition produces its documented outcome.

| Use Case | Acceptance Criteria Source |
|---|---|
| UC-001 Register Person | 02_Use_Cases.md UC-001 Postconditions (7 conditions) — cross-verified by §6.1's RegisterPerson Integration Test and §6.2's Unit of Work tests |
| UC-002 Create Credential | 02_Use_Cases.md UC-002 Postconditions — cross-verified by §5.1, §5.5 |
| UC-003 Authenticate Person | 02_Use_Cases.md UC-003 Postconditions — cross-verified by §5.2, §6.1 |
| UC-004 Create Session | 02_Use_Cases.md UC-004 Postconditions — cross-verified by §4.4, §5.2 |
| UC-005 Manage Person Profile | 02_Use_Cases.md UC-005 Postconditions — cross-verified by §5.4, §6.1 |
| UC-006 Change Credential | 02_Use_Cases.md UC-006 Postconditions — cross-verified by §5.5, §6.1 |
| UC-007 End Session | 02_Use_Cases.md UC-007 Postconditions (both Main Flows) — cross-verified by §5.3, §6.1 |
| UC-008 Create Organization Membership | 02_Use_Cases.md UC-008 Postconditions — cross-verified by §6.2 (part of the Core Ownership Transaction) |
| UC-009 Refresh Session | 02_Use_Cases.md UC-009 Postconditions — cross-verified by §5.3, §6.1 |

## 9.2 MVP Readiness Cross-Reference

The following table maps each item in 14_MVP.md §8's Readiness
Checklist to the test suite(s) in this document that verify it, closing
the loop between "what MVP requires" (14_MVP.md) and "how it is
confirmed" (this document).

| 14_MVP.md §8 Checklist Item | Verified By |
|---|---|
| User registration succeeds (atomic Person + Organization + Membership) | §6.1, §6.2 |
| Login succeeds | §6.1, §5.2 |
| Logout succeeds | §6.1, §5.3 |
| Refresh token works | §6.1, §5.3 |
| Personal Organization created automatically | §6.1, §6.2 |
| Owner Membership created automatically | §6.1, §6.2 |
| Session management functions correctly | §4.4, §5.3, §8.3 |
| Password change is implemented | §6.1, §5.5 |
| Identity events published per Event Ownership Table | §7.1, §7.2, §7.3 |
| REST APIs operational per API Scope | §6.7 |
| All MVP Domain Services and RegistrationApplicationService implemented | §5.1-§5.5 |
| No unreachable states exist | §4.6 |
| All lifecycle states classified (MVP or Future) | §2.4, §4.6 |

---

# 10. Test Coverage Matrix

This matrix confirms every Command, Query, and Event has at least one
assertion at every applicable test level. "N/A" indicates the level
does not apply to that item by its nature (e.g. Queries produce no
Events).

| Item | Unit (§3) | Aggregate (§4) | Domain/App Service (§5) | Integration (§6) | Event (§7) | Security (§8) |
|---|---|---|---|---|---|---|
| RegisterPerson | ✅ | ✅ (Person, Org, Membership, Credential, Session) | ✅ (§5.1) | ✅ (§6.1, §6.2, §6.3) | ✅ (3 events) | ✅ (§8.4 §11.5) |
| AuthenticatePerson | ✅ | ✅ (Session, Credential) | ✅ (§5.2) | ✅ (§6.1) | ✅ (3 events) | ✅ (§8.1, §8.4 §11.1 §11.2 §11.8) |
| UpdatePersonProfile | ✅ | ✅ (Person) | ✅ (§5.4) | ✅ (§6.1, §6.4) | ✅ (1 event) | ✅ (§8.5) |
| LogoutSession | ✅ | ✅ (Session) | ✅ (§5.3) | ✅ (§6.1) | ✅ (1 event) | ✅ (§8.3 §11.3) |
| RefreshSession | ✅ | ✅ (Session) | ✅ (§5.3) | ✅ (§6.1, §6.4) | N/A (no event) | ✅ (§8.3 §11.4 §11.10) |
| ChangePassword | ✅ | ✅ (Credential) | ✅ (§5.5) | ✅ (§6.1, §6.5) | ✅ (1 event) | ✅ (§8.2) |
| GetCurrentPerson | N/A | N/A | N/A | ✅ (§6.6) | N/A | ✅ (§8.5) |
| GetOrganizationsForPerson | N/A | N/A | N/A | ✅ (§6.6) | N/A | ✅ (§8.5) |
| GetMembershipsForPerson | N/A | N/A | N/A | ✅ (§6.6) | N/A | ✅ (§8.5) |
| GetSessionsForPerson | N/A | N/A | N/A | ✅ (§6.6) | N/A | ✅ (§8.5) |
| GetPersonById | N/A | N/A | N/A | ✅ (§6.6, §6.7) | N/A | N/A (platform-to-platform, no end-user authorization boundary applies per 05_Queries §5.1) |

**Result**: All 6 Commands and 5 Queries have an explicit test
disposition at every applicable level. No orphaned Contract remains.

---

# 11. MVP Boundary Protection for Testing

Consistent with §2.4, this document explicitly confirms:

- No test case in this document exercises SuspendOrganization,
  ResumeOrganization, ArchiveOrganization, RevokeMembership,
  RevokePerson, or RestoreArchivedOrganization — none exist as
  Commands (14_MVP.md §2)
- No test case exercises MFA, OAuth, or Enterprise SSO flows
  (00_Overview.md §11)
- No test case asserts a fixed event-arrival order where
  06_Domain_Events.md §6.2 explicitly declines to guarantee one
- No test case asserts specific validation rule text, format, or
  ordering beyond what 04_Commands.md already states (§2.3)

---

# 12. Cross-Document Alignment

## 12.1 Against 01_Domain_Model.md and 03_Aggregates.md

✅ Every Aggregate, Entity, Value Object, and Invariant tested in §3-§4
traces to an attribute or rule already declared there. No new
Aggregate, Entity, or Invariant introduced.

## 12.2 Against 02_Use_Cases.md

✅ Every Acceptance Criterion in §9.1 is sourced directly from a Use
Case's own Postconditions. No new Use Case behavior introduced.

## 12.3 Against 04_Commands.md

✅ Every Command-level test in §6.1 enumerates exactly the Postconditions
and Failure Conditions already stated per-Command in 04_Commands.md
§4.1-§4.6. No new Command behavior, Validation Rule, or Failure
Condition introduced.

## 12.4 Against 05_Queries.md

✅ Every Query-level test in §6.6 matches the Output, Preconditions, and
Failure Conditions already stated per-Query in 05_Queries.md §4-§5.

## 12.5 Against 06_Domain_Events.md

✅ Event Testing (§7) covers all 10 MVP events, matching the Envelope
(§4.1), per-event Payload (§4.2-§4.11), Publishing Rules (§5), and
Ordering guarantees (§6) exactly. No new Event introduced.

## 12.6 Against 09_Persistence.md

✅ Integration Testing (§6.2-§6.5) verifies exactly the Unit of Work
(§5), Post-Commit Isolation (§6), Concurrency (§8.4), and Uniqueness
(§8.3) guarantees 09_Persistence.md already defines. No new
Repository, transaction boundary, or constraint introduced.

## 12.7 Against 11_Security.md

✅ Security Testing (§8) covers Authentication (§3), Secrets (§5),
Session/Token Security (§8), and all 10 Threat Considerations (§11) of
11_Security.md. No new security requirement introduced; §8.4 explicitly
preserves 11_Security.md's own implementation-deferral boundaries
(e.g. §11.8 Timing Attack) rather than converting them into Blueprint
obligations.

## 12.8 Against 14_MVP.md

✅ §9.2's cross-reference table confirms every item in 14_MVP.md §8's
MVP Readiness Checklist has a corresponding verification path in this
document. §4.6, §11 confirm no Future-Scope lifecycle state is
positively tested.

## 12.9 Against 12_Validation.md (Forward Reference Only)

⏳ Not yet cross-checked — 12_Validation.md does not yet exist (see
header, §2.3). This is recorded as a forward note, not a failed
alignment check.

---

# 13. MVP Readiness Checklist

Testing Blueprint Version 1.0 is complete when:

✓ Unit Testing is defined for every Value Object and Entity attribute
  rule (§3)

✓ Aggregate Testing is defined for all 5 Aggregates, with MVP-reachable
  and Future-Scope lifecycle states explicitly distinguished (§4)

✓ Domain Service and Application Service Testing is defined for all 5
  services, with RegistrationApplicationService explicitly
  distinguished as an Application Service per ADR-0002 Decision 7.1
  (§5)

✓ Integration Testing covers every Command's Postconditions and
  Failure Conditions, the Core Ownership Transaction's atomicity, Post-
  Commit isolation, concurrency, and every required uniqueness
  constraint (§6)

✓ Event Testing covers the Common Envelope, all 10 per-event Payloads,
  Publishing Rules, and Ordering guarantees (§7)

✓ Security Testing covers Authentication, Secrets Handling,
  Session/Token Security, all 10 Threat Considerations, and the
  Authorization Boundary (§8)

✓ Acceptance Criteria are defined for all 9 Use Cases and cross-
  referenced against 14_MVP.md §8's Readiness Checklist (§9)

✓ A Test Coverage Matrix confirms no Command, Query, or Event lacks an
  applicable test disposition (§10)

✓ No test case exercises Future-Scope behavior (§11)

✓ Alignment verified against 01, 02, 03, 04, 05, 06, 09, 11, and
  14_MVP.md (§12); the 12_Validation.md forward reference is recorded
  as non-blocking (§12.9, §2.3)

✓ No new Aggregates, Commands, Queries, Events, or Business Rules
  introduced

---

# 14. Change Log

## Version 1.0.0 (2026-07-18)

Initial Testing Blueprint.

- Established Testing Design Principles (§2), including the explicit,
  non-blocking forward reference to 12_Validation.md (§2.3) and MVP
  Boundary Protection for testing (§2.4)
- Defined Unit Testing for Value Objects and Entity attributes (§3)
- Defined Aggregate Testing for all 5 Aggregates, with a Lifecycle
  Coverage Cross-Check against 14_MVP.md §4's "No Unreachable States"
  requirement (§4.6)
- Defined Domain Service and Application Service Testing for all 5
  services (§5), correctly distinguishing RegistrationApplicationService
  as an Application Service per ADR-0002 Decision 7.1
- Defined Integration Testing (§6), including full Command-level
  coverage (§6.1), Core Ownership Transaction / Unit of Work atomicity
  testing (§6.2), Post-Commit isolation testing (§6.3), concurrency
  testing (§6.4), uniqueness constraint testing (§6.5), Query
  integration testing (§6.6), and REST Transport integration testing
  (§6.7)
- Defined Event Testing covering the Common Envelope, all 10 per-event
  Payloads, Publishing Rules, and Ordering guarantees (§7)
- Defined Security Testing covering Authentication, Secrets Handling,
  Session/Token Security, all 10 Threat Considerations from
  11_Security.md §11.1-§11.10, and the Authorization Boundary (§8)
- Defined Acceptance Criteria for all 9 Use Cases, cross-referenced
  against 14_MVP.md §8's Readiness Checklist (§9)
- Added a consolidated Test Coverage Matrix confirming no Command,
  Query, or Event lacks an applicable test disposition (§10)
- Verified alignment against 01, 02, 03, 04, 05, 06, 09, 11, and
  14_MVP.md (§12); recorded the 12_Validation.md forward reference as
  non-blocking (§12.9)
- No new Aggregates, Commands, Queries, Events, or Business Rules
  introduced

---

**END OF DOCUMENT**

<!--
File: 13_Testing.md
-->
