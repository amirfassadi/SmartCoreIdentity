<!--
Document ID: ID-10
Title: SmartCore Identity Platform Blueprint - Configuration
Version: 1.0.2
Status: READY_FOR_GENERATION

Purpose:
Defines externally configurable behavior of the Identity Platform:
Feature Flags, Default Values, Environment Settings, Policy References,
Timeout Values, and Retry Policies, per 064_SmartCore_Blueprint_Standard
§8.11.

Dependencies:
00_Overview.md
01_Domain_Model.md
03_Aggregates.md
04_Commands.md
14_MVP.md
057_SmartCore_Tenancy_and_Ownership_Model.md
059_SmartCore_Identity_Platform.md
062_SmartCore_Core_Engine_Boundaries.md
064_SmartCore_Blueprint_Standard.md

Change Log:
  - Version 1.0.2 (2026-07-15): Editorial precision correction. §2.2.1
    now identifies the specific Argon2id variant/specification
    (RFC 9106) rather than the bare identifier "argon2id", since
    Argon2 defines multiple variants (Argon2i, Argon2d, Argon2id) and
    implementations of argon2id itself may otherwise diverge in
    unstated ways. This removes a remaining source of ambiguity for
    deterministic AI Code Generation (064 §9.4). No allow-list
    membership, cost parameter, Domain invariant, or MVP scope changed
    by this revision.
  - Version 1.0.1 (2026-07-15): Review corrections. (1) §2.1 Refresh
    Token Rotation strengthened from a permissive restatement of
    04_Commands.md §4.5 to an explicit SHALL-in-production
    configuration default, since a leaked Refresh Token makes rotation
    a security control, not an optional implementation detail. (2)
    §2.2 Password Hashing Algorithm reclassified from "Yes
    (restricted)" to an explicit closed allow-list ("argon2id only in
    MVP; additional algorithms require a Security ADR") to remove the
    possibility of reading "restricted" as "any algorithm, with extra
    steps." (3) §2.2 added Argon2id cost parameters (memory_cost,
    time_cost, parallelism) as separately configurable per environment,
    since the algorithm identifier alone was insufficient for
    deterministic AI Code Generation (064 §9.4). (4) §2.1 Session Idle
    Timeout note extended to state that a future idle-timeout
    capability would not require modifying the Session Aggregate, for
    Extensibility traceability. (5) §5 Policy Engine table updated to
    match the closed allow-list framing in (2). No MVP scope, Command,
    Event, or Domain invariant changed by this revision.
  - Version 1.0.0 (2026-07-15): Initial Configuration Blueprint.
-->

# 1. Purpose and Scope

This document defines the externally configurable behavior of the
Identity Platform.

Configuration values MAY be changed without modifying application
source code.

Per 064 §8.11:

> Configuration SHALL NOT redefine domain rules.

Accordingly, this document defines *values*, not *rules*. The
existence of a rule (e.g. "Password SHALL satisfy credential strength
requirements", 04_Commands.md §4.1, §4.6) is fixed by the Domain Model
and Commands Blueprint. The concrete threshold or parameter used to
satisfy that rule is configuration, and is defined here.

Where a value below is evaluated by policy rather than hard-coded
business logic, ownership follows 062_SmartCore_Core_Engine_Boundaries.md
§6-§7: configurable behavior is evaluated by the Policy Engine;
intrinsic invariants remain in Domain Services. Section 5 records this
mapping explicitly.

---

# 2. Default Values

## 2.1 Session and Token Defaults

| Parameter | Default Value | Configurable | Notes |
|---|---|---|---|
| Access Token TTL | 1 hour | Yes | Applies to `AccessTokenId` issued by `AuthenticationDomainService` (01_Domain_Model.md §7) and reissued by `RefreshSession` (04_Commands.md §4.5) |
| Refresh Token TTL | 7 days | Yes | Applies to `RefreshTokenId` (01_Domain_Model.md §3) |
| Refresh Token Rotation | Enabled | Restricted — see note below | On successful `RefreshSession`, 04_Commands.md §4.5 Postconditions permits either preserving or refreshing the RefreshTokenId ("RefreshTokenId is preserved or refreshed according to implementation"); this document exercises that implementation freedom as follows |
| Session Idle Timeout | Not enforced in MVP | N/A | No idle-timeout Command exists in MVP scope (14_MVP.md §1-§2) |

**Refresh Token Rotation Policy**: Rotation SHALL be enabled in
Production environments — a new RefreshTokenId SHALL be issued on
every successful `RefreshSession`, and the prior RefreshTokenId SHALL
be invalidated. This is a security control against Refresh Token
leakage, not merely an implementation preference, and is therefore not
left to per-deployment discretion in Production. Rotation MAY be
disabled only in non-Production environments (e.g. local development)
for debugging convenience.

**Session Idle Timeout — Future Note**: Idle-timeout enforcement is
Excluded from MVP (14_MVP.md §1-§2). Should a future version introduce
it, it MAY be implemented purely as an additional configurable
Timeout Value evaluated by `SessionManagementDomainService` against
the existing `ExpiresAt`/`Status` attributes (01_Domain_Model.md §2);
it would not require a new attribute on the Session Aggregate or a
change to the Session lifecycle states already defined in
01_Domain_Model.md §5. This note is informative only and introduces no
MVP obligation, per 064 §8.17 (Examples/notes SHALL NOT introduce
additional requirements).

**MVP Constraint**: These values govern token lifetime only. They do
not introduce new Session lifecycle states beyond those defined in
01_Domain_Model.md §5 (Created → Authenticated → Active → Suspended
(optional) → Expired → Closed).

---

## 2.2 Credential Defaults

| Parameter | Default Value | Configurable | Notes |
|---|---|---|---|
| Password Hashing Algorithm | argon2id (RFC 9106) | Closed allow-list — see 2.2.1 | Used by `CredentialManagementDomainService` (01_Domain_Model.md §7) and `AuthenticationDomainService` credential validation (04_Commands.md §5.2) |
| Minimum Password Length | 10 characters | Yes | Enforced during `RegisterPerson` and `ChangePassword` validation (04_Commands.md §4.1, §4.6) |
| Password Complexity | At least 1 letter and 1 digit | Yes | Same enforcement points as above |
| Password History Check | Not applicable | N/A | 01_Domain_Model.md §2 explicitly excludes Credential History from MVP |

### 2.2.1 Algorithm Change Note

**Allowed Algorithms (MVP)**: `argon2id`, as specified by RFC 9106
(the Argon2 variant combining Argon2i and Argon2d addressing modes),
is the only algorithm permitted in Version 1.0. The bare identifier
`argon2id` alone is insufficient for deterministic implementation,
since Argon2 defines multiple variants (Argon2i, Argon2d, Argon2id)
and implementations of argon2id may otherwise diverge in unstated
ways; RFC 9106 conformance removes that ambiguity. "Configurable" for
this parameter does NOT
mean any hashing algorithm may be substituted by environment
configuration — it means the cost parameters of `argon2id` (below) MAY
be tuned. Introducing a second allowed algorithm (e.g. bcrypt as a
fallback, or a future argon2id revision with a different identifier)
is a security-relevant change and SHALL require a Security ADR, not a
configuration change alone.

#### Argon2id Cost Parameters

The following parameters govern the `argon2id` computation and MAY be
tuned per environment without a Security ADR, since they adjust
hashing cost rather than the algorithm itself:

| Parameter | Default (Production) | Default (Development) | Notes |
|---|---|---|---|
| `memory_cost` | 256 MB | 64 MB | Lower Development value is for local iteration speed only; SHALL NOT be used in Production |
| `time_cost` (iterations) | 3 | 2 | |
| `parallelism` | 4 | 2 | |

Changing these values affects only newly created or newly replaced
Credentials (registration, `ChangePassword`), consistent with the
immutability behavior described below. Existing stored `PasswordHash`
values retain the cost parameters used at creation time; they are not
recomputed retroactively.

**Immutability Consequence**: Should a future Security ADR introduce a
new allowed algorithm, that change would apply only to newly created
Credentials (registration, `ChangePassword`) — never retroactively.
Because `PasswordHash` (01_Domain_Model.md §3) is immutable per stored
Credential, existing PasswordHash values SHALL NOT be silently
reinterpreted or rewritten under a different algorithm. This
constraint is a data-integrity consequence of the Domain Model, not a
new domain rule introduced by this document, and applies equally to
cost-parameter changes under the current `argon2id` allow-list entry.

---

## 2.3 Organization and Membership Defaults

| Parameter | Default Value | Configurable | Notes |
|---|---|---|---|
| Personal Organization Category | `Personal` | No | Fixed per 01_Domain_Model.md §2 ("Category MVP: Personal") |
| Initial Membership Role | `Owner` | No | Fixed per 01_Domain_Model.md §2 ("MVP Role: Owner") |
| Initial Organization/Membership Status | `Active` | No | Fixed per 14_MVP.md §3 |

These three values are listed for completeness but are **not**
configurable — they are frozen by the Domain Model and Tenancy and
Ownership Model (057 §8). Listing them here makes explicit that no
environment or tenant MAY override them in MVP, consistent with 064
§6.5 (Explicit Scope).

---

# 3. Feature Flags

Feature flags in MVP exist only to represent capabilities that are
architecturally present but intentionally inactive, per 14_MVP.md §2
and §4. No MVP behavior is itself gated behind a flag — the six Public
Commands (04_Commands.md §3) are always active.

| Flag | Default | Purpose |
|---|---|---|
| `feature.mfa.enabled` | `false` | Reserved for future MFA Credential type (00_Overview.md §11; 059 §17) |
| `feature.oauth.enabled` | `false` | Reserved for future OAuth Credential (059 §4, §17) |
| `feature.enterprise_sso.enabled` | `false` | Reserved for future Enterprise SSO (00_Overview.md §11) |
| `feature.organization_switching.enabled` | `false` | Reserved; Membership remains single-path per Person in MVP (00_Overview.md §11) |
| `feature.session_suspension.enabled` | `false` | Governs whether the optional `Suspended` Session state (01_Domain_Model.md §5) is reachable. Disabled by default since no MVP Command transitions to it |

Flags SHALL default to `false` for every capability marked Excluded in
14_MVP.md §2 or 00_Overview.md §11. Enabling any flag above without a
corresponding Command, Use Case, and Event definition is a Blueprint
violation and SHALL fail Architectural Validation (064 §9.3).

---

# 4. Timeout and Retry Policy

## 4.1 Timeout Values

| Operation | Timeout | Notes |
|---|---|---|
| Credential validation (password hash comparison) | 2 seconds | Applies within `AuthenticatePerson` (04_Commands.md §4.2) |
| Core Ownership Transaction commit | 5 seconds | Applies to the atomic Person + Organization + Membership transaction (04_Commands.md §4.1, §9.2) |
| Post-Commit operation (Credential/Session creation) | 5 seconds per operation | Failure after timeout follows the Post-Commit Failure path (04_Commands.md §4.1 Failure Conditions); ownership remains valid |

## 4.2 Retry Policy

Retry applies only to Post-Commit Operations (Credential creation,
Initial Session creation), consistent with 00_Overview.md §6 and
04_Commands.md §9.3, which classify recovery of these operations as
implementation-specific.

| Parameter | Default | Notes |
|---|---|---|
| Post-Commit retry attempts | 3 | Exponential backoff, base 500ms |
| Core Ownership Transaction retry | 0 (no retry) | A failed Core Ownership Transaction is rolled back and returned as a Command failure, not silently retried, per 04_Commands.md §9.2 ("Partial ownership state SHALL never exist") |

**Boundary Note**: Retry orchestration for Post-Commit operations is
an Application Service concern (`RegistrationApplicationService`,
01_Domain_Model.md §8), not a Core Engine concern. Scheduler-driven
retry, if introduced operationally, would be invoked BY the
Application Service per 062 §5 ("Application Services MAY invoke one
or more Core Engines... Domain Services SHALL NOT invoke Core Engines
directly").

---

# 5. Policy Engine References

Per 062_SmartCore_Core_Engine_Boundaries.md §6, a rule belongs to the
Policy Engine only if it SHOULD be changeable through configuration
without modifying source code. The table below classifies each
Identity Platform variable rule against that test.

| Rule | Owner | Rationale |
|---|---|---|
| Password strength thresholds (§2.2) | Policy Engine | Configurable without code change; does not alter the invariant itself ("Password SHALL NOT be stored in plain text" remains a Domain invariant, 01_Domain_Model.md §6) |
| Access/Refresh Token TTLs (§2.1) | Policy Engine | Configurable value; Session lifecycle states themselves remain Domain Model-owned (01_Domain_Model.md §5) |
| Feature Flags (§3) | Policy Engine | Configuration toggles; do not introduce new Aggregates, Commands, or Events (064 §9.4) |
| Argon2id cost parameters (`memory_cost`, `time_cost`, `parallelism`; §2.2.1) | Policy Engine | Tunable per environment; does not change the algorithm or bypass the immutability behavior in §2.2.1 |
| Password hashing algorithm selection (§2.2) | Domain / Security — NOT configuration | Closed allow-list of one (`argon2id`) in MVP; adding an algorithm is a security decision requiring a Security ADR, not a configuration change |
| Refresh Token Rotation in Production (§2.1) | Domain / Security — NOT configuration in Production | SHALL be enabled in Production as a leak-mitigation control; only disable-able in non-Production environments |
| Membership Role = `Owner` only (§2.3) | Domain Model — NOT configurable | Fixed invariant (Invariant-006, 04_Commands.md §10); Policy Engine SHALL NOT override |
| Registration atomicity (Core Ownership Transaction) | Domain Model / Application Service — NOT configurable | Rule-002 (01_Domain_Model.md §9); no environment MAY disable atomicity |

This table exists so that no future environment configuration can be
used to silently bypass a Domain invariant. Anything not listed in
this table as Policy-Engine-owned SHALL be treated as fixed Domain
behavior.

---

# 6. Environment Settings

Environment Settings distinguish deployment context from business
configuration. They SHALL NOT influence Domain behavior or MVP scope.

| Setting | Example Values | Notes |
|---|---|---|
| `IDENTITY_DB_CONNECTION` | environment-specific | Persistence connection string; detailed persistence configuration is deferred to 09_Persistence.md |
| `IDENTITY_EVENT_ENGINE_ENDPOINT` | environment-specific | Per 00_Overview.md §9, Identity depends on the Event Engine; endpoint is environment configuration only |
| `IDENTITY_TOKEN_SIGNING_KEY` | environment-specific, secret | Key material; management detail deferred to 11_Security.md |
| `IDENTITY_LOG_LEVEL` | `info` (default) | Operational only; no domain meaning |

Secret material (signing keys, DB credentials) SHALL be sourced from
environment configuration or a secrets manager, never hard-coded or
stored alongside this document. Detailed key-management requirements
are the responsibility of 11_Security.md; this document only
identifies that such settings exist and are environment-scoped, not
tenant- or policy-scoped.

---

# 7. Configuration Ownership and Change Control

- Default Values (§2) and Feature Flags (§3) MAY be changed by
  Governance-approved configuration change, without a Blueprint
  version change, provided no Domain invariant is affected (§5).
- Any change that would alter a value classified as "NOT configurable"
  in §2.3 or §5 SHALL require a Blueprint amendment and, where it
  affects MVP scope, an update to 14_MVP.md.
- Timeout and Retry values (§4) are operational tuning parameters and
  MAY be adjusted per environment without Blueprint impact.

---

# 8. MVP Constraints Summary

- No Feature Flag in §3 SHALL default to `true` while its associated
  capability remains Excluded per 14_MVP.md §2.
- Configuration SHALL NOT introduce Commands, Aggregates, or Events
  (064 §9.4 AI Readiness Validation).
- Configuration SHALL NOT alter the Core Ownership Transaction's
  atomicity guarantee (04_Commands.md §9.2).
- Configuration SHALL NOT alter the fixed MVP Role (`Owner`) or
  Organization Category (`Personal`) values (§2.3).

---

# 9. Validation Alignment

This document supports 065_SmartCore_Blueprint_Validator_Specification.md
Structural and Architectural Validation by:

- Providing explicit default values for every configurable parameter
  referenced elsewhere in the Blueprint Package as "configurable" or
  "implementation-specific" (e.g. 04_Commands.md §4.1 Post-Commit
  recovery, §9.3).
- Explicitly classifying which rules belong to the Policy Engine
  versus the Domain Model (§5), preventing ambiguity flagged by 064
  §9.4 ("No ambiguous terminology").

---

**END OF DOCUMENT**
