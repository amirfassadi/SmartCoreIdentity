<!--
Document ID: ID-11
Title: SmartCore Identity Platform Blueprint - Security
Version: 1.1.1
Status: READY_FOR_GENERATION

Purpose:
Defines capability-specific security requirements for the Identity
Platform, including Authentication, Authorization boundary, Secrets,
Encryption, Key Management, Privacy Requirements, Sensitive Data
Handling, and Threat Considerations, per 064_SmartCore_Blueprint_Standard
§8.12.

Dependencies:
00_Overview.md
01_Domain_Model.md
03_Aggregates.md
057_SmartCore_Tenancy_and_Ownership_Model
059_SmartCore_Identity_Platform
062_SmartCore_Core_Engine_Boundaries
064_SmartCore_Blueprint_Standard
ADR-0002_Identity_Foundation_Clarifications

Change Log:
See Section 15 for detailed version history.
-->

# 1. Overview

This document defines the capability-specific security requirements of
the Identity Platform, as required by
064_SmartCore_Blueprint_Standard.md §8.12.

Security requirements defined here govern how the Identity Platform
protects Person identity, Credentials, and Sessions.

This document does **not** define:

- Business authorization or permission evaluation (belongs to
  consuming Capability Platforms, per 059 §9)
- Persistence-layer storage mechanics (belongs to 09_Persistence.md)
- Transport/API-level contract details (belongs to 08_API.md)
- Configuration value defaults (belongs to 10_Configuration.md)
- Policy Engine implementation (belongs to a future Core Engine
  Blueprint, per 062)

This document references those documents rather than duplicating their
content, per 064 §6.4 (Single Source of Truth).

------------------------------------------------------------------------

# 2. Scope

Security requirements in this document apply to the following
Aggregates and Application/Domain Services (01_Domain_Model.md,
03_Aggregates.md):

- Person
- Credential
- Session
- AuthenticationDomainService
- SessionManagementDomainService
- CredentialManagementDomainService
- RegistrationApplicationService

Organization and Membership are included only insofar as they define
the Authorization Boundary (§4); they hold no independent secrets and
introduce no additional security surface in MVP.

## 2.1 Terminology Note: RefreshToken vs. RefreshTokenId

This document adopts the terminology used by the referenced documents.
01_Domain_Model.md §3 defines `RefreshToken` as the Value Object
governing session-continuation token rules ("Must be revocable"), and
01_Domain_Model.md §2 lists `RefreshTokenId` as the corresponding
Session attribute; 02_Use_Cases.md consistently uses `RefreshTokenId`
when describing operational behavior (creation, validation, revocation,
refresh). Where both terms appear, this document follows that same
convention throughout: `RefreshTokenId` is used when describing
operational behavior — the concrete, revocable, per-Session value that
Commands and Use Cases operate on — and `RefreshToken` is used only
when referring to the Value Object definition itself (§5.2).

------------------------------------------------------------------------

# 3. Authentication

Authentication answers "Who are you?" and SHALL remain separate from
Authorization (00_Overview.md §7; 059 §7; Domain Rule-005,
01_Domain_Model.md §9).

## 3.1 Requirements

- Authentication SHALL enter the Domain through
  AuthenticationDomainService (01 §7). This states the Domain entry
  point, not the internal credential mechanism; future credential
  types (e.g., Passkey, OAuth) MAY be supported by
  AuthenticationDomainService without contradicting this requirement.
- Authentication SHALL validate a Credential against the Person it
  claims to identify before any Session is created (UC-003,
  02_Use_Cases.md).
- Authentication SHALL NOT modify Person, Organization, or Membership
  state (02_Use_Cases.md UC-003 Postconditions).
- Authentication outcomes SHALL be recorded as LoginSucceeded or
  LoginFailed Domain Events (059 §9 Event Ownership Table).
- Failure to resolve a Person, failure to find an active Credential,
  and password mismatch SHALL all be treated as authentication failure
  and SHALL produce the same class of outward response, to avoid
  disclosing which failure condition occurred (see §11.2, User
  Enumeration).
- Multi-attempt / brute-force throttling behavior MAY be applied to
  authentication attempts; the specific throttling mechanism and
  thresholds are implementation-specific and belong to
  10_Configuration.md (02_Use_Cases.md UC-003 Alternative Flows).

## 3.2 Out of Scope for MVP

- Multi-Factor Authentication (MFA)
- OAuth / Social Login / Enterprise SSO

These are explicitly excluded from MVP per 00_Overview.md §11 and
059 §17, and SHALL NOT be implemented as part of this Blueprint.

------------------------------------------------------------------------

# 4. Authorization Boundary

The Identity Platform SHALL NOT evaluate business permissions (059 §9;
00_Overview.md §2).

## 4.1 What Identity Provides

Per 059 §9 (Authorization Boundary), the Identity Platform SHALL
provide only:

- Identity context (resolved Person)
- Authentication outcomes
- Membership information
- Organization information

## 4.2 What Identity SHALL NOT Do

- Identity SHALL NOT evaluate whether a Person is permitted to perform
  a business operation on a Resource.
- Identity SHALL NOT implement Role-Based Access Control (RBAC) or
  permission assignment; these belong to a future Authorization
  Platform (02_Use_Cases.md, Out of Scope UC-F18, UC-F19).
- Direct Person → Resource authorization evaluation is prohibited
  (057 §7; 059 §9). All authorization SHALL traverse:

```text
Person
    ↓
Membership
    ↓
Organization
    ↓
Resource
```

## 4.3 Session-Scoped Authorization Checks

The only authorization-adjacent check owned by Identity is verifying
that a Session or Credential operation belongs to the Person making the
request (e.g., UC-005 Manage Person Profile, UC-006 Change Credential,
UC-007 End Session — each requires "Session belongs to Person"
validation). This is Session-ownership validation, not business
permission evaluation, and SHALL remain the only exception to §4.2.

## 4.4 Relationship to Policy Engine

059 §17 and 00_Overview.md §9 list a Policy Engine as a **future**
dependency of Identity. Per 062_SmartCore_Core_Engine_Boundaries.md §6,
configurable authorization policies are a Policy Engine concern, not a
Domain Service concern.

- In MVP, no Policy Engine integration exists.
- Any future Policy Engine integration SHALL be invoked only from an
  Application Service (062 §5: "Domain Services SHALL NOT invoke Core
  Engines directly").
- Such integration SHALL NOT introduce business permission evaluation
  into the Identity Platform itself; it remains a boundary the
  consuming Capability Platform is responsible for (059 §9).

------------------------------------------------------------------------

# 5. Secrets Management

"Secrets" in the Identity Platform means: password material, password
hashes, access tokens, and refresh tokens.

## 5.1 Password Handling

- Passwords SHALL NOT be stored in plain text under any circumstance
  (059 §13; Domain Invariant, 01_Domain_Model.md §6 "Credential
  Invariants").
- Only PasswordHash (a Value Object, 01 §3) SHALL be persisted.
  PasswordHash is immutable once generated (01 §3).
- Authentication SHALL use a secure, industry-approved hashing
  algorithm (059 §13). The specific algorithm is an implementation
  detail per 064 §3 and SHALL be recorded in 10_Configuration.md, not
  frozen by this document; it SHALL, at minimum, be a salted,
  computationally-expensive algorithm resistant to offline brute-force
  attack (e.g. a memory-hard KDF), not a fast general-purpose hash.
- Raw password values SHALL NOT appear in logs, audit trails, or Domain
  Event payloads (see §10, §6.6 Event Audit Requirements,
  01_Domain_Model.md).

## 5.2 Token Handling

- AccessTokenId and RefreshToken are Value Objects (01 §3) and SHALL be
  revocable (01 §3 "Must be revocable"; 059 §13). See §2.1 for this
  document's convention distinguishing the `RefreshToken` Value Object
  from the operational `RefreshTokenId` Session attribute used
  elsewhere in this document.
- Refresh tokens SHALL be treated as bearer secrets: possession of a
  valid, non-revoked RefreshTokenId is sufficient to obtain a new
  AccessTokenId (UC-009, 02_Use_Cases.md).
- Access and refresh tokens SHALL be distinct values generated
  independently per Session (UC-004 Postconditions,
  02_Use_Cases.md: "AccessTokenId and RefreshTokenId exist and are
  distinct").
- Token generation SHALL fail closed: if token generation fails,
  Session creation SHALL fail and no partial Session SHALL be
  persisted (UC-004 Failure Conditions).
- Storage representation of these secrets at rest is not defined here;
  see §6.2 for the encryption-at-rest requirement and its ownership
  boundary with 09_Persistence.md.

------------------------------------------------------------------------

# 6. Encryption

## 6.1 Data in Transit

- All Identity Platform API endpoints (08_API.md) SHALL be served only
  over an encrypted transport. Transport-level configuration
  (TLS version, cipher suite policy) is implementation-specific and
  belongs to 10_Configuration.md; this document states the requirement
  only, per 064 §3 (Blueprints SHALL NOT freeze implementation
  details).

## 6.2 Data at Rest

- PasswordHash and RefreshTokenId values SHALL be protected at rest.
  The specific encryption mechanism (e.g., column-level encryption,
  storage-level encryption, envelope encryption) is a persistence
  concern and SHALL be defined in 09_Persistence.md, cross-referenced
  from here rather than duplicated (064 §6.4). This is the single
  authoritative statement of the encryption-at-rest requirement for
  this document (see §5.2).
- Email (EmailAddress Value Object, 01 §3) is personal data (§9) but is
  not a secret; it does not require the same protection level as
  PasswordHash, though it SHALL be protected under general Sensitive
  Data Handling requirements (§10).

## 6.3 Scope Boundary

This section defines *what* SHALL be encrypted. It intentionally does
not define *how* (algorithm, library, key size), consistent with 064
§3 and §6.4.

------------------------------------------------------------------------

# 7. Key Management

This section specifies platform-level key management requirements
independent of the underlying cryptographic mechanism.

Key management applies to any cryptographic key used to sign, verify,
or encrypt Identity Platform secrets — including, without limitation,
token-signing keys (e.g., JWT signing keys), at-rest encryption keys,
data-protection keys, and HMAC secrets. This document does not
enumerate which of these a given implementation uses; it defines the
requirements any of them SHALL satisfy.

- Cryptographic keys SHALL NOT be embedded in source code or
  configuration committed to version control.
- Cryptographic keys SHALL be rotatable without requiring Person
  re-registration, Credential replacement, or invalidation of
  Organization/Membership state (consistent with 059 §6
  Non-Invalidating Policy: ownership state SHALL survive independent
  of credential/session mechanics).
- Key rotation SHALL support a transition period in which
  previously-issued, still-valid Sessions and tokens remain verifiable
  until their natural expiration, unless an explicit revocation is
  required (§8.2).
- The specific key management system (e.g., managed KMS, HSM, secret
  manager) is implementation-specific and belongs to
  10_Configuration.md / 09_Persistence.md; this document defines only
  the requirements above, per 064 §3.

------------------------------------------------------------------------

# 8. Session and Token Security

This section elaborates the Session-specific security requirements
that follow from 03_Aggregates.md §5, §7 and 01_Domain_Model.md
Session Lifecycle (§5).

## 8.1 Session Independence

- Session compromise SHALL NOT compromise Person identity (Domain
  Invariant, 01 §6: "Session expiration SHALL NOT remove Identity").
- Session state SHALL NOT cascade to Person, Organization, or
  Membership state (03_Aggregates.md §7).

## 8.2 Revocation

- Sessions MAY be revoked independently of one another; a Person MAY
  hold multiple concurrent Sessions and revoking one SHALL NOT affect
  the others (01 §6 Session Invariants; 059 §8).
- Logout (UC-007) SHALL revoke both AccessTokenId and RefreshTokenId
  for the terminated Session (UC-007 Main Flow).
- Credential replacement (UC-006, Change Credential) SHALL NOT, by
  itself, revoke existing Sessions in MVP (UC-006 Constraints:
  "Credential change does not affect active Sessions"). Any future
  policy requiring session invalidation on password change is Future
  Scope and SHALL be introduced through an ADR before being added to
  this Blueprint (see 15_Extensibility.md).

## 8.3 Expiration

- Every Session SHALL have a defined ExpiresAt (01 §2 Session
  attributes; UC-004 Postconditions).
- Session expiration SHALL be enforced independently of user action
  (UC-007 Main Flow — System Expiration).
- Expired Sessions SHALL NOT be reactivated; a new Session SHALL
  require new authentication (UC-007 Constraints).

## 8.4 Refresh Semantics

- Refresh (UC-009) SHALL NOT create a new Person, Credential, or
  Membership (UC-009 Constraints).
- Refresh SHALL require a valid, non-revoked, non-expired
  RefreshTokenId (UC-009 Failure Conditions).
- The Domain Model does not require refresh token rotation in MVP
  (UC-009 Alternative Flows: "Optionally generate new RefreshTokenId
  (implementation-specific)"). Deployment-specific defaults — including
  whether rotation is enabled, and under which environments — are
  owned by 10_Configuration.md, not by this document; this document
  neither mandates nor prohibits rotation, so that Configuration
  remains the single owner of that decision. This is distinct from
  whether reuse detection or token-family revocation is introduced as
  an Aggregate-level concern, which is separately deferred by
  03_Aggregates.md §11.1 to a future Aggregate-boundary decision and is
  likewise not decided by this document.

------------------------------------------------------------------------

# 9. Privacy Requirements

- The Identity Platform SHALL treat Email and DisplayName (01 §2
  Person attributes) as personal data.
- DeviceInfo and IpAddress (01 §2 Session attributes) SHALL also be
  treated as personal data for storage, logging, and retention
  purposes. Their collection is justified by the Session Security
  purposes described in §11.3, but that justification does not exempt
  them from the personal-data handling requirements of this section.
- Email SHALL be unique across all Persons and SHALL be used as the
  primary lookup key for authentication (UC-001, UC-003); this
  uniqueness constraint SHALL NOT be weakened for privacy reasons, as
  it is a Domain Invariant (01 §6 Person Invariants).
- Comparison of Email SHALL be case-insensitive (EmailAddress Value
  Object rules, 01 §3), which SHALL be applied consistently so that
  case variation cannot be used to bypass the uniqueness constraint or
  to enumerate accounts.
- Profile updates (UC-005) SHALL NOT be used as a vector to disclose
  whether a given email is already registered to a different Person
  beyond the minimum conflict response required by UC-005 Failure
  Conditions.
- No additional personal data collection (beyond Email, DisplayName,
  and the attributes listed in 01_Domain_Model.md §2) is in MVP scope.
  Any expansion is Future Scope and SHALL be introduced through
  15_Extensibility.md and governed by 064 §16.

------------------------------------------------------------------------

# 10. Sensitive Data Handling

## 10.1 Classification

| Data Element                | Classification | Handling Requirement |
|------------------------------|-----------------|------------------------|
| Plain-text password (transient input) | Secret | Never persisted; never logged (§5.1) |
| PasswordHash                 | Secret          | Persisted only as hash; protected at rest (§5.1, §6.2) |
| AccessTokenId                | Secret          | Revocable; not logged in full (§5.2) |
| RefreshTokenId                | Secret          | Revocable; treated as bearer credential (§5.2) |
| Email                        | Personal Data    | Unique, case-insensitive; not a secret (§9) |
| DisplayName                  | Personal Data    | No uniqueness constraint (01 §2) |
| DeviceInfo                   | Personal Data    | Collected for Session security (§11.3); subject to retention limits (§9) |
| IpAddress                    | Personal Data    | Collected for Session security (§11.3); subject to retention limits (§9) |
| PersonId / OrganizationId / MembershipId / SessionId / CredentialId | Identifier | Immutable; not secret, but SHALL NOT be guessable in a way that enables enumeration of other Persons' resources |

## 10.2 Event Payloads

- Per 01_Domain_Model.md "Event Audit Requirements," all Domain Events
  SHALL support Actor Identity, Session Reference (optional), Delegated
  Identity (optional), Timestamp, and Execution Context.
- Domain Event payloads (06_Domain_Events.md) SHALL NOT include
  PasswordHash, plain-text password, AccessTokenId, or RefreshTokenId
  values. Events such as LoginFailed and SessionCreated SHALL reference
  identifiers (PersonId, SessionId) rather than secret material.
- This exclusion applies to the full event envelope, not only its
  business payload: Event metadata (including the Actor Identity,
  Session Reference, Delegated Identity, and Execution Context fields
  required by the Event Audit Requirements, 01_Domain_Model.md) SHALL
  likewise exclude secret values. Metadata is not an exempt channel for
  data that §10.2's payload restriction otherwise prohibits.

## 10.3 Logging

- Application and diagnostic logs SHALL NOT contain plain-text
  passwords, PasswordHash values, or full token values.
- Sensitive values SHALL be redacted before logging, including in
  error messages and exception traces (see §11.9); redaction SHALL
  occur at the point of logging, not be left to downstream log
  processing.
- Logs MAY reference PersonId, SessionId, CredentialId, and outcome
  (success/failure) for observability and the Threat Considerations
  described in §11.

------------------------------------------------------------------------

# 11. Threat Considerations

This section identifies threats intrinsic to the Identity Platform's
responsibilities. Countermeasure *implementation* (rate limiting
thresholds, WAF rules, specific anomaly-detection logic) is
implementation-specific and belongs to 10_Configuration.md; this
document states which threats the Blueprint's design already
constrains, and which remain open for implementation-level mitigation.

## 11.1 Credential Stuffing / Brute Force

- Threat: repeated authentication attempts (UC-003) using guessed or
  leaked credentials.
- Blueprint-level mitigation: authentication failures produce a
  uniform LoginFailed outcome regardless of cause (§3.1), preventing
  the failure response itself from narrowing the attack.
- Deferred to implementation: throttling/rate-limiting thresholds
  (UC-003 Alternative Flows).

## 11.2 User Enumeration

- Threat: distinguishing "email not found" from "wrong password" to
  enumerate registered Persons.
- Blueprint-level mitigation: §3.1 requires that Person-not-found,
  no-active-Credential, and password-mismatch all resolve to the same
  outward authentication-failure response.
- Registration (UC-001) intentionally returns a conflict error for a
  duplicate email (UC-001 Failure Conditions), which is a narrower,
  accepted disclosure (the platform must reject duplicate
  registration); this is a deliberate trade-off, not an oversight.

## 11.3 Session/Token Theft

- Threat: an attacker obtains a valid AccessTokenId or RefreshTokenId
  and impersonates the Session.
- Blueprint-level mitigation: Sessions are revocable independently
  (§8.2); logout revokes both tokens (UC-007); Sessions carry
  DeviceInfo and IpAddress (01 §2) enabling future anomaly detection.
- Deferred to implementation: anomaly detection, IP/device binding
  enforcement, and forced re-authentication policies are not part of
  MVP (00_Overview.md §11 excludes Advanced Authorization / MFA).

## 11.4 Refresh Token Replay

- Threat: a captured RefreshTokenId is replayed after legitimate use.
- Blueprint-level mitigation: none mandatory by the Domain Model in
  MVP; whether rotation on refresh is enabled is a deployment-specific
  decision owned by 10_Configuration.md (§8.4).
- Explicitly deferred: reuse detection and token-family revocation are
  flagged in 03_Aggregates.md §11.1 as a future Aggregate-boundary
  decision, not resolved by this document.

## 11.5 Cross-Aggregate Consistency Abuse

- Threat: exploiting the Registration Core Transaction / Post-Commit
  split (059 §6) to create ownership records without valid
  Credential/Session, or vice versa.
- Blueprint-level mitigation: ownership consistency (Person,
  Organization, Membership) is guaranteed by the Core Ownership
  Transaction independent of post-commit outcomes (059 §6); a Person
  can never exist in a state where ownership is partial (057 §8:
  "Partial ownership registration states are prohibited").
- This is an architectural guarantee, not a threat fully closed by
  Security alone; it is enforced jointly with 059 §6 and
  057 §8.

## 11.6 Privilege Escalation via Direct Resource Access

- Threat: a Person attempts to bypass Membership to act directly on a
  Resource.
- Blueprint-level mitigation: this is structurally prevented by the
  mandatory evaluation path Person → Membership → Organization →
  Resource (057 §7; 059 §9 §4.2 above); Identity provides no API that
  accepts a direct Person-to-Resource reference.

## 11.7 Session Fixation

- Threat: an attacker pre-establishes or supplies a Session identifier
  and induces a victim to authenticate under it, then reuses the same
  identifier.
- Blueprint-level mitigation: this is structurally out of scope for
  MVP because Session identifiers are always generated by
  AuthenticationDomainService as part of UC-004 Create Session (UC-004
  Main Flow); no Use Case accepts a client-supplied Session identifier
  as input to authentication.

## 11.8 Timing Attack

- Threat: an attacker infers Credential validity (e.g., whether an
  email is registered, or how close a password guess is) from response
  timing differences during authentication (UC-003) or registration
  (UC-001).
- Blueprint-level mitigation: none mandated by the Domain Model in
  MVP. Constant-time comparison and timing-attack mitigation are an
  implementation concern — properties of the cryptographic library,
  runtime, and infrastructure used — rather than an externally
  configurable parameter; they are not owned by 10_Configuration.md.
  This is stated as an implementation concern rather than frozen
  guidance, consistent with 064 §3 (Blueprints SHALL NOT freeze
  implementation details).

## 11.9 Secret Leakage via Exception / Error Handling

- Threat: an unhandled exception or verbose error response exposes
  PasswordHash, token values, or other secrets defined in §5 and §10.1
  (e.g., in a stack trace, error message, or diagnostic payload).
- Blueprint-level mitigation: the redaction requirement in §10.3
  applies to error messages and exception traces, not only routine
  logs. Error responses returned to clients (08_API.md) SHALL NOT
  include secret values under any failure condition defined in
  02_Use_Cases.md.

## 11.10 Replay of Expired Access Token

- Threat: an attacker replays a previously valid but now-expired
  AccessTokenId to gain access after it should no longer be honored.
- Blueprint-level mitigation: every Session has a defined ExpiresAt
  (§8.3; 01 §2), and expired Sessions SHALL NOT be reactivated (UC-007
  Constraints). Enforcement of expiration during token validation is
  part of Authentication (§3), and SHALL reject any AccessTokenId
  associated with a Session that has passed ExpiresAt, independent of
  whether SessionExpired has yet been processed asynchronously.

------------------------------------------------------------------------

# 12. Security Policy Configurability

Per 059 §13, "Security policies SHALL be configurable."

- Configurable security parameters (e.g., password strength rules,
  session expiration duration, refresh token lifetime, throttling
  thresholds) SHALL be externalized to 10_Configuration.md.
- This document (11_Security.md) defines *that* such parameters exist
  and *what* invariant they must not violate (e.g., a configured
  session expiration SHALL still be a finite value that respects §8.3);
  it SHALL NOT hardcode default values, per 064 §6.4 and §8.11
  (Configuration SHALL NOT redefine domain rules; Security SHALL NOT
  duplicate Configuration's ownership of defaults).
- Configuration SHALL NOT weaken mandatory security invariants defined
  by this document (e.g., §5.1's prohibition on plain-text password
  storage, §8.3's requirement that every Session have a finite
  ExpiresAt). Configuration governs the value within an invariant;
  it SHALL NOT govern whether the invariant applies.

------------------------------------------------------------------------

# 13. MVP Scope vs Future Scope

## 13.1 In MVP

- Password-based Authentication with secure hashing (§3, §5.1)
- Session issuance, revocation, and expiration (§8)
- Refresh token issuance without mandatory rotation (§8.4)
- Authorization Boundary enforcement (identity context only) (§4)
- Uniform authentication-failure responses (§11.1, §11.2)

## 13.2 Future Scope (Not This Blueprint)

- Multi-Factor Authentication (MFA)
- OAuth / Social Login / Enterprise SSO
- Policy Engine–backed configurable authorization policies (§4.4)
- Refresh token rotation enforcement and reuse detection (§8.4, §11.4)
- Device/IP anomaly detection and session binding (§11.3)
- Role-Based Access Control / delegated administration
  (02_Use_Cases.md UC-F18–UC-F20)

Future Scope items SHALL NOT influence MVP implementation (064 §6.5)
and SHALL be introduced only through the process defined in
15_Extensibility.md and governed by an ADR, consistent with how prior
Identity clarifications were introduced (ADR-0002, ADR-0003).

------------------------------------------------------------------------

# 14. Validation Against Reference Standards

**064 – Blueprint Standard §8.12 Security**:

- ✅ Authentication requirements defined (§3)
- ✅ Authorization requirements defined, scoped to identity context
  only (§4)
- ✅ Secrets handling defined (§5)
- ✅ Encryption requirements defined, deferring mechanism to
  09_Persistence.md (§6)
- ✅ Key Management requirements defined (§7)
- ✅ Privacy Requirements defined (§9)
- ✅ Sensitive Data Handling defined (§10)
- ✅ Threat Considerations defined (§11)
- ✅ References platform-wide security principles (059 §13; 057 §7)
  rather than redefining them (064 §11)

**057 / 059 Alignment**:

- ✅ No direct Person → Resource authorization introduced (057 §7)
- ✅ Identity Platform does not evaluate business permissions (059 §9)
- ✅ Passwords never stored in plain text (059 §13)
- ✅ Tokens revocable; Sessions support expiration (059 §13)

**062 Alignment**:

- ✅ No Domain Service is described as invoking a Core Engine directly
  (062 §5)
- ✅ Policy Engine integration, where it appears in future scope, is
  scoped to Application Service orchestration only (§4.4)

**01 / 03 Alignment**:

- ✅ No new Aggregates, Value Objects, or Domain Services introduced
- ✅ No contradiction of documented Aggregate lifecycles or invariants

------------------------------------------------------------------------

# 15. Change Log

## Version 1.1.1 (2026-07-18)

**Editorial Polish** (final review pass; 0 Blocking, 0 High, 0 Medium
issues remaining — this revision addresses the 2 Low/editorial items
noted):

1. **§2.1 Terminology Note reworded**: Removed the phrase "remains a
   candidate correction for 01_Domain_Model.md and 03_Aggregates.md,"
   which read as this document commenting on the correctness of
   another Blueprint document — a Blueprint SHALL specialize
   architecture, not evaluate other documents (064 §2). §2.1 now
   states only the terminology convention this document adopts, per
   the wording used by 01_Domain_Model.md and 02_Use_Cases.md, with no
   implied judgment on those documents.
2. **§11.8 Timing Attack corrected**: Removed the inaccurate framing
   that constant-time comparison / timing-attack mitigation "belongs
   to 10_Configuration.md." Constant-time comparison is a property of
   the cryptographic library, runtime, and infrastructure — not an
   externally configurable parameter — so attributing it to
   Configuration risked an AI Generator searching for a non-existent
   configuration flag. Reworded to state it is an implementation
   concern, consistent with 064 §3.
3. **§11.10 wording refinement**: Reworded "is an Authentication
   requirement rather than a Session-lifecycle requirement" to "is
   part of Authentication," to avoid conflating requirement ownership
   with runtime behavior. No change in meaning.

No architectural or normative meaning changed beyond the above three
corrections; this is a Patch-class release per 064 §13.

## Version 1.1.0 (2026-07-18)

**Review Corrections** (Enterprise Architecture / DDD / Blueprint
Standard review pass; no Blocking Issues found; 2 High Priority and 6
Medium improvements addressed below):

1. **Refresh Token Rotation ownership** (§8.4, §11.4): Removed
   "implementation-specific" language that could read as contradicting
   10_Configuration.md's Production rotation default. Security now
   states only that the Domain Model does not mandate rotation;
   10_Configuration.md is affirmed as sole owner of the deployment
   default. No architectural meaning changed.
2. **Terminology unification** (new §2.1): Added a Terminology Note
   distinguishing the `RefreshToken` Value Object (01 §3) from the
   operational `RefreshTokenId` Session attribute (01 §2), and applied
   `RefreshTokenId` consistently throughout this document (§5.2, §6.2)
   except where explicitly citing the Value Object catalog.
3. **Key Management scope clarification** (§7): Added an opening
   statement that key management requirements are independent of the
   underlying cryptographic mechanism, and enumerated illustrative key
   types (JWT signing, at-rest, data-protection, HMAC) without
   mandating any of them.
4. **Removed duplication** (§5.3 removed): The storage/encryption-at-
   rest statement previously appeared in both §5.3 and §6.2. §5.3 is
   removed; §6.2 remains the single authoritative statement, per 064
   §6.4 (Single Source of Truth).
5. **Threat Model expanded** (§11.7–§11.10 added): Added Session
   Fixation, Timing Attack, Secret Leakage via Exception/Error
   Handling, and Replay of Expired Access Token. Consistent with the
   rest of §11, these are recorded as threats with an architectural
   disposition (structurally out of scope, or deferred to
   implementation); no new MVP mitigation behavior is introduced.
6. **Logging redaction** (§10.3): Added an explicit redaction
   requirement, including for error messages and exception traces.
7. **Event metadata coverage** (§10.2): Extended the secret-exclusion
   requirement to the full event envelope (metadata, not only payload),
   closing a gap where secrets could otherwise be placed in Event
   Audit Requirement fields instead of the payload.
8. **DeviceInfo / IpAddress classified as personal data** (§9, §10.1):
   Added both Session attributes to Privacy Requirements and to the
   Sensitive Data Handling classification table; previously only Email
   and DisplayName were classified.
9. **AuthenticationDomainService wording loosened** (§3.1): Reworded
   "SHALL be performed only through" to "SHALL enter the Domain
   through," so that future credential mechanisms (Passkey, OAuth) can
   be supported by the same Domain Service without contradicting this
   requirement.
10. **Configuration cannot weaken invariants** (§12): Added an explicit
    statement that Configuration governs values within a security
    invariant, not whether the invariant applies.

No new Aggregates, Commands, Queries, Events, or MVP scope changes were
introduced by this revision. All corrections are clarifying or
duplication-removing per 064 §13 (Patch/clarification-class changes do
not require Major version increments); this revision is issued as
Minor per 064 §13 given the net-new §2.1 and §11.7–§11.10 content.

## Version 1.0.0 (2026-07-18)

**Initial Security Specification**:

- Created Security Blueprint document per 064_SmartCore_Blueprint_Standard
  §8.12
- Defined Authentication, Authorization Boundary, Secrets, Encryption,
  Key Management, Session/Token Security, Privacy Requirements,
  Sensitive Data Handling, Threat Considerations, and Security Policy
  Configurability
- Aligned with 00_Overview.md (v1.1.0), 01_Domain_Model.md (v1.2.0),
  03_Aggregates.md (v1.1.1), 02_Use_Cases.md (v1.2.0)
- Aligned with 057_SmartCore_Tenancy_and_Ownership_Model.md (v1.3),
  059_SmartCore_Identity_Platform.md (v1.1),
  062_SmartCore_Core_Engine_Boundaries.md (v1.0)
- No new Aggregates, Commands, Queries, or Events introduced
- No MVP scope expansion
- Explicitly deferred decisions cross-referenced rather than resolved
  (refresh token rotation/reuse detection per 03_Aggregates.md §11.1;
  encryption-at-rest mechanism per 09_Persistence.md)

**Dependencies**:

- 00_Overview.md (v1.1.0)
- 01_Domain_Model.md (v1.2.0)
- 03_Aggregates.md (v1.1.1)
- 057_SmartCore_Tenancy_and_Ownership_Model.md (v1.3)
- 059_SmartCore_Identity_Platform.md (v1.1)
- 062_SmartCore_Core_Engine_Boundaries.md (v1.0)
- 064_SmartCore_Blueprint_Standard.md (v1.1.4)
- ADR-0002_Identity_Foundation_Clarifications.md

------------------------------------------------------------------------

**END OF DOCUMENT**
