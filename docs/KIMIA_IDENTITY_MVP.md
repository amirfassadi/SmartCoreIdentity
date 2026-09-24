# Kimia Beauty — SmartCoreIdentity MVP

**Status:** MVP Scope  
**Target:** Kimia Beauty Release 1  
**Module:** SmartCoreIdentity

## 1. Objective

Provide the minimum identity and authentication capabilities required to launch Kimia Beauty Release 1.

The MVP must allow a customer to:

```text
Register
→ obtain a valid identity
→ log in
→ maintain a session
→ view/update basic personal profile
```

The scope intentionally follows the existing SmartCoreIdentity blueprint rather than defining a new identity model for Kimia.

## 2. In Scope

### Registration

- Register Person
- atomically create Personal Organization
- atomically create Owner Membership
- create Credential as part of the registration flow
- establish the identity required for later authentication
- track PendingCredential until one active Credential is ready; retry provisioning idempotently by registrationId and offer secure completion after retry exhaustion

Core invariant:

```text
Person
+
Personal Organization
+
Membership(Owner)
```

must be established as the registration core.

### Authentication

- Login
- Create Session
- Refresh Session
- Logout / Close Session
- password-based credential flow as defined by SCI

### Failed Authentication Event

Per [ADR-0002 v1.3, Decision 5](https://github.com/amirfassadi/SmartCorePlatform/blob/main/SmartCore_Platform_Docs_v1/ADR-0002_Identity_Foundation_Clarifications.md), `LoginFailed` is an
Identity-owned **Security Event** used for audit, not a Domain Event.
Identity publishes it for the documented failed-authentication outcomes without
requiring successful login, a business-state commit, or an authenticated Session.

The event name, producer, existing payload, and conditional identity-reference
rules are retained. Consumers must not interpret it as a successful Domain state
transition. This classification does not select a new transport, topic, retention
policy, or delivery guarantee.

The decision direction has been agreed; ADR-0002 as a whole remains Proposed
pending its acceptance criteria. The full SCI event/contract/machine specification
is not present in this repository snapshot and must be synchronized and validated
before generation readiness can be claimed.

### Profile

- retrieve own Person information
- update own Person profile

### Context exposed to consuming modules

SmartCoreIdentity may provide the authenticated context required by other SmartCore modules:

- PersonId
- Organization context
- Membership context
- Owner role for the v1 MVP

This context is identity information, not a business authorization decision.

## 3. Out of Scope

The Kimia Identity MVP does not include:

- Organization suspension
- Organization archive
- Membership revoke
- invitation flow
- additional Membership roles
- social login
- MFA
- general account recovery (the specific PendingCredential setup path is in scope)
- capability/business authorization
- Staff employment semantics
- Customer/business relationship semantics
- Business creation
- salon permissions
- appointment permissions
- payment permissions

These must not be added merely because they may be useful later.

## 4. Existing Aggregate Model

The MVP uses the existing SCI aggregate boundaries:

```text
Person
Organization
Membership
Credential
Session
```

No Kimia-specific aggregate is introduced into SmartCoreIdentity.

## 5. Registration Flow

The minimum registration flow is:

```text
RegisterPerson
      ↓
RegistrationApplicationService
      ↓
Atomic Core Transaction
├── Person
├── Personal Organization
└── Membership(Owner)
      ↓
PendingCredential workflow + Outbox work item committed with core
      ↓
Idempotent Credential provisioning / bounded retry
      ↓
Ready + PersonRegistered after active Credential
      ↓
Session may be established according to application flow
```

### Registration rule

The core identity/ownership state must not exist partially. Under ADR-0002
v1.4 (Proposed), a committed registration may temporarily be PendingCredential;
it cannot log in or receive an authenticated Session until an active Credential
exists and the workflow is Ready. Pending is a registration workflow state,
not a Person, Organization, or Membership lifecycle state. A pending result
must not claim registration/authentication completion. A secure, one-time
Credential setup challenge completes registration without registering again
when automated retries are exhausted.

For example, these states are invalid as completed registration outcomes:

```text
Person without Personal Organization
Person + Organization without Owner Membership
```

## 6. Organization and Membership Lifecycle for MVP

The current MVP creates:

```text
Organization → Active
Membership   → Active
```

The staged `Created` lifecycle state is bypassed for the MVP.

Future transitions such as:

```text
Organization:
Active → Suspended → Archived

Membership:
Active → Revoked
```

are outside the Kimia Release 1 scope.

## 7. Role Model

The MVP supports:

```text
Membership.role = Owner
```

Only the Owner role is required by the current Identity MVP.

This must not be interpreted as a generic business permission role.

Future employee, staff, manager, partner, customer, or other business relationships belong outside the Identity Owner-only MVP unless separately approved.

## 8. Commands Required

The Kimia Identity MVP consumes the command surface already defined by SCI:

- RegisterPerson
- AuthenticatePerson
- LogoutSession
- RefreshSession
- ChangePassword
- UpdatePersonProfile

The Kimia Release 1 UI does not need to expose every command immediately, but the implementation must remain compatible with this command model.

## 9. Queries Required

Minimum customer-facing query need:

- retrieve current authenticated Person
- retrieve/update self-owned profile data

Platform integration may use the SCI integration query model where required.

`GetPersonById` must not become a public customer REST endpoint merely for Kimia convenience.

## 10. Public API Needed by Kimia

The exact route names should follow the existing SCI API specification.

Functionally, Kimia needs endpoints/contracts for:

- register
- login
- refresh session/token
- logout
- get current person
- update current person profile

KimiaBeauty should consume these contracts rather than creating its own user tables or authentication implementation.

## 11. Authentication vs Authorization

SmartCoreIdentity answers:

```text
Who is this actor?
Which identity/organization/membership context do they have?
```

It does not answer:

```text
May this user edit Kimia services?
May this user cancel another person's appointment?
May this user refund a payment?
```

Those decisions belong to the owning capability/product layer.

## 12. KimiaBeauty Integration

Expected Release 1 dependency:

```text
KimiaBeauty
    ↓
SmartCoreIdentity
    ├── Register
    ├── Login
    ├── Session
    └── My Profile
```

After successful identity establishment:

```text
SmartCoreBusiness
```

can use the Organization/Person context required for Business creation and ownership without duplicating Identity models.

## 13. Minimum UI Flow

### Registration

```text
Register page
→ submit identity credentials/profile
→ registration succeeds
→ user becomes authenticated or is directed to login
```

### Login

```text
Login page
→ authenticate
→ create session
→ redirect to Kimia customer experience
```

### Profile

```text
My Profile
→ retrieve own Person data
→ update allowed profile fields
```

## 14. Acceptance Criteria

The Identity portion of Kimia Release 1 is complete when:

1. a new user can register successfully once an active Credential exists and the registration is Ready;
2. registration establishes Person + Personal Organization + Owner Membership according to SCI invariants;
3. the user can authenticate;
4. a valid Session can be created and refreshed;
5. the user can log out;
6. the user can retrieve their own Person information;
7. the user can update permitted profile fields;
8. KimiaBeauty does not maintain a duplicate Person/User identity source of truth;
9. no business authorization logic is placed inside SmartCoreIdentity;
10. committed ownership with failed Credential provisioning remains pending, retries are idempotent, and a user with a verified one-time setup challenge can complete it without duplicate Person/Organization/Membership;
11. no deferred Organization/Membership lifecycle features are required for launch;
12. documented failed-authentication outcomes publish `LoginFailed` as an Identity-owned Security Event, without creating an authenticated Session or treating it as a successful Domain state transition.

## 15. Explicit Non-Goals

Release 1 is not intended to complete the entire SmartCoreIdentity roadmap.

Success means:

```text
Kimia customer can register, authenticate, keep a valid session,
and maintain their basic identity profile.
```

The registration recovery and readiness requirements above are architectural
proposals in ADR-0002 v1.4. The complete SCI Blueprint, machine specification,
public contracts, API responses, event consumers, and security tests must be
reviewed before calling this launch-ready. No new route is defined by this
high-level scope document.


