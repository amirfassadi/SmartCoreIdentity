<!-- 
Document ID: ID-04
Title: SmartCore Identity Platform - Commands Blueprint
Version: 1.1.0
Status: READY_FOR_GENERATION

Purpose:
Define Identity Platform Public Command contracts,
orchestration boundaries, and Domain Event outcomes.

Dependencies:
- 00_Overview.md
- 01_Domain_Model.md
- 02_Use_Cases.md
- 03_Aggregates.md
- 14_MVP.md
- 057_SmartCore_Tenancy_and_Ownership_Model.md
- 059_SmartCore_Identity_Platform.md
- 064_SmartCore_Blueprint_Standard.md
- 065_SmartCore_Blueprint_Validator_Specification.md
- 066_SmartCore_AI_Code_Generation_Specification.md
- ADR-0002_Identity_Foundation_Clarifications.md
- ADR-0003_Organization_and_Membership_Lifecycle_Standardization.md

Change Log:
- Version 1.1.0 (2026-07-14): Corrected RegisterPerson's orchestrator
  references from `RegistrationDomainService` to
  `RegistrationApplicationService` throughout (§2.2, §3, §4.1, §5.1, §7),
  to synchronize with 01_Domain_Model.md v1.2.0 and 03_Aggregates.md
  v1.1.0, which reclassified this orchestrator as an Application Service
  per ADR-0002 Decision 7.1. Renamed §3 and §7 table columns from
  "Domain Service" to "Orchestrating Service" and added a Type column
  in §7 to make the single Application Service exception explicit. No
  Command behavior, contract, or MVP scope changed.
- Version 1.0.0:
  Initial Commands Blueprint creation.
-->

# 1. Overview

This document defines the Public Command contracts of the SmartCore
Identity Platform.

Commands represent intentional state-changing operations exposed by the
Identity Capability.

Each Command specification defines:

- Command intent
- Actor
- Preconditions
- Input requirements
- Validation rules
- Aggregate interaction boundaries
- Domain Service orchestration ownership
- Domain Event outcomes
- Failure conditions
- MVP constraints

Commands describe business intent only. Transport concerns,
serialization formats, API endpoints, and external communication
protocols are intentionally excluded and deferred to contract and API
documentation.

This document introduces no new:

- Aggregates
- Domain Events
- Business Rules
- Lifecycle States
- Capabilities

All Command definitions are derived from:

- Domain Model
- Aggregate Definitions
- Use Cases
- MVP Scope
- Identity Platform Event Ownership Model
- Approved Architectural Decisions

---

# 2. Command Design Principles

## 2.1 Intent-Based Operations

Commands represent business intent, not technical operations.

A Command answers:

> "What business action is requested?"

A Command does not define:

- HTTP methods
- Message schemas
- Database operations
- Infrastructure implementation

---

## 2.2 Aggregate Boundary Protection

Each Command SHALL operate within the consistency boundaries defined by
the Aggregate Model.

Commands SHALL NOT directly manipulate multiple Aggregates.

When a business operation requires coordination between multiple
Aggregates, orchestration SHALL be performed by the responsible Domain
Service, or, for the single approved exception described below, by an
Application Service.

Example:

`RegisterPerson` coordinates:

- Person creation
- Personal Organization creation
- Owner Membership creation

through `RegistrationApplicationService` (ADR-0002 Decision 7 exception;
see 01_Domain_Model.md §8). This is the only Command in MVP orchestrated
by an Application Service rather than a Domain Service; all other
Commands in this catalog are orchestrated by Domain Services as
described in §2.3.

---

## 2.3 Domain Service Orchestration

Command execution SHALL delegate business workflow coordination to the
appropriate Domain Service.

The Command handler SHALL NOT contain:

- Cross-Aggregate business workflows
- Ownership transaction logic
- Lifecycle orchestration rules

---

## 2.4 Event Ownership Compliance

Every Command SHALL produce only the Domain Events defined by the
Identity Platform Event Ownership Table.

Commands SHALL NOT:

- Introduce new Event types
- Rename existing Events
- Change Event ownership

Event taxonomy classification is outside the scope of this document.

---

## 2.5 MVP Boundary Protection

Commands SHALL respect MVP scope boundaries.

No Command SHALL introduce:

- Future lifecycle transitions
- Authorization capabilities
- RBAC management
- Enterprise identity providers
- Administrative workflows

outside the approved MVP scope.

---

# 3. Public Command Catalog

The Identity Platform MVP defines exactly six Public Commands.

| # | Command Name | Primary Aggregate | Orchestrating Service |
|---|--------------|-------------------|------------------------|
| 1 | RegisterPerson | Person | RegistrationApplicationService (Application Service — ADR-0002 Decision 7 exception) |
| 2 | AuthenticatePerson | Session | AuthenticationDomainService |
| 3 | UpdatePersonProfile | Person | PersonManagementDomainService |
| 4 | LogoutSession | Session | SessionManagementDomainService |
| 5 | RefreshSession | Session | SessionManagementDomainService |
| 6 | ChangePassword | Credential | CredentialManagementDomainService |

RegisterPerson is the only Command orchestrated by an Application
Service rather than a Domain Service (see §2.2, §7). No additional
Public Commands exist within MVP scope.

---

# 4. Command Specifications

# 4.1 RegisterPerson

## Command Name

`RegisterPerson`

## Purpose

Creates a new Person identity together with the initial ownership
boundary consisting of:

- Personal Organization
- Owner Membership

This Command establishes the canonical Identity ownership structure
defined by the Tenancy and Ownership Model.

---

## Actor

New User

---

## Preconditions

- No existing Person exists with the provided email
- Identity Platform is operational

---

## Input

Required:

- Email
- Password
- DisplayName

---

## Validation Rules

- Email SHALL be provided
- Email SHALL be valid and normalized
- Email SHALL be unique across all Persons
- Password SHALL satisfy credential requirements
- DisplayName SHALL satisfy format and length constraints

---

## Aggregate Interaction

### Core Ownership Transaction

The following Aggregates participate in the atomic ownership creation
transaction:

| Aggregate | Operation |
|-----------|-----------|
| Person | Create |
| Organization | Create |
| Membership | Create |

The Core Ownership Transaction SHALL commit atomically.

Partial ownership state is prohibited.

---

### Post-Commit Operations

After successful Core Ownership Transaction commit:

| Aggregate | Operation |
|-----------|-----------|
| Credential | Create |
| Session | Create |

Failure of Post-Commit Operations SHALL NOT invalidate already committed
ownership consistency.

Recovery behavior is implementation-specific.

---

## Orchestrating Service

`RegistrationApplicationService`

This is an Application Service, not a Domain Service — the single
approved exception authorized by ADR-0002 Decision 7 (Command Model
Coordination Exception for Identity Registration). See
01_Domain_Model.md §8.

---

## Events Produced

- PersonRegistered
- OrganizationCreated
- MembershipCreated

---

## Failure Conditions

### Duplicate Person

Condition:

- Person with the same email already exists

Result:

- Command rejected
- Conflict error returned
- No ownership state created

---

### Validation Failure

Condition:

- Invalid email
- Invalid password
- Invalid display name

Result:

- Validation error returned
- Transaction does not begin

---

### Core Ownership Transaction Failure

Condition:

Failure during:

- Person creation
- Organization creation
- Membership creation

Result:

- Entire transaction rolled back
- No partial ownership state persists

---

### Post-Commit Failure

Condition:

Failure during:

- Credential creation
- Initial Session creation

Result:

- Registration remains successful
- Person ownership remains valid
- Recovery is handled outside the Command contract

---

## Postconditions

After successful execution:

- Person exists with status = Active
- Personal Organization exists with status = Active
- Organization category = Personal
- Owner Membership exists with status = Active
- Credential exists for Person
- Initial Session creation has been requested
- Ownership Events have been published

---

## MVP Constraints

- Personal Organization is the only Organization type created
- Owner is the only Membership role created
- Registration is the only flow that creates Organization and Membership
- No Credential History tracking exists in MVP

---

# 4.2 AuthenticatePerson

## Command Name

`AuthenticatePerson`

---

## Purpose

Establishes an authenticated identity context by validating credentials
and creating a Session.

This Command answers:

> "Who are you?"

It does not answer:

> "What are you allowed to do?"

Authorization remains outside Identity Platform scope.

---

## Actor

User

---

## Preconditions

- Person exists with status = Active
- Active Credential exists for Person
- Identity Platform is operational

---

## Input

Required:

- Email
- Password

Optional:

- DeviceInfo

---

## Validation Rules

- Email SHALL be provided
- Password SHALL be provided
- Password SHALL validate against stored PasswordHash

---

## Aggregate Interaction

| Aggregate | Operation |
|-----------|-----------|
| Person | Read / Validate |
| Credential | Validate |
| Session | Create |

---

## Domain Service

`AuthenticationDomainService`

---

## Events Produced

Success:

- LoginSucceeded
- SessionCreated

Failure:

- LoginFailed

---

## Failure Conditions

### Person Not Found

Result:

- LoginFailed Event produced
- Authentication rejected

---

### Credential Missing

Result:

- LoginFailed Event produced
- Authentication rejected

---

### Password Validation Failure

Result:

- LoginFailed Event produced
- No Session created

---

### Authentication Infrastructure Failure

Result:

- Temporary failure returned
- Authentication outcome is not finalized

---

## Postconditions

Successful authentication:

- Session is created
- Session enters authenticated lifecycle state
- LoginSucceeded is published

Failed authentication:

- No Session is created
- LoginFailed is published

Authentication attempts do not modify:

- Person identity state
- Credential state

---

## MVP Constraints

- Authentication remains independent from Authorization
- No MFA
- No OAuth
- No Enterprise SSO
- Multiple concurrent authentication attempts are allowed

---
# 4.3 UpdatePersonProfile

## Command Name

`UpdatePersonProfile`

---

## Purpose

Updates mutable Person profile attributes without changing Person
lifecycle state or ownership relationships.

Within MVP scope, supported profile attributes are:

- Email
- DisplayName

This Command does not create, delete, suspend, or restore Person
identities.

---

## Actor

User (Person)

---

## Preconditions

- Person exists with status = Active
- Authenticated Session exists for the Person
- Identity Platform is operational

---

## Input

Optional:

- Email
- DisplayName

At least one mutable attribute SHALL be provided.

---

## Validation Rules

### Email

If Email is provided:

- Email SHALL be valid
- Email SHALL be normalized
- Email SHALL remain unique across all Persons

---

### DisplayName

If DisplayName is provided:

- DisplayName SHALL satisfy length constraints
- DisplayName SHALL satisfy format constraints

---

### Session Ownership

- Authenticated Session SHALL belong to the Person being updated

---

## Aggregate Interaction

| Aggregate | Operation |
|-----------|-----------|
| Person | Update |
| Session | Read / Validate |

Session participation exists only to validate authenticated ownership.
Session state is not modified.

---

## Domain Service

`PersonManagementDomainService`

---

## Events Produced

- PersonUpdated

---

## Failure Conditions

### Person Not Found

Result:

- Not-found error returned
- No update committed

---

### Invalid Session Ownership

Condition:

Authenticated Session does not belong to requested Person.

Result:

- Permission error returned
- No update committed

---

### Email Conflict

Condition:

New email is already used by another Person.

Result:

- Conflict error returned
- No update committed

---

### Validation Failure

Condition:

Invalid Email or DisplayName.

Result:

- Validation error returned
- No update committed

---

## Postconditions

After successful execution:

- Person attributes are updated
- PersonId remains immutable
- Person status remains unchanged
- PersonUpdated Event is published
- Existing Sessions remain unaffected
- Credential state remains unaffected

---

## MVP Constraints

- PersonId is immutable
- No Person lifecycle transition is introduced
- No Credential update occurs
- No Membership changes occur

---

# 4.4 LogoutSession

## Command Name

`LogoutSession`

---

## Purpose

Terminates an authenticated Session through explicit user logout.

---

## Actor

User

---

## Preconditions

- Session exists
- Session is not already Closed

---

## Input

Required:

- SessionId

or

- AccessTokenId identifying the Session

---

## Validation Rules

- Session SHALL exist
- Session SHALL belong to requesting Person
- Session SHALL be eligible for closure

---

## Aggregate Interaction

| Aggregate | Operation |
|-----------|-----------|
| Session | Update |

---

## Domain Service

`SessionManagementDomainService`

---

## Events Produced

- LogoutCompleted

---

## Failure Conditions

### Session Not Found

Result:

- Not-found error returned

---

### Session Already Closed

Result:

- Already-closed error returned

---

### Invalid Session Ownership

Condition:

Logout request belongs to another Person.

Result:

- Permission error returned

---

### Persistence Failure

Result:

- Failure returned
- Session state remains unchanged

---

## Postconditions

After successful execution:

- Session status = Closed
- AccessTokenId is revoked
- RefreshTokenId is revoked
- LogoutCompleted Event is published
- Person identity remains unchanged

---

## MVP Constraints

- Closed Sessions cannot be reactivated
- Logout does not affect:
  - Person state
  - Organization state
  - Membership state
  - Credential state

---

# 4.5 RefreshSession

## Command Name

`RefreshSession`

---

## Purpose

Refreshes an existing authenticated Session token context without
requiring credential re-authentication.

This Command is a token lifecycle operation and does not represent a new
Identity lifecycle transition within MVP scope.

---

## Actor

Authenticated Client / System

---

## Preconditions

- Existing valid Session exists
- Session contains an active RefreshTokenId

---

## Input

Required:

- RefreshTokenId

---

## Validation Rules

- RefreshTokenId SHALL belong to an existing Session
- RefreshTokenId SHALL not be expired
- RefreshTokenId SHALL not be revoked

---

## Aggregate Interaction

| Aggregate | Operation |
|-----------|-----------|
| Session | Read / Validate / Update |

---

## Domain Service

`SessionManagementDomainService`

---

## Events Produced

None.

RefreshSession does not produce a Domain Event in MVP because token
maintenance does not represent a business state transition.

---

## Failure Conditions

### Refresh Token Not Found

Result:

- Not-found error returned

---

### Refresh Token Invalid

Conditions:

- Expired token
- Revoked token

Result:

- Invalid token error returned

---

### Session Validation Failure

Result:

- Unauthorized error returned

---

### Persistence Failure

Result:

- Failure returned
- Session state remains unchanged

---

## Postconditions

After successful execution:

- Session continues as authenticated
- New AccessTokenId is issued
- RefreshTokenId is preserved or refreshed according to implementation
- Session lifecycle state remains unchanged
- Person identity remains unchanged

---

## MVP Constraints

- Refresh does not create Person
- Refresh does not create Credential
- Refresh does not create Membership
- Refresh does not invalidate other Sessions

---

# 4.6 ChangePassword

## Command Name

`ChangePassword`

---

## Purpose

Replaces the active Credential of a Person with a new Credential.

---

## Actor

User (Person)

---

## Preconditions

- Person exists with status = Active
- Active Credential exists
- Authenticated Session exists for Person

---

## Input

Required:

- CurrentPassword
- NewPassword

---

## Validation Rules

### Session Ownership

- Authenticated Session SHALL belong to the Person

---

### Current Password

- CurrentPassword SHALL validate against existing Credential PasswordHash

---

### New Password

- NewPassword SHALL satisfy credential strength requirements

---

## Aggregate Interaction

| Aggregate | Operation |
|-----------|-----------|
| Person | Read |
| Credential | Create / Update |
| Session | Read / Validate |

---

## Domain Service

`CredentialManagementDomainService`

---

## Events Produced

- PasswordChanged

---

## Failure Conditions

### Person Not Found

Result:

- Not-found error returned

---

### Session Ownership Failure

Result:

- Permission error returned

---

### Current Password Invalid

Result:

- Invalid password error returned
- Existing Credential remains active
- No new Credential created

---

### New Password Validation Failure

Result:

- Validation error returned
- No Credential change occurs

---

### Credential Persistence Failure

Result:

- Failure returned
- Previous active Credential remains valid

---

## Postconditions

After successful execution:

- New Credential exists with status = Active
- Previous Credential status = Replaced
- Person authenticates using new password
- PasswordChanged Event is published
- Active Sessions remain unaffected

---

## MVP Constraints

- Only one Active Credential exists per Person
- No Credential History tracking
- Password change does not revoke existing Sessions
- Credential change does not modify Person ownership state

---

# 5. Internal Domain Operations

The following operations are internal orchestration steps performed by
Domain Services, or, for Registration (§5.1), by the
RegistrationApplicationService (ADR-0002 Decision 7 exception).

They SHALL NOT be exposed as independent Public Commands.

---

# 5.1 Registration Internal Operations

Performed by:

`RegistrationApplicationService` (Application Service — ADR-0002
Decision 7 exception; see 01_Domain_Model.md §8)

as part of:

`RegisterPerson`

---

## Create Person

Purpose:

Creates the Person Aggregate within the Core Ownership Transaction.

---

## Create Personal Organization

Purpose:

Creates the initial Personal Organization owned by the Person.

---

## Create Membership

Purpose:

Creates the Owner Membership connecting Person and Personal
Organization.

---

## Commit Core Ownership Transaction

Rules:

- Person
- Organization
- Membership

commit atomically.

Partial ownership state is prohibited.

---

## Post-Commit Credential Creation

Purpose:

Creates initial Credential after successful ownership commit.

Failure:

- Does not invalidate ownership state.

---

## Initial Session Creation Request

Purpose:

Requests initial Session creation through Authentication lifecycle.

Failure:

- Does not invalidate ownership state.

---

# 5.2 Authentication Internal Operations

Performed by:

`AuthenticationDomainService`

as part of:

`AuthenticatePerson`

---

## Credential Validation

Responsibilities:

- Resolve Person by email
- Validate supplied password
- Confirm active Credential state

---

## Session Creation

Responsibilities:

- Create Session Aggregate
- Generate AccessTokenId
- Generate RefreshTokenId
- Transition Session into authenticated lifecycle state

---

# 5.3 Session Internal Operations

Performed by:

`SessionManagementDomainService`

---

## Token and Session Lifecycle Handling

Responsibilities:

- Session state transitions
- Token issuance
- Token revocation
- Expiration handling

These operations are only available through Public Commands defined in
this Blueprint.

They SHALL NOT become independent Public Commands.

---
# 6. Command → Aggregate Mapping

The following table defines the relationship between Public Commands and
Aggregate interactions.

| Command | Person | Organization | Membership | Credential | Session |
|---------|--------|--------------|------------|------------|---------|
| RegisterPerson | Create | Create | Create | Create (Post-Commit) | Create (Post-Commit) |
| AuthenticatePerson | Read / Validate | - | - | Validate | Create |
| UpdatePersonProfile | Update | - | - | - | Read / Validate |
| LogoutSession | - | - | - | - | Update |
| RefreshSession | - | - | - | - | Read / Validate / Update |
| ChangePassword | Read | - | - | Create / Update | Read / Validate |

---

# 7. Command → Orchestrating Service Mapping

Each Public Command SHALL be orchestrated by exactly one owning Domain
Service, except RegisterPerson, which is orchestrated by an Application
Service under the approved ADR-0002 Decision 7 exception.

| Command | Orchestrating Service | Type |
|---------|------------------------|------|
| RegisterPerson | RegistrationApplicationService | Application Service (ADR-0002 Decision 7 exception) |
| AuthenticatePerson | AuthenticationDomainService | Domain Service |
| UpdatePersonProfile | PersonManagementDomainService | Domain Service |
| LogoutSession | SessionManagementDomainService | Domain Service |
| RefreshSession | SessionManagementDomainService | Domain Service |
| ChangePassword | CredentialManagementDomainService | Domain Service |

---

# 8. Command → Event Mapping

This mapping follows the Identity Platform Event Ownership Table.

No new Domain Events are introduced by this Blueprint.

| Command | Events Produced |
|---------|-----------------|
| RegisterPerson | PersonRegistered, OrganizationCreated, MembershipCreated |
| AuthenticatePerson | LoginSucceeded, LoginFailed, SessionCreated |
| UpdatePersonProfile | PersonUpdated |
| LogoutSession | LogoutCompleted |
| RefreshSession | None |
| ChangePassword | PasswordChanged |

---

# 9. Command Lifecycle Rules

## 9.1 Aggregate Ownership Boundary

Each Command SHALL respect Aggregate consistency boundaries.

A Command SHALL NOT directly mutate multiple Aggregates.

When multiple Aggregates participate in one business operation:

- Domain Service owns orchestration
- Aggregate rules remain isolated
- Transaction boundaries follow approved architecture decisions

---

## 9.2 Registration Ownership Transaction Boundary

`RegisterPerson` contains a special Core Ownership Transaction.

The transaction includes:

- Person creation
- Personal Organization creation
- Owner Membership creation

The following rule applies:

> Partial ownership state SHALL never exist.

If any Core Ownership operation fails:

- Entire transaction is rolled back
- No incomplete ownership structure remains

---

## 9.3 Post-Commit Operation Boundary

Operations after Core Ownership Transaction commit SHALL NOT invalidate
ownership consistency.

Post-Commit operations include:

- Credential creation
- Initial Session creation

Failure handling of Post-Commit operations is implementation-specific.

---

## 9.4 Identity and Authorization Separation

Identity Commands SHALL NOT evaluate permissions.

Identity Platform responsibilities:

- Establish identity
- Manage credentials
- Manage sessions
- Maintain membership context

Authorization decisions remain outside MVP scope.

---

# 10. Command Invariants

The following invariants SHALL remain true throughout the MVP lifecycle.

---

## Invariant-001: Person Identity Stability

PersonId is immutable.

A Person identity cannot be replaced through profile update operations.

---

## Invariant-002: Ownership Creation Boundary

During MVP:

Only `RegisterPerson` can create:

- Personal Organization
- Owner Membership

---

## Invariant-003: Authentication Isolation

Authentication attempts SHALL NOT modify:

- Person state
- Credential state

unless the Command explicitly represents a Credential lifecycle change.

---

## Invariant-004: Session Refresh Isolation

`RefreshSession` SHALL NOT create:

- Person
- Organization
- Membership
- Credential

---

## Invariant-005: Credential Active State

A Person SHALL have at most one Active Credential.

Credential replacement SHALL preserve this invariant.

---

## Invariant-006: Membership Role Limitation

Within MVP:

- Owner is the only Membership Role
- Role management is outside Command scope

---

# 11. MVP Command Boundary

This Blueprint confirms the following MVP limitations.

---

## Included Commands

The Identity Platform MVP exposes exactly:

1. RegisterPerson
2. AuthenticatePerson
3. UpdatePersonProfile
4. LogoutSession
5. RefreshSession
6. ChangePassword

---

## Excluded Capabilities

The following capabilities are intentionally excluded:

- Authorization evaluation
- Permission management
- RBAC management
- Delegation
- Enterprise SSO
- OAuth providers
- MFA
- Administrative lifecycle management

---

# 12. Out of Scope Commands

The following Commands are future scope and SHALL NOT be implemented
within this Blueprint version.

---

## Organization Lifecycle

- SuspendOrganization
- ResumeOrganization
- ArchiveOrganization

---

## Membership Lifecycle

- RevokeMembership
- ChangeMembershipRole

---

## Person Lifecycle

- SuspendPerson
- ArchivePerson
- RestoreArchivedPerson

---

## Authorization Commands

- AssignRole
- RemoveRole
- CreatePermission
- GrantPermission
- RevokePermission

---

## Alternative Credential Commands

- RegisterOAuthCredential
- ConfigureEnterpriseSSO
- ManageExternalIdentityProvider

---

# 13. Open Architectural Notes

The following topics are intentionally not resolved by this Blueprint.

They represent possible future ADR candidates and SHALL NOT alter MVP
Command contracts.

---

## 13.1 Event Classification

Future consideration:

- Domain Event classification
- Security Event classification
- Audit Event classification

Current Blueprint follows the existing Identity Platform Event
Ownership model.

---

## 13.2 Session Security Policies

Future consideration:

- Password change session invalidation policy
- Suspicious session detection
- Advanced session revocation workflows

Current MVP behavior:

Password changes do not affect active Sessions.

---

## 13.3 Email Verification Lifecycle

Future consideration:

- Email ownership verification
- Email change confirmation workflow
- Identity identifier migration rules

Current MVP behavior:

Email update remains part of Person profile update.

---

## 13.4 Session Lifecycle Refinement

Future consideration:

- Session state machine simplification
- Additional security lifecycle states

Current Blueprint follows the existing Session lifecycle definition.

---

# 14. Validation Against Standards

## 14.1 Blueprint Standard Compliance

This document complies with:

`064_SmartCore_Blueprint_Standard.md`

Command specifications define:

- Name
- Purpose
- Actor
- Preconditions
- Input
- Validation Rules
- Aggregate Interaction
- Domain Service
- Events Produced
- Failure Conditions
- Postconditions
- MVP Constraints

---

## 14.2 Blueprint Validator Compatibility

This document supports:

`065_SmartCore_Blueprint_Validator_Specification.md`

Validation coverage includes:

- Command naming consistency
- Aggregate mapping consistency
- Domain Service ownership
- Event ownership compliance
- MVP boundary validation

Each Command either:

- Produces declared Events

or:

- Explicitly documents why no Event is produced

---

## 14.3 AI Code Generation Readiness

This document provides deterministic generation inputs:

- Command intent
- Orchestration ownership
- Aggregate boundaries
- Validation rules
- Failure behavior
- Event outcomes

AI-assisted implementation SHALL derive behavior only from approved
Command contracts and referenced architecture documents.

---

# 15. ADR Alignment

This Blueprint aligns with:

## ADR-0002: Identity Foundation Clarifications

Applied decisions:

- Core Ownership Transaction boundary
- Post-Commit Credential and Session handling
- Identity / Authorization separation

---

## ADR-0003: Organization and Membership Lifecycle Standardization

Applied decisions:

- Personal Organization creation
- Owner Membership creation
- MVP lifecycle limitations

---

# 16. Change Log

## Version 1.1.0

Corrected RegisterPerson's orchestrator terminology from
`RegistrationDomainService` to `RegistrationApplicationService`
throughout the document, to synchronize with 01_Domain_Model.md v1.2.0
and 03_Aggregates.md v1.1.0 (ADR-0002 Decision 7.1).

Renamed the "Domain Service" columns in §3 and §7 to "Orchestrating
Service" and made the Application Service exception explicit.

No Command contract, behavior, Aggregate mapping, Event mapping, or MVP
scope changed.

---

## Version 1.0.0

Initial Commands Blueprint creation.

Included:

- Six MVP Public Commands
- Command contracts
- Aggregate mappings
- Domain Service mappings
- Event mappings
- Internal Domain Operations
- Command invariants
- MVP boundaries
- Future scope documentation

No new:

- Aggregates
- Events
- Capabilities
- Business Rules

were introduced.

---

**END OF DOCUMENT**