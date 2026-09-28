<!--
Document ID: ID-06
Title: SmartCore Identity Platform Blueprint - Domain Events
Version: 1.0.2
Status: READY_FOR_GENERATION

Purpose:
Define the Domain Event Contract of the Identity Platform: the common
event envelope (including the Event Audit Requirements named but not
specified by 01_Domain_Model.md), event ownership, and the per-event
payload schema for all 10 MVP Identity Domain Events. This is the
document 01_Domain_Model.md's "Event Audit Requirements" section
explicitly defers to ("Detailed event contracts are defined in
06_Domain_Events.md").

Dependencies (Required):
- 00_Overview.md
- 01_Domain_Model.md
- 03_Aggregates.md
- 04_Commands.md
- 14_MVP.md
- 059_SmartCore_Identity_Platform.md
- ADR-0002_Identity_Foundation_Clarifications.md

Cross-Checked Against (Optional / informative only):
- 02_Use_Cases.md
- 05_Queries.md

Explicitly NOT a Dependency:
- 07_Contracts.md
- 08_API.md
Domain Events are independent of REST Transport and Wire Contract
concerns. Nothing in this document is derived from, or constrained by,
07_Contracts.md or 08_API.md, and this document's naming convention
(§3.1) is deliberately decoupled from 07_Contracts.md's REST-specific
camelCase convention.

Change Log:
  - Version 1.0.2 (2026-07-14): Architect-review refinement pass —
    approved with minor non-blocking comments, both addressed. (1)
    §6.1 reworded to stop implying `OccurredAt` is itself the
    per-Aggregate ordering mechanism: two events on the same Aggregate
    committed in the same transaction, or at coarse timestamp
    resolution, could carry equal or non-monotonic `OccurredAt`
    values. The guarantee is now stated as "observable in actual
    occurrence order", with `OccurredAt` merely expected to be
    consistent with that order, not the enforcement mechanism itself
    (a sequence/version/stream-position concept, left out of scope
    per §1.2). (2) Added an explicit note on the deliberate absence of
    an `EventVersion`/`SchemaVersion` envelope field: not needed while
    there is a single internal consumer context and no external
    Integration Event surface, but flagged as the most likely first
    envelope addition if either changes in a future version. No new
    Aggregates, Commands, Queries, or Events introduced; no MVP scope
    changed.
  - Version 1.0.1 (2026-07-14): Review-driven correction pass.
    (1) **Contradiction resolved**: `AggregateId` was previously
    unconditionally Required, contradicting `LoginFailed`'s own §4.6
    exception for `Reason = PersonNotFound` (no Person, hence no
    Aggregate, exists). `AggregateId` now explicitly shares the same
    exception as `ActorIdentity`, and `AggregateType` is fixed to
    `Person` for all `LoginFailed` events regardless of Reason. This
    was the one item flagged as blocking before v1.0.0 lock. (2)
    `CorrelationId` changed from unconditionally Optional to Required
    for all Person-initiated events, Optional only when
    `ActorIdentity = System` (i.e. `SessionExpired`), strengthening
    the audit-trail guarantee. (3) Added an explicit note
    distinguishing `OccurredAt` (commit time) from any future
    publish/delivery timestamp, anticipating Outbox-style delivery
    delay. (4) Added a forward-compatibility note to `PersonUpdated`
    flagging the full-snapshot payload as an MVP-appropriate choice
    that may need revisiting (delta shape, or field-specific events)
    if Person's mutable attribute set grows substantially. No new
    Aggregates, Commands, Queries, or Events introduced; no MVP scope
    changed.
  - Version 1.0.0 (2026-07-14): Initial Domain Events Blueprint.
    Defines the common Event Envelope satisfying 01_Domain_Model.md's
    Event Audit Requirements (Actor Identity, Session Reference,
    Delegated Identity, Timestamp, Execution Context); specifies all
    10 MVP events' payloads; defines Publishing Rules and Event
    Ordering guarantees; verifies alignment against 059, 01, 04, 14,
    and ADR-0002. No new Aggregates, Commands, Queries, or Events
    introduced. No Event Bus, Outbox, or Integration Event mechanics
    defined — explicitly out of scope (§1.2).
-->

# 1. Overview

This document defines the **Domain Event Contract** of the Identity
Platform: what a Domain Event *is*, structurally, and what each of the
10 MVP Identity Events *contains*.

A Domain Event answers:

> "What business fact has already, irreversibly, happened?"

Domain Events are immutable records of completed state transitions.
They are published only after the underlying Aggregate state change
has been durably committed (§5).

## 1.1 What This Document Defines

- The common Event Envelope (§4.1) — satisfying the Event Audit
  Requirements named by 01_Domain_Model.md but left unspecified there
- Event ownership (§3, restating and confirming ADR-0002 Decision 5)
- The payload schema for each of the 10 MVP events (§4.2–§4.11)
- Publishing Rules (§5) — when an event SHALL and SHALL NOT be
  published
- Event Ordering guarantees (§6) — what a consumer MAY and MAY NOT
  assume about the relative order of events

## 1.2 What This Document Does NOT Define

- **Event Bus / Message Broker technology** (Kafka, RabbitMQ, SNS/SQS,
  etc.) — an infrastructure choice, out of scope
- **Outbox pattern or delivery guarantee mechanics** (at-least-once,
  exactly-once, retry/dedup strategy) — an infrastructure/reliability
  concern, out of scope
- **Integration Event contracts** — whether, how, or in what shape
  these Domain Events cross the Identity Platform's boundary to be
  consumed by other Capability Platforms is a separate concern
  (Integration Event design is typically a translation/adapter layer
  over Domain Events, not the Domain Event itself) and is deferred to
  a future Integration/Messaging document
- **REST/Wire representation** — how (or whether) an event's data
  might ever be surfaced through a REST API is a 07_Contracts.md /
  08_API.md concern, not this one; those documents are explicitly not
  dependencies of this one (see header)

This document introduces no new:

- Aggregates
- Commands
- Queries
- Events beyond the 10 already declared in 059 §9 / ADR-0002 Decision 5
- Business Rules

---

# 2. Event Design Principles

## 2.1 Events Represent Completed Facts

Per ADR-0002 Decision 5: "Events represent completed business facts."

Every event name is a past-tense statement (`PersonRegistered`, not
`RegisterPerson`; `SessionExpired`, not `ExpireSession`). An event is
never published speculatively or in advance of the state change it
describes.

## 2.2 Events Are Immutable

Per 059 §10: "Events SHALL be immutable." Once published, an event's
content SHALL NOT be revised. A correction, if ever needed, is
expressed as a new, later event — never as a mutation of a
previously published one.

## 2.3 Events SHALL NOT Coordinate Initial Ownership Creation

Per ADR-0002 Decision 5: "Events SHALL NOT be used to coordinate
initial ownership creation." The atomicity of Person + Personal
Organization + Owner Membership creation is guaranteed by the Core
Ownership Transaction (ADR-0002 Decision 1; 059 §6), not by any
event-driven saga. `PersonRegistered`, `OrganizationCreated`, and
`MembershipCreated` are announcements of an already-consistent,
already-committed fact — never a mechanism used to *achieve* that
consistency. This principle directly shapes the Event Ordering
guarantees in §6.3.

## 2.4 Payload Fields Trace to the Domain Model

Every payload field defined in §4 traces to an attribute already
declared in 01_Domain_Model.md §2, or is a reference (ID) to another
Aggregate already named in the Domain Model's Relationships (§2 / §5
of 01_Domain_Model.md). No event invents a business concept the
Domain Model does not already have.

## 2.5 Credential Secrets Are Never Present in Any Event

No event payload SHALL ever contain `PasswordHash`, a plaintext
password, or any other Credential secret material. Where a Credential
lifecycle change must be represented (`PasswordChanged`), the payload
carries only the Credential's identity (`CredentialId`), never its
content. This follows directly from 01_Domain_Model.md §2 ("Credential
never stores plain text passwords") and 059 §13 ("Passwords SHALL
never be stored in plain text") — the same principle extended to the
event stream, where a leak would be equally damaging.

## 2.6 Field-Level Sensitivity Classification Is Deferred

This document defines which fields exist on each event payload. It
does NOT classify those fields by sensitivity (PII, restricted,
internal-only, etc.) or define redaction/masking policy for
downstream consumers or logs. Per ADR-0002 Decision 5: "Security and
audit classification of events is outside the scope of this ADR" —
this document, being the detailed continuation of that same
Decision, inherits the same boundary. Sensitivity classification is
future scope for a dedicated Security/Data Classification document.

---

# 3. Event Ownership

Per 059 §9 (Event Ownership Table) and ADR-0002 Decision 5, the
Identity Platform owns and exclusively publishes the following 10
events. Both source documents agree on this table exactly; no
discrepancy exists.

| Event | Owner | MVP | Producing Service (01_Domain_Model §"Event Producer Mapping") |
|---|---|---|---|
| PersonRegistered | Identity | Yes | RegistrationApplicationService |
| OrganizationCreated | Identity | Yes | RegistrationApplicationService |
| MembershipCreated | Identity | Yes | RegistrationApplicationService |
| LoginSucceeded | Identity | Yes | AuthenticationDomainService |
| LoginFailed | Identity | Yes | AuthenticationDomainService |
| SessionCreated | Identity | Yes | AuthenticationDomainService |
| SessionExpired | Identity | Yes | SessionManagementDomainService |
| LogoutCompleted | Identity | Yes | SessionManagementDomainService |
| PersonUpdated | Identity | Yes | PersonManagementDomainService |
| PasswordChanged | Identity | Yes | CredentialManagementDomainService |

No event in this table is owned by, or co-published with, any other
Capability Platform. No additional Identity event exists beyond these
10 in Version 1.0 (14_MVP §5).

---

# 4. Event Specifications

## 4.1 Common Event Envelope

Every event published by the Identity Platform SHARES this envelope.
Per-event payload schemas (§4.2–§4.11) define only the `Payload`
field's contents; every other envelope field below applies uniformly.

| Field | Type | Required | Description |
|---|---|---|---|
| EventId | string (UUID) | Required | Unique identifier of this event instance |
| EventType | string | Required | Exact event name, e.g. `PersonRegistered` |
| AggregateType | string enum: `Person` \| `Organization` \| `Membership` \| `Session` \| `Credential` | Required | Which Aggregate (01_Domain_Model §1) this event pertains to |
| AggregateId | string (UUID) | Required — see exception in §4.6 | The identifier of the specific Aggregate instance this event is about (e.g. PersonId for a Person-type event, SessionId for a Session-type event) |
| OccurredAt | string (timestamp, ISO 8601 UTC) | Required | When the underlying state change was committed |
| ActorIdentity | string (PersonId), or the reserved value `System` | Required — see exception in §4.6 | The Identity responsible for causing this event. `System` is reserved for events triggered by scheduled/system processes rather than a Person-initiated request (§4.8) |
| SessionReference | string (SessionId) | Optional | The Session under which the causing request was made, if any (see per-event tables for when this applies) |
| DelegatedIdentity | string (PersonId) | Optional — always absent in MVP | Reserved for a future delegation feature (00_Overview §17 / 059 §17). No MVP Command or Use Case populates this field; it exists so the envelope shape does not need to change when delegation is introduced |
| ExecutionContext | object (§4.1.1) | Optional | Operational context describing how the causing request was made |
| Payload | object | Required | Event-specific business data; schema defined per event in §4.2–§4.11 |

These five audit-related fields (`ActorIdentity`, `SessionReference`,
`DelegatedIdentity`, `OccurredAt`, `ExecutionContext`) are exactly the
five named, but not specified, by 01_Domain_Model.md's Event Audit
Requirements section ("Actor Identity", "Session Reference (optional)",
"Delegated Identity (optional)", "Timestamp", "Execution Context").
This table is their detailed contract.

**On `OccurredAt` vs. publish time**: `OccurredAt` is strictly the
moment the underlying Aggregate state change was committed — it is
**not** the moment the event was published, transmitted, or received
by any consumer. This document defines no `PublishedAt` field, since
publication timing is a delivery-mechanism concern (§1.2, §5.5) outside
this Contract's scope. If a future Event Bus/Outbox implementation
introduces a delay between commit and publish (which is expected and
normal for outbox-style delivery), that delay is invisible to this
Contract: `OccurredAt` remains fixed at commit time regardless of when
the event actually reaches a consumer. A future Transport/delivery
document, not this one, would be the place to add a `PublishedAt`
field if a consumer-facing need for it is demonstrated.

**On the absence of a schema `EventVersion` field**: This envelope
does not include a payload-schema version field (e.g. `EventVersion`
or `SchemaVersion`). For Version 1.0, with a single internal consumer
context and no external Integration Event surface yet defined (§1.2),
this is not a gap. If a future version introduces external/cross-
platform consumers of these events, or the payload schemas in §4.2–
§4.11 need to evolve in a way that isn't purely additive, an
`EventVersion` field is the most likely first addition to this
envelope — but adding it speculatively now, before such a consumer
exists, would be scope creep beyond what Version 1.0 needs.

### 4.1.1 ExecutionContext Object

| Field | Type | Required |
|---|---|---|
| CorrelationId | string | Required, except Optional when `ActorIdentity = System` |
| IpAddress | string | Optional |
| DeviceInfo | string | Optional |

**On `CorrelationId`**: A request-initiated event (i.e. `ActorIdentity`
is a real PersonId — every event in §4 except `SessionExpired`, and
except `LoginFailed` when `Reason = PersonNotFound`, per §4.6) SHALL
carry a `CorrelationId` linking it back to the originating request,
since a full audit trail for a Person-initiated action requires being
able to trace the event to the request that caused it. `CorrelationId`
is Optional only for the one case where no external request exists to
correlate against: `SessionExpired`, triggered by a scheduled job with
`ActorIdentity = System` (§4.8). `IpAddress` and `DeviceInfo` remain
Optional throughout, since not every request transport surfaces them
(e.g. an internal service-to-service call may have no meaningful
client IP/device to report).

`ExecutionContext` is the single place operational request metadata
lives for every event — including events on Aggregates that have no
`DeviceInfo`/`IpAddress` attribute of their own (Person, Organization,
Membership, Credential). For Session-related events, this is
populated from the same source data as the Session Aggregate's own
`DeviceInfo`/`IpAddress` attributes (01_Domain_Model §2), but it is
captured here at the envelope level rather than duplicated inside the
event-specific Payload, so that every event — not only Session events
— has one consistent place for this information.

## 4.2 PersonRegistered

**Producer**: RegistrationApplicationService (§3)

**ActorIdentity**: The newly created Person's own PersonId (this is
self-registration — the actor causing the event is the new Identity
itself).

**SessionReference**: The initial Session's SessionId, if Post-Commit
Session creation succeeded; absent if it failed (059 §6 Non-Invalidating
Policy — Session creation failure SHALL NOT invalidate the
PersonRegistered fact itself).

### Payload

| Field | Type | Required |
|---|---|---|
| PersonId | string (UUID) | Required |
| Email | string | Required |
| DisplayName | string | Required |
| OrganizationId | string (UUID) | Required |
| MembershipId | string (UUID) | Required |

`OrganizationId` and `MembershipId` are included directly on this
payload — rather than requiring a consumer to separately correlate
`OrganizationCreated`/`MembershipCreated` — because, per ADR-0002
Decision 1, a Person without ownership context is an invalid Identity
state: by the time `PersonRegistered` exists at all, these two
references are guaranteed to already exist and be valid (§2.3, §6.3).

## 4.3 OrganizationCreated

**Producer**: RegistrationApplicationService (§3)

**ActorIdentity**: The PersonId of the registering Person (the
Organization's Owner).

**SessionReference**: Absent. Organization creation is part of the
Core Ownership Transaction, which precedes any authenticated Session
context (059 §6) — there is no session to reference at this point in
the flow.

### Payload

| Field | Type | Required |
|---|---|---|
| OrganizationId | string (UUID) | Required |
| Name | string | Required |
| Category | string enum: `Personal` | Required |
| Status | string enum: `Active` | Required |

**MVP Constraint**: `Category` is always `Personal` and `Status` is
always `Active` in MVP (01_Domain_Model §2; 14_MVP §3).

## 4.4 MembershipCreated

**Producer**: RegistrationApplicationService (§3)

**ActorIdentity**: The PersonId of the registering Person.

**SessionReference**: Absent — same rationale as §4.3.

### Payload

| Field | Type | Required |
|---|---|---|
| MembershipId | string (UUID) | Required |
| PersonId | string (UUID) | Required |
| OrganizationId | string (UUID) | Required |
| Role | string enum: `Owner` | Required |
| Status | string enum: `Active` | Required |

**MVP Constraint**: `Role` is always `Owner` and `Status` is always
`Active` in MVP (14_MVP §1, §3; 059 §9 Role Model).

## 4.5 LoginSucceeded

**Producer**: AuthenticationDomainService (§3)

**ActorIdentity**: The authenticated Person's PersonId.

**SessionReference**: The SessionId of the Session created as part of
this same authentication (04_Commands §4.2). See §6.2 for the ordering
relationship between `LoginSucceeded` and `SessionCreated`.

### Payload

| Field | Type | Required |
|---|---|---|
| PersonId | string (UUID) | Required |
| SessionId | string (UUID) | Required |

## 4.6 LoginFailed

**Producer**: AuthenticationDomainService (§3)

**ActorIdentity**: **Exception to the envelope's general rule** — this
field is Optional for `LoginFailed` only, and is absent specifically
when `Reason = PersonNotFound` (there is no Person to identify as the
actor). When `Reason` is `CredentialMissing` or `InvalidPassword`, a
Person record does exist and `ActorIdentity` SHALL be populated with
that PersonId.

**AggregateType / AggregateId — same exception, extended**: `AggregateType`
is always `Person` for `LoginFailed` (a login attempt is conceptually
an operation against a Person, whether or not resolution succeeds).
`AggregateId`, however, follows exactly the same rule as `ActorIdentity`
above: it SHALL be populated with the resolved PersonId when `Reason`
is `CredentialMissing` or `InvalidPassword` (a Person record exists),
and SHALL be absent when `Reason = PersonNotFound` (there is no
Aggregate instance to identify — no Person, and therefore no
Aggregate, exists to attempt authentication against). This is the same
underlying exception stated once, not a second independent one: both
`ActorIdentity` and `AggregateId` are populated from the same resolved
PersonId when a Person exists, and both are absent together when one
does not.

**SessionReference**: Always absent — no Session is created on a
failed authentication attempt (04_Commands §4.2 Postconditions).

### Payload

| Field | Type | Required |
|---|---|---|
| Email | string | Required |
| Reason | string enum: `PersonNotFound` \| `CredentialMissing` \| `InvalidPassword` | Required |

**Security Note**: `Reason` carries the precise internal cause for
audit purposes, per 04_Commands §4.2's Security Note that "internally,
LoginFailed is still published with the precise cause for audit
purposes" even though any external-facing surface built on top of this
event (e.g. a REST API) is expected to collapse these three reasons
into a single generic outcome to prevent account enumeration. That
collapsing, if any, is a concern for whatever document defines that
external surface — not for this Domain Event, whose entire purpose
includes preserving the precise audit trail.

## 4.7 SessionCreated

**Producer**: AuthenticationDomainService (§3)

**ActorIdentity**: The Person the Session belongs to.

**SessionReference**: Not applicable as a separate field — this event
*is* the Session's own creation; `AggregateId` (envelope) already
carries the SessionId.

### Payload

| Field | Type | Required |
|---|---|---|
| SessionId | string (UUID) | Required |
| PersonId | string (UUID) | Required |
| ExpiresAt | string (timestamp) | Required |

`DeviceInfo` and `IpAddress` are deliberately not duplicated into this
Payload — they are available on this same event via
`ExecutionContext` (§4.1.1), which is where all request-execution
metadata lives uniformly across every event, not only Session events.

## 4.8 SessionExpired

**Producer**: SessionManagementDomainService (§3)

**ActorIdentity**: The reserved value `System`. Per 02_Use_Cases UC-007
"Main Flow (System Expiration)", this event originates from a
scheduled job, not a Person-initiated request — there is no human
actor to name.

**SessionReference**: Not applicable as a separate field; `AggregateId`
carries the SessionId.

### Payload

| Field | Type | Required |
|---|---|---|
| SessionId | string (UUID) | Required |
| PersonId | string (UUID) | Required |

## 4.9 LogoutCompleted

**Producer**: SessionManagementDomainService (§3)

**ActorIdentity**: The Person who performed the logout.

**SessionReference**: Not applicable as a separate field; `AggregateId`
carries the SessionId (the same Session being closed).

### Payload

| Field | Type | Required |
|---|---|---|
| SessionId | string (UUID) | Required |
| PersonId | string (UUID) | Required |

## 4.10 PersonUpdated

**Producer**: PersonManagementDomainService (§3)

**ActorIdentity**: The Person whose profile was updated (Session
ownership validation in 04_Commands §4.3 guarantees the caller is
always this same Person — self-service update only in MVP).

**SessionReference**: The Session under which the update request was
authenticated.

### Payload

| Field | Type | Required |
|---|---|---|
| PersonId | string (UUID) | Required |
| Email | string | Required |
| DisplayName | string | Required |

**Design Note**: This payload carries the Person's full current
`Email`/`DisplayName` state after the update (not a before/after diff).
04_Commands §4.3 allows updating either or both fields in a single
request; rather than modeling optional delta fields, this event
reports the resulting full state, which is simpler for consumers to
project and avoids ambiguity about which fields actually changed
versus were merely re-submitted unchanged.

**Forward-Compatibility Note**: This snapshot approach is appropriate
while Person carries a small, fixed attribute set (01_Domain_Model §2:
`Email`, `DisplayName` are the only mutable profile fields in MVP). If
a future version grows Person's mutable attribute set substantially,
a full-snapshot `PersonUpdated` may become disproportionately large
for what actually changed. That future scenario would be a candidate
for either a delta-shaped `PersonUpdated`, or splitting into
field-specific events (e.g. `PersonEmailChanged`,
`PersonDisplayNameChanged`) — a decision explicitly deferred, since
introducing that complexity now would be speculative given MVP's
two-field profile.

## 4.11 PasswordChanged

**Producer**: CredentialManagementDomainService (§3)

**ActorIdentity**: The Person whose Credential was replaced.

**SessionReference**: The Session under which the change request was
authenticated.

### Payload

| Field | Type | Required |
|---|---|---|
| PersonId | string (UUID) | Required |
| CredentialId | string (UUID) | Required |

Per §2.5, this payload SHALL NEVER include `PasswordHash` or any
password value, plain or hashed. `CredentialId` identifies *which*
Credential record became active — it is an identity reference, not
secret material, consistent with the distinction 01_Domain_Model.md
§3 draws between `AccessTokenId` (an identity) and token content
(implementation-specific, and in that case client-facing) — here, by
contrast, `CredentialId` is deliberately never accompanied by its
underlying secret in any form.

---

# 5. Publishing Rules

## 5.1 Publish-After-Commit

An event SHALL only be published after the state change it describes
has been durably committed to its owning Aggregate's store. No event
is published speculatively, optimistically, or before commit.

## 5.2 Failed Persistence Never Produces an Event

If an Aggregate's state change fails to persist, no event describing
that (non-)change is published. This applies uniformly across all 10
events — including, notably, that a failed Core Ownership Transaction
(rolled back per ADR-0002 Decision 1) produces none of
`PersonRegistered`, `OrganizationCreated`, or `MembershipCreated`.

## 5.3 LoginFailed Is the One Event That Is Not a State Change

`LoginFailed` is the sole exception to "events describe a committed
Aggregate state change": no Aggregate state changes on a failed
authentication attempt (04_Commands §4.2 Postconditions: "Authentication
attempts do not modify: Person identity state, Credential state").
`LoginFailed` instead reports a completed *business fact of a
different kind* — "this authentication attempt was rejected" — which
is why it is still governed by the same immutability and audit rules
as every other event (§2.1, §2.2) even though §5.1's "state change"
framing does not literally apply to it. It is published once the
authentication decision itself (accept/reject) is finalized, not tied
to any Aggregate persistence.

## 5.4 Post-Commit Operation Failures Do Not Suppress Already-Valid Events

Per 059 §6 (Non-Invalidating Policy) and ADR-0002 Decision 1: if a
Post-Commit Operation fails (Credential creation, initial Session
creation), the already-committed Core Ownership Transaction's events
(`PersonRegistered`, `OrganizationCreated`, `MembershipCreated`) are
still published. Only the specific field that depended on the failed
operation is affected — e.g. `PersonRegistered.SessionReference` is
simply absent (§4.2) if Session creation failed. The event as a whole
is never withheld because of a Post-Commit failure.

## 5.5 Delivery Mechanics Are Out of Scope

Whether publishing is at-least-once or exactly-once, whether retries
occur, and how a consumer deduplicates are Event Bus / Messaging
Engine concerns (00_Overview §9 lists "Messaging Engine" as a
dependency of the Identity Platform, not a component the Identity
Platform itself implements) and are explicitly out of scope for this
document (§1.2).

---

# 6. Event Ordering

## 6.1 Per-Aggregate Ordering

Events sharing the same `AggregateId` (envelope, §4.1) SHALL be
observable by a consumer in the actual order those state transitions
occurred on that Aggregate instance — e.g. for a single Session:
`SessionCreated` always precedes that same Session's eventual
`SessionExpired` or `LogoutCompleted` (never the reverse).

`OccurredAt` (§4.1) is expected to be consistent with this order, but
is **not itself the ordering mechanism**: two events on the same
Aggregate committed within the same transaction, or at timestamp
resolutions too coarse to distinguish, could carry equal or
non-monotonic `OccurredAt` values. The actual ordering guarantee is
provided by whatever mechanism assigns a strict per-Aggregate sequence
(e.g. an Aggregate version/sequence number, or event-store stream
position) — that mechanism is an Event Bus/store implementation
concern, out of scope per §1.2. This section states only the guarantee
a consumer MAY rely on (state transitions are observable in true
occurrence order); it does not claim `OccurredAt` is the field that
enforces it.

## 6.2 Cross-Event Ordering Is Not Guaranteed Except Where Stated

No ordering is guaranteed between events on *different* Aggregate
instances unless explicitly stated below. In particular:

- `LoginSucceeded` and `SessionCreated` (same AuthenticatePerson call):
  **no publish-order guarantee**. A consumer needing both SHALL
  correlate them via the shared `SessionId`/`PersonId`, not via the
  order in which it happens to receive them.
- `PersonRegistered`, `OrganizationCreated`, `MembershipCreated` (same
  RegisterPerson call): **no publish-order guarantee among the three**
  — see §6.3 for why this is architecturally safe.

## 6.3 Why Unordered Registration Events Are Safe

Per §2.3 (Events SHALL NOT coordinate initial ownership creation) and
ADR-0002 Decision 1, the Core Ownership Transaction has already
committed Person + Organization + Membership atomically *before any of
the three events is published*. A consumer that receives
`OrganizationCreated` before `PersonRegistered` is not observing a
race condition or an inconsistent intermediate state — the Person and
Membership already exist, fully committed, regardless of which event
arrives first. This is precisely what distinguishes an event
*announcing* a fact from an event *coordinating* one (§2.3): ordering
would only matter if the events were themselves part of achieving
consistency, and per ADR-0002 Decision 1 they are not.

## 6.4 SessionExpired and LogoutCompleted Are Mutually Exclusive

Per the Session lifecycle (01_Domain_Model §5 — a Session transitions
to exactly one terminal state, `Expired` or `Closed`), at most one of
`SessionExpired` / `LogoutCompleted` SHALL ever be published for a
given `SessionId`. A consumer SHALL treat these as alternative,
not sequential, outcomes for the same Session instance.

---

# 7. Cross-Document Alignment

## 7.1 Against 059_SmartCore_Identity_Platform.md §9–§10

✅ The 10 events specified in §4 are exactly the 10 events in 059's
Event Ownership Table and §10 Identity Events list. No addition, no
omission.

## 7.2 Against 01_Domain_Model.md

✅ Every "Produces" mapping in 01_Domain_Model.md's Domain Services
(§7) and Application Services (§8) sections, and its consolidated
"Event Producer Mapping" section, matches §3 of this document exactly.

✅ The five Event Audit Requirement fields named by 01_Domain_Model.md
are fully specified by the Common Event Envelope (§4.1) — this
document is the "06_Domain_Events.md" that section explicitly
forward-referenced.

## 7.3 Against 04_Commands.md

✅ Every Command's "Events Produced" (04_Commands §8, Command → Event
Mapping) matches the events specified per-Contract in §4 of this
document. `RefreshSession`, which produces no event (04_Commands §4.5),
correctly has no corresponding section in §4.

## 7.4 Against 14_MVP.md §5

✅ The 10-event list in 14_MVP.md §5 ("Event Ownership Alignment")
matches §3 of this document exactly, including the note that "future
events... are explicitly scoped as future" — this document defines no
such future event.

## 7.5 Against ADR-0002_Identity_Foundation_Clarifications.md

✅ Decision 5 (Event Ownership Documentation)'s table matches §3
exactly.

✅ Decision 5's statement "Events SHALL NOT be used to coordinate
initial ownership creation" is upheld and elaborated in §2.3 and §6.3.

✅ Decision 5's statement "Security and audit classification of events
is outside the scope of this ADR" is respected — this document defines
audit *structure* (§4.1) and per-field *existence* (§4.2–§4.11), but
explicitly defers sensitivity *classification* (§2.6).

---

# 8. MVP Readiness Checklist

Domain Events Blueprint Version 1.0 is complete when:

✓ All 10 MVP events (059 §9 / ADR-0002 Decision 5) have a full payload
  specification (§4.2–§4.11)

✓ The Common Event Envelope satisfies all five Event Audit Requirement
  fields named by 01_Domain_Model.md (§4.1)

✓ No event payload contains Credential secret material (§2.5)

✓ Publishing Rules are defined for the normal case, the Post-Commit
  failure case, and the one non-state-change event (`LoginFailed`)
  (§5)

✓ Event Ordering guarantees are stated precisely enough that a
  consumer knows what it MAY and MAY NOT assume (§6)

✓ No new Aggregates, Commands, Queries, or Events introduced

✓ Alignment verified against 059, 01_Domain_Model, 04_Commands,
  14_MVP, and ADR-0002 (§7)

✓ No Event Bus, Outbox, or Integration Event mechanics defined (out of
  scope, §1.2)

---

# 9. Change Log

## Version 1.0.2 (2026-07-14)

Architect-review refinement pass. Approved with minor non-blocking
comments; both addressed here.

- §6.1: reworded the per-Aggregate ordering guarantee so it no longer
  implies `OccurredAt` itself enforces ordering. Two events on the
  same Aggregate could share an `OccurredAt` value (same transaction,
  or coarse timestamp resolution); the actual guarantee is that
  consumers observe true occurrence order via whatever sequencing
  mechanism the Event Bus/store provides (out of scope, §1.2) —
  `OccurredAt` is expected to be consistent with that order, not the
  mechanism itself.
- §4.1: added an explicit note on why no `EventVersion`/`SchemaVersion`
  field exists in Version 1.0, and under what future condition
  (external/cross-platform consumers, non-additive payload evolution)
  it would likely become the first envelope addition.

No new Aggregates, Commands, Queries, or Events introduced. No MVP
scope changed.

## Version 1.0.1 (2026-07-14)

Review-driven correction pass.

- **Resolved the one flagged contradiction**: `AggregateId` is no
  longer unconditionally Required. It now explicitly follows the same
  `LoginFailed(PersonNotFound)` exception already established for
  `ActorIdentity` (§4.6) — absent when no Person can be resolved,
  since there is then no Aggregate instance to identify.
  `AggregateType` for `LoginFailed` is fixed to `Person` regardless of
  `Reason`, since a login attempt is conceptually always an operation
  against a Person, resolved or not.
- `CorrelationId` (§4.1.1) changed from unconditionally Optional to
  Required for every Person-initiated event, and Optional only for
  `System`-actor events (`SessionExpired`), closing an audit-trail gap.
- §4.1 now explicitly states `OccurredAt` is commit time, not publish
  time, and that no `PublishedAt` field is defined here — anticipating
  that Outbox-style delivery may introduce a commit/publish delay in a
  future Transport/delivery document without affecting this Contract.
- §4.10 `PersonUpdated` now carries a forward-compatibility note
  flagging that its full-snapshot payload is an MVP-appropriate choice
  that may need revisiting if Person's mutable attribute set grows.

No new Aggregates, Commands, Queries, or Events introduced. No MVP
scope changed.

## Version 1.0.0 (2026-07-14)

Initial Domain Events Blueprint.

- Defined the Common Event Envelope (§4.1), giving concrete shape to
  the five Event Audit Requirement fields named but not specified by
  01_Domain_Model.md
- Specified per-event Producer, ActorIdentity behavior,
  SessionReference applicability, and Payload schema for all 10 MVP
  events (§4.2–§4.11)
- Documented two intentional exceptions to the general envelope rule:
  `LoginFailed`'s optional `ActorIdentity` when the Person cannot be
  resolved (§4.6), and `SessionExpired`'s `System`-valued
  `ActorIdentity` for scheduled-job-triggered expiration (§4.8)
- Established Publishing Rules (§5), including the Post-Commit
  non-invalidation carry-through (§5.4) and `LoginFailed`'s status as
  the one event not tied to an Aggregate state change (§5.3)
- Established Event Ordering guarantees (§6): per-aggregate ordering
  (§6.1), explicit non-guarantees for `LoginSucceeded`/`SessionCreated`
  and the three registration events (§6.2), the architectural
  rationale for why unordered registration events remain safe (§6.3),
  and the mutual exclusivity of `SessionExpired`/`LogoutCompleted`
  (§6.4)
- Verified full alignment against 059, 01_Domain_Model, 04_Commands,
  14_MVP, and ADR-0002 (§7)
- Explicitly excluded Event Bus, Outbox, and Integration Event
  mechanics from scope (§1.2), per this Blueprint's stated intent to
  define only the Domain Event Contract

---

**END OF DOCUMENT**