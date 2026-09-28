<!--
Document ID: ID-07
Title: SmartCore Identity Platform Blueprint - Contracts (Request/Response Schemas)
Version: 1.0.1
Status: READY_FOR_GENERATION

Purpose:
Define the Request/Response Schemas, DTO field contracts, and Error
Model for the Identity Platform's REST Transport Surface. This document
is the deferred target named by 04_Commands.md §1, 05_Queries.md §2.1,
and 08_API.md §1/§8: those documents intentionally excluded schema,
DTO, and error-body definition and pointed here.

Dependencies:
- 00_Overview.md
- 01_Domain_Model.md
- 03_Aggregates.md
- 04_Commands.md
- 05_Queries.md
- 08_API.md
- 14_MVP.md
- 059_SmartCore_Identity_Platform.md
- ADR-0002_Identity_Foundation_Clarifications.md

Change Log:
  - Version 1.0.1 (2026-07-14): Review-driven clarification pass, no
    schema, mapping, or Contract content changed. (1) §3.1 expanded the
    AccessTokenId → accessToken naming note to clarify that the wire
    field name describes client-facing usage (the literal bearer
    credential the client presents on subsequent requests), not a
    claim about server-side token representation — resolving an
    ambiguity between the wire naming choice and 01_Domain_Model.md
    §3's deliberately implementation-agnostic "Token content is
    implementation-specific" wording. (2) §2.3 narrowed the "No schema
    SHALL introduce a field" constraint to explicitly scope it to
    *business data* fields, and added a note that transport-level
    envelope metadata (traceId, correlationId, pagination, links) is
    not restricted by this section, to prevent the constraint from
    being misread as blocking ordinary future API evolution.
  - Version 1.0.0 (2026-07-14): Initial Contracts Blueprint. Defines
    Request/Response JSON schemas for all 11 Contracts (6 Commands,
    5 Queries), a shared DTO catalog derived from 01_Domain_Model.md
    entity attributes, a common Error Response shape, and a Status
    Code Disambiguation table resolving the "401 vs 403 vs 404" gap
    explicitly deferred by 08_API.md §8. No new Aggregates, Commands,
    Queries, Events, or REST endpoints introduced.
-->

# 1. Overview

This document defines the **Contract layer** artifacts that
04_Commands.md, 05_Queries.md, and 08_API.md each deferred:

- Request Schemas (JSON body / fields per Contract)
- Response Schemas (success body per Contract)
- Shared DTOs (Person, Organization, Membership, Session)
- Common Error Response shape
- HTTP status code disambiguation per failure condition

This document does not redefine business intent, orchestration, or
Transport mapping — those remain owned by 04_Commands.md,
05_Queries.md, and 08_API.md respectively. This document only answers:

> "What does the request/response body actually look like on the
> wire, and what does each failure condition return?"

This document introduces no new:

- Aggregates
- Commands
- Queries
- Events
- REST endpoints
- Business Rules

Every schema in this document maps to exactly one Contract already
defined in 04_Commands.md or 05_Queries.md, transported per
08_API.md.

---

# 2. Scope Boundary

## 2.1 What This Document Defines

- JSON field names, types, and optionality for every Request body
- JSON field names, types, and optionality for every success Response
  body
- A shared DTO catalog (Person, Organization, Membership, Session)
  reused across multiple Contracts
- A common Error Response shape
- The specific HTTP status code for every failure condition already
  enumerated in 04_Commands.md / 05_Queries.md

## 2.2 What This Document Does NOT Define

- Business intent or validation *rules* (owned by 04_Commands.md /
  05_Queries.md — this document only gives those rules a wire shape)
- Which HTTP method/path a Contract uses (owned by 08_API.md)
- Authentication transport mechanics — bearer token header name,
  placement, refresh-cookie strategy, etc. (still out of scope; see
  §11)
- API versioning strategy (still out of scope; see §11)
- Rate limiting / pagination conventions (still out of scope; see §11)

## 2.3 MVP Boundary Protection

No schema SHALL introduce a **business data field** not already
present as an attribute in 01_Domain_Model.md §2, or as an explicit
Input field in 04_Commands.md. Where a DTO intentionally narrows a
domain entity's attribute set (e.g. GetPersonById's minimal projection
per 05_Queries §5.1), this document follows that same narrowing — it
does not restore omitted fields.

**Scope of this constraint**: This protects against Contract Layer
drift — the Wire Contract inventing domain concepts that were never
approved by the Domain Model or Command/Query specifications. It does
NOT prohibit transport-level envelope metadata that carries no
business meaning of its own (e.g. a future `traceId`, `correlationId`,
pagination cursors, or HATEOAS `links`). Such fields, if introduced in
a future version, describe the delivery of the response, not the
Identity domain, and their addition would not constitute a schema
violation under this section. None are introduced in Version 1.0 (see
§11); this note exists only to prevent this section from being
misread as blocking ordinary API evolution.

---

# 3. Conventions

## 3.1 Naming Convention: Domain Model vs. Wire Format

01_Domain_Model.md attributes are written in PascalCase (e.g.
`PersonId`, `DisplayName`) as domain-level names. This is a modeling
convention, not a wire format.

**This document establishes**: JSON Request/Response bodies use
**camelCase** field names, consistent with standard REST/JSON
convention. This is a one-to-one, purely mechanical rename — no
attribute is added, removed, or reinterpreted.

| Domain Model (PascalCase) | Wire Format (camelCase) |
|---|---|
| PersonId | personId |
| Email | email |
| DisplayName | displayName |
| Status | status |
| CreatedAt | createdAt |
| UpdatedAt | updatedAt |
| OrganizationId | organizationId |
| Name | name |
| Category | category |
| MembershipId | membershipId |
| Role | role |
| SessionId | sessionId |
| AccessTokenId | accessToken |
| RefreshTokenId | refreshToken |
| DeviceInfo | deviceInfo |
| IpAddress | ipAddress |
| ExpiresAt | expiresAt |

**Note on token fields**: `AccessTokenId` / `RefreshTokenId` are
rendered on the wire as `accessToken` / `refreshToken` rather than
`accessTokenId` / `refreshTokenId`.

01_Domain_Model.md §3 defines AccessTokenId as "access token identity"
and explicitly states "Token content is implementation-specific" — the
domain model deliberately does not mandate whether the Value Object's
content is an opaque reference or a self-contained bearer credential
(e.g. a JWT). This document does not resolve that implementation
choice either; it only names the wire field for what the client does
with it, not for what the server stores.

The wire field is named `accessToken` (not `accessTokenId`) because,
regardless of internal representation, the value the client receives
in this field is the literal bearer credential it SHALL present on
subsequent requests — the client never needs to dereference an
identifier to obtain a separate token value. If a future implementation
represents AccessTokenId internally as a database-side reference to a
token record, the value returned in this field is still expected to be
the usable bearer credential (e.g. a signed/opaque string the resource
server can validate directly), not the internal reference itself. This
rename is therefore a statement about client-facing usage, not a claim
about server-side storage, and does not constrain or contradict
01_Domain_Model.md §3.

## 3.2 Type Conventions

| Domain Type | Wire Type | Format |
|---|---|---|
| Identifier (PersonId, OrganizationId, etc.) | string | UUID (RFC 4122) |
| Timestamp (CreatedAt, ExpiresAt, etc.) | string | ISO 8601 / RFC 3339 UTC (e.g. `2026-07-14T10:30:00Z`) |
| EmailAddress | string | RFC 5322-valid email, normalized lowercase |
| Status / Category / Role (enums) | string | Fixed enum values, see §4 per-entity tables |
| Free text (DisplayName, DeviceInfo) | string | UTF-8 |

## 3.3 Optionality Notation

- **Required**: field SHALL be present in every request/response of
  that shape.
- **Optional**: field MAY be absent or `null`. Absent and explicit
  `null` are treated identically.

## 3.4 Common Error Response Shape

Every non-2xx response returns the same envelope:

```json
{
  "error": {
    "code": "STRING_ERROR_CODE",
    "message": "Human-readable description"
  }
}
```

| Field | Type | Required | Notes |
|---|---|---|---|
| error.code | string | Required | Stable, machine-readable identifier (see per-Contract tables in §5, §6) |
| error.message | string | Required | Human-readable; not guaranteed stable across versions; not for programmatic branching |

No additional top-level fields are defined in MVP (no `details`,
`traceId`, etc.). Extending the envelope is future scope and would not
require a breaking change, since clients SHALL only branch on
`error.code`, not on envelope shape.

---

# 4. Shared DTO Catalog

These DTOs are reused across multiple Contracts. Each is derived
directly from its Aggregate's attributes in 01_Domain_Model.md §2,
narrowed only where a specific Contract's output is explicitly scoped
narrower (noted per-DTO below).

## 4.1 Person DTO (full projection)

Used by: RegisterPerson response, GetCurrentPerson response,
UpdatePersonProfile response.

| Field | Type | Required |
|---|---|---|
| personId | string (UUID) | Required |
| email | string | Required |
| displayName | string | Required |
| status | string enum: `Active` \| `Suspended` \| `Archived` | Required |
| createdAt | string (timestamp) | Required |
| updatedAt | string (timestamp) | Required |

**MVP Constraint**: `status` is always `Active` in Version 1.0
responses (14_MVP §3 — no MVP Command transitions Person out of
Active). The enum's other values are declared for domain-model
completeness (01_Domain_Model §5) but are not reachable in MVP
responses.

## 4.2 Person DTO (minimal projection — GetPersonById only)

Used by: GetPersonById (Platform Integration; no REST Transport in
MVP — see §6.5).

| Field | Type | Required |
|---|---|---|
| personId | string (UUID) | Required |
| displayName | string | Required |

Per 05_Queries §5.1, `email` and `status` are deliberately excluded
from this projection. This is a distinct, narrower DTO from §4.1 — the
two SHALL NOT be unified, consistent with 05_Queries §4.1's explicit
note that the full projection is not precedent for this one.

## 4.3 Organization DTO

Used by: RegisterPerson response, GetOrganizationsForPerson response.

| Field | Type | Required |
|---|---|---|
| organizationId | string (UUID) | Required |
| name | string | Required |
| category | string enum: `Personal` | Required |
| status | string enum: `Active` \| `Suspended` \| `Archived` | Required |
| createdAt | string (timestamp) | Required |

**MVP Constraint**: `category` is always `Personal` (01_Domain_Model
§2 — "Category MVP: Personal"). `status` is always `Active` in MVP
responses (14_MVP §3).

## 4.4 Membership DTO

Used by: RegisterPerson response, GetMembershipsForPerson response.

| Field | Type | Required |
|---|---|---|
| membershipId | string (UUID) | Required |
| personId | string (UUID) | Required |
| organizationId | string (UUID) | Required |
| role | string enum: `Owner` | Required |
| status | string enum: `Active` \| `Revoked` | Required |
| createdAt | string (timestamp) | Required |

**MVP Constraint**: `role` is always `Owner` (14_MVP §1; 059 §9 Role
Model). `status` is always `Active` in MVP responses (14_MVP §3).

## 4.5 Session DTO (full projection — includes tokens)

Used by: RegisterPerson response (post-commit session), AuthenticatePerson
response, RefreshSession response.

| Field | Type | Required |
|---|---|---|
| sessionId | string (UUID) | Required |
| accessToken | string | Required |
| refreshToken | string | Required |
| expiresAt | string (timestamp) | Required |
| status | string enum: `Authenticated` \| `Active` | Required |

**Design Note**: `deviceInfo` and `ipAddress` are captured internally
(01_Domain_Model §2) but are not returned to the caller who already
possesses that context (the caller is the device in question) — they
are only exposed via GetSessionsForPerson (§4.6), where a caller lists
*all* of their own Sessions across devices.

## 4.6 Session DTO (list projection — GetSessionsForPerson only)

Used by: GetSessionsForPerson response.

| Field | Type | Required |
|---|---|---|
| sessionId | string (UUID) | Required |
| deviceInfo | string | Optional (absent if not captured at Session creation) |
| ipAddress | string | Required |
| expiresAt | string (timestamp) | Required |
| status | string enum: `Created` \| `Authenticated` \| `Active` \| `Suspended` \| `Expired` \| `Closed` | Required |
| createdAt | string (timestamp) | Required |

Per 05_Queries §4.4, `accessToken`/`refreshToken` (AccessTokenId /
RefreshTokenId) SHALL NOT be included in this projection. This is a
distinct, narrower DTO from §4.5 and the two SHALL NOT be unified.

---

# 5. Command Contracts

## 5.1 RegisterPerson

**Transport**: `POST /auth/register` (08_API §3) — success 201

### Request Body

| Field | Type | Required |
|---|---|---|
| email | string | Required |
| password | string | Required |
| displayName | string | Required |

### Success Response (201)

| Field | Type | Required |
|---|---|---|
| person | Person DTO (§4.1) | Required |
| organization | Organization DTO (§4.3) | Required |
| membership | Membership DTO (§4.4) | Required |
| session | Session DTO (§4.5) | Optional |

**Note on `session` optionality**: Per 059 §6 / 04_Commands §4.1, Credential
and Session creation are Post-Commit Operations whose failure SHALL NOT
invalidate the ownership result. Consequently, `person`, `organization`,
and `membership` are always present on a 201 response, but `session` MAY
be absent if post-commit Session creation failed. A client receiving a
201 without `session` SHALL treat registration as successful and prompt
the user to authenticate via AuthenticatePerson (§5.2) to obtain a
Session. This is the only Command response in this catalog where a
field's absence does not indicate any error.

### Error Responses

| Condition (04_Commands §4.1) | error.code | HTTP Status |
|---|---|---|
| Person with email already exists | `PERSON_ALREADY_EXISTS` | 409 |
| Invalid email format | `VALIDATION_ERROR` | 400 |
| Invalid password (fails strength rules) | `VALIDATION_ERROR` | 400 |
| Invalid displayName | `VALIDATION_ERROR` | 400 |
| Core Ownership Transaction failure (infrastructure) | `REGISTRATION_FAILED` | 500 |

---

## 5.2 AuthenticatePerson

**Transport**: `POST /auth/login` (08_API §3) — success 200

### Request Body

| Field | Type | Required |
|---|---|---|
| email | string | Required |
| password | string | Required |
| deviceInfo | string | Optional |

### Success Response (200)

| Field | Type | Required |
|---|---|---|
| session | Session DTO (§4.5) | Required |

### Error Responses

| Condition (04_Commands §4.2) | error.code | HTTP Status |
|---|---|---|
| Person not found by email | `INVALID_CREDENTIALS` | 401 |
| No active Credential for Person | `INVALID_CREDENTIALS` | 401 |
| Password validation failure | `INVALID_CREDENTIALS` | 401 |
| Authentication infrastructure unavailable | `SERVICE_UNAVAILABLE` | 503 |

**Security Note**: "Person not found", "no active Credential", and
"password mismatch" all return the identical `error.code`
(`INVALID_CREDENTIALS`) and identical HTTP status (401). This is
intentional: distinguishing them on the wire would allow account
enumeration (confirming whether a given email is registered). This
collapsing applies only at the Transport/wire level — internally,
LoginFailed (059 Event Ownership Table) is still published with the
precise cause for audit purposes (01_Domain_Model §7 Event Audit
Requirements).

---

## 5.3 UpdatePersonProfile

**Transport**: `PATCH /me` (08_API §3) — success 200

### Request Body

| Field | Type | Required |
|---|---|---|
| email | string | Optional |
| displayName | string | Optional |

At least one of `email` / `displayName` SHALL be present
(04_Commands §4.3).

### Success Response (200)

| Field | Type | Required |
|---|---|---|
| person | Person DTO (§4.1) | Required |

### Error Responses

| Condition (04_Commands §4.3) | error.code | HTTP Status |
|---|---|---|
| Neither field provided | `VALIDATION_ERROR` | 400 |
| Invalid email format | `VALIDATION_ERROR` | 400 |
| Invalid displayName format/length | `VALIDATION_ERROR` | 400 |
| New email already used by another Person | `EMAIL_ALREADY_IN_USE` | 409 |
| Session missing / invalid / expired | `UNAUTHORIZED` | 401 |
| Session does not belong to Person | `FORBIDDEN` | 403 |
| Person not found (system integrity — should not occur) | `PERSON_NOT_FOUND` | 404 |

---

## 5.4 LogoutSession

**Transport**: `POST /auth/logout` (08_API §3) — success 200

### Request Body

| Field | Type | Required |
|---|---|---|
| sessionId | string (UUID) | Required — see note |

**Note**: 04_Commands §4.4 allows identifying the Session by
`SessionId` **or** by the AccessTokenId of the calling request. In
Version 1.0, the REST Transport resolves the Session from the caller's
bearer Access Token (out of scope for this document — see §11), so
`sessionId` in the body is Optional in practice; it is Required only
when a client explicitly logs out a Session other than the one making
the call. Exactly one of {implicit current session, explicit
`sessionId`} SHALL resolve to a single Session.

### Success Response (200)

Empty body (`{}`).

### Error Responses

| Condition (04_Commands §4.4) | error.code | HTTP Status |
|---|---|---|
| Session not found | `SESSION_NOT_FOUND` | 404 |
| Session already Closed | `SESSION_ALREADY_CLOSED` | 409 |
| Session belongs to a different Person | `FORBIDDEN` | 403 |

---

## 5.5 RefreshSession

**Transport**: `POST /auth/refresh` (08_API §3) — success 200

### Request Body

| Field | Type | Required |
|---|---|---|
| refreshToken | string | Required |

### Success Response (200)

| Field | Type | Required |
|---|---|---|
| sessionId | string (UUID) | Required |
| accessToken | string | Required |
| refreshToken | string | Required |
| expiresAt | string (timestamp) | Required |

**Note**: This is a distinct, narrower response shape than the full
Session DTO (§4.5) — it omits `status` because RefreshSession does not
change or report lifecycle state (04_Commands §4.5 Postconditions:
"Session lifecycle state remains unchanged").

### Error Responses

| Condition (04_Commands §4.5) | error.code | HTTP Status |
|---|---|---|
| RefreshTokenId not found | `INVALID_REFRESH_TOKEN` | 404 |
| RefreshTokenId expired | `INVALID_REFRESH_TOKEN` | 401 |
| RefreshTokenId revoked | `INVALID_REFRESH_TOKEN` | 401 |
| Session validation failure | `UNAUTHORIZED` | 401 |

**Note on not-found vs. expired/revoked**: A refresh token that never
existed returns 404 (nothing to authenticate against). A refresh token
that existed but is no longer valid (expired or revoked) returns 401
(the caller is making an authentication claim that is rejected), not
404 — consistent with 04_Commands §4.5 treating "not found" and
"invalid" as distinct failure conditions.

---

## 5.6 ChangePassword

**Transport**: `POST /me/password` (08_API §3) — success 200

### Request Body

| Field | Type | Required |
|---|---|---|
| currentPassword | string | Required |
| newPassword | string | Required |

### Success Response (200)

Empty body (`{}`).

### Error Responses

| Condition (04_Commands §4.6) | error.code | HTTP Status |
|---|---|---|
| Person not found (system integrity — should not occur) | `PERSON_NOT_FOUND` | 404 |
| Session missing / invalid / expired | `UNAUTHORIZED` | 401 |
| Session does not belong to Person | `FORBIDDEN` | 403 |
| currentPassword does not match | `INVALID_CURRENT_PASSWORD` | 400 |
| newPassword fails strength validation | `VALIDATION_ERROR` | 400 |

**Note on `INVALID_CURRENT_PASSWORD` as 400, not 401**: The caller is
already authenticated (a valid Session is required as a precondition —
04_Commands §4.6). Supplying the wrong *current* password is a request
validation failure against a Command field, not a failure to
authenticate the Session itself — the Session's own validity is
reported separately via `UNAUTHORIZED`/`FORBIDDEN` above. This mirrors
the general rule in §7.2.

---

# 6. Query Contracts

## 6.1 GetCurrentPerson

**Transport**: `GET /me` (08_API §4.1) — success 200

### Request

No body. Identity resolved from Session context (bearer Access Token;
Transport mechanics out of scope — see §11).

### Success Response (200)

| Field | Type | Required |
|---|---|---|
| person | Person DTO (§4.1) | Required |

### Error Responses

| Condition (05_Queries §4.1) | error.code | HTTP Status |
|---|---|---|
| Session invalid or expired | `UNAUTHORIZED` | 401 |

---

## 6.2 GetOrganizationsForPerson

**Transport**: `GET /organizations` (08_API §4.1) — success 200

### Request

No body. Identity resolved from Session context.

### Success Response (200)

| Field | Type | Required |
|---|---|---|
| organizations | array of Organization DTO (§4.3) | Required (MVP: always length 1 — 05_Queries §4.2) |

### Error Responses

| Condition (05_Queries §4.2) | error.code | HTTP Status |
|---|---|---|
| Session invalid or expired | `UNAUTHORIZED` | 401 |

---

## 6.3 GetMembershipsForPerson

**Transport**: `GET /me/memberships` (08_API §4.1) — success 200

### Request

No body. Identity resolved from Session context.

### Success Response (200)

| Field | Type | Required |
|---|---|---|
| memberships | array of Membership DTO (§4.4) | Required (MVP: always length 1 — 05_Queries §4.3) |

### Error Responses

| Condition (05_Queries §4.3) | error.code | HTTP Status |
|---|---|---|
| Session invalid or expired | `UNAUTHORIZED` | 401 |

---

## 6.4 GetSessionsForPerson

**Transport**: `GET /sessions` (08_API §4.1) — success 200

### Request

No body. Identity resolved from Session context.

### Success Response (200)

| Field | Type | Required |
|---|---|---|
| sessions | array of Session DTO — list projection (§4.6) | Required |

### Error Responses

| Condition (05_Queries §4.4) | error.code | HTTP Status |
|---|---|---|
| Session invalid or expired | `UNAUTHORIZED` | 401 |

---

## 6.5 GetPersonById

**Transport**: None in Version 1.0 (08_API §4.2 / 05_Queries §5.1 —
platform-to-platform Contract, no public REST endpoint).

This Contract's wire shape is documented here for completeness, since
it remains valid Public Surface (05_Queries §3.2), even though it has
no REST Transport to bind it to in MVP. If a future version introduces
an internal/service-to-service Transport for this Contract, the shape
below is the one that Transport SHALL carry — this section pre-commits
to that shape so a future Transport addition is not also a Contract
change.

### Request (abstract — no Transport in MVP)

| Field | Type | Required |
|---|---|---|
| personId | string (UUID) | Required |

### Response (abstract — no Transport in MVP)

| Field | Type | Required |
|---|---|---|
| person | Person DTO — minimal projection (§4.2) | Required |

### Error Conditions (abstract)

| Condition (05_Queries §5.1) | error.code |
|---|---|
| PersonId does not exist | `PERSON_NOT_FOUND` |

No HTTP status is assigned, since no REST Transport exists in Version
1.0 (consistent with 08_API §4.2's rationale that this Contract's
transport, if any, is an internal/service-to-service concern).

---

# 7. Status Code Disambiguation

This section resolves the item 08_API.md §8 explicitly deferred:
*"HTTP status codes beyond the single success code per endpoint (e.g.
401 vs 403 vs 404 disambiguation per failure condition)."*

## 7.1 General Rule

| Status | Meaning in this API | Applies when |
|---|---|---|
| 400 | Bad Request | The request body itself is malformed or fails a stated validation/strength rule (including a supplied "current password" that fails to match, per §5.6) |
| 401 | Unauthorized | No valid authenticated context exists at all — missing, invalid, expired Session/token, or (for AuthenticatePerson) credentials that fail to resolve to any valid Person+Credential pair |
| 403 | Forbidden | A valid authenticated context exists, but it does not have Identity Data Ownership Scope (05_Queries §2.4) over the specific resource requested — e.g. a valid Session belonging to a *different* Person than the one being acted on |
| 404 | Not Found | The referenced resource (by ID/token) does not exist at all |
| 409 | Conflict | The request conflicts with existing state — duplicate email, already-closed Session |
| 500 | Internal Error | Core Ownership Transaction or other infrastructure failure not attributable to caller input |
| 503 | Service Unavailable | A dependency (e.g. credential validation subsystem) is temporarily unavailable |

## 7.2 The 401 vs. 403 Rule (worked example)

Both `UpdatePersonProfile` and `ChangePassword` distinguish:

- **401** — the Session itself is missing, invalid, or expired. The
  caller has not established *any* valid identity context.
- **403** — the Session is valid and belongs to a real, currently
  authenticated Person, but that Person is not the one the request is
  attempting to act on (04_Commands §4.3 "Invalid Session Ownership",
  §4.6 "Session Ownership Failure").

A wrong *current password* in ChangePassword (§5.6) is neither of
these — the Session is valid and does belong to the correct Person, so
it is a **400** (a failed field-level validation against Credential
state), not a 401/403.

## 7.3 The 404 vs. 401 Rule (worked example)

RefreshSession (§5.5) distinguishes:

- **404** — no Session/RefreshToken record exists at all for the
  supplied value.
- **401** — a record exists, but its current state (expired, revoked)
  means it can no longer authenticate a request.

This mirrors the same distinction LogoutSession (§5.4) makes between
"Session not found" (404) and "Session already Closed" (409 — the
record exists, but the requested transition is invalid given its
current state, which is a conflict rather than an authentication
failure).

---

# 8. Error Code Catalog

Consolidated list of every `error.code` value introduced across §5–§6.
No two Contracts define conflicting semantics for the same code.

| error.code | HTTP Status | Used By |
|---|---|---|
| VALIDATION_ERROR | 400 | RegisterPerson, UpdatePersonProfile, ChangePassword |
| INVALID_CURRENT_PASSWORD | 400 | ChangePassword |
| UNAUTHORIZED | 401 | UpdatePersonProfile, LogoutSession (implicit session resolution), RefreshSession, ChangePassword, GetCurrentPerson, GetOrganizationsForPerson, GetMembershipsForPerson, GetSessionsForPerson |
| INVALID_CREDENTIALS | 401 | AuthenticatePerson |
| INVALID_REFRESH_TOKEN | 401 or 404 (see §5.5) | RefreshSession |
| FORBIDDEN | 403 | UpdatePersonProfile, LogoutSession, ChangePassword |
| PERSON_NOT_FOUND | 404 | UpdatePersonProfile, ChangePassword, GetPersonById (abstract) |
| SESSION_NOT_FOUND | 404 | LogoutSession |
| PERSON_ALREADY_EXISTS | 409 | RegisterPerson |
| EMAIL_ALREADY_IN_USE | 409 | UpdatePersonProfile |
| SESSION_ALREADY_CLOSED | 409 | LogoutSession |
| REGISTRATION_FAILED | 500 | RegisterPerson |
| SERVICE_UNAVAILABLE | 503 | AuthenticatePerson |

---

# 9. Contract Coverage Verification

| Contract | Type | Request Schema | Response Schema | Error Codes |
|---|---|---|---|---|
| RegisterPerson | Command | ✅ §5.1 | ✅ §5.1 | ✅ §5.1 |
| AuthenticatePerson | Command | ✅ §5.2 | ✅ §5.2 | ✅ §5.2 |
| UpdatePersonProfile | Command | ✅ §5.3 | ✅ §5.3 | ✅ §5.3 |
| LogoutSession | Command | ✅ §5.4 | ✅ §5.4 | ✅ §5.4 |
| RefreshSession | Command | ✅ §5.5 | ✅ §5.5 | ✅ §5.5 |
| ChangePassword | Command | ✅ §5.6 | ✅ §5.6 | ✅ §5.6 |
| GetCurrentPerson | Query | N/A (no body) | ✅ §6.1 | ✅ §6.1 |
| GetOrganizationsForPerson | Query | N/A (no body) | ✅ §6.2 | ✅ §6.2 |
| GetMembershipsForPerson | Query | N/A (no body) | ✅ §6.3 | ✅ §6.3 |
| GetSessionsForPerson | Query | N/A (no body) | ✅ §6.4 | ✅ §6.4 |
| GetPersonById | Query | ✅ §6.5 (abstract — no Transport) | ✅ §6.5 (abstract — no Transport) | ✅ §6.5 (abstract) |

**Result**: 11 of 11 Contracts have an explicit schema disposition.
No orphaned Contract remains.

---

# 10. MVP Constraints Verification

- ✅ Every field in every schema traces to a 01_Domain_Model.md
  attribute or an explicit 04_Commands.md Input field
- ✅ No schema exposes Credential (PasswordHash, PasswordVersion) —
  Credential is never part of any Response DTO, consistent with
  01_Domain_Model §2 ("Credential never stores plain text passwords")
  and the general principle that Credential is a mechanism, not
  Identity (00_Overview §4)
- ✅ Enum values for status/role/category match exactly the lifecycle
  and role values declared reachable in MVP (14_MVP §3)
- ✅ GetPersonById's minimal projection (§4.2, §6.5) matches
  05_Queries §5.1 exactly (personId, displayName only)
- ✅ GetSessionsForPerson's list projection (§4.6) excludes tokens,
  matching 05_Queries §4.4 exactly
- ✅ No new Aggregates, Commands, Queries, Events, or REST endpoints
  introduced

---

# 11. Deferred / Out of Scope

The following remain explicitly out of scope for this document:

- Authentication transport mechanics: bearer token header name
  (`Authorization: Bearer <token>` vs. custom header), cookie-based
  session strategy, CSRF handling
- API versioning strategy (URL path versioning, header versioning)
- Pagination, filtering, or sorting conventions for
  `/organizations`, `/sessions`, `/me/memberships` (all three return
  unpaginated arrays in MVP, which is consistent with 14_MVP — every
  MVP Person has exactly one Organization and one Membership, and
  Session counts are expected to be small)
- Rate limiting and throttling policy
- Idempotency-key conventions for POST Commands
- Internal/service-to-service Transport shape for GetPersonById beyond
  the abstract Contract shape given in §6.5

These remain candidates for a future Transport-mechanics document or
ADR and do not block Version 1.0 REST Contract readiness.

---

# 12. Validation Against Standards

**Against 04_Commands.md**: ✅ Every Request field maps to a stated
Input field (§4.1–§4.6 of 04_Commands); every error.code maps to a
stated Failure Condition; no new Command behavior introduced.

**Against 05_Queries.md**: ✅ Every Response field maps to a stated
Output field (§4, §5 of 05_Queries); GetPersonById's abstract shape
matches §5.1's minimal projection exactly.

**Against 08_API.md**: ✅ Every schema in this document is anchored to
the HTTP method + path + success status already fixed in 08_API §3–§4;
this document adds no new endpoint and changes no existing mapping.

**Against 059_SmartCore_Identity_Platform.md**: ✅ No schema field
exposes data beyond what §9 (Authorization Boundary) and §12 (Data
Model) permit; Credential fields remain internal-only.

---

# 13. MVP Readiness Checklist

Contracts Blueprint Version 1.0 is complete when:

✓ All 6 Commands have a Request Schema, Response Schema, and Error
  Code mapping

✓ All 5 Queries have a Response Schema and Error Code mapping (Request
  Schema N/A where no body is transported)

✓ A common Error Response envelope is defined and used uniformly

✓ Every failure condition in 04_Commands.md / 05_Queries.md maps to
  exactly one HTTP status code, with worked-example rationale for every
  401/403/404/409 distinction

✓ A shared DTO catalog eliminates duplicate schema definitions across
  Contracts

✓ No new Aggregates, Commands, Queries, Events, or REST endpoints
  introduced

✓ Alignment verified against 04_Commands.md, 05_Queries.md, 08_API.md,
  and 059_SmartCore_Identity_Platform.md

---

# 14. Change Log

## Version 1.0.1 (2026-07-14)

Review-driven clarification pass. No schema, mapping, or Contract
content changed.

- §3.1: expanded the AccessTokenId → accessToken naming rationale to
  clarify that the wire field name reflects client-facing usage (the
  bearer credential itself), not a claim about internal token
  representation, resolving an apparent tension with
  01_Domain_Model.md §3's implementation-agnostic Value Object
  definition.
- §2.3: narrowed the MVP field-introduction constraint to explicitly
  scope it to business data fields, and clarified that transport-level
  envelope metadata (traceId, pagination, links, etc.) is not
  restricted by this section, preventing the rule from over-reaching
  into ordinary future API evolution.

## Version 1.0.0 (2026-07-14)

Initial Contracts Blueprint.

- Established the PascalCase (domain) → camelCase (wire) naming
  convention and a type/format table (§3)
- Defined a common Error Response envelope (§3.4) and a consolidated
  Error Code Catalog (§8)
- Defined a shared DTO catalog: Person (full + minimal projections),
  Organization, Membership, Session (full + list projections) (§4)
- Specified Request/Response schemas for all 6 Commands (§5) and all
  5 Queries (§6), including GetPersonById's abstract (no-Transport)
  shape
- Resolved the 08_API.md §8 deferred item: full 401 vs. 403 vs. 404
  vs. 409 disambiguation, with worked examples (§7)
- Verified Contract coverage (11/11) and MVP constraint compliance
  (§9, §10)
- Documented remaining out-of-scope Transport-mechanics items for
  future work, without blocking Version 1.0 readiness (§11)

---

**END OF DOCUMENT**