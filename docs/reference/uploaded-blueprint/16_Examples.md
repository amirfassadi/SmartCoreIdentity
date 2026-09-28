<!--
Document ID: ID-16
Title: SmartCore Identity Platform Blueprint - Examples
Version: 1.1.0
Status: READY_FOR_GENERATION

Purpose:
Provide illustrative, non-normative scenarios for the Identity
Capability Platform: sequence diagrams, example Command and Query
invocations, Aggregate interaction traces, Domain Event flows, and
example wire payloads. Per 064_SmartCore_Blueprint_Standard §8.17,
this document is informative only. It introduces no new Aggregates,
Commands, Queries, Events, or business rules, and is not to be read as
a source of additional requirements beyond what is defined in
02_Use_Cases.md, 03_Aggregates.md, 04_Commands.md, 05_Queries.md,
06_Domain_Events.md, and 07_Contracts.md.

Dependencies:
- 00_Overview.md
- 01_Domain_Model.md
- 02_Use_Cases.md
- 03_Aggregates.md
- 04_Commands.md
- 05_Queries.md
- 06_Domain_Events.md
- 07_Contracts.md
- 08_API.md
- 14_MVP.md
- 064_SmartCore_Blueprint_Standard.md
- ADR-0002_Identity_Foundation_Clarifications.md

Change Log:
  - Version 1.1.0 (2026-07-18): Review pass. (1) Reduced reliance on
    Section-number citations in narrative prose in favor of naming the
    Contract/Event/Query directly, so this document degrades more
    gracefully if referenced documents' Section numbers shift; Section
    numbers are retained only in §13 Cross-Document Alignment Notes,
    where precision matters most and where 05_Queries.md §6 /
    08_API.md §7 set the same precedent. (2) Softened normative
    ("SHALL") language inside example narrative to descriptive
    language, since this document is informative only; the two
    remaining SHALL instances are meta-statements about this
    document's own status, not about example behavior. (3) Simplified
    sequence diagram swimlanes to Client / Identity Platform /
    Aggregate, moving internal orchestrator names (e.g.
    RegistrationApplicationService) to a caption below each diagram
    rather than a diagram lane, since which internal service performs
    the work is 01_Domain_Model.md's concern, not this document's. (4)
    Added Register Person failure examples for Core Ownership
    Transaction failure and Post-Commit Operation failure (previously
    only Duplicate Person was shown). (5) Added a second RefreshSession
    failure example (unknown token, 404) alongside the existing
    expired-token example (401), and a note on revoked-token sharing
    the expired-token shape. (6) UpdatePersonProfile example now shows
    both `email` and `displayName` supplied together, not
    `displayName` alone. (7) GetSessionsForPerson example changed both
    Sessions to `Active` status (previously one was `Expired`), with a
    note explaining this avoids taking an interpretive position 05_
    Queries.md itself does not resolve (whether expired Sessions
    continue to appear in this list is an implementation/retention
    concern, not specified by the Query Contract). (8) Added an
    explicit "Illustrative only — no REST Transport exists in Version
    1.0" note at the top of the GetPersonById example. (9) Added a
    one-line "Processing Flow" annotation under each Command example
    (Validate → Command → Aggregate → Event → Response), illustrating
    internal shape without adding new rules. (10) Added §11 End-to-End
    New User Journey, chaining existing Use Cases into one narrative
    with no new content. (11) Added §12 Appendix: Complete Event
    Payload Samples, showing full envelopes for all 10 MVP events in
    one place (several were previously only referenced, not shown, for
    brevity). (12) Empty response bodies now annotated "No response
    body returned" rather than shown as a bare `{}` with no
    explanation. No new Aggregates, Commands, Queries, Events, or
    business rules introduced by any of the above; all changes are
    presentation and completeness improvements to an already-
    informative document.
  - Version 1.0.0 (2026-07-18): Initial Examples document. Covers all
    9 MVP Use Cases (UC-001 through UC-009), all 6 MVP Commands, all 5
    MVP Queries (4 User Interaction + 1 Platform Integration), and all
    10 MVP Domain Events, with sequence diagrams, Aggregate
    interaction traces, and example wire payloads consistent with
    07_Contracts.md and 08_API.md. No new Aggregates, Commands,
    Queries, Events, or business rules introduced.
-->

# 1. Overview

This document is **informative only**, per 064_SmartCore_Blueprint_Standard
§8.17.

Every scenario below illustrates behavior already fully specified by
the Use Cases, Aggregates, Commands, Queries, Domain Events, and
Contracts documents in this Blueprint Package. If anything here
appears to conflict with any of those documents, those documents are
authoritative and this document is in error. No example here is to be
read as introducing a new requirement, Aggregate, Command, Query,
Event, or business rule.

All example values (UUIDs, timestamps, tokens) are illustrative
placeholders, not literal specification.

**On diagram style**: sequence diagrams below use a simplified
`Client` / `Identity Platform` / `Aggregate` swimlane structure. Which
internal Domain Service or Application Service performs a given step
(e.g. `RegistrationApplicationService`, `AuthenticationDomainService`)
is noted as a caption below the diagram, not as its own swimlane —
that assignment belongs to 01_Domain_Model.md and 04_Commands.md, and
keeping it out of the diagram itself keeps these examples focused on
externally observable behavior.

**On the internal processing shape**: every Command example below
follows the same internal shape, shown as a one-line annotation:

```
Validate → Command → Aggregate(s) → Event(s) → Response
```

This is illustrative only; it restates, in shorthand, exactly what
04_Commands.md and 06_Domain_Events.md already specify per Command,
not a new rule.

---

# 2. Example 1 — Register Person (UC-001)

## 2.1 Narrative

A new user registers with an email, password, and display name. This
is the only MVP flow that creates Organization and Membership, and the
only Command in MVP orchestrated by an Application Service rather than
a Domain Service (ADR-0002 Decision 7).

**Processing Flow**: `Validate → RegisterPerson → Person + Organization + Membership (atomic) → Credential + Session (post-commit) → PersonRegistered + OrganizationCreated + MembershipCreated → Response`

## 2.2 Sequence Diagram

```
Client                    Identity Platform                  Person Agg   Organization Agg   Membership Agg   Credential Agg   Session Agg
  |  POST /auth/register        |                                 |             |                  |                |                |
  |----------------------------->|                                |             |                  |                |                |
  |                               | -- Core Ownership Transaction (atomic) --   |                  |                |                |
  |                               |--Create----------------------->|             |                  |                |                |
  |                               |--Create------------------------------------->|                  |                |                |
  |                               |--Create---------------------------------------------------------->|                |                |
  |                               | -- Commit -- (rollback all three if any step fails) --            |                |                |
  |                               |                                |             |                  |                |                |
  |                               | -- Post-Commit Operations (failure here does NOT invalidate above) --            |                |
  |                               |--Create Credential------------------------------------------------------------->|                |
  |                               |--Create Session------------------------------------------------------------------------------------>|
  |                               |                                |             |                  |                |                |
  |                               | Publish: PersonRegistered, OrganizationCreated, MembershipCreated                                    |
  |<------------------------------| 201 Created (person, organization, membership, session)                                              |
```

**Orchestration note**: the Core Ownership Transaction is coordinated
by `RegistrationApplicationService`; Post-Commit Credential and Session
creation delegate to `CredentialManagementDomainService` and the
Session-creation lifecycle respectively (01_Domain_Model.md §8).

## 2.3 Aggregate Interaction Trace

| Step | Aggregate | Operation | Phase |
|---|---|---|---|
| 1 | Person | Create | Core Ownership Transaction |
| 2 | Organization | Create | Core Ownership Transaction |
| 3 | Membership | Create | Core Ownership Transaction |
| 4 | Credential | Create | Post-Commit |
| 5 | Session | Create | Post-Commit |

## 2.4 Example Request — RegisterPerson Contract

`POST /auth/register`

```json
{
  "email": "ava.morgan@example.com",
  "password": "Tr0ub4dor&3xample!",
  "displayName": "Ava Morgan"
}
```

## 2.5 Example Success Response (201)

```json
{
  "person": {
    "personId": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
    "email": "ava.morgan@example.com",
    "displayName": "Ava Morgan",
    "status": "Active",
    "createdAt": "2026-07-18T09:12:00Z",
    "updatedAt": "2026-07-18T09:12:00Z"
  },
  "organization": {
    "organizationId": "3c7a1b90-2d4e-4f8a-8b1c-9d0e1f2a3b4c",
    "name": "Ava Morgan",
    "category": "Personal",
    "status": "Active",
    "createdAt": "2026-07-18T09:12:00Z"
  },
  "membership": {
    "membershipId": "5a6b7c8d-9e0f-4a1b-8c2d-3e4f5a6b7c8d",
    "personId": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
    "organizationId": "3c7a1b90-2d4e-4f8a-8b1c-9d0e1f2a3b4c",
    "role": "Owner",
    "status": "Active",
    "createdAt": "2026-07-18T09:12:00Z"
  },
  "session": {
    "sessionId": "b2c3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d6e",
    "accessToken": "eyJhbGciOiJIUzI1NiJ9.example.access",
    "refreshToken": "9f8e7d6c-5b4a-4321-9876-abcdef123456",
    "expiresAt": "2026-07-18T10:12:00Z",
    "status": "Authenticated"
  }
}
```

Per the RegisterPerson Contract (07_Contracts.md), `session` is
Optional — a 201 response without `session` still indicates successful
registration; see §2.7 below for that case.

## 2.6 Example Event: PersonRegistered

```json
{
  "eventId": "1a2b3c4d-0000-4000-8000-000000000001",
  "eventType": "PersonRegistered",
  "aggregateType": "Person",
  "aggregateId": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
  "occurredAt": "2026-07-18T09:12:00Z",
  "actorIdentity": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
  "sessionReference": "b2c3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d6e",
  "delegatedIdentity": null,
  "executionContext": {
    "correlationId": "req-3f9a8b7c",
    "ipAddress": "203.0.113.42",
    "deviceInfo": "iOS/18.1 SmartCoreApp/2.3.0"
  },
  "payload": {
    "personId": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
    "email": "ava.morgan@example.com",
    "displayName": "Ava Morgan",
    "organizationId": "3c7a1b90-2d4e-4f8a-8b1c-9d0e1f2a3b4c",
    "membershipId": "5a6b7c8d-9e0f-4a1b-8c2d-3e4f5a6b7c8d"
  }
}
```

`OrganizationCreated` and `MembershipCreated` are published as
independent events alongside `PersonRegistered`; full payloads for
both appear in the Appendix (§12). Their `sessionReference` is absent,
since they occur during the Core Ownership Transaction, before any
Session exists.

## 2.7 Example Failure — Duplicate Person

`POST /auth/register` with an email already registered:

```json
{
  "error": {
    "code": "PERSON_ALREADY_EXISTS",
    "message": "A person with this email already exists."
  }
}
```

HTTP 409. No ownership state is created; the Core Ownership Transaction
never begins.

## 2.8 Example Failure — Validation Error

`POST /auth/register` with a malformed email:

```json
{
  "email": "not-an-email",
  "password": "Tr0ub4dor&3xample!",
  "displayName": "Ava Morgan"
}
```

```json
{
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Email format is invalid."
  }
}
```

HTTP 400. The Core Ownership Transaction never begins.

## 2.9 Example Failure — Core Ownership Transaction Failure

Condition: an infrastructure error occurs while persisting Person,
Organization, or Membership (e.g. a transient database failure during
Membership creation, after Person and Organization were tentatively
written within the same transaction).

```json
{
  "error": {
    "code": "REGISTRATION_FAILED",
    "message": "Registration could not be completed. Please try again."
  }
}
```

HTTP 500. Per the Core Ownership Transaction's atomicity guarantee, the
entire transaction is rolled back — no Person, Organization, or
Membership record persists, even though Person and Organization
creation had tentatively succeeded before Membership creation failed.
No partial ownership state remains observable to any later request.

## 2.10 Example — Post-Commit Operation Failure (Session Creation Fails)

Condition: the Core Ownership Transaction commits successfully, but
Initial Session creation fails afterward (e.g. a transient token-
issuance failure). Per the Registration Model, this does **not**
invalidate the already-committed ownership result, and the response is
still a 201 — not an error:

```json
{
  "person": {
    "personId": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
    "email": "ava.morgan@example.com",
    "displayName": "Ava Morgan",
    "status": "Active",
    "createdAt": "2026-07-18T09:12:00Z",
    "updatedAt": "2026-07-18T09:12:00Z"
  },
  "organization": {
    "organizationId": "3c7a1b90-2d4e-4f8a-8b1c-9d0e1f2a3b4c",
    "name": "Ava Morgan",
    "category": "Personal",
    "status": "Active",
    "createdAt": "2026-07-18T09:12:00Z"
  },
  "membership": {
    "membershipId": "5a6b7c8d-9e0f-4a1b-8c2d-3e4f5a6b7c8d",
    "personId": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
    "organizationId": "3c7a1b90-2d4e-4f8a-8b1c-9d0e1f2a3b4c",
    "role": "Owner",
    "status": "Active",
    "createdAt": "2026-07-18T09:12:00Z"
  }
}
```

`session` is absent — 201 with no `session` field. A typical client
treats this as successful registration and prompts the user to
authenticate (§3 below) to obtain a Session. Recovery of the failed
Post-Commit operation itself is implementation-specific and outside
this Blueprint's scope.

---

# 3. Example 2 — Authenticate Person + Create Session (UC-003, UC-004)

## 3.1 Narrative

An existing user logs in. Credential validation and Session creation
happen within the same operation on success.

**Processing Flow**: `Validate → AuthenticatePerson → Person (read) + Credential (validate) + Session (create) → LoginSucceeded + SessionCreated → Response`

## 3.2 Sequence Diagram

```
Client                Identity Platform            Person Agg   Credential Agg   Session Agg
  | POST /auth/login        |                            |             |               |
  |------------------------->|                            |             |               |
  |                          |--Resolve by email--------->|             |               |
  |                          |<--Person found--------------|             |               |
  |                          |--Validate password------------------------>|               |
  |                          |<--Valid-------------------------------------|               |
  |                          |--Create Session--------------------------------------------->|
  |                          |<--Session created--------------------------------------------|
  |                          | Publish: LoginSucceeded, SessionCreated   |               |
  |<-------------------------| 200 OK (session)          |             |               |
```

**Orchestration note**: performed by `AuthenticationDomainService`
(01_Domain_Model.md §7).

## 3.3 Example Request — AuthenticatePerson Contract

`POST /auth/login`

```json
{
  "email": "ava.morgan@example.com",
  "password": "Tr0ub4dor&3xample!",
  "deviceInfo": "Chrome/126.0 Windows 11"
}
```

## 3.4 Example Success Response (200)

```json
{
  "session": {
    "sessionId": "c4d5e6f7-a8b9-4c0d-9e1f-2a3b4c5d6e7f",
    "accessToken": "eyJhbGciOiJIUzI1NiJ9.example.access2",
    "refreshToken": "1a2b3c4d-5e6f-4789-9012-3456789abcde",
    "expiresAt": "2026-07-18T11:45:00Z",
    "status": "Authenticated"
  }
}
```

## 3.5 Example Event: LoginSucceeded

```json
{
  "eventId": "1a2b3c4d-0000-4000-8000-000000000002",
  "eventType": "LoginSucceeded",
  "aggregateType": "Person",
  "aggregateId": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
  "occurredAt": "2026-07-18T10:45:00Z",
  "actorIdentity": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
  "sessionReference": "c4d5e6f7-a8b9-4c0d-9e1f-2a3b4c5d6e7f",
  "delegatedIdentity": null,
  "executionContext": {
    "correlationId": "req-7c6b5a4d",
    "ipAddress": "203.0.113.77",
    "deviceInfo": "Chrome/126.0 Windows 11"
  },
  "payload": {
    "personId": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
    "sessionId": "c4d5e6f7-a8b9-4c0d-9e1f-2a3b4c5d6e7f"
  }
}
```

`SessionCreated` is published alongside it, sharing
`aggregateId = sessionId`; full payload in the Appendix (§12).

## 3.6 Example Failure — Invalid Credentials

```json
{
  "error": {
    "code": "INVALID_CREDENTIALS",
    "message": "The email or password is incorrect."
  }
}
```

HTTP 401. This identical response is returned regardless of whether the
cause is "person not found", "no active credential", or "password
mismatch" — collapsed at the wire level to prevent account
enumeration. Internally, `LoginFailed` still records the precise
`reason`:

```json
{
  "eventType": "LoginFailed",
  "aggregateType": "Person",
  "aggregateId": null,
  "actorIdentity": null,
  "sessionReference": null,
  "occurredAt": "2026-07-18T10:46:00Z",
  "payload": {
    "email": "unknown.user@example.com",
    "reason": "PersonNotFound"
  }
}
```

`aggregateId` and `actorIdentity` are absent here specifically because
`reason = PersonNotFound` — no Person record exists to reference (this
is the one documented exception to the event envelope's usual
requiredness, per 06_Domain_Events.md).

---

# 4. Example 3 — Update Person Profile (UC-005)

## 4.1 Narrative

An authenticated user changes both their email and display name in the
same request. (Either field alone is also valid — the Contract accepts
`email`, `displayName`, or both, as long as at least one is present.)

**Processing Flow**: `Validate → UpdatePersonProfile → Person (update) → PersonUpdated → Response`

## 4.2 Example Request — UpdatePersonProfile Contract

`PATCH /me`

```json
{
  "email": "ava.r.morgan@example.com",
  "displayName": "Ava R. Morgan"
}
```

## 4.3 Aggregate Interaction

| Aggregate | Operation |
|---|---|
| Person | Update |
| Session | Read / Validate (ownership check only; not modified) |

## 4.4 Example Success Response (200)

```json
{
  "person": {
    "personId": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
    "email": "ava.r.morgan@example.com",
    "displayName": "Ava R. Morgan",
    "status": "Active",
    "createdAt": "2026-07-18T09:12:00Z",
    "updatedAt": "2026-07-18T10:50:00Z"
  }
}
```

## 4.5 Example Event: PersonUpdated

```json
{
  "eventType": "PersonUpdated",
  "aggregateType": "Person",
  "aggregateId": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
  "actorIdentity": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
  "sessionReference": "c4d5e6f7-a8b9-4c0d-9e1f-2a3b4c5d6e7f",
  "occurredAt": "2026-07-18T10:50:00Z",
  "payload": {
    "personId": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
    "email": "ava.r.morgan@example.com",
    "displayName": "Ava R. Morgan"
  }
}
```

The payload carries the full resulting profile state (both fields),
not a before/after diff — this is a deliberate design choice
(06_Domain_Events.md), not an artifact of this example.

## 4.6 Example Failure — Email Already In Use

```json
{
  "error": {
    "code": "EMAIL_ALREADY_IN_USE",
    "message": "This email is already associated with another account."
  }
}
```

HTTP 409. No update is committed; `displayName` is not applied either,
even though only `email` conflicted — the Command is validated and
applied as a whole.

---

# 5. Example 4 — Change Password (UC-006)

## 5.1 Narrative

**Processing Flow**: `Validate → ChangePassword → Credential (create new, mark old Replaced) → PasswordChanged → Response`

## 5.2 Example Request — ChangePassword Contract

`POST /me/password`

```json
{
  "currentPassword": "Tr0ub4dor&3xample!",
  "newPassword": "C0rrect#Horse!Battery9"
}
```

## 5.3 Aggregate Interaction

| Aggregate | Operation |
|---|---|
| Person | Read |
| Credential | Create (new) / Update (old → Replaced) |
| Session | Read / Validate |

## 5.4 Example Success Response (200)

No response body returned.

```json
{}
```

## 5.5 Example Event: PasswordChanged

```json
{
  "eventType": "PasswordChanged",
  "aggregateType": "Credential",
  "aggregateId": "e1f2a3b4-c5d6-4e7f-8a9b-0c1d2e3f4a5b",
  "actorIdentity": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
  "sessionReference": "c4d5e6f7-a8b9-4c0d-9e1f-2a3b4c5d6e7f",
  "occurredAt": "2026-07-18T10:55:00Z",
  "payload": {
    "personId": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
    "credentialId": "e1f2a3b4-c5d6-4e7f-8a9b-0c1d2e3f4a5b"
  }
}
```

Consistent with the payload rules for this event (06_Domain_Events.md),
no password value — plain or hashed — appears anywhere in this
payload; `credentialId` is an identity reference only.

## 5.6 Example Failure — Wrong Current Password

```json
{
  "error": {
    "code": "INVALID_CURRENT_PASSWORD",
    "message": "The current password provided is incorrect."
  }
}
```

HTTP 400 (not 401) — the Session is already validated; this is a
Command field validation failure, not a Session authentication
failure.

---

# 6. Example 5 — Logout (UC-007, User-Initiated)

## 6.1 Narrative

**Processing Flow**: `Validate → LogoutSession → Session (update → Closed) → LogoutCompleted → Response`

## 6.2 Example Request — LogoutSession Contract

`POST /auth/logout`

```json
{}
```

Session resolved implicitly from the bearer Access Token; no explicit
`sessionId` needed for the common case of closing the caller's own
current Session.

## 6.3 Example Success Response (200)

No response body returned.

```json
{}
```

## 6.4 Example Event: LogoutCompleted

```json
{
  "eventType": "LogoutCompleted",
  "aggregateType": "Session",
  "aggregateId": "c4d5e6f7-a8b9-4c0d-9e1f-2a3b4c5d6e7f",
  "actorIdentity": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
  "occurredAt": "2026-07-18T11:30:00Z",
  "payload": {
    "sessionId": "c4d5e6f7-a8b9-4c0d-9e1f-2a3b4c5d6e7f",
    "personId": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f"
  }
}
```

## 6.5 Example Failure — Session Already Closed

```json
{
  "error": {
    "code": "SESSION_ALREADY_CLOSED",
    "message": "This session has already been closed."
  }
}
```

HTTP 409.

---

# 7. Example 6 — System Session Expiration (UC-007, System-Initiated)

## 7.1 Narrative

A scheduled process identifies a Session past its `expiresAt` and
expires it. Unlike every other MVP event, this one has no human actor.

**Processing Flow**: `Scheduled trigger → Session (update → Expired) → SessionExpired`

## 7.2 Sequence Diagram

```
Scheduled Process        Identity Platform             Session Agg
   | tick (ExpiresAt <= now)   |                              |
   |---------------------------->|                              |
   |                             |--Read + Update status=Expired->|
   |                             |<--------------------------------|
   |                             | Publish: SessionExpired      |
```

**Orchestration note**: performed by `SessionManagementDomainService`
(01_Domain_Model.md §7). No client request or response exists in this
flow — it is not client-initiated.

## 7.3 Example Event: SessionExpired

```json
{
  "eventType": "SessionExpired",
  "aggregateType": "Session",
  "aggregateId": "c4d5e6f7-a8b9-4c0d-9e1f-2a3b4c5d6e7f",
  "actorIdentity": "System",
  "sessionReference": null,
  "occurredAt": "2026-07-18T12:00:00Z",
  "executionContext": {
    "correlationId": null
  },
  "payload": {
    "sessionId": "c4d5e6f7-a8b9-4c0d-9e1f-2a3b4c5d6e7f",
    "personId": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f"
  }
}
```

`actorIdentity = "System"` and `correlationId` absent — the only event
in MVP with no originating human request to correlate against.

---

# 8. Example 7 — Refresh Session (UC-009)

## 8.1 Narrative

**Processing Flow**: `Validate → RefreshSession → Session (update tokens) → (no event) → Response`

## 8.2 Example Request — RefreshSession Contract

`POST /auth/refresh`

```json
{
  "refreshToken": "1a2b3c4d-5e6f-4789-9012-3456789abcde"
}
```

## 8.3 Example Success Response (200)

```json
{
  "sessionId": "c4d5e6f7-a8b9-4c0d-9e1f-2a3b4c5d6e7f",
  "accessToken": "eyJhbGciOiJIUzI1NiJ9.example.access3",
  "refreshToken": "1a2b3c4d-5e6f-4789-9012-3456789abcde",
  "expiresAt": "2026-07-18T13:45:00Z"
}
```

No `status` field — RefreshSession does not change or report
lifecycle state.

## 8.4 Events Produced

None. Token maintenance is not a business state transition and does
not produce a Domain Event.

## 8.5 Example Failure — Unknown Refresh Token

```json
{
  "error": {
    "code": "INVALID_REFRESH_TOKEN",
    "message": "This refresh token is not recognized."
  }
}
```

HTTP 404 — the token never existed; there is nothing to authenticate
against.

## 8.6 Example Failure — Expired Refresh Token

```json
{
  "error": {
    "code": "INVALID_REFRESH_TOKEN",
    "message": "This refresh token has expired."
  }
}
```

HTTP 401 (not 404) — the token existed but is no longer valid, distinct
from a token that never existed. A revoked token returns the identical
shape (same `error.code`, same 401 status) — the Contract does not
distinguish revoked from expired on the wire.

---

# 9. Query Examples

## 9.1 GetCurrentPerson

`GET /me`

```json
{
  "person": {
    "personId": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
    "email": "ava.r.morgan@example.com",
    "displayName": "Ava R. Morgan",
    "status": "Active",
    "createdAt": "2026-07-18T09:12:00Z",
    "updatedAt": "2026-07-18T10:50:00Z"
  }
}
```

## 9.2 GetOrganizationsForPerson

`GET /organizations`

```json
{
  "organizations": [
    {
      "organizationId": "3c7a1b90-2d4e-4f8a-8b1c-9d0e1f2a3b4c",
      "name": "Ava Morgan",
      "category": "Personal",
      "status": "Active",
      "createdAt": "2026-07-18T09:12:00Z"
    }
  ]
}
```

Always length 1 in MVP.

## 9.3 GetMembershipsForPerson

`GET /me/memberships`

```json
{
  "memberships": [
    {
      "membershipId": "5a6b7c8d-9e0f-4a1b-8c2d-3e4f5a6b7c8d",
      "personId": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
      "organizationId": "3c7a1b90-2d4e-4f8a-8b1c-9d0e1f2a3b4c",
      "role": "Owner",
      "status": "Active",
      "createdAt": "2026-07-18T09:12:00Z"
    }
  ]
}
```

## 9.4 GetSessionsForPerson

`GET /sessions`

```json
{
  "sessions": [
    {
      "sessionId": "c4d5e6f7-a8b9-4c0d-9e1f-2a3b4c5d6e7f",
      "deviceInfo": "Chrome/126.0 Windows 11",
      "ipAddress": "203.0.113.77",
      "expiresAt": "2026-07-18T13:45:00Z",
      "status": "Active",
      "createdAt": "2026-07-18T10:45:00Z"
    },
    {
      "sessionId": "b2c3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d6e",
      "deviceInfo": "iOS/18.1 SmartCoreApp/2.3.0",
      "ipAddress": "203.0.113.42",
      "expiresAt": "2026-07-19T09:12:00Z",
      "status": "Active",
      "createdAt": "2026-07-18T09:12:00Z"
    }
  ]
}
```

Note `accessToken`/`refreshToken` are absent from this list projection
by design. Both Sessions shown are `Active`, illustrating the
multi-device Session model without taking a position this document
isn't in a position to take: whether a non-Active (e.g. `Expired`)
Session continues to appear in this list is a retention/cleanup
implementation detail that 05_Queries.md does not resolve, so this
example does not assume an answer either way.

## 9.5 GetPersonById

**Illustrative only — no REST Transport exists for this Query in
Version 1.0.** The shape below is shown for completeness, since the
Query Contract remains valid Public Surface even without a REST
binding; it is not an internal API a client can currently call over
HTTP.

Illustrative internal/service-to-service call from a consuming
Capability Platform (e.g. Resource Platform resolving a `PersonId` it
holds as a foreign reference):

**Abstract Request**:

```json
{
  "personId": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f"
}
```

**Abstract Response** (minimal projection):

```json
{
  "person": {
    "personId": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
    "displayName": "Ava R. Morgan"
  }
}
```

`email` and `status` are deliberately absent — this is a distinct,
narrower projection than `GetCurrentPerson` (§9.1 above).

---

# 10. Event Flow Summary

| Use Case | Events Illustrated in This Document |
|---|---|
| UC-001 Register Person | §2.6 PersonRegistered (OrganizationCreated, MembershipCreated — full payloads in §12) |
| UC-003 Authenticate Person | §3.5 LoginSucceeded / LoginFailed |
| UC-004 Create Session | SessionCreated — full payload in §12 |
| UC-005 Manage Person Profile | §4.5 PersonUpdated |
| UC-006 Change Credential | §5.5 PasswordChanged |
| UC-007 End Session (user) | §6.4 LogoutCompleted |
| UC-007 End Session (system) | §7.3 SessionExpired |
| UC-008 Create Organization Membership | MembershipCreated — full payload in §12 |
| UC-009 Refresh Session | §8.4 (no event produced) |

---

# 11. End-to-End New User Journey

This section chains the individual examples above into a single
continuous narrative for one illustrative user, Ava. It introduces no
behavior beyond what §2–§9 already specify — it only sequences them.

```
1. Register           POST /auth/register              → Person, Organization, Membership created (atomic)
                                                          → Credential, Session created (post-commit)
                                                          → PersonRegistered, OrganizationCreated,
                                                            MembershipCreated published
2. Get current user   GET /me                           → confirms registered profile
3. Update profile     PATCH /me                         → email + displayName updated
                                                          → PersonUpdated published
4. Refresh token      POST /auth/refresh                → new accessToken issued, session unchanged
5. Change password    POST /me/password                 → Credential replaced
                                                          → PasswordChanged published
6. List sessions      GET /sessions                     → shows this session (and any others)
7. Logout             POST /auth/logout                 → Session closed
                                                          → LogoutCompleted published
   (— time passes —)
8. (System) expire    (scheduled process, not client)   → any other unattended Session past expiresAt
                                                            transitions to Expired
                                                          → SessionExpired published
9. Login again        POST /auth/login                  → credentials validated, new Session created
                                                          → LoginSucceeded, SessionCreated published
```

Steps 1–7 correspond directly to §2, §9.1, §4, §8, §5, §9.4, and §6
respectively. Step 8 corresponds to §7 and may occur independently of
the user's own actions, at any point after a Session's `expiresAt`
passes. Step 9 corresponds to §3. At no point does Ava's `PersonId`,
`OrganizationId`, or `MembershipId` change — these remain stable
across the entire journey, consistent with Identity persisting
independently of Session lifecycle (00_Overview.md).

---

# 12. Appendix: Complete Event Payload Samples

For completeness, this appendix shows one full envelope for each of
the 10 MVP Domain Events in one place. Several appeared only by
reference above for brevity; this section is the single place all ten
can be viewed together. Field-by-field rules for each event remain
authoritative in 06_Domain_Events.md — this appendix is a rendering,
not a restatement, of those rules.

## 12.1 PersonRegistered

See §2.6.

## 12.2 OrganizationCreated

```json
{
  "eventType": "OrganizationCreated",
  "aggregateType": "Organization",
  "aggregateId": "3c7a1b90-2d4e-4f8a-8b1c-9d0e1f2a3b4c",
  "actorIdentity": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
  "sessionReference": null,
  "occurredAt": "2026-07-18T09:12:00Z",
  "payload": {
    "organizationId": "3c7a1b90-2d4e-4f8a-8b1c-9d0e1f2a3b4c",
    "name": "Ava Morgan",
    "category": "Personal",
    "status": "Active"
  }
}
```

## 12.3 MembershipCreated

```json
{
  "eventType": "MembershipCreated",
  "aggregateType": "Membership",
  "aggregateId": "5a6b7c8d-9e0f-4a1b-8c2d-3e4f5a6b7c8d",
  "actorIdentity": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
  "sessionReference": null,
  "occurredAt": "2026-07-18T09:12:00Z",
  "payload": {
    "membershipId": "5a6b7c8d-9e0f-4a1b-8c2d-3e4f5a6b7c8d",
    "personId": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
    "organizationId": "3c7a1b90-2d4e-4f8a-8b1c-9d0e1f2a3b4c",
    "role": "Owner",
    "status": "Active"
  }
}
```

## 12.4 LoginSucceeded

See §3.5.

## 12.5 LoginFailed

See §3.6.

## 12.6 SessionCreated

```json
{
  "eventType": "SessionCreated",
  "aggregateType": "Session",
  "aggregateId": "c4d5e6f7-a8b9-4c0d-9e1f-2a3b4c5d6e7f",
  "actorIdentity": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
  "occurredAt": "2026-07-18T10:45:00Z",
  "executionContext": {
    "correlationId": "req-7c6b5a4d",
    "ipAddress": "203.0.113.77",
    "deviceInfo": "Chrome/126.0 Windows 11"
  },
  "payload": {
    "sessionId": "c4d5e6f7-a8b9-4c0d-9e1f-2a3b4c5d6e7f",
    "personId": "8f14e2a1-6b3d-4c9a-9e2f-1a2b3c4d5e6f",
    "expiresAt": "2026-07-18T11:45:00Z"
  }
}
```

## 12.7 SessionExpired

See §7.3.

## 12.8 LogoutCompleted

See §6.4.

## 12.9 PersonUpdated

See §4.5.

## 12.10 PasswordChanged

See §5.5.

---

# 13. Cross-Document Alignment Notes

This section is informative, consistent with the precedent set by
05_Queries.md §6 and 08_API.md §7. It is not a substitute for the
formal Blueprint Validator (065). Section-number references are used
here deliberately, since this section's purpose is precise
cross-checking, not narrative readability.

- ✅ All 6 MVP Commands (04_Commands.md §3) have at least one worked
  example, including at least one failure case each (§2–§8)
- ✅ All 5 MVP Queries (05_Queries.md §3) have at least one worked
  example (§9)
- ✅ All 10 MVP Domain Events (06_Domain_Events.md §4) appear with a
  full envelope at least once, either inline or in the Appendix (§12)
- ✅ Every wire payload shown matches the DTO shapes defined in
  07_Contracts.md §4–§6; no field is invented
- ✅ Every HTTP method/path shown matches 08_API.md §3–§4
- ✅ UpdatePersonProfile example exercises both optional fields
  together (§4), not only one
- ✅ RefreshSession failure examples cover both not-found (404) and
  expired (401) cases (§8.5–§8.6)
- ✅ GetSessionsForPerson example avoids asserting a position on
  Expired-Session list visibility that 05_Queries.md itself does not
  take (§9.4)
- ✅ No new Aggregate, Command, Query, Event, or business rule is
  introduced by this document

---

# 14. MVP Readiness Checklist

Examples Blueprint is complete when:

✓ Every MVP Command has a worked example, including at least one
  failure case

✓ Every MVP Query has a worked example

✓ Every MVP Domain Event appears with a full envelope at least once

✓ Example payloads are consistent with 07_Contracts.md DTO shapes

✓ Example endpoints are consistent with 08_API.md Transport mapping

✓ At least one representative failure scenario is shown per Aggregate
  area (Person, Session, Credential)

✓ An End-to-End journey exists chaining Use Cases without introducing
  new behavior

✓ No new requirements, Aggregates, Commands, Queries, or Events
  introduced

---

**END OF DOCUMENT**
