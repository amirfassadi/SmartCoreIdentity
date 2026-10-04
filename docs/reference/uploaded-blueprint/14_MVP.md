<!--
Document ID: ID-14
Title: SmartCore Identity Platform Blueprint - MVP Scope
Version: 1.2.1
Status: READY_FOR_GENERATION
Purpose: Define MVP scope and explicitly exclude future lifecycle operations from Version 1.0
Dependencies: 01_Domain_Model.md, 03_Aggregates.md, 08_API.md, 064_SmartCore_Blueprint_Standard, ADR-0002_Identity_Foundation_Clarifications.md
Change Log:
  - Version 1.2.1 (2026-07-14): Editorial polish pass on the v1.2.0
    PATCH /me alignment fix. (1) §7 now cross-references 08_API.md
    directly in the narrative text, not only in the Change Log, so the
    authoritative Transport source is visible in context. (2) §7
    endpoint list reordered so all `/me`-scoped endpoints are grouped
    together, followed by `/organizations` and `/sessions`, for
    readability only — no method, path, or Contract changed. (3) §7
    first reference to the capability now reads "Update Person Profile
    (`UpdatePersonProfile`)" to pair the narrative name with the
    formal Command name from 04_Commands.md on first use. (4) §7 adds
    a clarifying sentence that a capability's absence from the Future
    APIs list does not imply removal from the Domain Model, only
    deferred REST exposure. Added 08_API.md to this document's
    Dependencies list, reflecting the new in-text cross-reference. No
    MVP scope, lifecycle, Domain Service, Application Service,
    Command, Event, or business behavior changed.
  - Version 1.2.0 (2026-07-14): Added the missing REST endpoint mapping
    for UpdatePersonProfile (`PATCH /me`) to §7 (API Scope for MVP),
    aligning this document with 08_API.md v1.0.3.

    No MVP scope, lifecycle, Domain Service, Application Service,
    Command, Event, or business behavior changed.

    This update resolves the cross-document inconsistency identified
    during the API Transport Mapping review.
  - Version 1.1.1 (2026-07-14): Corrected §8 MVP Readiness Checklist, which still read "All MVP Domain Services are implemented" after §6 was split (v1.1.0) into separate Domain Service and Application Service lists. The checklist item now reads "All MVP Domain Services and the RegistrationApplicationService are implemented" so RegistrationApplicationService is not silently excluded from Version 1.0 readiness criteria. No MVP scope, Command, Event, or API content changed.
  - Version 1.1.0 (2026-07-14): Reclassified RegistrationDomainService as RegistrationApplicationService in §6 per ADR-0002 Decision 7.1, to synchronize with 01_Domain_Model.md v1.2.0 and 03_Aggregates.md v1.1.0; retitled §6 to "Domain Service and Application Service Scope for MVP" and split the single Domain Service list into separate Domain Service and Application Service lists. Removed a self-referencing entry (14_MVP.md) from this document's own Dependencies list and added ADR-0002_Identity_Foundation_Clarifications.md, which §6 now cites directly. No MVP scope, Command, Event, or API content changed.
  - Version 1.0.0 (2026-07-08): Initial MVP scope clarification, explicit exclusion of Suspend/Archive/Revoke operations
-->

# 1. MVP Scope Definition

The Identity Platform Version 1.0 supports the following operations:

**Person Management**
- Register Person
- Update Person Profile
- Retrieve Person Information

**Authentication**
- Login (create session)
- Logout (close session)
- Refresh Session

**Organization Management**
- Create Personal Organization (automatic during registration)
- Retrieve Organization Information

**Membership Management**
- Create Membership (automatic during registration)
- Retrieve Membership Information

**Session Management**
- Create Session
- Refresh Token
- Close Session

**Credential Management**
- Create Credential (during registration)
- Change Password

---

# 2. Future Lifecycle Operations

The following operations are NOT part of MVP:

- SuspendOrganization
- ResumeOrganization
- ArchiveOrganization
- RevokeMembership
- RevokePerson
- RestoreArchivedOrganization

**Rationale**: Lifecycle states (Suspended, Archived, Revoked) exist in the domain model for architectural completeness and future evolution. Version 1.0 does not implement the Commands, Use Cases, or APIs required to transition through these states.

---

# 3. Lifecycle State Constraints for MVP

**Organization Lifecycle in MVP**:

- Organizations created in Active state
- No transitions to Suspended, Archived, or other states
- Active state is operational for entire Version 1.0 lifecycle

**Membership Lifecycle in MVP**:

- Memberships created in Active state
- No transitions to Revoked or other states
- Active state is operational for entire Version 1.0 lifecycle

**Session Lifecycle in MVP**:

- All transitions (Created → Authenticated → Active → Expired → Closed) are implemented
- Optional Suspended state is supported but not required
- Session expiration is implemented

**Person Lifecycle in MVP**:

- Persons created in Active state
- No transitions to Suspended, Archived, or other states
- Active state is operational for entire Version 1.0 lifecycle

**Credential Lifecycle in MVP**:

- Credentials created in Active state
- Password change creates new Active credential (no history)
- No Credential History tracking in Version 1.0

---

# 4. No Unreachable States

Every lifecycle state in the Identity Domain Model is either:

1. **Reachable via MVP Command/Use Case**: Directly transitioned to through implemented operations
   - Examples: Session → Expired (via Session Expiration), Session → Closed (via Logout)

2. **Explicitly Marked Future Scope**: Documented as excluded from Version 1.0
   - Examples: Organization → Suspended (future), Membership → Revoked (future)

**No orphaned or unclassified states exist.**

---

# 5. Event Ownership Alignment

All events published in MVP are declared in 059_SmartCore_Identity_Platform.md Event Ownership Table:

MVP Events:
- PersonRegistered
- PersonUpdated
- PasswordChanged
- LoginSucceeded
- LoginFailed
- SessionCreated
- SessionExpired
- LogoutCompleted
- OrganizationCreated
- MembershipCreated

Future events (SuspendOrganizationRequested, RevokeMembershipCompleted, etc.) are explicitly scoped as future.

---

# 6. Domain Service and Application Service Scope for MVP

Implemented Domain Services in MVP:

1. **AuthenticationDomainService**: Credential validation and session creation
2. **SessionManagementDomainService**: Session refresh, expiration, and closure
3. **PersonManagementDomainService**: Person profile updates
4. **CredentialManagementDomainService**: Password changes

Implemented Application Services in MVP:

1. **RegistrationApplicationService**: Atomic registration coordination
   (Person + Personal Organization + Owner Membership). This is an
   Application Service, not a Domain Service — the single approved
   exception authorized by ADR-0002 Decision 7 (Command Model
   Coordination Exception for Identity Registration). See
   01_Domain_Model.md §8.

Future Domain Services (deferred to future versions):

- OrganizationManagementDomainService (suspend, archive, restore)
- MembershipManagementDomainService (revoke, reinstate, role change)
- DelegationDomainService
- AuditingDomainService

---

# 7. API Scope for MVP

Public REST APIs implemented in MVP:

```
POST   /auth/register         - User registration
POST   /auth/login            - User login (create session)
POST   /auth/logout           - User logout (close session)
POST   /auth/refresh          - Refresh access token
GET    /me                    - Get current user (Person) info
PATCH  /me                    - Update current Person profile
POST   /me/password           - Change password
GET    /me/memberships        - List user's memberships
GET    /organizations         - List user's organizations
GET    /sessions              - List active sessions
```

The `PATCH /me` endpoint exposes the Update Person Profile
(`UpdatePersonProfile`) Command defined in 04_Commands.md.

This endpoint is part of the Version 1.0 MVP scope because
Update Person Profile is listed as an MVP capability in §1.
Its omission from previous versions of this section was an
editorial inconsistency rather than an intentional scope
exclusion.

See 08_API.md §3 and §5 for the authoritative REST Transport mapping,
including HTTP method, path, and success status for every MVP Command
and Query. This section (§7) summarizes that mapping for MVP scope
purposes; 08_API.md remains the authoritative source for Transport
detail.

Future APIs (deferred):

- Suspend / Resume / Archive Organization
- Revoke / Reinstate Membership
- Role management and assignment
- Delegated administration

The absence of a Future API from this list does not imply removal
from the Domain Model; only that its public REST exposure is
deferred.

---

# 8. MVP Readiness Checklist

Identity Platform Version 1.0 is complete when:

✓ User registration succeeds (atomic Person + Organization + Membership creation)
✓ Login succeeds (Session creation, token issuance)
✓ Logout succeeds (Session closure)
✓ Refresh token works (Session refresh)
✓ Personal Organization is created automatically
✓ Owner Membership is created automatically
✓ Session management functions correctly
✓ Password change is implemented
✓ Identity events are published per Event Ownership Table
✓ REST APIs are operational per API Scope
✓ All MVP Domain Services and the RegistrationApplicationService are implemented
✓ No unreachable states exist
✓ All lifecycle states are classified (MVP or Future)
✓ Blueprint passes Structural Validation per 065_SmartCore_Blueprint_Validator_Specification
✓ Blueprint remains compatible with 066_SmartCore_AI_Code_Generation_Specification

---

**END OF DOCUMENT**