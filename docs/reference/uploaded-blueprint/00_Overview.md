<!--
Document ID: ID-00
Title: SmartCore Identity Platform Blueprint - Overview
Version: 1.1.0
Status: READY_FOR_GENERATION

Purpose:
Defines the purpose, scope, boundaries, terminology, dependencies,
and architectural intent of the SmartCore Identity Capability Platform.

Dependencies:
019_SmartCore_Identity_and_Session_Continuity_Model
041_SmartCore_Identity_Model
057_SmartCore_Tenancy_and_Ownership_Model
059_SmartCore_Identity_Platform
064_SmartCore_Blueprint_Standard

Change Log:
See Section 13 for detailed version history.
-->

# 1. Purpose

The Identity Capability Platform provides the foundational identity,
authentication, organization ownership, membership participation,
credential management, and session management services required by
all SmartCore Capability Platforms.

Identity establishes the canonical actor model of SmartCore.

Identity is the authoritative source for:

- Person identity
- Authentication
- Session continuity
- Organization ownership
- Membership participation
- Credential management

Identity SHALL be implemented exactly once and reused by all
Capability Platforms.

---

# 2. Capability Boundary

The Identity Capability owns:

- Persons
- Organizations
- Memberships
- Credentials
- Sessions

The Identity Capability SHALL NOT own:

- Resources
- Devices
- Buildings
- Contracts
- Financial Accounts
- Inventory
- Workflow Definitions
- Authorization Policies

Authorization evaluation belongs to consuming platforms.

Identity only provides identity context and membership information.

---

# 3. Architectural Intent

Identity establishes the canonical ownership model of SmartCore.

Ownership SHALL follow:

Person
→ Membership
→ Organization
→ Resource

Direct Person → Resource ownership is prohibited.

Identity SHALL remain independent from:

- IoT
- Finance
- Manufacturing
- Resource
- Reservation
- Communication

All business capabilities consume Identity.

Identity consumes none of them.

---

# 4. Core Concepts

## Person

Represents a human identity capable of interacting with the platform.

A Person is a persistent identity.

A Person is not a Session.

---

## Organization

Represents the ownership and tenancy boundary.

Every resource belongs to exactly one Organization.

Every Person belongs to at least one Organization.

---

## Membership

Represents participation of a Person inside an Organization.

Membership is the only architectural path connecting a Person to an Organization.

---

## Credential

Represents authentication information.

MVP supports Password Credentials only.

Credential is not Identity.

Credential is a mechanism used to authenticate Identity.

---

## Session

Represents a temporary authenticated execution context.

Session references Identity.

Session is not Identity.

Identity persists after Session expiration.

---

# 5. Ownership Model

Canonical ownership path:

Person
↓
Membership
↓
Organization
↓
Resource

The Identity Platform SHALL enforce the ownership constraints defined by
the SmartCore Tenancy and Ownership Model.

---

# 6. Registration Model

Registration establishes the initial ownership structure of a new
SmartCore identity.

Registration consists of two phases:

## Core Ownership Transaction

The atomic ownership transaction SHALL include:

1. Create Person
2. Create Personal Organization
3. Create Owner Membership
4. Commit

The ownership transaction SHALL commit only when all ownership
entities are successfully created.

Partial ownership states are forbidden.

Rollback SHALL occur if any ownership step fails before commit.

## Post-Commit Identity Operations

After successful ownership commit, the Identity Platform MAY perform:

1. Create Credential
2. Create Initial Session
3. Publish PersonRegistered Event

These operations occur after ownership consistency has been established.

Failure of any post-commit operation SHALL NOT invalidate:

- Person
- Organization
- Membership

Ownership consistency SHALL remain valid after commit.

Recovery, retry, compensation, or operational handling of post-commit
failures is implementation-specific and outside the scope of this
Blueprint.

---

# 7. Authentication Model

Authentication answers:

Who are you?

Authentication SHALL:

- Validate Credentials
- Resolve Identity
- Create Session
- Issue Tokens

Authentication SHALL NOT evaluate permissions.

Authorization remains separate.

---

# 8. Session Model

A Person MAY own multiple Sessions.

Sessions are device independent.

Examples:

- Browser Session
- Mobile Session
- Desktop Session
- API Client Session

Session expiration SHALL NOT invalidate Identity.

---

# 9. Dependencies

Identity depends on:

- Shared Kernel
- Policy Engine (future)
- Event Engine
- Messaging Engine

Identity SHALL NOT depend on any Capability Platform.

Dependency direction:

Identity
↓
Core Engines
↓
Shared Kernel

---

# 10. Public Surface

Identity exposes:

Commands

- RegisterPerson
- AuthenticatePerson
- LogoutSession
- RefreshSession
- ChangePassword

Queries

- GetPersonById
- GetCurrentPerson
- GetOrganizationsForPerson
- GetMembershipsForPerson
- GetSessionsForPerson

Events

- PersonRegistered
- PersonUpdated
- OrganizationCreated
- MembershipCreated
- PasswordChanged
- LoginSucceeded
- LoginFailed
- SessionCreated
- SessionExpired
- LogoutCompleted

---

# 11. MVP Scope

Included:

- Password Authentication
- Personal Organization Creation
- Owner Membership
- Session Management
- Refresh Tokens
- Public API Contracts
- Domain Events

Excluded:

- MFA
- OAuth
- Google Login
- Apple Login
- Telegram Login
- Enterprise SSO
- Delegated Administration
- Organization Switching
- Advanced Authorization

---

# 12. Success Criteria

Identity MVP is complete when:

✓ Registration succeeds

✓ Login succeeds

✓ Logout succeeds

✓ Refresh succeeds

✓ Organization creation succeeds

✓ Membership creation succeeds

✓ Sessions are managed correctly

✓ Events are published

✓ APIs are operational

---

# 13. Change Log

## Version 1.1.0 (2026-07-08)

**Changes Implemented**:
- Registration Model refactored into Core Ownership Transaction and Post-Commit Identity Operations
- Explicit separation of ownership consistency from post-commit operations
- Session creation documented as post-commit operation via AuthenticationDomainService

**Aligned With**:
- 057_SmartCore_Tenancy_and_Ownership_Model.md (v1.2)
- 059_SmartCore_Identity_Platform.md (v1.1)

**Authorized By**:
- ADR-0002_Identity_Foundation_Clarifications.md

---

**END OF DOCUMENT**