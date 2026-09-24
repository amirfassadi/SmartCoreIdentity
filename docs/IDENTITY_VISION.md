# SmartCoreIdentity — Capability Vision

**Status:** Architecture Vision  
**Repository:** SmartCoreIdentity

## Purpose

SmartCoreIdentity is the identity and authentication foundation of SmartCore.

It answers:
- Who is the actor?
- How are they authenticated?
- What identity, organization, membership, credential, and session context exists?

Business authorization remains outside Identity.

## Long-Term Vision

SmartCoreIdentity should support, under governed evolution:

- Person identity lifecycle
- Credential lifecycle
- Session lifecycle
- Organization context
- Membership context
- authentication factors
- account recovery
- security events
- future identity types such as Device, Service, and AI Agent only through explicit architectural decisions

## Core Aggregates

- Person
- Organization
- Membership
- Credential
- Session

## Registration Invariant

Registration establishes the initial ownership core atomically:

```text
Person
+
Personal Organization
+
Membership(Owner)
```

The application orchestration boundary is `RegistrationApplicationService`.
ADR-0002 v1.5 (Proposed) asks for DisplayName, password, and one verified
mobile number OR email. A one-time code verifies the chosen contact before
the ownership transaction; other profile data can be collected later.
The same ADR keeps ownership creation atomic while recording a
separate durable PendingCredential workflow and Outbox provisioning work in the
same commit. Idempotent retry and a secure one-time setup challenge complete
Credential provisioning without a second registration. Authentication requires
Ready and an active Credential. Person/Organization/Membership lifecycle states
are unchanged, and initial Session creation is separate from readiness.

## Owns

- registration
- authentication
- identity profile
- credential management
- sessions
- identity-owned Organization/Membership semantics approved by governance
- authentication events and identity context

## Does Not Own

- Business
- Service
- Staff/employment semantics
- Scheduling
- Reservation
- Payment
- Commerce
- product-specific authorization

## Authorization Boundary

Identity provides authenticated context. Consuming modules decide business permissions.

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

## Growth Path

### Phase 1 — Kimia MVP
Registration, login, session, self profile.

### Phase 2 — Mature Human Identity
Recovery, richer verification, security controls, additional credential flows.

### Phase 3 — Organization/Membership Operations
Invitation/revocation and governed lifecycle extensions where approved.

### Phase 4 — Future Identity Types
Device/Service/AI Agent identities only through future ADRs.

## Architecture Constraints

1. Authentication and business authorization remain separate.
2. Registration atomicity is preserved.
3. Person-centric v1.x semantics remain stable unless changed by ADR.
4. Identity data has one authoritative owner.
5. Future identity types do not silently reuse Person semantics.

---

> Prove identity once; let each capability own its business authorization.



