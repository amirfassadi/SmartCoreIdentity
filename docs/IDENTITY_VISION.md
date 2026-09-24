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
