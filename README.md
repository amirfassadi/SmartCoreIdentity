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

The approved orchestration boundary is:

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
