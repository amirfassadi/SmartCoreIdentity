<!--
Document ID: ID-05
Title: SmartCore Identity Platform Blueprint - Queries
Version: 1.0.2
Status: READY_FOR_GENERATION

Purpose:
Define the Public Query contracts of the Identity Capability Platform,
separating Query Contract (business-level read intent) from Transport
(REST, gRPC, internal service call, etc.). Resolves the apparent gap
between 00_Overview §10 (5 Queries in Public Surface) and 14_MVP §7 /
08_API (4 REST read endpoints) by formally distinguishing
User Interaction Queries from Platform Integration Queries.

Dependencies:
- 00_Overview.md
- 01_Domain_Model.md
- 03_Aggregates.md
- 14_MVP.md
- 059_SmartCore_Identity_Platform.md
- ADR-0002_Identity_Foundation_Clarifications.md

Change Log:
  - Version 1.0.0 (2026-07-14): Initial Queries Blueprint. Formalizes
    the Query Contract / Transport separation and classifies the 5
    Queries declared in 00_Overview §10 into User Interaction Queries
    (exposed via REST, see 08_API.md) and Platform Integration Queries
    (GetPersonById; no public REST endpoint in Version 1.0). Does not
    modify 00_Overview, 04_Commands, or 14_MVP.
  - Version 1.0.2 (2026-07-14): Added a clarifying sentence to §4.1
    GetCurrentPerson stating that its extended projection (Email,
    Status) reflects self-service resolution of the caller's own
    identity context and SHALL NOT be treated as a precedent for
    platform-to-platform identity resolution (see 5.1). No
    architectural meaning changed.
  - Version 1.0.1 (2026-07-14): Editorial/precision corrections from
    review pass, none of which change architectural meaning:
    (1) §4.4 GetSessionsForPerson corrected to use the "Identity Data
    Ownership Scope" terminology established in §2.4, replacing a
    stray "Authorization Scope" label that was inconsistent with the
    rest of Section 4 and risked being read as Capability
    Authorization under ADR-0002 §2. (2) §5.1 GetPersonById output
    narrowed from (PersonId, DisplayName, Status) to (PersonId,
    DisplayName); Status deferred for the same reason Email was
    already deferred in v1.0.0 — no current consuming Capability
    Platform has a documented use case for it, and it may expose
    Person lifecycle state without a demonstrated need. (3) Section 6
    renamed from "Cross-Reference Validation" to "Architecture
    Alignment Notes" to avoid implying this document performs the
    role of the formal Blueprint Validator (065).
-->

# 1. Overview

This document defines the Public Query contracts of the SmartCore
Identity Platform.

Queries represent read-only, non-state-changing operations exposed by
the Identity Capability.

A Query answers:

> "What information is requested?"

A Query does not:

- Change Aggregate state
- Produce Domain Events
- Perform business authorization evaluation

This document introduces no new:

- Aggregates
- Domain Events
- Business Rules
- Capabilities

All Query definitions are derived from the 5 Queries declared in
00_Overview.md §10 (Public Surface). No Query is added to, or removed
from, that list. This document only classifies and specifies them.

---

# 2. Query Design Principles

## 2.1 Query Contract vs. Transport

A **Query Contract** is a business-level read operation: a name, an
input, an output, and the conditions under which it may be invoked.

A **Transport** is the mechanism through which a Query Contract is
invoked: REST, gRPC, internal service call, message bus request/reply,
etc.

A Query Contract MAY have zero, one, or multiple Transports.

00_Overview §10 defines the Query Contract surface. 08_API.md defines
which of those contracts are exposed via REST in Version 1.0, and how.
The absence of a REST Transport for a given Query Contract does not
remove it from the Public Surface; it only means that Contract has no
public REST Transport in this version.

This principle is what resolves the apparent inconsistency between
00_Overview §10 (5 Queries) and 14_MVP §7 / 08_API (4 REST read
endpoints): all 5 Query Contracts remain valid Public Surface; only 4
are exposed over REST in MVP.

---

## 2.2 Read-Only Guarantee

Queries SHALL NOT modify any Aggregate state.

Queries SHALL NOT produce Domain Events.

Queries SHALL NOT trigger Command execution.

---

## 2.3 Aggregate Boundary Respect

Each Query SHALL read from one or more Aggregates without crossing
their consistency boundaries. Queries MAY read across Aggregates (e.g.
GetOrganizationsForPerson reads Membership and Organization data), but
this is a read projection, not a transactional coordination, and
therefore does not require Domain Service or Application Service
orchestration.

---

## 2.4 Authorization Boundary Compliance

Per ADR-0002 §2 (Authorization Boundary Clarification) and 059 §9
(Authorization Boundary):

- The Identity Platform SHALL NOT implement Capability-specific
  business authorization rules.
- The Identity Platform SHALL enforce intrinsic identity data
  ownership rules for User Interaction Queries — i.e. that a caller is
  who they claim to be (authentication) and that a caller reads only
  data belonging to their own authenticated Person (e.g. their own
  Sessions, their own Memberships).

  This is **not** Capability Authorization. It does not evaluate what
  a Person is business-permitted to do; it only protects access to
  Identity-owned data belonging to the authenticated Person, and is
  therefore not the kind of rule ADR-0002 §2 prohibits.
- Business authorization — whether a Person may see another Person's
  data, or another Capability Platform's business-specific access
  rules — remains outside Identity's responsibility and belongs to
  consuming Capability Platforms.

This distinction is applied per-Query in Section 4 and Section 5. To
avoid ambiguity with the term "authorization" as scoped by ADR-0002 §2,
the per-Query fields below use "Identity Data Ownership Scope" instead
of "Authorization Scope" for User Interaction Queries. This labeling
is applied consistently across all four User Interaction Queries in
Section 4.

---

## 2.5 MVP Boundary Protection

Queries SHALL respect MVP scope boundaries defined in 14_MVP.md.

No Query SHALL introduce:

- Cross-Organization search
- Administrative or elevated-privilege read operations
- Business authorization evaluation
- Query capabilities beyond the 5 declared in 00_Overview §10

---

# 3. Query Surface

## 3.1 User Interaction Queries

Queries intended for authenticated user-facing scenarios. The caller
is the Person whose data is being read, acting through an authenticated
Session. These Queries are exposed via REST in Version 1.0 — see
08_API.md.

- GetCurrentPerson
- GetOrganizationsForPerson
- GetMembershipsForPerson
- GetSessionsForPerson

## 3.2 Platform Integration Queries

Queries intended for Capability Platform consumption. The caller is
another SmartCore Capability Platform (e.g. Resource, Finance, IoT)
that holds a PersonId and needs to resolve identity data it does not
own. Per 00_Overview §1, Identity is consumed by all other Capability
Platforms; this Query is the read contract that consumption depends on.

- GetPersonById

No public REST endpoint is defined for this Query in Version 1.0. See
Section 5 and 08_API.md §"Platform Integration Queries" for the
rationale.

---

# 4. User Interaction Query Specifications

## 4.1 GetCurrentPerson

**Purpose**: Retrieve the Person record associated with the currently
authenticated Session.

**Actor**: Authenticated User

**Input**: Session context (resolved from Access Token)

**Output**: Person (PersonId, Email, DisplayName, Status, CreatedAt,
UpdatedAt)

**Preconditions**:

- A valid, non-expired Session exists for the caller

**Identity Data Ownership Scope**: Caller MAY only retrieve their own
Person record, as resolved from their own Session. This is an identity-
resolution check intrinsic to Identity's own data (059 §9), not
Capability Authorization (ADR-0002 §2).

**Failure Conditions**:

- Session invalid or expired: Return unauthorized error
- Person not found for Session's PersonId: System integrity error
  (should never occur)

**MVP Constraints**: Returns only the caller's own Person record.

**Note on Projection Scope**: GetCurrentPerson returns extended
identity information (including Email and Status) because the caller
is resolving their own identity context under an authenticated
Session. This projection SHALL NOT be considered equivalent to, or a
precedent for, platform-to-platform identity resolution; see 5.1
GetPersonById for the narrower projection used in that case.

---

## 4.2 GetOrganizationsForPerson

**Purpose**: Retrieve the Organizations a Person participates in via
Membership.

**Actor**: Authenticated User

**Input**: Session context (resolved from Access Token)

**Output**: List of Organization (OrganizationId, Name, Category,
Status, CreatedAt)

**Preconditions**:

- A valid, non-expired Session exists for the caller

**Identity Data Ownership Scope**: Caller MAY only retrieve
Organizations reached through their own Membership records. In MVP
this returns exactly one Organization (the Personal Organization
created at registration; see 14_MVP §1).

**Failure Conditions**:

- Session invalid or expired: Return unauthorized error

**MVP Constraints**: Every Person has exactly one Organization in MVP
(Personal Organization). Multi-Organization membership is future scope.

---

## 4.3 GetMembershipsForPerson

**Purpose**: Retrieve the Memberships belonging to a Person.

**Actor**: Authenticated User

**Input**: Session context (resolved from Access Token)

**Output**: List of Membership (MembershipId, PersonId, OrganizationId,
Role, Status, CreatedAt)

**Preconditions**:

- A valid, non-expired Session exists for the caller

**Identity Data Ownership Scope**: Caller MAY only retrieve their own
Membership records.

**Failure Conditions**:

- Session invalid or expired: Return unauthorized error

**MVP Constraints**: Returns exactly one Membership (Owner role, Active
status) in MVP.

---

## 4.4 GetSessionsForPerson

**Purpose**: Retrieve active Sessions belonging to a Person.

**Actor**: Authenticated User

**Input**: Session context (resolved from Access Token)

**Output**: List of Session (SessionId, DeviceInfo, IpAddress,
ExpiresAt, Status, CreatedAt) — AccessTokenId and RefreshTokenId SHALL
NOT be included in the output of this Query; a Session is identified by
SessionId for this purpose.

**Preconditions**:

- A valid, non-expired Session exists for the caller

**Identity Data Ownership Scope**: Caller MAY only retrieve their own
Sessions. This is an identity-resolution check intrinsic to Identity's
own data (059 §9), not Capability Authorization (ADR-0002 §2),
consistent with the labeling established in §2.4.

**Failure Conditions**:

- Session invalid or expired: Return unauthorized error

**MVP Constraints**: A Person MAY have multiple concurrent Sessions
(00_Overview §8); all are returned regardless of originating device.

---

# 5. Platform Integration Query Specifications

## 5.1 GetPersonById

**Purpose**: Resolve identity data for a PersonId held by another
Capability Platform. This is the read contract that allows Capability
Platforms consuming Identity (Resource, Finance, IoT, Manufacturing,
Reservation, Communication, Workflow, Business — per 059 §15) to
resolve a PersonId they store as a foreign reference into the Person
attributes they need (e.g. DisplayName for a UI).

**Actor**: Consuming Capability Platform (platform-to-platform, not an
end user)

**Input**: PersonId

**Output**: Person (PersonId, DisplayName) — a minimal projection.

Email and Status SHALL NOT be included in this projection.

- Email resolution for a third-party PersonId is not required by any
  current consuming Capability Platform's documented use case and is
  deferred pending a demonstrated need.
- Status is deferred for the same reason: no current consuming
  Capability Platform has a documented use case requiring it.
  Additionally, Status may expose Person lifecycle state (e.g.
  Archived, Suspended) to consuming platforms; exposing lifecycle
  state across platform boundaries without a demonstrated business
  need is avoided until such a need is documented.

Additional fields, including Status, MAY be introduced through future
Query Contract evolution once a consuming Capability Platform
documents an actual need.

**Preconditions**:

- Caller is a recognized SmartCore Capability Platform
- PersonId is provided

**Authorization Scope**:

Business authorization decisions belong to consuming Capability
Platforms. Authentication, service identity validation, and platform
communication security remain infrastructure and integration concerns
outside the scope of this Query contract.

This Query Contract does not itself define how a calling platform is
authenticated or how platform-to-platform traffic is secured — that is
a Transport and infrastructure concern (see 08_API.md), not a business-
authorization concern, and is therefore explicitly out of scope for
this document, consistent with ADR-0002 §2.

**Failure Conditions**:

- PersonId does not exist: Return not-found result
- Caller is not a recognized platform (transport/infrastructure
  concern): Handled at the Transport layer, not by this Query Contract

**MVP Constraints**:

- No public REST endpoint exists for this Query in Version 1.0 (see
  08_API.md)
- Returns a minimal projection only (PersonId, DisplayName)
- Does not evaluate whether the calling platform *should* be allowed to
  act on the resolved Person for its own business purposes — that
  determination is made by the calling platform

---

# 6. Architecture Alignment Notes

This section records the alignment between this document's Query
surface and the Command, MVP, and Platform documents where they
intersect. It is informative, not a substitute for the formal
Blueprint Validator defined by 065.

## 6.1 Against 00_Overview.md §10 (Public Surface)

- ✅ All 5 Queries declared in §10 are specified in this document
  (GetPersonById, GetCurrentPerson, GetOrganizationsForPerson,
  GetMembershipsForPerson, GetSessionsForPerson)
- ✅ No Query is added beyond the declared 5
- ✅ 00_Overview §10 is unchanged by this document

## 6.2 Against 04_Commands.md

- ✅ Queries introduce no new Aggregates, Events, or Business Rules,
  consistent with 04_Commands §1's equivalent constraint for Commands
- ✅ No Query overlaps with, or duplicates, any of the 6 Public
  Commands

## 6.3 Against 14_MVP.md §7 (API Scope for MVP)

- ✅ The 4 REST read endpoints in 14_MVP §7 (`GET /me`,
  `GET /organizations`, `GET /sessions`, `GET /me/memberships`)
  correspond exactly to the 4 User Interaction Queries in §3.1 of this
  document
- ✅ GetPersonById correctly has no corresponding REST endpoint in
  14_MVP §7 — this is intentional, not a gap (see §5.1)
- ✅ 14_MVP.md is unchanged by this document

## 6.4 Against 059_SmartCore_Identity_Platform.md §9, §11

- ✅ Authorization Boundary (§9) is respected: no Query performs
  business authorization
- ✅ Public APIs (§11) list matches the User Interaction Query set;
  §11 is described there as a minimum endpoint list, consistent with
  GetPersonById existing as a Query Contract without a listed REST
  endpoint

---

# 7. MVP Readiness Checklist

Query Blueprint Version 1.0 is complete when:

✓ All 5 Queries from 00_Overview §10 are specified

✓ User Interaction Queries map exactly to 14_MVP §7 REST endpoints

✓ Platform Integration Query (GetPersonById) is documented as a
  Contract without a public REST Transport in MVP

✓ No Query performs business authorization evaluation

✓ No new Aggregates, Events, or Business Rules introduced

✓ Architecture Alignment Notes against 00_Overview, 04_Commands,
  14_MVP, and 059 pass (Section 6)

---

# 8. Change Log

## Version 1.0.2 (2026-07-14)

Editorial precision pass from second review round. No architectural
meaning changed.

- §4.1 GetCurrentPerson: added a "Note on Projection Scope" clarifying
  that its extended output (Email, Status) reflects self-service
  resolution of the caller's own identity context, and is not a
  precedent for the narrower platform-to-platform projection used by
  GetPersonById (§5.1).

## Version 1.0.1 (2026-07-14)

Editorial precision pass from review feedback. No architectural
meaning changed.

- §4.4 GetSessionsForPerson: replaced the "Authorization Scope" label
  with "Identity Data Ownership Scope" to match the terminology
  §2.4 establishes for all User Interaction Queries. This was a
  genuine internal inconsistency in v1.0.0, not merely a stylistic
  choice.
- §5.1 GetPersonById: narrowed output from (PersonId, DisplayName,
  Status) to (PersonId, DisplayName). Status is deferred alongside
  Email pending a documented consuming-platform use case, and because
  it may expose Person lifecycle state across platform boundaries
  without a demonstrated need.
- §6 renamed from "Cross-Reference Validation" to "Architecture
  Alignment Notes" to avoid implying this document performs the role
  of the formal Blueprint Validator (065).

## Version 1.0.0 (2026-07-14)

Initial Queries Blueprint.

- Formalized the Query Contract / Transport distinction (§2.1)
- Classified the 5 Queries from 00_Overview §10 into User Interaction
  Queries (§3.1) and Platform Integration Queries (§3.2)
- Specified all 5 Queries individually (§4, §5)
- Resolved the apparent 00_Overview §10 / 14_MVP §7 count mismatch
  (5 vs. 4) by establishing that GetPersonById is valid Public Surface
  without requiring a public REST endpoint
- Authorization language for GetPersonById scoped precisely: Identity
  does not perform business authorization (ADR-0002 §2), without
  implying an absence of authentication, service identity validation,
  or platform communication security, which remain infrastructure/
  integration concerns
- Added Cross-Reference Validation (§6, renamed in 1.0.1) against
  00_Overview, 04_Commands, 14_MVP, and 059
- Does not modify 00_Overview.md, 04_Commands.md, or 14_MVP.md

---

**END OF DOCUMENT**
