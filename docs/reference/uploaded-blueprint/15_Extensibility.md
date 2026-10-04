<!--
Document ID: ID-15
Title: SmartCore Identity Platform Blueprint - Extensibility
Version: 1.0.2
Status: READY_FOR_GENERATION

Purpose:
Consolidate every extension point already named across the Identity
Platform Blueprint set into one place, and define the governance
process — required by 11_Security.md §13.2 — through which a Future
Scope item may eventually enter Version 1.x. This document does not
make new architectural decisions. Every item catalogued below is
already stated as future/deferred/excluded scope in an upstream
document; this document adds no item that is not independently
verifiable there.

Dependencies:
- 064_SmartCore_Blueprint_Standard.md (§8.16 Extensibility requirement)
- 00_Overview.md
- 01_Domain_Model.md
- 02_Use_Cases.md
- 03_Aggregates.md
- 06_Domain_Events.md
- 09_Persistence.md
- 10_Configuration.md
- 11_Security.md
- 14_MVP.md
- 057_SmartCore_Tenancy_and_Ownership_Model.md
- ADR-0002_Identity_Foundation_Clarifications.md
- ADR-0003_Organization_and_Membership_Lifecycle_Standardization.md

Change Log:
  - Version 1.0.2 (2026-07-19): Correction pass following a citation-
    accuracy review of v1.0.1. §2.2's Promotion Test was presented as a
    Markdown blockquote directly under a citation to "09_Persistence.md
    §7.0", implying a verbatim quotation. It was not verbatim: the
    actual text of 09_Persistence §7.0 scopes its test to whether a
    persistence structure "can be fully owned by its current Aggregate
    Root" — a domain-boundary concept — while this document's version
    read "cannot be fully absorbed by an existing Aggregate, Domain
    Service, or Contract," silently broadening the test to two concepts
    (Domain Service, Contract) that are not domain-boundary concepts
    and that 09_Persistence §7.0 does not itself invoke. Fixed by (1)
    removing the blockquote formatting, (2) stating explicitly that
    this is a restatement, not a quotation, of 09_Persistence §7.0, and
    (3) explaining why the restatement generalizes the test — this
    document's extension categories (Aggregates, Domain Services,
    Contracts; §1.1) are broader than 09_Persistence.md's own scope
    (persistence structures only), so the generalization is this
    document's own and is now labeled as such rather than attributed to
    09_Persistence.md. No new Aggregates, Commands, Queries, Events, or
    business rules introduced; no extension category, ADR-governance
    process, or Future Scope item changed — this is a citation-accuracy
    and presentation fix only.
  - Version 1.0.1 (2026-07-19): Correction pass following an
    architecture review of v1.0.0. (1) §2.2 Promotion Test rescoped
    from "any extension category" to domain/consistency-boundary
    candidates only (§3, §4, §6, §7); §9's Transport concerns and
    §10's Configuration concerns now explicitly follow their own
    owning document's governance instead. (2) §9 reworded to remove
    the appearance of contradicting §2 — Transport-mechanics items are
    governed by 07_Contracts.md rather than exempted from ADR
    governance generally. (3) §2.1 removed an unverifiable "051§5"
    citation — 051 is not in this document's Dependencies. (4) §6
    removed seven invented illustrative event names
    (OrganizationResumed, MembershipRoleChanged, PersonRestored, etc.);
    now states the naming convention by reference to actual MVP/14_MVP
    §5 examples only, minting no new name. (5) §10 reworded to state a
    Feature Flag is optional per candidate, not mandatory. (6) §11
    condensed from a seven-subsection ✅ checklist to four substantive
    paragraphs; also corrected a stale claim there (inherited from
    v1.0.0's original §2.2 wording) that the Promotion Test generalizes
    to every extension category. (7) Change Log wording changed from
    "Derived... naming pattern" to "Documented... convention" to avoid
    implying a new rule was extracted. No new Aggregates, Commands,
    Queries, Events, or Business Rules introduced. See §12 for full
    detail.
  - Version 1.0.0 (2026-07-19): Initial Extensibility Blueprint.
    Consolidates every future-scope item independently verifiable
    across the current Identity Platform Blueprint set into a single
    catalog (§3-§9), and defines the ADR-governed Extension Process
    required by 11_Security §13.2 (§2), grounded in 09_Persistence
    §7.0's existing Promotion Rule and 10_Configuration §3's existing
    Feature Flag reservations. No new Aggregates, Commands, Queries,
    Events, or Business Rules introduced. See §12 for full detail.
-->

# 1. Purpose and Scope

This document satisfies 064_SmartCore_Blueprint_Standard.md §8.16:
"Extensibility documents intentional extension points... This document
SHALL describe supported evolution paths."

## 1.1 What This Document Is

A **consolidation and process document**. Every extension point listed
in §3-§9 already exists as a "future scope," "deferred," or "excluded"
statement in an upstream Blueprint document (01_Domain_Model.md,
02_Use_Cases.md, 03_Aggregates.md, 06_Domain_Events.md,
09_Persistence.md, 11_Security.md, 14_MVP.md, 057, or ADR-0002/ADR-0003).
This document's contribution is to gather those statements into one
place and define the process (§2) through which any one of them may
eventually be promoted into Version 1.x scope.

## 1.2 What This Document Is Not

- It does not decide that any listed item **will** be built, or when.
- It does not decide the eventual shape of any listed item (e.g.
  whether Device Identity becomes an independent Aggregate or a child
  entity) — per 03_Aggregates.md §11.1, that determination is
  explicitly deferred to the ADR that eventually introduces the item.
- It introduces no new Aggregate, Command, Query, Event, API endpoint,
  Domain Rule, Policy Rule, or Infrastructure Constraint. Every noun
  named in §3-§9 already appears in an upstream document.
- It is not a substitute for the ADR process. An item moving from this
  catalog into implementation still requires its own ADR (§2).

## 1.3 Relationship to 11_Security.md §13.2

11_Security.md §13.2 states: "Future Scope items SHALL NOT influence
MVP implementation (064 §6.5) and SHALL be introduced only through the
process defined in 15_Extensibility.md and governed by an ADR,
consistent with how prior Identity clarifications were introduced
(ADR-0002, ADR-0003)." §2 below is this document's discharge of that
obligation.

---

# 2. Extension Governance Process

This section defines the process referenced by 11_Security §13.2. It
introduces no new governance body or authority beyond what 057 §12 and
064 already establish (ADR-based change control); it states the
sequence explicitly so that "the process defined in 15_Extensibility.md"
has an actual referent.

## 2.1 Step 1 — Candidacy

An item becomes an Extension Candidate when it is named as future,
deferred, or excluded scope in any accepted Blueprint document. §3-§9
of this document are the current, consolidated list of candidates. A
new candidate not yet listed here may be added to this catalog by a
documentation-only update — adding a candidate to this list is not
itself an architectural decision and does not require an ADR.

## 2.2 Step 2 — Promotion Test

The Promotion Test governs extensions that may introduce or modify
domain consistency boundaries — that is, candidates in §3 (Future
Aggregates and Identity Types), §4 (Future Domain/Application
Services), §6 (Future Domain Events), and §7 (Future Persistence
Structures). Other extension categories — Transport/API concerns (§9)
and Configuration concerns (§10) — continue to follow their own
governing Blueprint document (08_API.md, 07_Contracts.md,
10_Configuration.md respectively) and require an ADR only where an
architectural decision is actually involved, not by default; §9 states
this explicitly for its own candidates.

Before a domain-boundary candidate may be designed or implemented, it
is evaluated using a restatement of the test 09_Persistence.md §7.0
establishes ("General Rule — Promotion to Aggregate Boundary") for
persistence structures:

A candidate remains non-binding future scope unless and until it
acquires independent lifecycle rules, independent invariants, or
transactional/consistency requirements that its current design does
not already own — the same threshold 09_Persistence §7.0 states as
"cannot be fully owned by its current Aggregate Root." Only once one
of these conditions is demonstrated does the candidate become eligible
for Step 3.

**This is a restatement, not a verbatim quotation, of 09_Persistence
§7.0.** That section's test is scoped specifically to whether a
persistence structure can be owned by its Aggregate Root — Aggregate
Root is a domain-boundary concept; Domain Service and Contract are
not, and are not part of 09_Persistence §7.0's own test. This document
generalizes the same underlying question — has independent lifecycle,
invariant, or consistency need emerged? — to the broader set of
extension categories this document covers (future Aggregates, Domain
Services, and Contracts; §1.1), since 09_Persistence.md addresses only
persistence structures and does not itself speak to non-persistence
extension categories. That generalization is this document's own; it
is not an extension 09_Persistence.md has already made.

This restatement applies the same underlying threshold 09_Persistence
§7.0 already uses for RefreshToken (§7.1) and that 03_Aggregates §11.1
references for both of its open candidates, extended here to the
categories 09_Persistence.md does not itself cover — this document
does not introduce a second, materially different test.

## 2.3 Step 3 — ADR

An eligible candidate requires its own Architecture Decision Record,
following the precedent already set by ADR-0002 (Registration Boundary,
Command Model Coordination Exception) and ADR-0003 (Lifecycle
Standardization). The ADR SHALL:

- Identify every upstream document requiring a version bump (following
  the "Document Updates Required" table pattern ADR-0002 and ADR-0003
  both already use)
- State explicitly whether the candidate introduces a new Aggregate,
  a new child entity on an existing Aggregate, or no new consistency
  boundary at all (per the Promotion Test outcome, §2.2)
- Not cite this document (15_Extensibility.md) as its own authorizing
  decision — this document catalogs candidacy, it does not authorize
  implementation. The authorizing decision is the ADR itself, per the
  same governance-timing rule already applied to every other Normative
  document in this Blueprint set (Normative documents SHALL NOT cite
  Proposed ADRs as their authorizing decisions until those ADRs are
  Accepted).
- Follow standard ADR Scope Limitation practice already established by
  ADR-0002 (exception applies only to the named operation, not as
  general precedent) and ADR-0003 (future re-evaluation is a distinct
  future decision, not automatically approved).

## 2.4 Step 4 — Upstream Document Revision

Once an ADR is Accepted, the affected upstream documents are revised in
the same manner ADR-0002/ADR-0003 already required for their own
decisions (01_Domain_Model.md, 03_Aggregates.md, 02_Use_Cases.md,
04_Commands.md, 05_Queries.md, 06_Domain_Events.md, 08_API.md,
07_Contracts.md, as applicable to the specific candidate). This
document's own catalog entry for that candidate (§3-§9) is then updated
to reflect the Accepted ADR reference, and the candidate is removed
from "future scope" framing in the documents that formerly excluded it.

## 2.5 Step 5 — Feature Flag Activation

Where the candidate has a corresponding reserved Feature Flag in
10_Configuration.md §3 (e.g. `feature.mfa.enabled`,
`feature.oauth.enabled`, `feature.enterprise_sso.enabled`,
`feature.organization_switching.enabled`), that flag's default MAY
change from `false` to environment-configurable only after Step 4 is
complete. This is not a new rule — it restates 10_Configuration §3's
own existing constraint: "Enabling any flag above without a
corresponding Command, Use Case, and Event definition is a Blueprint
violation and SHALL fail Architectural Validation (064 §9.3)." Not
every candidate has a pre-reserved flag (see §10 for the current
reservation list); a candidate without one does not skip Step 4, it
simply has no flag to flip.

## 2.6 Process Diagram

```text
Candidate named in an upstream document
        ↓
Step 1: Listed in this document's catalog (§3-§9)
        ↓
Step 2: Promotion Test (09_Persistence §7.0, generalized)
        ↓ (only if independent lifecycle/invariant/consistency shown)
Step 3: ADR drafted, reviewed, Accepted
        ↓
Step 4: Upstream documents revised (Domain Model, Aggregates,
        Use Cases, Commands, Queries, Events, Contracts, API)
        ↓
Step 5: Feature Flag (if reserved) becomes configurable (10_Configuration §3)
```

---

# 3. Future Aggregates and Identity Types

| Candidate | Source | Current Status |
|---|---|---|
| Device Identity | 03_Aggregates §11; ADR-0002 §6 | Named future extension; SHALL extend Identity capabilities "without changing Person identity semantics" (ADR-0002 §6) |
| Service Identity | 03_Aggregates §11; ADR-0002 §6 | Same as above |
| AI Agent Identity | 03_Aggregates §11; ADR-0002 §6 | Same as above |
| Organization Hierarchy | 03_Aggregates §11 | Named future Aggregate candidate; no lifecycle or consistency boundary yet asserted |
| Delegation | 03_Aggregates §11; 057 §12 ("Delegated administration") | Named future Aggregate candidate |
| Audit Log | 03_Aggregates §11 | Named future Aggregate candidate |

## 3.1 Open Candidates Explicitly Deferred to This Document by Others

Per 03_Aggregates §11.1, two items are recorded there as candidates
this document ("the document named as the owner of the eventual
decision") should not be silent about, while explicitly not deciding
their outcome:

- **RefreshToken re-evaluation** (09_Persistence §7.1): currently
  internal storage detail owned by the Session Aggregate. Becomes a
  Step 2 (Promotion Test) candidate only if a future capability
  requires token-family revocation, reuse detection, or comparable
  multi-device security behavior — none exists today.
- **Additional Credential Types** (Passkey, WebAuthn, TOTP, Recovery
  Code, OAuth Identity; 09_Persistence §4.5, §8.3; 02_Use_Cases
  UC-F11): whether each becomes a child entity under a broadened
  Credential Aggregate or a sibling Aggregate family is explicitly a
  decision for the ADR that introduces the first such type (03_Aggregates
  §11.1), not decided here.

## 3.2 Organization Ownership Model Enhancements

057 §12 additionally names, as future enhancements that "SHALL
preserve the architectural principles defined in [057]":

- Multiple memberships (one Person, more than one Membership per
  Organization, or Memberships across more Organizations than today's
  single-Personal-Organization MVP constraint — 05_Queries §4.2 states
  "Multi-Organization membership is future scope")
- Cross-organization collaboration
- Organization federation
- Hierarchical organizations (see also §3 table above)

---

# 4. Future Domain Services and Application Services

Per 14_MVP.md §6, the following Domain Services are explicitly deferred
to future versions:

| Future Domain Service | Responsibility (implied by name and 14_MVP §6) |
|---|---|
| OrganizationManagementDomainService | Suspend, Archive, Restore Organization |
| MembershipManagementDomainService | Revoke, Reinstate Membership, Role change |
| DelegationDomainService | Supports future Delegation Aggregate (§3) |
| AuditingDomainService | Supports future Audit Log Aggregate (§3) |

01_Domain_Model.md §7 additionally states explicitly: "Do NOT introduce
`OrganizationManagementDomainService` or `MembershipManagementDomainService`
into MVP. Lifecycle transition commands for Suspended / Archived /
Revoked are future scope" — consistent with the table above.

No future Application Service is named in any upstream document.
RegistrationApplicationService (01_Domain_Model §8) remains the sole
Application Service, and ADR-0002's Scope Limitation explicitly
prohibits treating its coordination exception as precedent for a
future Application Service without independent architectural review
(ADR-0002, "Future Decisions": "Any future request for a similar
cross-aggregate atomic coordination exception... SHALL require its own
independent architectural review and SHALL NOT cite this ADR as a
general precedent").

---

# 5. Future Commands and Use Cases

02_Use_Cases.md §4 ("Out of Scope") is the authoritative catalog of
Future Use Cases. It is reproduced here by category for consolidation;
02_Use_Cases.md §4 remains the source of truth if the two ever diverge.

**Organization Lifecycle Operations**: UC-F01 Suspend Organization,
UC-F02 Resume Organization, UC-F03 Archive Organization, UC-F04 Create
Additional Organizations.

**Membership Operations**: UC-F05 Revoke Membership, UC-F06 Change
Membership Role, UC-F07 Create Multiple Memberships.

**Credential Operations**: UC-F09 Credential History, UC-F10 Multiple
Active Credentials per Person, UC-F11 Alternative Credential Types
(OAuth, SSO, etc.).

**Person Operations**: UC-F12 Suspend Person, UC-F13 Archive Person,
UC-F14 Restore Archived Person.

**Identity Extensions**: UC-F15 Device Identity, UC-F16 Service
Identity, UC-F17 AI Agent Identity (see §3).

**Authorization Operations**: UC-F18 Role-Based Access Control (RBAC)
— explicitly scoped to a future Authorization Platform, not Identity;
UC-F19 Permission Assignment — same; UC-F20 Delegated Administration.

**Note on numbering**: 02_Use_Cases.md §4 skips from UC-F07 to UC-F09
— no UC-F08 exists in that document as currently written. This
document does not assign a UC-F08 to fill the gap, since doing so would
be inventing a Use Case rather than consolidating an existing one; the
gap is noted here for 02_Use_Cases.md's own future correction pass,
out of this document's editable scope.

---

# 6. Future Domain Events

06_Domain_Events.md itself defines no future event (06_Domain_Events
§7.4: "this document defines no such future event"). The only named
future events are 14_MVP.md §5's own non-exhaustive examples:
`SuspendOrganizationRequested`, `RevokeMembershipCompleted`, "etc."

Rather than inventing a complete future event catalog not stated
anywhere upstream, this document instead states the naming convention
already implicit in the MVP Event Ownership Table (ADR-0002 Decision 5;
01_Domain_Model.md "Event Producer Mapping"; 06_Domain_Events §3): an
event name pairs an entity or aggregate with a past-tense outcome, as
already demonstrated by every MVP event name (`PersonRegistered`,
`LoginSucceeded`, `SessionExpired`) and by 14_MVP §5's own two future
examples above. This document does not mint additional event names for
§4-§5's other Future Use Cases beyond what 14_MVP §5 already names —
per §2.3, the ADR that eventually introduces each Command decides its
actual Event Producer Mapping, consistent with 01_Domain_Model §7's
existing pattern of a Domain Service stating "Produces:" for each event
it owns. A name minted here, even labeled illustrative, risks being
read later as a de facto reservation.

---

# 7. Future Persistence Structures

09_Persistence.md §7.1-§7.5 already classifies five candidate
supporting structures. Reproduced here for consolidation:

| Structure | Current Classification | Promotion Trigger (§2.2) |
|---|---|---|
| RefreshTokens | Internal to SessionRepository (§7.1) | Token-family revocation, reuse detection, or device-trust behavior |
| LoginHistory | Optional, non-transactional (§7.2) | Not currently a promotion candidate — best-effort projection only |
| IdentityEvents | Explicitly deferred (§7.3) | Deferred to a future Messaging/Integration document, not this one |
| AuditLogs | Explicitly deferred (§7.4) | Deferred to a future Security/Data Classification document |
| PasswordHistory | Explicitly excluded from MVP as a queryable capability (§7.5) | Decision for a future ADR; `Replaced`-status Credential rows already persist without a dedicated structure |

---

# 8. Future Security Capabilities

Per 11_Security.md §13.2 ("Future Scope (Not This Blueprint)"):

- Multi-Factor Authentication (MFA)
- OAuth / Social Login / Enterprise SSO
- Policy Engine–backed configurable authorization policies (11_Security
  §4.4)
- Refresh token rotation enforcement and reuse detection (11_Security
  §8.4, §11.4)
- Device/IP anomaly detection and session binding (11_Security §11.3)
- Role-Based Access Control / delegated administration (= UC-F18-F20,
  §5 above)

Each of the first three already has a reserved, disabled-by-default
Feature Flag (10_Configuration §3; see §10 below).

---

# 9. Future APIs

Per 14_MVP.md §7 ("Future APIs (deferred)"), consolidated with 08_API.md
§2.3's MVP Boundary Protection (no endpoint exists today for any of
these — they require their corresponding future Command first, per §2
of this document):

- Suspend / Resume / Archive Organization
- Revoke / Reinstate Membership
- Role management and assignment
- Delegated administration

14_MVP §7 additionally notes: "The absence of a Future API from this
list does not imply removal from the Domain Model; only that its
public REST exposure is deferred" — i.e. a Command could in principle
exist (via a future ADR, §2 above) before its REST Transport is added,
following the same Contract/Transport separation 08_API §1 and
05_Queries §2.1 already establish for MVP.

07_Contracts.md §11 separately names Transport-mechanics items not
tied to any specific future Command. Per §2.2's scoping, these are
governed by their owning document (07_Contracts.md) rather than the
Extension Candidate process in §2, because they introduce no domain
capability, Aggregate, Command, or Event — not because they are exempt
from ADR governance generally. An architectural decision among them
(e.g. choosing a rate-limiting strategy with cross-cutting
consequences) would still require its own ADR under 07_Contracts.md's
own governance, just not the domain-boundary Promotion Test in §2.2:

- Pagination, filtering, or sorting conventions for list-returning
  endpoints
- Rate limiting and throttling policy
- Idempotency-key conventions for POST Commands
- Internal/service-to-service Transport shape for GetPersonById beyond
  its current abstract Contract shape (05_Queries §5.1; 07_Contracts
  §6.5)

---

# 10. Feature Flag Reservations

10_Configuration.md §3 already reserves the following flags, all
`false` by default, all subject to the Step 5 constraint in §2.5:

| Flag | Reserved For | Extension Candidate (this document) |
|---|---|---|
| `feature.mfa.enabled` | Future MFA Credential type | §8 |
| `feature.oauth.enabled` | Future OAuth Credential | §8; §3.1 (Additional Credential Types) |
| `feature.enterprise_sso.enabled` | Future Enterprise SSO | §8 |
| `feature.organization_switching.enabled` | Multi-Organization membership | §3.2 |
| `feature.session_suspension.enabled` | Optional `Suspended` Session state (01_Domain_Model §5) | Not listed above — this state is already modeled in MVP's Session lifecycle, only its reachability is gated |

No flag is currently reserved for Organization/Person/Membership
lifecycle transitions (§5, §9), Device/Service/AI Agent Identity (§3),
or RBAC/Delegation (§5, §8). A Feature Flag is not mandatory for every
candidate — 10_Configuration §3's existing flags exist specifically to
gate operational rollout of a capability behind a runtime toggle; a
candidate's Step 4 (§2.4) ADR MAY introduce a reserved Feature Flag in
10_Configuration.md §3 if operational rollout requires one, following
the same pattern as the flags already reserved there, but is not
required to.

---

# 11. Cross-Document Alignment

This section is deliberately brief — a checklist of ✅ marks adds
little beyond what §1-§10 already demonstrate through direct citation.
Three points are worth stating explicitly because they are not
otherwise obvious from reading §1-§10 in isolation:

- This document discharges 11_Security §13.2's requirement for "the
  process defined in 15_Extensibility.md" through §2 specifically —
  every Future Scope item named in 11_Security §13.2 is catalogued in
  §8.
- §2.2's Promotion Test is the same rule stated once in 09_Persistence
  §7.0, scoped here to domain/consistency-boundary candidates (§3, §4,
  §6, §7) rather than generalized to every extension category — §9's
  Transport-mechanics candidates are explicitly governed by
  07_Contracts.md instead (§2.2, §9).
- §3.1's two open candidates (RefreshToken re-evaluation, Additional
  Credential Types) are reproduced from 03_Aggregates §11.1 without
  deciding their outcome, consistent with that section's own statement
  that neither is proposed as an Aggregate by its current revision.

Every other citation in §2-§10 above (064 §8.16, ADR-0002, ADR-0003,
10_Configuration §3, 02_Use_Cases §4) is a direct, in-place reference
at the point it is used, rather than repeated here.

---

# 12. Change Log

## Version 1.0.1 (2026-07-19)

Correction pass following an architecture review of v1.0.0 (Blueprint
consistency, governance, DDD, traceability, and minimality focus,
scored 9.8/10). No new Aggregates, Commands, Queries, Events, or
Business Rules introduced; no MVP scope changed.

- **§2.2 — rescoped the Promotion Test**: v1.0.0 stated the Promotion
  Test was "extended here as the general test for any extension
  category, not only persistence structures." 09_Persistence §7.0's
  rule is specifically about Aggregate Boundary promotion; applying it
  to categories with no lifecycle or invariant concept at all (e.g.
  rate-limiting strategy, §9) stretched it past its actual scope. §2.2
  now states explicitly that the Promotion Test governs
  domain/consistency-boundary candidates only (§3 Future Aggregates,
  §4 Future Domain/Application Services, §6 Future Domain Events, §7
  Future Persistence Structures); Transport (§9) and Configuration
  (§10) concerns follow their own owning document's governance instead.
- **§9 — resolved the appearance of contradicting §2**: v1.0.0 said
  Transport-mechanics items "do not go through the ADR process in §2,"
  immediately after §2 stated every candidate follows Steps 1-5. Per
  the §2.2 rescoping above, this is not actually an exception to §2 —
  these items were never domain-boundary candidates in the first
  place. Reworded to state they are governed by 07_Contracts.md's own
  process, not exempted from ADR governance in general.
- **§2.1 — removed an unverifiable citation**: "documentation-only
  update (Level 2 per 051§5)" cited a document (051) not in this
  document's Dependencies list and not independently verified in this
  session. Removed rather than added to Dependencies without
  verification, consistent with this Blueprint set's established
  practice of not asserting a citation that has not been checked
  against the actual source document.
- **§6 — removed seven invented illustrative event names**:
  `OrganizationSuspended`, `OrganizationResumed`, `OrganizationArchived`,
  `MembershipRevoked`, `MembershipRoleChanged`, `PersonSuspended`,
  `PersonArchived`, `PersonRestored` were minted by v1.0.0 to
  illustrate a naming convention, despite this document's own stated
  purpose of inventing nothing. Labeling them "illustrative" did not
  prevent them from being concrete enough to later be mistaken for a
  reservation. §6 now states the convention by reference to actual MVP
  event names and 14_MVP §5's own two named future examples only, and
  mints no new name.
- **§10 — softened an implied Feature Flag mandate**: "would need a
  flag added" read as though every future candidate requires a
  reserved Feature Flag. Reworded to state a flag is optional per
  candidate's own Step 4 ADR, matching how 10_Configuration §3's
  existing flags are actually used (operational rollout gating, not a
  universal requirement).
- **§11 — condensed from a seven-subsection checklist to four
  substantive paragraphs**: the original ✅-per-subsection format added
  little beyond what §1-§10's direct citations already state. While
  condensing, also corrected a stale claim carried over from v1.0.0's
  original §2.2 wording — §11.3 said the Promotion Test "generalized
  ... to every extension category," which the §2.2 fix above makes
  inaccurate.
- **Change Log wording**: "Derived an illustrative... naming pattern"
  reworded to "Documented the existing... naming convention," removing
  language that could be read as claiming a new rule was extracted
  rather than an existing pattern described.

## Version 1.0.0 (2026-07-19)

Initial Extensibility Blueprint.

- Established the Extension Governance Process (§2) required by
  11_Security §13.2, built from three already-existing mechanisms
  rather than new ones: 09_Persistence §7.0's Promotion Rule
  (generalized as the Step 2 test), the ADR precedent set by ADR-0002
  and ADR-0003 (Step 3), and 10_Configuration §3's existing Feature
  Flag reservations (Step 5)
- Consolidated Future Aggregates and Identity Types from 03_Aggregates
  §11/§11.1, ADR-0002 §6, and 057 §12 (§3)
- Consolidated Future Domain Services from 14_MVP §6 and
  01_Domain_Model §7 (§4)
- Reproduced the Future Use Case catalog from 02_Use_Cases §4 in full,
  flagging the UC-F08 numbering gap rather than filling it (§5)
- Documented the existing MVP event naming convention
  (`<Entity><PastTense>`) by reference to actual MVP and 14_MVP §5
  examples, since 06_Domain_Events.md itself defines no future event,
  without minting any new event name (§6)
- Consolidated Future Persistence Structures from 09_Persistence
  §7.1-§7.5 (§7)
- Consolidated Future Security Capabilities from 11_Security §13.2 (§8)
- Consolidated Future APIs from 14_MVP §7 and Transport-mechanics
  candidates from 07_Contracts §11 (§9)
- Cross-referenced every Feature Flag already reserved in
  10_Configuration §3 against its corresponding candidate in this
  document (§10)
- Verified alignment against 064, 11_Security, 09_Persistence,
  03_Aggregates, ADR-0002, ADR-0003, 10_Configuration, and
  02_Use_Cases (§11)
- No new Aggregates, Commands, Queries, Events, or Business Rules
  introduced. Every item catalogued above is independently verifiable
  in an upstream document as of this version.

---

**END OF DOCUMENT**
