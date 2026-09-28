<!--
Document ID: ID-03
Title: SmartCore Identity Platform Blueprint - Aggregates
Version: 1.1.1
Status: READY_FOR_GENERATION
Purpose: Define aggregate design rationale and boundaries for the Identity Platform Blueprint
Dependencies: 01_Domain_Model.md, 064_SmartCore_Blueprint_Standard, ADR-0002_Identity_Foundation_Clarifications
Change Log:
  - Version 1.1.1 (2026-07-15): Cross-check pass against 01_Domain_Model.md,
    02_Use_Cases.md, 04_Commands.md, 05_Queries.md, and 09_Persistence.md,
    following those documents' own recent revision passes. No new
    Aggregates, Commands, Queries, Events, or business rules introduced;
    no MVP scope changed. (1) CORRECTION: §4's Membership "Child Value
    Objects" field incorrectly listed `Role`. 01_Domain_Model.md §3's
    Value Object catalog defines exactly four Value Objects
    (EmailAddress, PasswordHash, AccessTokenId, RefreshToken); `Role`
    is not among them — it is a plain field on the Membership Aggregate
    per 01_Domain_Model.md §2. Corrected to "None in MVP" with an
    explanatory note, removing an assertion this document's own stated
    Dependency (01_Domain_Model.md) does not support. (2) Added §11.1,
    recording two Aggregate-boundary candidates (RefreshToken
    re-evaluation; additional Credential Types) that 09_Persistence.md
    §7.1/§4.5/§8.3 explicitly defer to this document's future decision.
    Neither is proposed as an Aggregate by this revision; §11.1 exists
    so this document is not silent about a decision it has been named
    as the owner of elsewhere in the Blueprint set.
  - Version 1.1.0 (2026-07-12): Added exception note in §9 clarifying that RegistrationApplicationService (not a Domain Service) coordinates initial cross-aggregate registration, aligned with 01_Domain_Model.md v1.2.0 and ADR-0002 Decision 7
  - Version 1.0.0 (2026-07-08): Initial aggregate design rationale for Session and Credential
-->

# 1. Aggregate Overview

The Identity Domain consists of five Aggregates:

- Person
- Organization
- Membership
- Session
- Credential

Each Aggregate owns an independent lifecycle and consistency boundary.

---

# 2. Person Aggregate

**Aggregate Root**: Person

**Root Entity Responsibilities**:
- Owns identity
- Manages profile attributes (Email, DisplayName)
- Tracks lifecycle status

**Child Entities**: None in MVP

**Child Value Objects**: EmailAddress

**Independent Lifecycle**:
- Registered → Active → Suspended → Archived
- Person identity persists across Session and Credential changes

---

# 3. Organization Aggregate

**Aggregate Root**: Organization

**Root Entity Responsibilities**:
- Owns organizational boundary
- Manages resource ownership
- Tracks lifecycle status

**Child Entities**: None in MVP

**Child Value Objects**: None

**Independent Lifecycle**:
- Created → Active → Suspended → Archived
- Future versions will implement suspend/resume/archive operations

Lifecycle diagrams represent MVP-visible lifecycle flow.

Full lifecycle transition rules, including reversible transitions such as Suspended → Active, are defined in ADR-0003.

No lifecycle transition commands are part of MVP.

---

# 4. Membership Aggregate

**Aggregate Root**: Membership

**Root Entity Responsibilities**:
- Connects Person to Organization
- Stores role and status
- Manages participation lifecycle

**Child Entities**: None in MVP

**Child Value Objects**: None in MVP. `Role` is a plain field on the
Membership Aggregate (01_Domain_Model.md §2), not a modeled Value
Object — 01_Domain_Model.md §3's Value Object catalog defines exactly
four: `EmailAddress`, `PasswordHash`, `AccessTokenId`, `RefreshToken`.
`Role` is not among them. (Corrected in v1.1.1; earlier versions of
this document listed `Role` here, which was not supported by
01_Domain_Model.md.)

**Independent Lifecycle**:
- Created → Active → Revoked
- Future versions will support multiple roles and delegated management

---

# 5. Session Aggregate

**Aggregate Root**: Session

**Root Entity Responsibilities**:
- Owns authenticated context
- Manages token lifecycle
- Tracks device and IP information

**Child Entities**: None in MVP

**Child Value Objects**: AccessTokenId, RefreshToken

**Independent Lifecycle**:
- Created → Authenticated → Active → Suspended (optional) → Expired → Closed
- Sessions are temporary; they have no effect on Person identity or Membership status

---

# 6. Credential Aggregate

**Aggregate Root**: Credential

**Root Entity Responsibilities**:
- Owns authentication material
- Manages credential lifecycle
- Handles password changes

**Child Entities**: None in MVP

**Child Value Objects**: PasswordHash

**Independent Lifecycle**:
- Created → Active → Replaced → Revoked
- Credential changes do not affect Person identity
- Version 1.x supports exactly one active Credential per Person

---

# 7. Session Aggregate Rationale

Session is modeled as an independent Aggregate because:

- It owns an independent lifecycle (Created → Authenticated → Active → Suspended (optional) → Expired → Closed)
- It may be revoked independently without affecting Person identity
- Multiple sessions may exist for a single Person simultaneously
- Session consistency is independent from Person consistency
- Session expiration SHALL NOT affect Person identity or Membership status
- Sessions represent temporary execution context, distinct from identity ownership

**Design Consequence**: Session changes do not cascade to Person. Person changes do not invalidate Sessions. Each maintains independent state.

---

# 8. Credential Aggregate Rationale

Credential is modeled as an independent Aggregate because:

- Credential lifecycle differs from Person lifecycle (Created → Active → Replaced → Revoked)
- Credential replacement is independent from Person profile updates
- Future authentication mechanisms may coexist with existing Credentials
- Credential consistency is independent from Person consistency
- Credential history and rotation do not affect Person identity
- Multiple credentials may be managed through future extensibility

**Design Note**: Version 1.x SHALL support exactly one active Credential per Person. Credential History is future scope.

**Design Consequence**: Credential changes represent authentication mechanism evolution, not identity change. Person identity remains stable across credential rotations.

---

# 9. Aggregate Consistency Boundaries

Each Aggregate maintains:

- **Consistency Boundary**: Internal state is consistent without external coordination
- **Root Entity**: Single aggregate root entity owns all internal entities
- **Transaction Scope**: All changes to an aggregate occur within a single transaction
- **Cross-Aggregate Consistency**: Maintained by Domain Services, not aggregates

**Exception**: Initial creation of Person, Organization, and Membership during registration is coordinated by RegistrationApplicationService (an Application Service, not a Domain Service), per the approved exception in ADR-0002 Decision 7. See 01_Domain_Model.md §8.

---

# 10. Future Persistence Considerations

Persistence layer design (Repository patterns, storage strategies, consistency mechanisms) will be defined in blueprint document 09_Persistence.md.

Each Aggregate Root will have exactly one owning Repository, to be specified in the Persistence blueprint.

This document establishes Aggregate boundaries; persistence implementation is deferred to the dedicated Persistence document.

---

# 11. Future Aggregate Considerations

Future versions MAY introduce:

- Device Identity as separate Aggregate
- Service Identity as separate Aggregate
- AI Agent Identity as separate Aggregate
- Organization Hierarchy as separate Aggregate
- Delegation as separate Aggregate
- Audit Log as separate Aggregate

Each future Aggregate MUST articulate its independent lifecycle and
consistency boundary per this pattern.

## 11.1 Candidates Flagged by Downstream Documents (Not Yet Decided)

The following two items are not proposed Aggregates in their own
right — no independent lifecycle or consistency boundary is asserted
for either here. They are recorded because 09_Persistence.md's
Persistence Blueprint explicitly defers their eventual Aggregate-
boundary evaluation to this document, and this document should not be
silent about a decision it has been named as the owner of:

- **RefreshToken re-evaluation** (deferred from 09_Persistence.md
  §7.1): `RefreshToken` is currently classified as internal storage
  detail owned by the Session Aggregate (§5; 01_Domain_Model.md §3).
  If a future capability requires token-family revocation, reuse
  detection, or comparable multi-device security behavior, that
  behavior would introduce independent lifecycle rules and invariants
  not owned by Session — at which point RefreshToken becomes a
  candidate for promotion to a child entity or independent Aggregate,
  per this document's own pattern (§9) and 09_Persistence.md §7.0's
  general Promotion Rule. No such behavior exists today, and no
  promotion is proposed by this revision.
- **Additional Credential Types** (deferred from 09_Persistence.md
  §4.5, §8.3): the Credential Aggregate (§6, §8) is scoped to exactly
  one Credential Type (Password) in MVP. Should future Credential
  Types (Passkey, WebAuthn, TOTP, Recovery Code, OAuth Identity) be
  introduced, whether each becomes a distinct child entity under a
  broadened Credential Aggregate, a family of sibling Aggregates, or
  something else is a decision for the ADR that introduces the first
  such type — not decided here. §8's "Version 1.x SHALL support
  exactly one active Credential per Person" constraint is scoped to
  MVP's single-Credential-Type model and would need re-evaluation
  alongside that same future decision.

---

**END OF DOCUMENT**