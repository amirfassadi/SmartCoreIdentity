# TASK: Identity Foundation Hardening and Reference Blueprint Freeze

## Objective

Resolve all architectural ambiguities, foundation gaps, governance
requirements, and blueprint validation issues identified during Identity
Platform review.

The goal is to establish Identity as the first SmartCore Reference
Blueprint (v1.0) and ensure compatibility with:

- 019_SmartCore_Identity_and_Session_Continuity_Model
- 041_SmartCore_Identity_Model
- 057_SmartCore_Tenancy_and_Ownership_Model
- 059_SmartCore_Identity_Platform
- 064_SmartCore_Blueprint_Standard
- 065_SmartCore_Blueprint_Validator_Specification
- 066_SmartCore_AI_Code_Generation_Specification

## File Locations

This task operates across two directory levels. Do not mix them.

**Foundation documents — stay in project root**
`SmartCore_Platform_Docs_v1/`
- 057_SmartCore_Tenancy_and_Ownership_Model.md (edit in place)
- 059_SmartCore_Identity_Platform.md (edit in place)
- ADR-0002_Identity_Foundation_Clarifications.md (create new, root level,
  alongside existing ADR-0001 / ADR-0001b)
- ADR-0003_Organization_and_Membership_Lifecycle_Standardization.md
  (create new, root level)

**Identity Blueprint Package — stay inside the `Identity/` subfolder**
`SmartCore_Platform_Docs_v1/Identity/`
- 00_Overview.md (already present, no change required by this task)
- 01_Domain_Model.md (edit in place — Changes 8–13)
- 03_Aggregates.md (create if missing, otherwise edit in place — Change 14–15)
- 14_MVP.md (create if missing, otherwise edit in place — Phase 5)

Do not create Blueprint files (00–16, capability.machine.yaml) anywhere
outside `Identity/`. Do not create Foundation documents or ADRs inside
`Identity/` — they are platform-wide and belong in the project root.

All changes MUST preserve backward compatibility.

Do NOT introduce new business capabilities.

Do NOT expand MVP scope.

Do NOT add implementation-specific technology decisions.

Do NOT modify 019, 041, 064, 065, or 066. These remain fixed
reference inputs for this task.

---

# PHASE 0 — Governance

## Task 0.1 — Create ADR-0002

Create:

`ADR-0002_Identity_Foundation_Clarifications.md`

Purpose:

Formalize architectural clarifications required before Identity
Blueprint freeze.

Status: **Proposed**

Decision Scope:

1. Registration Boundary Clarification
2. Authorization Boundary Clarification
3. Membership Role Model Clarification
4. Identity Platform Person-Centric Boundary
5. Event Ownership Documentation
6. Future Identity Types Documentation

Decision Text MUST include:

```text
Identity Platform SHALL remain Person-centric in Version 1.x.

Future identity types including:
- Device Identity
- Service Identity
- AI Agent Identity

SHALL be introduced through extension and SHALL NOT alter existing
Person identity semantics.
```

Consequences:

- 057 version: 1.0 → 1.1
- 059 version: 1.0 → 1.1

Note: Status SHALL remain "Proposed" until the resulting document
changes have passed Architecture Validation Review. Only then SHALL
Status be updated to "Accepted", with an acceptance date recorded in
the ADR.

---

## Task 0.2 — Create ADR-0003

Create:

`ADR-0003_Organization_and_Membership_Lifecycle_Standardization.md`

Purpose:

Standardize Organization and Membership lifecycles for all future
SmartCore Capability Platforms.

Status: **Proposed** (same acceptance rule as ADR-0002 above)

Decision Scope:

1. Organization Lifecycle
2. Membership Lifecycle

Consequences:

- 057 version: 1.1 → 1.2

---

## Task 0.3 — Version Updates and Traceability

Update metadata and change logs:

**057_SmartCore_Tenancy_and_Ownership_Model.md**
- Version: 1.0 → 1.2
- Change Log SHALL explicitly reference both ADR-0002 and ADR-0003

**059_SmartCore_Identity_Platform.md**
- Version: 1.0 → 1.1
- Change Log SHALL explicitly reference ADR-0002

Every Change Log entry SHALL cite the ADR that authorized it. Untraceable
changes are not permitted (per 064 §6.6 Traceability).

---

# PHASE 1 — Foundation Document Hardening

## File: 057_SmartCore_Tenancy_and_Ownership_Model.md

### Change 1 — Registration Core Transaction

Add after "Registration Model":

```text
Registration Core Transaction

The atomic ownership transaction SHALL include:
- Person creation
- Organization creation
- Membership creation

The transaction SHALL commit after these ownership entities are
successfully created.

Additional Identity Platform operations MAY occur after successful
commit, including:
- Credential creation
- Initial session creation
- Domain event publication

These operations SHALL NOT invalidate ownership consistency.
```

### Change 2 — Organization Lifecycle

```text
Organization Lifecycle

Created → Active → Suspended → Archived

Organizations SHALL follow this lifecycle unless superseded by a
future ADR.
```

### Change 3 — Membership Lifecycle

```text
Membership Lifecycle

Created → Active → Revoked

Memberships SHALL follow this lifecycle unless superseded by a
future ADR.
```

---

# PHASE 2 — Identity Platform Hardening

## File: 059_SmartCore_Identity_Platform.md

### Change 4 — Explicit Authorization Boundary

```text
Authorization Boundary

The Identity Platform SHALL provide:
- Identity context
- Authentication outcomes
- Membership information
- Organization information

The Identity Platform SHALL NOT evaluate business permissions.

Business authorization remains the responsibility of consuming
Capability Platforms.
```

### Change 5 — MVP Role Model

```text
Role Model

In Version 1.0:
Role SHALL be an attribute of Membership.

Supported value:
- Owner

Future versions MAY introduce additional role values without
introducing a separate Role Aggregate.
```

### Change 6 — Event Ownership Table

```text
| Event               | Owner Capability |
|---------------------|-------------------|
| PersonRegistered    | Identity |
| PersonUpdated       | Identity |
| PasswordChanged     | Identity |
| LoginSucceeded      | Identity |
| LoginFailed         | Identity |
| SessionCreated      | Identity |
| SessionExpired      | Identity |
| LogoutCompleted     | Identity |
| OrganizationCreated | Identity |
| MembershipCreated   | Identity |

This hardening task SHALL NOT introduce additional lifecycle events.

Lifecycle events beyond those listed above are outside the scope of
Identity Blueprint Version 1.0 and MAY be introduced through future ADRs.
```

### Change 7 — Future Identity Types

```text
Future Identity Types

Future versions MAY introduce:
- Device Identity
- Service Identity
- AI Agent Identity

These identity types SHALL extend the platform without modifying
Person identity semantics.
```

---

# PHASE 3 — Blueprint Corrections

## File: 01_Domain_Model.md

### Change 8 — Complete Metadata

Document header SHALL contain:
- Document ID
- Title
- Version
- Status
- Purpose
- Dependencies
- Change Log

### Change 9 — Add Relationships Section

```text
Relationships

Person (1) → (1..*) Membership
Organization (1) → (1..*) Membership
Membership (1) → (1) Person
Membership (1) → (1) Organization
Person (1) → (0..*) Session
Person (1) → (1) Active Credential

Do NOT introduce Credential History in MVP.
Credential History belongs to future extensibility.
```

### Change 10 — Add Missing Domain Services

```text
PersonManagementDomainService
Responsibilities: Update Person Profile
Produces: PersonUpdated
```

```text
CredentialManagementDomainService
Responsibilities: Change Password, Replace Credential
Produces: PasswordChanged
```

Do NOT introduce `OrganizationManagementDomainService` or
`MembershipManagementDomainService` into MVP. Lifecycle transition
commands for Suspended / Archived / Revoked are future scope.

### Change 11 — Correct Session Lifecycle

```text
Created → Authenticated → Active → Suspended (optional) → Expired → Closed
```

Must align with Document 019 §6.

### Change 12 — Resolve SessionToken Inconsistency

Choose exactly one:
- **Preferred**: add `AccessTokenId` to Session attributes
- **Alternative**: remove the SessionToken Value Object entirely

Unused Value Objects SHALL NOT exist.

### Change 13 — Event Audit Requirements

```text
Event Audit Requirements

All Domain Events SHALL support:
- Actor Identity
- Session Reference (optional)
- Delegated Identity (optional)
- Timestamp
- Execution Context

Detailed event contracts are defined in 06_Domain_Events.md.
```

---

# PHASE 4 — Aggregate Design Rationale

## File: 03_Aggregates.md

### Change 14 — Session Aggregate Rationale

```text
Session is modeled as an independent Aggregate because:
- It owns an independent lifecycle.
- It may be revoked independently.
- Multiple sessions may exist for a single Person.
- Session consistency is independent from Person consistency.
```

### Change 15 — Credential Aggregate Rationale

```text
Credential is modeled as an independent Aggregate because:
- Credential lifecycle differs from Person lifecycle.
- Credential replacement is independent from Person updates.
- Future authentication mechanisms may coexist.
- Credential consistency is independent from Person consistency.

Note:
Version 1.x SHALL support exactly one active Credential per Person.
Credential History is future scope.
```

---

# PHASE 5 — MVP Scope Clarification

## File: 14_MVP.md

```text
Future Lifecycle Operations

The following operations are NOT part of MVP:
- SuspendOrganization
- ArchiveOrganization
- RevokeMembership

Lifecycle states exist for architectural completeness and future
evolution.

No Commands, APIs, or Use Cases in Identity Blueprint Version 1.0 SHALL trigger these
transitions.
```

---

# GUARDRAIL FOR FUTURE BLUEPRINT SECTIONS (not part of this task's Acceptance Criteria)

These rules do not apply yet — no `09_Persistence.md` or `03_Aggregates.md`
exist as complete documents at this stage beyond what Phase 4 adds. Record
them now so they are enforced when those documents are authored next:

```text
- Every Aggregate SHALL have exactly one owning Repository, to be
  defined in 09_Persistence.md.
- No Repository SHALL be created for a non-Aggregate Entity
  (e.g., no separate CredentialHistoryRepository unless Credential
  History becomes an Aggregate in a future version).
```

---

# Acceptance Criteria

- [ ] ADR-0002 created (Status: Proposed)
- [ ] ADR-0003 created (Status: Proposed)
- [ ] 057 updated to version 1.2, Change Log references ADR-0002 and ADR-0003
- [ ] 059 updated to version 1.1, Change Log references ADR-0002
- [ ] No Domain Service produces an event that is not declared in the 059 Event Ownership Table
- [ ] Registration Boundary ambiguity resolved
- [ ] Authorization Boundary explicitly closed
- [ ] Role Model clarified
- [ ] Organization Lifecycle standardized
- [ ] Membership Lifecycle standardized
- [ ] Event Ownership table added
- [ ] Future Identity Types documented
- [ ] 01_Domain_Model metadata complete
- [ ] Relationships section added (no Credential History in MVP)
- [ ] Missing Domain Services added (Person, Credential only)
- [ ] Session lifecycle aligned with 019 (Suspended marked optional)
- [ ] SessionToken inconsistency resolved (one option chosen)
- [ ] Event audit requirements documented (reference only, not full spec)
- [ ] Aggregate rationale documented for Session and Credential
- [ ] 14_MVP.md explicitly excludes Suspend/Archive/Revoke operations
- [ ] No new business capabilities introduced
- [ ] No MVP scope expansion performed
- [ ] Every event declared in the 059 Event Ownership Table has either
      a documented producing Domain Service, or an explicit Future Scope
      designation in 14_MVP.md — no orphaned or unclassified events remain
- [ ] No unreachable MVP states exist (every lifecycle state is either
      reachable via a Command or explicitly marked Future Scope)
- [ ] Blueprint passes Structural Validation
- [ ] Blueprint remains compatible with 065 Validator
- [ ] Blueprint remains compatible with 066 AI Generation rules

Expected Result:

Identity becomes the SmartCore Reference Blueprint v1.0 and serves as
the architectural template for future Capability Blueprints.

Next step after this task completes: bring the modified 057, 059,
01_Domain_Model, 03_Aggregates, 14_MVP, ADR-0002, and ADR-0003 back for
Architecture Validation Review before proceeding to generate 02_Use_Cases.md
through 16_Examples.md.
