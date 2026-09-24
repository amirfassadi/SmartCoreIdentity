# SmartCoreIdentity

SmartCoreIdentity is the identity and authentication foundation of the SmartCore ecosystem.

> Identity proves who the actor is and provides identity, organization, membership, and session context. Business authorization belongs to consuming capabilities.

## Core Aggregates

SmartCoreIdentity is based on five primary aggregates:

- Person
- Organization
- Membership
- Credential
- Session

## Core Responsibilities

SmartCoreIdentity owns:

- person registration
- personal identity profile
- authentication
- credential lifecycle required by MVP
- session creation, refresh, and logout
- Personal Organization creation during registration
- Owner Membership creation during registration
- identity / organization / membership context for consuming modules

## Registration Invariant

Registration uses the architecture defined by ADR-0002.

The core registration transaction atomically creates:

```text
Person
+
Personal Organization
+
Membership(role = Owner)
```

Partial creation of those three core records is not allowed.

The orchestration boundary proposed by ADR-0002 (pending full acceptance) is:

```text
RegistrationApplicationService
```

This is the limited Application Service exception for initial multi-aggregate registration coordination.

Credential and Session creation occur after the atomic identity/ownership core has been established according to the SCI design.

## Authorization Boundary

SmartCoreIdentity does **not** own business authorization.

It provides context such as:

- authenticated Person
- Organization
- Membership
- Membership role/context

Consuming capabilities decide whether an actor may perform a business action.

Examples:

- SmartCoreBusiness decides Business-level authorization.
- SmartCoreReservation decides reservation-related authorization.
- SmartCoreFinance decides finance-related authorization.

## MVP Lifecycle Boundary

For the current MVP:

- Person starts Active.
- Organization starts Active.
- Membership starts Active.
- Credential starts Active.
- Organization suspend/archive is out of scope.
- Membership revoke is out of scope.
- Role expansion beyond Owner is out of scope.

Session lifecycle supports the minimum authentication flow required by the MVP.

## Failed Authentication Event

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

## Kimia Beauty Release 1

Kimia Beauty requires only the minimum Identity capabilities needed to support a real public website:

- Register Person
- automatic Personal Organization creation
- automatic Owner Membership creation
- Credential creation during registration flow
- Login
- Logout
- Refresh Session
- Update Person Profile
- Retrieve own Person information

No salon-specific concepts belong in this repository.

## Does Not Own

SmartCoreIdentity does not own:

- Business
- BusinessProfile
- Service
- Staff business relationship
- Scheduling
- Reservation
- Appointment
- Payment
- Finance
- Communication
- product-specific UI

## Related Repositories

- SmartCorePlatform
- SmartCoreBusiness
- SmartCoreBusinessCapability
- KimiaBeauty

## Current Status

**Status:** Existing Identity Blueprint / Kimia MVP Integration

The Kimia implementation must remain consistent with the existing SCI documents and governing ADRs.

---

> Authenticate identity once. Keep business authorization where the business rules live.

