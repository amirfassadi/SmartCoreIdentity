```{=html}
<!--
Document ID: ID-02
Title: SmartCore Identity Platform Blueprint - Use Cases
Version: 1.2.0
Status: READY_FOR_GENERATION
Purpose: Define user goals, business-level use cases, and actor interactions for the Identity Platform Blueprint
Dependencies: 00_Overview.md, 01_Domain_Model.md, 03_Aggregates.md, 14_MVP.md, 019_SmartCore_Identity_and_Session_Continuity_Model, 041_SmartCore_Identity_Model, 057_SmartCore_Tenancy_and_Ownership_Model, 059_SmartCore_Identity_Platform, 064_SmartCore_Blueprint_Standard, 065_SmartCore_Blueprint_Validator_Specification, 066_SmartCore_AI_Code_Generation_Specification, ADR-0002_Identity_Foundation_Clarifications
Change Log:
  - Version 1.2.0 (2026-07-12): Reclassified RegistrationDomainService references as RegistrationApplicationService per ADR-0002 Decision 7.1 and 01_Domain_Model.md v1.2.0; UC-001 and UC-008 Domain References now distinguish Application Services from Domain Services; Event Publication Map column header updated accordingly. No use case behavior changed.
  - Version 1.1.0 (2026-07-09): Added Refresh Session MVP use case, corrected Identity event publication mapping, corrected aggregate participation matrix consistency, corrected lifecycle validation statements. No architecture changes introduced.
  - Version 1.0.1 (2026-07-09): Clarified registration event timing, MembershipCreated ownership reference, and Session lifecycle wording. No scope or capability changes.
  - Version 1.0.0 (2026-07-09): Initial use case specification for Identity Platform MVP
-->
```
# 1. Overview

This document defines all externally-visible use cases for the Identity
Platform Blueprint.

Use cases describe user goals and the main flow of interactions required
to achieve those goals within the Identity Platform.

Use cases do NOT define:

-   Database design or persistence strategy
-   API contracts or technology choices
-   Authorization decisions or permission evaluation
-   New business capabilities beyond Identity Platform scope
-   New aggregates or lifecycle states
-   New events not defined by Identity Foundation (059)

All use cases align with:

-   Domain Model (01_Domain_Model.md)
-   Aggregate Definitions (03_Aggregates.md)
-   MVP Scope (14_MVP.md)
-   Event Ownership (059_SmartCore_Identity_Platform.md §9)
-   Registration Architecture (059_SmartCore_Identity_Platform.md §6)

------------------------------------------------------------------------

# 2. Use Case Structure

Each use case includes:

-   **UC Code**: Unique identifier (UC-NNN)
-   **Title**: Use case name
-   **Actor**: Primary actor initiating the use case
-   **Goal**: Desired outcome from actor perspective
-   **Preconditions**: State required before use case begins
-   **Main Flow**: Primary sequence of steps to achieve goal
-   **Alternative Flows**: Optional variations of main flow
-   **Failure Conditions**: Scenarios where use case cannot complete
-   **Postconditions**: State after successful completion
-   **Domain References**: Aggregates, Domain Services, Events involved

------------------------------------------------------------------------

# 3. Use Cases

## UC-001: Register Person

**Actor**: New User

**Goal**: Create a new Person identity and establish the ownership
boundary.

**Preconditions**:

-   Registration request received with user identity information
    (typically email, password, display name)
-   No Person currently exists with the provided identity (email)
-   System is operational

**Main Flow**:

1.  Receive registration request
2.  Execute Registration Core Transaction:
    -   Create Person (Aggregate: Person)
    -   Create Personal Organization (Aggregate: Organization)
    -   Create Owner Membership (Aggregate: Membership)
    -   Commit transaction atomically
3.  Upon successful commit, execute Post-Commit Identity Operations:
    -   Create Credential for the Person (Aggregate: Credential)
    -   Create Initial Session through Authentication lifecycle
        (Aggregate: Session)
    -   Publish Identity Events: PersonRegistered, OrganizationCreated, MembershipCreated

Note: PersonRegistered indicates completion of the registration
experience including initial authentication capability. OrganizationCreated
and MembershipCreated are independent events published by RegistrationApplicationService.
Ownership consistency is already guaranteed by the Core Ownership Transaction.
Failure of post-commit operations SHALL NOT invalidate Person,
Organization, or Membership.
4. Return registration success to user

**Alternative Flows**:

-   Core Transaction may be retried if transient failures occur
-   Post-commit operation failures do not invalidate registration
    success
-   Post-commit recovery is implementation-specific

**Failure Conditions**:

-   Person with same email already exists: Return conflict error;
    rollback transaction
-   Core Transaction fails: Rollback all ownership changes; return
    failure to user
-   Email format invalid: Return validation error before transaction
-   Personal Organization creation fails: Rollback transaction; return
    failure
-   Membership creation fails: Rollback transaction; return failure

**Postconditions**:

-   Person exists with status = Active
-   Personal Organization exists with status = Active, category =
    Personal
-   Owner Membership exists linking Person to Organization, status =
    Active
-   Credential exists for Person, status = Active
-   Initial Session exists, status = Authenticated
-   PersonRegistered, OrganizationCreated, MembershipCreated Events published
-   Email is unique across all Persons

**Domain References**:

-   Aggregates: Person, Organization, Membership, Credential, Session
-   Application Services: RegistrationApplicationService (ADR-0002 Decision 7 exception; see 01_Domain_Model.md §8)
-   Events: PersonRegistered, OrganizationCreated, MembershipCreated
-   Lifecycle: Person (Registered→Active), Organization
    (Created→Active), Membership (Created→Active)
-   Architecture: Core Ownership Transaction + Post-Commit Operations
    (059 §6)

**Constraints**:

-   Registration establishes canonical ownership path (057 §3)
-   Personal Organization is the only Organization type created in MVP
-   Owner is the only Membership Role created in MVP
-   No Credential History tracking in MVP
-   Sessions created as independent Aggregate (Session is not owned by
    Person in consistency boundary)

------------------------------------------------------------------------

## UC-002: Create Credential

**Actor**: Registration Flow / Person (during initial setup or
credential change)

**Goal**: Create an authentication credential for a Person.

**Preconditions**:

-   Person exists with status = Active (UC-001 creates initial
    credential)
-   No active Credential currently exists for the Person (or existing
    credential is being replaced)
-   System is operational

**Main Flow**:

1.  Receive credential creation request (typically password)
2.  Validate credential format and strength
3.  Create Credential Aggregate for Person:
    -   Generate PasswordHash
    -   Set status = Active
    -   Record CreatedAt timestamp
4.  Persist Credential
5.  Return credential creation success

**Alternative Flows**:

-   Credential replacement (see UC-006 Change Credential for lifecycle)

**Failure Conditions**:

-   Person does not exist: Return not-found error
-   Credential format invalid or too weak: Return validation error
-   Persistence fails: Return failure; transaction not committed
-   Person has existing active Credential: Return conflict (use UC-006
    for replacement)

**Postconditions**:

-   Credential exists for Person, status = Active
-   PasswordHash securely stores password information
-   Credential can be used for authentication (UC-003)
-   One active Credential per Person exists in MVP

**Domain References**:

-   Aggregates: Credential, Person
-   Domain Services: CredentialManagementDomainService
-   Events: (Published during registration via UC-001; not published
    independently in MVP)
-   Lifecycle: Credential (Created→Active)

**Constraints**:

-   No Credential History in MVP (Version 1.x)
-   No multiple active Credentials per Person in MVP
-   Credential is independent Aggregate with own consistency boundary
-   Credential replaces password, not appends (see UC-006)

------------------------------------------------------------------------

## UC-003: Authenticate Person

**Actor**: User

**Goal**: Establish authenticated context by validating credentials.

**Preconditions**:

-   Person exists with status = Active
-   Active Credential exists for Person
-   Authentication request received (typically email and password)
-   System is operational

**Main Flow**:

1.  Receive authentication request (email, password)
2.  Resolve Person by email
3.  Retrieve active Credential for Person
4.  Validate provided password against PasswordHash
5.  Validation result:
    -   If valid: Proceed to UC-004 Create Session
    -   If invalid: Generate LoginFailed Event, return authentication
        failure
6.  Return authentication outcome

**Alternative Flows**:

-   Multi-attempt authentication may be rate-limited
    (implementation-specific)
-   Session creation may occur in same operation or follow separately
    (see UC-004)

**Failure Conditions**:

-   Person not found by email: Generate LoginFailed Event
-   No active Credential for Person: Generate LoginFailed Event
-   Password validation fails: Generate LoginFailed Event
-   Credential validation system unavailable: Return temporary failure
-   Multiple Persons with same email: System integrity error (should
    never occur)

**Postconditions**:

-   LoginSucceeded Event published if authentication succeeded
-   LoginFailed Event published if authentication failed
-   Session created if authentication succeeded (see UC-004)
-   No Session created if authentication failed
-   No Person state changed by authentication attempt

**Domain References**:

-   Aggregates: Person, Credential, Session
-   Domain Services: AuthenticationDomainService
-   Events: LoginSucceeded, LoginFailed, SessionCreated
-   Lifecycle: No state changes to Person or Credential

**Constraints**:

-   Authentication answers "Who are you?" not "What can you do?" (059
    §7)
-   Authorization decisions remain separate from authentication
-   Multiple concurrent authentication attempts allowed
-   Authentication does not change identity state

------------------------------------------------------------------------

## UC-004: Create Session

**Actor**: Authentication Flow / System

**Goal**: Establish an authenticated session after successful
authentication.

**Preconditions**:

-   Person exists with status = Active
-   Authentication succeeded (UC-003)
-   Session creation request received with Person context
-   System is operational

**Main Flow**:

1.  Create Session Aggregate for Person:
    -   Set PersonId
    -   Generate AccessTokenId
    -   Generate RefreshTokenId
    -   Capture DeviceInfo (if provided)
    -   Capture IpAddress
    -   Calculate ExpiresAt timestamp
    -   Set status = Created
    -   Record CreatedAt timestamp
2.  Persist Session
3.  Transition Session status to Authenticated
4.  Publish SessionCreated Event
5.  Return Session and tokens to client

**Alternative Flows**:

-   Session may be created during registration (UC-001 post-commit
    phase)
-   Session may be created on independent login (UC-003 complete flow)

**Failure Conditions**:

-   Person does not exist: Return not-found error; no Session created
-   Authentication did not succeed: No Session created (see UC-003)
-   Session persistence fails: Return failure; transaction not committed
-   Token generation fails: Return system error

**Postconditions**:

-   Session exists for Person, status = Authenticated or Active
-   AccessTokenId and RefreshTokenId exist and are distinct
-   Session has defined expiration time
-   SessionCreated Event published
-   Session is independent from Person lifecycle
-   Multiple Sessions may exist simultaneously for same Person

**Domain References**:

-   Aggregates: Session, Person
-   Domain Services: AuthenticationDomainService,
    SessionManagementDomainService
-   Events: SessionCreated
-   Lifecycle: Session (Created→Active)

**Constraints**:

-   Session is independent Aggregate with own consistency boundary
    (03_Aggregates.md §7)
-   Session changes do not cascade to Person
-   Person changes do not invalidate Sessions
-   Session expiration does not affect Person identity (019 §2)
-   Multiple Sessions per Person allowed
-   Sessions are device-independent

------------------------------------------------------------------------

## UC-005: Manage Person Profile

**Actor**: User (Person)

**Goal**: Update Person profile information.

**Preconditions**:

-   Person exists with status = Active
-   Authenticated session exists for Person
-   Profile update request received (typically email, displayName, or
    both)
-   System is operational

**Main Flow**:

1.  Validate authenticated session belongs to Person
2.  Receive profile update request (email, displayName, etc.)
3.  Validate update constraints:
    -   New email (if provided) not already in use
    -   DisplayName length and format valid
4.  Update Person attributes:
    -   Email (if provided and valid)
    -   DisplayName (if provided and valid)
    -   Set UpdatedAt timestamp
5.  Persist Person changes
6.  Publish PersonUpdated Event
7.  Return updated profile to Person

**Alternative Flows**:

-   Email change requires additional validation (uniqueness across all
    Persons)
-   DisplayName change is unconstrained

**Failure Conditions**:

-   Person does not exist: Return not-found error
-   Session does not belong to Person: Return permission error
-   New email already in use: Return conflict error; no update committed
-   Email format invalid: Return validation error; no update committed
-   DisplayName validation fails: Return validation error; no update
    committed
-   Persistence fails: Return failure; transaction not committed

**Postconditions**:

-   Person attributes updated (email and/or displayName)
-   PersonId remains immutable
-   Person status unchanged (remains Active in MVP)
-   UpdatedAt timestamp reflects change
-   PersonUpdated Event published
-   Person identity remains stable across profile changes

**Domain References**:

-   Aggregates: Person, Session
-   Domain Services: PersonManagementDomainService
-   Events: PersonUpdated
-   Lifecycle: No state changes to Person lifecycle (status remains
    Active)

**Constraints**:

-   PersonId is immutable
-   Email must be unique across all Persons
-   DisplayName has no uniqueness requirement
-   Profile changes do not affect Session or Credential state
-   Profile changes do not invalidate existing Sessions

------------------------------------------------------------------------

## UC-006: Change Credential

**Actor**: User (Person)

**Goal**: Replace active credential (password).

**Preconditions**:

-   Person exists with status = Active
-   Active Credential exists for Person
-   Authenticated session exists for Person
-   Credential change request received (current password and new
    password)
-   System is operational

**Main Flow**:

1.  Validate authenticated session belongs to Person
2.  Receive credential change request (current password, new password)
3.  Validate current password against existing Credential PasswordHash
4.  Validate new password format and strength
5.  Create new Credential Aggregate:
    -   Generate new PasswordHash from new password
    -   Set status = Active
    -   Set PasswordVersion = prior version + 1
    -   Record CreatedAt timestamp
6.  Persist new Credential
7.  Mark old Credential status = Replaced (or archive/delete per
    implementation)
8.  Publish PasswordChanged Event
9.  Return credential change success

**Alternative Flows**:

-   Initial Credential creation during registration (UC-001) does not
    require current password validation
-   Administrative credential reset (if implemented in future) may not
    require current password

**Failure Conditions**:

-   Person does not exist: Return not-found error
-   Session does not belong to Person: Return permission error
-   Current password validation fails: Return invalid password error; no
    new credential created
-   New password format invalid or too weak: Return validation error; no
    new credential created
-   New Credential persistence fails: Return failure; old Credential
    remains active
-   Active Credential not found: Return system error

**Postconditions**:

-   New Credential exists for Person, status = Active
-   Old Credential status changed to Replaced
-   Person can authenticate with new password only
-   PasswordChanged Event published
-   No Credential History tracking in MVP

**Domain References**:

-   Aggregates: Credential, Person, Session
-   Domain Services: CredentialManagementDomainService
-   Events: PasswordChanged
-   Lifecycle: Credential (Active→Replaced)

**Constraints**:

-   No Credential History in MVP; replaced credentials archived or
    deleted
-   Only one active Credential per Person in MVP
-   Credential change does not affect active Sessions
-   Credential replacement requires current password validation
    (security)
-   Person identity unchanged by credential replacement

------------------------------------------------------------------------

## UC-007: End Session

**Actor**: User or System

**Goal**: Complete session lifecycle through logout or expiration.

**Preconditions**:

-   Session exists for Person, status = Active (or
    Authenticated/Suspended)
-   System is operational

**Main Flow (User Logout)**:

1.  Receive logout request from authenticated client
2.  Validate Session exists and belongs to requestor
3.  Update Session status to Closed
4.  Revoke AccessTokenId and RefreshTokenId (implementation-specific)
5.  Persist Session closure
6.  Publish LogoutCompleted Event
7.  Return logout success

**Main Flow (System Expiration)**:

1.  Scheduled job identifies Session where ExpiresAt \<= CurrentTime
2.  Update Session status to Expired
3.  Revoke AccessTokenId and RefreshTokenId (implementation-specific)
4.  Persist Session expiration
5.  Publish SessionExpired Event

**Alternative Flows**:

-   Session may transition through optional Suspended state before
    Closed (019 §6)
-   Concurrent requests using expired Session credentials return
    unauthorized

**Failure Conditions**:

-   Session does not exist: Return not-found error (for logout)
-   Session already closed: Return already-closed error (for logout)
-   Logout request from different Person: Return permission error
-   Persistence fails: Return failure; Session state not changed
-   Token revocation fails (if implementation uses external service):
    Log error; Session marked closed

**Postconditions**:

-   Session status = Closed (for logout) or Expired (for expiration)
-   AccessTokenId and RefreshTokenId revoked
-   LogoutCompleted Event published (for logout)
-   SessionExpired Event published (for expiration)
-   Person identity unchanged
-   Person can create new Session (authentication required)

**Domain References**:

-   Aggregates: Session, Person
-   Domain Services: SessionManagementDomainService
-   Events: LogoutCompleted, SessionExpired
-   Lifecycle: Session (Active→Closed or Active→Expired)

**Constraints**:

-   Session expiration does not affect Person identity (019 §2)
-   Session closure does not invalidate Person or Membership
-   Multiple concurrent Sessions for Person exist independently
-   Closed Session cannot be reactivated
-   New Session requires new authentication (UC-003)

------------------------------------------------------------------------

## UC-008: Create Organization Membership

**Actor**: Registration Flow

**Goal**: Establish membership relationship between Person and
Organization during registration.

**Preconditions**:

-   Person exists (created in UC-001)
-   Organization exists (created in UC-001)
-   Membership creation is part of registration transaction
-   System is operational

**Main Flow**:

1.  During Registration Core Transaction (UC-001):
    -   Create Membership Aggregate linking Person to Organization
    -   Set Role = Owner (only role in MVP)
    -   Set status = Active
    -   Record CreatedAt timestamp
2.  Persist Membership
3.  Commit Membership as part of atomic registration transaction

**Alternative Flows**:

-   Membership creation succeeds as part of registration transaction
-   Membership creation is not invoked independently in MVP

**Failure Conditions**:

-   Person does not exist: Transaction failure; rollback entire
    registration
-   Organization does not exist: Transaction failure; rollback entire
    registration
-   Membership persistence fails: Transaction failure; rollback entire
    registration
-   Duplicate membership already exists: System error (should not occur
    in MVP)

**Postconditions**:

-   Membership exists linking Person to Organization
-   Role = Owner
-   status = Active
-   Membership is part of atomic registration transaction success
-   Person now participates in Organization
-   MembershipCreated event is published independently by RegistrationApplicationService

**Domain References**:

-   Aggregates: Membership, Person, Organization
-   Application Services: RegistrationApplicationService (ADR-0002 Decision 7 exception; see 01_Domain_Model.md §8)
-   Events: MembershipCreated (published independently by RegistrationApplicationService)
-   Lifecycle: Membership (Created→Active)

**Constraints**:

-   Owner is the only role in MVP (14_MVP.md §1)
-   Membership created as part of registration only (not independent in
    MVP)
-   One membership per Person per Organization in MVP
-   Membership creation is atomic with Person and Organization creation
-   Membership cannot be revoked in MVP (14_MVP.md §2)

------------------------------------------------------------------------

## UC-009: Refresh Session

**Actor**: Authenticated Client / System

**Goal**: Refresh an existing authenticated session without
re-authentication.

**Preconditions**:

-   Existing valid Session exists for Person
-   Session has active RefreshTokenId
-   Refresh request received with valid refresh credentials
-   System is operational

**Main Flow**:

1.  Receive refresh session request with RefreshTokenId
2.  Validate RefreshTokenId belongs to existing Session
3.  Verify Session ownership and permissions
4.  Generate new AccessTokenId
5.  Optionally generate new RefreshTokenId (implementation-specific)
6.  Update Session attributes with new tokens
7.  Preserve Session status (Authenticated or Active)
8.  Persist Session token refresh
9.  Return new tokens to client

**Alternative Flows**:

-   Session may automatically refresh before expiration (background)
-   Refresh may trigger optional token rotation per security policy

**Failure Conditions**:

-   RefreshTokenId not found: Return not-found error
-   RefreshTokenId expired or revoked: Return invalid token error
-   Session validation fails: Return unauthorized error
-   Refresh validation system unavailable: Return temporary failure
-   Persistence fails: Return failure; Session state not changed

**Postconditions**:

-   Session continues with new AccessTokenId
-   RefreshTokenId preserved or refreshed (implementation-specific)
-   Session status unchanged (remains Authenticated or Active)
-   Person identity unchanged
-   No new events published (refresh is token maintenance only)
-   Session expiration time may be extended (implementation-specific)

**Domain References**:

-   Aggregates: Session, Person
-   Domain Services: SessionManagementDomainService
-   Events: (No new events; refresh is token maintenance)
-   Lifecycle: Session remains in current state (Authenticated or Active)

**Constraints**:

-   Refresh does not create new identity
-   Refresh does not create new Person or Credential
-   Refresh does not affect Person lifecycle
-   Refresh does not invalidate other Sessions for the Person
-   Refresh requires valid RefreshTokenId (security)
-   RefreshTokenId must be revocable for security

------------------------------------------------------------------------

# 4. Out of Scope

The following are NOT MVP use cases and remain future scope:

**Organization Lifecycle Operations**:

-   UC-F01: Suspend Organization (future)
-   UC-F02: Resume Organization (future)
-   UC-F03: Archive Organization (future)
-   UC-F04: Create Additional Organizations (future)

**Membership Operations**:

-   UC-F05: Revoke Membership (future)
-   UC-F06: Change Membership Role (future)
-   UC-F07: Create Multiple Memberships (future)



**Credential Operations**:

-   UC-F09: Credential History (future)
-   UC-F10: Multiple Active Credentials per Person (future)
-   UC-F11: Alternative Credential Types (OAuth, SSO, etc.) (future)

**Person Operations**:

-   UC-F12: Suspend Person (future)
-   UC-F13: Archive Person (future)
-   UC-F14: Restore Archived Person (future)

**Identity Extensions**:

-   UC-F15: Device Identity (future)
-   UC-F16: Service Identity (future)
-   UC-F17: AI Agent Identity (future)

**Authorization Operations**:

-   UC-F18: Role-Based Access Control (RBAC) (Authorization Platform,
    not Identity)
-   UC-F19: Permission Assignment (Authorization Platform, not Identity)
-   UC-F20: Delegated Administration (future)

**Rationale**:

-   Lifecycle operations require Commands and APIs not part of MVP
-   Lifecycle states exist in Domain Model for architectural
    completeness and future evolution
-   Alternative credential types require future authentication mechanism
    extension
-   Authorization decisions belong to future Authorization Platform
-   Additional identity types belong to future extensibility

------------------------------------------------------------------------

# 5. Use Case Dependencies

**Registration Chain**:

UC-001 → UC-002 (during post-commit) → UC-004 (during post-commit)

**Authentication Chain**:

UC-003 → UC-004 (upon success)

**Session End Chain**:

UC-007 (triggered by user logout or system timer)

**Profile Management Chain**:

UC-005 (user-initiated)

**Credential Management Chain**:

UC-006 (user-initiated after authentication)

**Membership Creation Chain**:

UC-008 (part of UC-001)

**Refresh Session Chain**:

UC-003 Authenticate Person
↓
UC-004 Create Session
↓
UC-009 Refresh Session

Note: UC-009 operates on existing Session without creating new Person, Credential, or Membership. Refresh is Session token maintenance only.

# 6. Event Publication Map

  --------------------------------------------------------------------------------
  Use Case        Events Published             Domain / Application Service
  --------------- ---------------------------- -----------------------------------
  UC-001:         PersonRegistered            RegistrationApplicationService
  Register Person OrganizationCreated          
                  MembershipCreated            

  UC-002: Create  (none, part of registration) CredentialManagementDomainService
  Credential                                   

  UC-003:         LoginSucceeded OR            AuthenticationDomainService
  Authenticate    LoginFailed                  
  Person                                       

  UC-004: Create  SessionCreated               AuthenticationDomainService
  Session                                      

  UC-005: Manage  PersonUpdated                PersonManagementDomainService
  Person Profile                               

  UC-006: Change  PasswordChanged              CredentialManagementDomainService
  Credential                                   

  UC-007: End     LogoutCompleted OR           SessionManagementDomainService
  Session         SessionExpired               

  UC-008: Create  MembershipCreated            RegistrationApplicationService
  Organization                                 
  Membership                                   

  UC-009: Refresh (none, token maintenance)    SessionManagementDomainService
  Session                                      
  --------------------------------------------------------------------------------

------------------------------------------------------------------------

# 7. Aggregate Participation Matrix

  ---------------------------------------------------------------------------------------------------------
  Aggregate      UC-001   UC-002   UC-003     UC-004   UC-005        UC-006        UC-007   UC-008  UC-009
  -------------- -------- -------- ---------- -------- -------------- -------------- -------- ------- --------
  Person         Create   Validate Read       \-       Update         Read/Validate  Read     Create  Read

  Organization   Create   \-       \-         \-       \-       \-       \-       Create  \-

  Membership     Create   \-       \-         \-       \-       \-       \-       Create  \-

  Credential     Create   Create   Validate   \-       \-       Create   \-       \-      \-

  Session        Create   \-       \-         Create   Read/Validate Read/Validate Update   \-      Update
  ---------------------------------------------------------------------------------------------------------

**Legend**: Create = Creates new aggregate instance, Update = Modifies
existing aggregate, Read = Accesses without modification, Validate =
Validates constraints, - = No interaction

------------------------------------------------------------------------

# 8. MVP Constraints Verification

This section verifies that all use cases respect MVP scope constraints
from 14_MVP.md.

**Person Operations**:

-   ✅ UC-001 creates Person in Active state
-   ✅ UC-005 updates Person profile without state transition
-   ✅ No UC transitions Person to Suspended, Archived, or other states

**Organization Operations**:

-   ✅ UC-001 creates Organization in Active state as Personal
    Organization
-   ✅ No UC creates additional Organizations
-   ✅ No UC transitions Organization to Suspended, Archived, or other
    states

**Membership Operations**:

-   ✅ UC-001 creates Membership with Owner role
-   ✅ UC-008 creates Membership with Owner role only
-   ✅ No UC supports additional roles (Admin, Member, Guest, Operator)
-   ✅ No UC revokes Membership

**Session Operations**:

-   ✅ UC-003 initiates authentication
-   ✅ UC-004 creates Session and transitions through lifecycle
-   ✅ UC-007 closes or expires Session
-   ✅ Session lifecycle operations required by MVP are documented,
    including session creation, authentication state establishment,
    expiration, and closure. Additional lifecycle transitions are
    outside current MVP scope.

**Credential Operations**:

-   ✅ UC-002 creates initial Credential (password)
-   ✅ UC-006 replaces Credential with new password
-   ✅ No UC implements Credential History
-   ✅ No UC creates multiple active Credentials per Person

**Event Publishing**:

-   ✅ All use cases publish only events defined in 059 Event Ownership
    Table
-   ✅ No use case introduces new event types
-   ✅ Events map correctly to Domain Services in 01_Domain_Model.md

------------------------------------------------------------------------

# 9. Validation Against Reference Standards

This use case specification aligns with:

**064 - Blueprint Standard §8.3 Use Cases**:

-   ✅ Each use case includes Goal, Actors, Preconditions, Main Flow,
    Alternative Flows, Failure Conditions, Postconditions
-   ✅ Use cases describe behavior, not implementation
-   ✅ Use cases map to Domain Model (01_Domain_Model.md)

**065 - Blueprint Validator Specification**:

-   ✅ Use cases reference only Domain Model entities
-   ✅ No new Aggregates introduced
-   ✅ No new Events beyond Event Ownership Table
-   ✅ No lifecycle contradictions
-   ✅ MVP constraints preserved

**066 - AI Code Generation Specification**:

-   ✅ Use cases provide sufficient detail for deterministic AI-assisted
    implementation
-   ✅ Preconditions and postconditions explicitly state state
    requirements
-   ✅ Main flow provides step-by-step orchestration guidance
-   ✅ Domain Service mapping clear for each use case
-   ✅ Event publication requirements explicit

------------------------------------------------------------------------

# 10. Change Log

## Version 1.1.0 (2026-07-09)

**Identity Use Cases Corrections and Alignment**:

- Added UC-009 Refresh Session aligned with MVP Session Management scope
- Corrected Identity event publication mapping in UC-001 to include all three independent events: PersonRegistered, OrganizationCreated, MembershipCreated
- Clarified MembershipCreated as independent event in UC-008
- Corrected Aggregate Participation Matrix to reflect actual Domain References for Person and Session interactions in all use cases
- Corrected lifecycle validation wording to reflect actual documented operations
- Added UC-009 Refresh Session to Use Case Dependencies chain
- Updated Event Publication Map to show all events for UC-001 and UC-008
- No architecture changes introduced
- No new aggregates or events introduced
- No MVP scope expansion

## Version 1.0.0 (2026-07-09)

**Initial Use Case Specification**:

-   Created 8 MVP use cases: UC-001 through UC-008
-   Each use case includes Goal, Actors, Preconditions, Main Flow,
    Alternative Flows, Failure Conditions, Postconditions
-   Aligned with 01_Domain_Model.md (5 Aggregates, 5 Domain Services)
-   Aligned with 14_MVP.md (no Suspend/Archive/Revoke operations)
-   Aligned with 059_SmartCore_Identity_Platform.md (Event Ownership,
    Registration Architecture)
-   Explicit Out of Scope section documenting 20 future use cases
-   Aggregate Participation Matrix showing which use cases affect which
    aggregates
-   Event Publication Map showing Domain Service associations
-   MVP Constraints Verification section validating scope adherence
-   Validation against 064, 065, 066 standards

**Dependencies**:

-   00_Overview.md (v1.1.0)
-   01_Domain_Model.md (v1.1.0)
-   03_Aggregates.md (v1.0.0)
-   14_MVP.md (v1.0.0)
-   059_SmartCore_Identity_Platform.md (v1.1)

------------------------------------------------------------------------

**END OF DOCUMENT**