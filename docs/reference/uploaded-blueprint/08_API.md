<!--
Document ID: ID-08
Title: SmartCore Identity Platform Blueprint - API (REST Transport Mapping)
Version: 1.0.3
Status: READY_FOR_GENERATION

Purpose:
Define the REST Transport mapping for the Identity Platform's Public
Command and Query Contracts. This document maps each Contract to a
concrete HTTP method + path. It does NOT define request/response
schemas, DTOs, error response bodies, versioning strategy, or transport
security. Those are deferred to 07_Contracts.md.

Dependencies:
- 00_Overview.md
- 01_Domain_Model.md
- 04_Commands.md
- 05_Queries.md
- 14_MVP.md
- 059_SmartCore_Identity_Platform.md
- ADR-0002_Identity_Foundation_Clarifications.md

Change Log:
  - Version 1.0.3 (2026-07-14): Final editorial polish pass, no scope
    or content changed. (1) §3 note on UpdatePersonProfile reworded
    from "is newly introduced by this document" to "was introduced
    during the initial API coverage review and is now part of the
    Version 1.0 REST Surface" — the present-tense "is newly
    introduced" read as stale once the correction had already been
    merged into 14_MVP.md. (2) §7.1 reworded from "now have a REST
    Transport" to "have a REST Transport in Version 1.0", removing a
    transient "now" reference for audit-friendliness, consistent with
    the same fix already applied to §7.2 in v1.0.2.
  - Version 1.0.2 (2026-07-14): Editorial cleanup pass, no scope or
    content changes. (1) §7.2 heading updated to reference 14_MVP.md
    v1.2.0 instead of v1.1.1, since that is the version in which the
    `PATCH /me` correction actually landed. (2) §7.2 wording changed
    from "Gap found" to "Previous gap identified and corrected" to
    reflect that the gap is now resolved rather than newly discovered
    — the "❌ Gap found" phrasing was leftover from v1.0.0 and read as
    contradicting v1.0.1's "aligned" framing. (3) Removed "— newly
    corrected" from the Contract Coverage Verification table (§6) for
    UpdatePersonProfile, since the correction is no longer new as of
    this version. No mapping, Contract, or scope content changed.
  - Version 1.0.1 (2026-07-14): Follow-up correction after 14_MVP.md
    v1.2.0 merged the `PATCH /me` addition this document identified as
    a gap in v1.0.0. §5 and §9 previously described that gap as still
    pending (referencing 14_MVP.md v1.1.1 and using "pending companion
    correction" wording). Now that the correction has landed in
    14_MVP.md, both sections are updated to state the endpoint lists
    are aligned, rather than describing a fix that has already been
    applied as still outstanding. No mapping, Contract, or scope
    content changed.
  - Version 1.0.0 (2026-07-14): Initial API Transport Mapping document.
    Formalizes Command/Query → REST mapping separately from Contract
    definition (04_Commands, 05_Queries). Identifies and corrects a gap:
    UpdatePersonProfile (04_Commands §4.3) had no REST Transport in any
    prior document (059 §11, 14_MVP §7). Adds `PATCH /me` as its
    Transport. See §7 (Alignment Notes) and companion update to
    14_MVP.md §7.
-->

# 1. Overview

This document defines the REST Transport layer for the Identity
Platform's Public Surface.

Per the Contract / Transport separation established in 05_Queries.md
§2.1, a **Contract** (Command or Query) is a business-level operation
defined in 04_Commands.md or 05_Queries.md. A **Transport** is the
mechanism through which a Contract is invoked — in this document,
specifically REST (HTTP method + path).

This document defines ONLY the Transport mapping. It explicitly does
NOT define:

- Request or response body schemas
- DTOs or serialization formats
- Full error response models (status codes only, no error body shape)
- API versioning strategy
- Authentication/authorization transport mechanics (bearer tokens,
  header names, etc.)
- Rate limiting, pagination, or other cross-cutting transport concerns

All of the above are deferred to **07_Contracts.md**.

This document introduces no new:

- Aggregates
- Commands
- Queries
- Events
- Business Rules

Every REST endpoint in this document corresponds to exactly one Command
Contract (04_Commands.md) or Query Contract (05_Queries.md). No
endpoint is introduced without a corresponding Contract.

---

# 2. Scope Boundary

## 2.1 What This Document Defines

- HTTP method for each exposed Contract
- HTTP path for each exposed Contract
- Which Contracts have no REST Transport in Version 1.0, and why

## 2.2 What This Document Does NOT Define

- Schema (deferred to 07_Contracts.md)
- DTO shape (deferred to 07_Contracts.md)
- Error body format (deferred to 07_Contracts.md)
- Status code catalog beyond the conventional success code per endpoint
  (deferred to 07_Contracts.md)

## 2.3 MVP Boundary Protection

No endpoint SHALL be introduced for a Command or Query outside the six
Commands (04_Commands §3) and five Queries (05_Queries §3) already
defined. This document classifies and maps; it does not expand Public
Surface.

---

# 3. Command → REST Mapping

| Command | HTTP Method | Path | Success Status |
|---|---|---|---|
| RegisterPerson | POST | `/auth/register` | 201 |
| AuthenticatePerson | POST | `/auth/login` | 200 |
| UpdatePersonProfile | PATCH | `/me` | 200 |
| LogoutSession | POST | `/auth/logout` | 200 |
| RefreshSession | POST | `/auth/refresh` | 200 |
| ChangePassword | POST | `/me/password` | 200 |

All six MVP Commands (04_Commands §3) have a REST Transport in Version
1.0. No Command is without a Transport.

**Note on UpdatePersonProfile**: This mapping (`PATCH /me`) was
introduced during the initial API coverage review and is now part of
the Version 1.0 REST Surface. See §7.2 for the historical gap
analysis.

---

# 4. Query → REST Mapping

## 4.1 User Interaction Queries (05_Queries §3.1)

| Query | HTTP Method | Path | Success Status |
|---|---|---|---|
| GetCurrentPerson | GET | `/me` | 200 |
| GetOrganizationsForPerson | GET | `/organizations` | 200 |
| GetMembershipsForPerson | GET | `/me/memberships` | 200 |
| GetSessionsForPerson | GET | `/sessions` | 200 |

All four User Interaction Queries have a REST Transport in Version 1.0.

## 4.2 Platform Integration Queries (05_Queries §3.2)

| Query | HTTP Method | Path |
|---|---|---|
| GetPersonById | — | No public REST Transport in Version 1.0 |

Per 05_Queries §5.1, GetPersonById remains valid Public Surface without
a public REST endpoint in Version 1.0. It is a platform-to-platform
Contract; its transport (if any) is an internal/service-to-service
concern outside public REST API scope, consistent with 05_Queries §2.1
and §5.1.

---

# 5. Consolidated Endpoint List

The following is the complete set of Version 1.0 public REST endpoints,
combining §3 and §4.1:

```
POST   /auth/register          - RegisterPerson
POST   /auth/login             - AuthenticatePerson
POST   /auth/logout            - LogoutSession
POST   /auth/refresh           - RefreshSession
GET    /me                     - GetCurrentPerson
PATCH  /me                     - UpdatePersonProfile
POST   /me/password            - ChangePassword
GET    /organizations          - GetOrganizationsForPerson
GET    /me/memberships         - GetMembershipsForPerson
GET    /sessions               - GetSessionsForPerson
```

This list is aligned with 14_MVP.md §7 (v1.2.0) after the `PATCH /me`
correction described in §7.2 below.

No corresponding entry exists in this list for GetPersonById, by design
(§4.2).

---

# 6. Contract Coverage Verification

This section verifies that every Contract in 04_Commands.md and
05_Queries.md has an explicit Transport disposition (mapped to REST, or
explicitly excluded with rationale).

| Contract | Type | REST Transport? |
|---|---|---|
| RegisterPerson | Command | ✅ Mapped (§3) |
| AuthenticatePerson | Command | ✅ Mapped (§3) |
| UpdatePersonProfile | Command | ✅ Mapped (§3) |
| LogoutSession | Command | ✅ Mapped (§3) |
| RefreshSession | Command | ✅ Mapped (§3) |
| ChangePassword | Command | ✅ Mapped (§3) |
| GetCurrentPerson | Query | ✅ Mapped (§4.1) |
| GetOrganizationsForPerson | Query | ✅ Mapped (§4.1) |
| GetMembershipsForPerson | Query | ✅ Mapped (§4.1) |
| GetSessionsForPerson | Query | ✅ Mapped (§4.1) |
| GetPersonById | Query | ✅ Explicitly excluded, with rationale (§4.2) |

**Result**: 11 of 11 Contracts have an explicit Transport disposition.
No orphaned Contract remains.

---

# 7. Alignment Notes

This section is informative, not a substitute for the formal Blueprint
Validator (065), consistent with the precedent set by 05_Queries §6.

## 7.1 Against 04_Commands.md

- ✅ All 6 Commands (§3) have a REST Transport in Version 1.0
- ✅ No new Command introduced
- ⚠️ **Prior gap identified and corrected**: 04_Commands.md itself does
  not define Transport (by design — see 04_Commands §1, "Transport
  concerns... are intentionally excluded"), so this gap was not visible
  within 04_Commands.md. It only became visible once Commands were
  cross-checked against the actual published endpoint lists (059 §11,
  14_MVP §7), where UpdatePersonProfile had no matching entry.

## 7.2 Against 14_MVP.md §7 (v1.2.0) and 059 §11

- ✅ **Previous gap identified and corrected**: `UpdatePersonProfile`
  (04_Commands §4.3) previously had no corresponding endpoint in
  14_MVP.md §7 or 059 §11. `POST /me/password` exists for
  `ChangePassword`, but no endpoint existed for profile updates (Email,
  DisplayName).
- This was a genuine omission, not an intentional deferral: unlike
  `GetPersonById` (which is explicitly and intentionally deferred with
  documented rationale in 05_Queries §5.1), no document anywhere stated
  that `UpdatePersonProfile` was deliberately excluded from Version 1.0
  REST exposure. 14_MVP §1 explicitly lists "Update Person Profile" as
  in-scope MVP functionality, and 02_Use_Cases UC-005 describes it as a
  user-initiated, session-authenticated flow — both consistent with it
  requiring a public REST endpoint that was simply never added.
- **Correction**: `PATCH /me` was introduced by this document (§3, §5)
  and has since been merged into 14_MVP.md §7 (v1.2.0), keeping that
  document's endpoint list authoritative and complete.
- 059 §11 is explicitly scoped as a "minimum endpoints" list (059 §11:
  "Future APIs may extend this list without breaking existing
  contracts"), so its omission of `PATCH /me` is not itself a defect in
  059 — 059 was never intended to be the exhaustive list. 14_MVP §7 is
  the document that is meant to be exhaustive for Version 1.0, which is
  why the correction targeted 14_MVP.md and not 059.

## 7.3 Against 05_Queries.md

- ✅ All 4 User Interaction Queries mapped
- ✅ GetPersonById correctly has no REST mapping, consistent with
  05_Queries §5.1's stated rationale
- ✅ No Query overlaps with any Command's REST mapping

## 7.4 Against 059_SmartCore_Identity_Platform.md §11

- ✅ Consistent: 059 §11 is a documented minimum; this document's list
  is a superset that includes `PATCH /me`, which 059 does not prohibit
  (059 §11: "Future APIs may extend this list without breaking existing
  contracts")

---

# 8. Deferred to 07_Contracts.md

The following remain explicitly out of scope for this document and are
deferred:

- Request/response JSON schemas for all 10 endpoints
- DTO field-level definitions (naming, types, optionality)
- Error response body shape and error code catalog
- HTTP status codes beyond the single success code per endpoint (e.g.
  401 vs 403 vs 404 disambiguation per failure condition)
- API versioning strategy (URL versioning, header versioning, etc.)
- Authentication transport mechanics (bearer token header format, token
  placement)
- Pagination, filtering, or sorting conventions for list-returning
  endpoints (`/organizations`, `/sessions`, `/me/memberships`)
- Rate limiting and throttling policy

---

# 9. MVP Readiness Checklist

API Transport Mapping is complete when:

✓ All 6 MVP Commands have a REST Transport or documented exclusion

✓ All 5 MVP Queries have a REST Transport or documented exclusion

✓ Endpoint list is consistent with 14_MVP.md §7

✓ No new Commands, Queries, Aggregates, or Events introduced

✓ Schema, DTO, and error-model concerns are explicitly deferred to
  07_Contracts.md, not defined here

---

# 10. Change Log

## Version 1.0.3 (2026-07-14)

Final editorial polish pass. No scope, mapping, or Contract content
changed.

- §3: reworded the UpdatePersonProfile note from "is newly introduced
  by this document" to "was introduced during the initial API coverage
  review and is now part of the Version 1.0 REST Surface", removing
  present-tense phrasing that read as stale post-merge.
- §7.1: reworded "now have a REST Transport" to "have a REST Transport
  in Version 1.0", removing a transient "now" reference, mirroring the
  same fix already made to §7.2 in v1.0.2.

## Version 1.0.2 (2026-07-14)

Editorial cleanup pass. No scope, mapping, or Contract content changed.

- §7.2 heading updated to reference 14_MVP.md v1.2.0 (the version in
  which the `PATCH /me` correction actually landed), replacing the
  v1.1.1 reference that pointed to the pre-correction state.
- §7.2 wording changed from "❌ Gap found" to "✅ Previous gap
  identified and corrected", since the gap is resolved as of v1.0.1
  and the negative framing was stale.
- §6 Contract Coverage Verification: removed "— newly corrected" from
  the UpdatePersonProfile row, since the correction is no longer new.

## Version 1.0.1 (2026-07-14)

Follow-up correction after 14_MVP.md v1.2.0 merged the `PATCH /me`
addition this document identified as a gap in v1.0.0. §5 and §9
previously described that gap as still pending (referencing 14_MVP.md
v1.1.1 and using "pending companion correction" wording). Now that the
correction has landed in 14_MVP.md, both sections are updated to state
the endpoint lists are aligned, rather than describing a fix that has
already been applied as still outstanding. No mapping, Contract, or
scope content changed.

## Version 1.0.0 (2026-07-14)

Initial API Transport Mapping document.

- Established Contract/Transport separation for Commands (extending the
  pattern 05_Queries.md §2.1 already established for Queries)
- Mapped all 6 Commands and 5 Queries to REST Transport or documented
  exclusion (§3, §4)
- Identified and corrected a genuine gap: `UpdatePersonProfile` had no
  REST Transport in any existing document; added `PATCH /me` (§3, §5,
  §7.2)
- Flagged required companion correction to 14_MVP.md §7
- Explicitly deferred schema, DTO, and error-model definition to
  07_Contracts.md

---

**END OF DOCUMENT**