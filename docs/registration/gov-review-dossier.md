# GOV review dossier — ADR-0002 and ADR-0003

Version: 0.3.5 — 2026-09-29 — DRAFT / decision pending

This is a preparation record in SmartCoreIdentity PR #1. The authoritative ADRs and any approval record belong to SmartCorePlatform. This file does not approve, amend, sign or mark either ADR Accepted.

## Pinned review inputs

- SmartCorePlatform `docs/identity-blueprint-completion`, ADR-0002 v1.7.1, blob `3c4a3ac44d605d2cc8f3ffca6de00387fde629fa` ([source](https://github.com/amirfassadi/SmartCorePlatform/blob/docs/identity-blueprint-completion/SmartCore_Platform_Docs_v1/ADR-0002_Identity_Foundation_Clarifications.md)).
- Same branch, ADR-0003 v1.2.1, blob `7eea075bcc23ca90e82b79b0b18246410fde3a18` ([source](https://github.com/amirfassadi/SmartCorePlatform/blob/docs/identity-blueprint-completion/SmartCore_Platform_Docs_v1/ADR-0003_Organization_and_Membership_Lifecycle_Standardization.md)).
- Same branch, normative 051 v1.0, blob `828ea8fb7010015858cacd849b05f8cc074bafbf` ([source](https://github.com/amirfassadi/SmartCorePlatform/blob/docs/identity-blueprint-completion/SmartCore_Platform_Docs_v1/051_SmartCore_Governance_and_Decision_Model.md)).
- ADR-0004 v1.1.1 separately records accepted architectural scope and explicitly leaves ADR-0002/0003 Proposed. Its approval must not be reused as their approval.

Blob SHA identifies file content, not the complete review commit. Before a platform decision, pin the exact candidate commit and review any intervening changes. After merge, record the accepted source commit separately.

## Proposed decision scope for review

| ADR | Relevant decisions for registration | Review conclusion requested |
|---|---|---|
| ADR-0002 | 1 atomic ownership triple; 2 authentication/business-authorization boundary; 3 Owner Membership role; 4 Person-centric v1.x; 5 event ownership and LoginFailed Security Event; 7 RegisterPerson-only Application Service exception; 8 post-commit PendingCredential/Ready and recovery; 9 verified mobile OR email, DisplayName and password | Accept/revise/defer each applicable decision with explicit scope. Decision 6 is future-identity documentation, not a requirement to implement those identities now. Preserve ADR-0004's earlier winner point and acknowledgment protocol. |
| ADR-0003 | Organization and Membership lifecycle models; direct Active creation for MVP, transition Commands deferred | Accept/revise/defer lifecycle scope and direct-Active initialization; do not infer suspend/archive/revoke endpoints. |

An acceptance of only some decisions needs a clear version/scope and a consistent status representation in the platform ADRs; a blanket Accepted label must not conceal unreviewed clauses. The Identity docs must subsequently reference the exact accepted scope and revision.

## Evidence still required by the ADRs

| Evidence | Current dossier result |
|---|---|
| Related 026/027/057/059 and Identity 00–16 narrative/machine/API/service/event synchronization, version references and consumer compatibility | Integrated platform proposal exists; independent architecture/consumer review and merge verification not evidenced here. |
| ADR-0002 verified contact, non-enumeration, proof/replay/material expiry, uniqueness, Outbox and Pending/Ready scenarios | Proposed contracts and R01–R20 test plan exist; runtime/race/security results not evidenced. |
| PersonRegistered Ready timing, OwnershipCommittedAt, optional Email, no initial SessionReference and actual actor; LoginFailed classification | Proposed contracts exist; consumer migration and application tests remain open. |
| ADR-0003 transition terminology across 057, 059, 01, 03 and 14; direct Active MVP semantics | Proposed wording exists; architecture review and structural validation result not evidenced. |
| Architecture Validation Review and Structural Validation under 065 | No complete passing result supplied by this Identity package. |
| Attributable approval, date, scope and immutable platform revision under 051 | Pending owner decision; no signature or acceptance recorded by this file. |

## Governance sequencing question to resolve explicitly

051 §7 orders Approval before Implementation. The acceptance criteria in ADR-0002 also say some proof/replay cases must be *tested* before changing its status, while Identity/13 describes runtime tests requiring an implementation. The architecture owner must distinguish an architectural review/approval gate from later implementation verification, or state what pre-approval evidence satisfies those criteria. Do not silently mark runtime tests passed, change ADR status, or assume this dossier resolves the tension. ADR-0004's scoped acceptance record is a precedent for making approval scope and later verification obligations explicit, not automatic authorization to apply that treatment to ADR-0002/0003.

## Proposed sequencing disposition — for owner review, not adopted

A concrete platform amendment is now [draft PR #6](https://github.com/amirfassadi/SmartCorePlatform/pull/6), ADR-0002 v1.8.0 at commit `ed16fff8b01fc4c7139d9837c154992858ae92c4`, targeting the integrated documentation branch. It is a proposal, not an accepted source. The pinned v1.7.1 review input above remains the current baseline until the owner reviews and approves the amendment; recheck its final commit and criteria before any GOV closure.

ADR-0002's current Acceptance Criteria say the ADR SHALL remain Proposed until specified proof, replay and security cases are tested. The following language is a proposed amendment to that ADR's criteria under 051 §9, followed by an attributable architecture approval record. Merely inserting it into this Identity dossier or an external decision record cannot override the current platform ADR.

> For ADR-0002, architectural approval may rely on reviewed design, synchronized narrative/machine/API/event contracts, architecture validation and applicable structural validation. Runtime, security, failure and concurrency test requirements remain mandatory post-approval implementation and verification gates, with reproducible results required before implementation acceptance, generation readiness where applicable, or release. Architectural approval does not assert that these tests have run or waive them. No implementation precedes architectural approval under 051 §7. The approval record SHALL enumerate the exact accepted decision scope, outstanding verification obligations, owner, source revision and effect on dependent documents.

If the owner chooses this sequencing, the ADR-0002 Acceptance Criteria must be revised and versioned in SmartCorePlatform first (or as one reviewable approval change), expressly moving each test-dependent checkbox to a separately tracked implementation/verification gate. The architecture/structural review and document-consistency criteria remain prerequisites to architectural acceptance. Record the reviewed platform commit and the actual test evidence later; do not replace checkboxes with unsupported PASS claims.

ADR-0003's Acceptance Criteria contain document synchronization, Architecture Validation Review, Structural Validation and dependent references. They do **not** contain a comparable requirement to run proof/replay/runtime tests before changing status. Review ADR-0003 on its own stated criteria. The above ADR-0002 amendment must not be copied to ADR-0003 as an invented prerequisite. Its lifecycle implementation still needs later verification, but that is separate from its current acceptance wording.

T16 and SESSION remain independent open decisions. Even an accepted GOV architecture does not automatically make the complete Blueprint READY_FOR_GENERATION.

## Review-ready decision template — DRAFT IN PROGRESS (not signed)

Draft prepared: 2026-09-29 (Asia/Tehran). This is a preparation checklist, not owner approval or an amendment to a Platform ADR.

- **Candidate platform commit:** PENDING. Pin one integrated candidate after disposition of platform PRs [#6](https://github.com/amirfassadi/SmartCorePlatform/pull/6), [#7](https://github.com/amirfassadi/SmartCorePlatform/pull/7), [#8](https://github.com/amirfassadi/SmartCorePlatform/pull/8), and the separately reviewed [SESSION draft PR #9](https://github.com/amirfassadi/SmartCorePlatform/pull/9). The current individual PR heads are not a single accepted baseline.
- **Decision authority and capacity:** Amir (`@amirfassadi`), project/architecture owner under 051 §7; this field identifies the proposed authority and does not constitute a signature.
- **ADR-0002 decisions accepted / revised / deferred:** PENDING individual review of 1, 2, 3, 4, 5, 6, 7 (including §7.1), 8 and 9. Decision 6 describes future identity types but does not require their MVP implementation. Record a disposition for it if accepting the entire ADR.
- **ADR-0003 lifecycle scope accepted / revised / deferred:** PENDING individual review of Decision 1 (Organization) and Decision 2 (Membership).
- **Evidence reviewed:** No verified event-subscriber inventory yet. Platform-wide dependence on Identity is a design premise, not a count of subscribers. Inventory actual/planned subscribers by name, owner, latest-state versus every-transition need, replay and version requirements; unknown is not zero. Architecture Validation Review and applicable Structural Validation under 065 §5 remain unperformed/unrecorded. The platform's limited package script checks multiple document/schema fixtures but is not the full 065 validator; no result for the final candidate is recorded here.
- **Treatment of pre-approval tests versus post-approval runtime verification:** PR #6 proposes separation, but remains Draft/Proposed. Making an accepted T16 and SESSION policy prerequisite to ADR-0002's architecture gate would itself require an explicit reviewable gate change in the Platform ADR; this dossier does not impose or approve it.
- **Remaining conditions and responsible owner:**

| Gate | Current position | Accountable decision owner / execution owner |
|---|---|---|
| T16 | Open. A means a defined per-Person publication **and consumer-application** ordering contract; B means version-aware unordered consumption. PR #8 explores shared consumer rules across both; buffer/lookup are supporting mechanisms, not a substitute for the event contract. No option selected. | Architecture decision: Amir; producer, consumer-library and operational execution owners: to assign in the accepted record. |
| SESSION | Open. [Draft PR #9](https://github.com/amirfassadi/SmartCorePlatform/pull/9) compares the historical and platform policy; the owner selected a Kimia BFF direction on 2026-09-29 (implementation/topology unverified); establish its trust boundary and threat model and review the owner-selected S2 direction (refresh rotation with fixed absolute Session cap, no sliding extension). The owner selected 900-second access and 86400-second absolute Session values for review; [S2 propagation map](https://github.com/amirfassadi/SmartCorePlatform/blob/docs/identity-session-policy-review/_Copilot_Reports/Identity_SESSION_S2_Propagation_Map.md) tracks the required contract changes. The owner also selected no separate idle timeout, bounded access-token validity to its 900-second expiry after logout/revocation, and all-Session closure on successful password change; see the [security checklist](https://github.com/amirfassadi/SmartCorePlatform/blob/docs/identity-session-policy-review/_Copilot_Reports/Identity_SESSION_S2_Security_Review_Checklist.md). Race/reuse implementation, cross-store consistency, compatibility and security acceptance remain open. For a public OAuth client, refresh tokens require rotation or sender constraint; do not generalize that rule to an unspecified protocol. | Security/architecture decision: Amir; implementation and operations owners: to assign. |
| POLICY / MATERIAL | Open. Approve policy values and protected material staging, transfer and disposal design with required evidence. | Architecture/security decision: Amir; execution owner: to assign. |
| CONSOLIDATION | Open. Reconcile active Blueprint, API, service/event schemas and machine package against one candidate; run applicable 065 validation. | Architecture decision: Amir; validation executor: to assign. |
| DEPLOYMENT / BEHAVIOR | Open. Configure services, keys, delivery, audit and operator recovery; implement and run required failure/security/race tests after architectural approval. | Operations/verification accountable owner: to assign explicitly; Amir retains decision authority until delegation is recorded. |

- **Approval/effective date and attributable record:** PENDING; do not backdate to draft preparation. Record only after decision, scope and required architecture evidence are actually reviewed.
- **Platform ADR/document changes and accepted commit:** PENDING integrated review and recorded decision.
- **SmartCoreIdentity contract/source-pin update commit:** PENDING final platform decision and reconciliation.

## Fastest reviewable path to Slice 0

1. Review PR #6's acceptance-sequencing amendment and #7's 059 correction independently, then assemble one candidate with exact SHAs. PR #8 is T16 comparison work, not a selected policy.
2. Inventory known and planned event subscribers; if none can be verified, record **unknown**, identify who can verify subscriptions, and choose a platform event contract on stated semantics and forecast rather than a fictitious zero.
3. Review T16 and SESSION draft comparisons with explicit alternatives, guarantees, threat model, owner and propagation list. If their acceptance is to be a GOV prerequisite, amend the Platform architecture gate explicitly before approving ADR-0002.
4. Perform architecture review and applicable 065 structural checks on the exact integrated candidate; attach command/output or report and unresolved findings. A limited package-script result may be attached separately but cannot substitute for 065.
5. Record an attributable Platform decision with each ADR decision's disposition. Then update Identity source pins and validation notes; implementation/generation readiness remains a later gate.

The sequence does not require runtime tests before architecture approval if the proposed #6 amendment is adopted. It does require the accepted architecture and contracts to be coherent before implementation under 051 §7.
