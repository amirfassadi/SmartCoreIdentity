# Identity capability — validation notes

Version: 0.1.0
Date: 2026-09-28
Status: DRAFT — generation BLOCKED
Scope: Registration-first documentary reconciliation; not a full Blueprint validation certificate.

## 1. Provenance and authority

This is a newly authored companion requested by the repository owner after confirming that the original capability.validation_notes.md is unavailable. It is NOT the missing historical report and does not reconstruct earlier reviewer findings or certify the uploaded YAML's PASS claims. Its version starts at 0.1.0 independently of capability_machine.yaml v1.0.7.

Review baseline: SmartCoreIdentity PR #1 at `f96f11fcdb50663a6dc00d2bfb5bf884c491f070`; platform input: SmartCorePlatform PR #5 at `91375b192dd0ec778529c4c7d8a785c94231269a`. These are unmerged proposal snapshots. Source bytes and hashes are recorded in [source-manifest.json](source-manifest.json). Before acceptance, revalidate against the candidate commit; after merge, record the final merged source references.

The [registration index](README.md) defines authority and links pinned ADRs, approval records and wire contracts. ADR-0004 architectural acceptance is scoped; ADR-0002, applicable ADR-0003 and T16 remain open in this baseline. This note grants no acceptance or generation readiness. The author of this note also prepared the reconciliation package; this is not independent review.

The archived YAML names a sibling companion. A newly added pointer at that location resolves the reference to this note while explicitly retaining the historical evidence gap. All 20 imported source files remain byte-identical. Neither the pointer nor this note makes the archived machine specification active.

## 2. Interpretation of validation status

| Dimension | Current evidence and disposition |
|---|---|
| Historical structural/semantic/architectural PASS | Claimed by archived YAML; supporting original report unavailable; not re-certified here |
| Historical ai_readiness / machine_consumability PASS | Historical specification-quality claim, never generation approval |
| Source integrity and local document checks | Limited reproducible checks described in §5; do not validate behavior |
| Registration reconciliation | Draft contract and requirement review exists; open conflicts in §4 prevent a complete consistency claim |
| Complete 00–16 / machine / 065 validation | Not completed by this work |
| Governance | Blocked by outstanding decisions; ADR-0004 acceptance does not waive them |
| Runtime, race, security and deployment tests | Required, not implemented or passed by this documentation work |
| Generation readiness | BLOCKED |

Unknown or untested means unverified, not PASS. Historical checkmarks must not be propagated into a new validation result without reproducible evidence.

## 3. Registration correction rationale and traceability

Detailed source-section dispositions are in [reconciliation-review.md](reconciliation-review.md); the replacement proposal is [workflow-contract.md](workflow-contract.md).

| Topic | Archived source | Current registration rationale | Required verification |
|---|---|---|---|
| Contact and initiation | 01 Person; 04 §4.1; 07/08 registration | ADR-0002 Decision 9 proposes one verified email OR mobile, no ownership before proof, no early existence disclosure | R01–R04 |
| Atomic ownership | 04 §5.1; 09 §§3–8 | Decision 8 adds workflow, material-reference transfer and Outbox to ownership commit; repositories do not independently commit | R05–R07 |
| Pending versus authentication | 02 UC-003/004; 04 §4.2 | Active Credential can precede Ready; login requires both Ready and current active Credential | R08, R19 |
| Retry and conflicting attempts | 04 §9.2–9.3; 08 §3 | Bounded authorized replay; expired proof cannot authorize lookup or overwrite an existing registration's password | R05, R06, R10, R20 |
| Credential winner and acknowledgment | 09 recovery clauses | ADR-0004 C01–C03 and acknowledgment guard prevent loser replacement and stale confirmation; timeout is not permission to unlock | R09, R11–R14 |
| Administrative recovery | 11 MFA/recovery boundaries | ADR-0004's two restricted internal actions require scoped authorization, operator step-up and durable audit | R15–R17 |
| Registration event | 06 §4.1/4.2/5.4; 16 examples | Ready OccurredAt, separate OwnershipCommittedAt, optional Email, no SessionReference, actual Ready actor; no new event type | R18 |
| Public API and examples | 07 §5.1–5.2; 08 §3; 16 §2/12.1 | Multi-step proposed responses and separate login replace immediate complete DTO/Session assumptions; exact encodings stay pinned upstream | R01, R02, R10, R19 |
| Validation taxonomy | 12 §§2–4 | Preserve Domain/Policy/Infrastructure checkpoints; application pre-checks do not enforce storage concurrency | R04–R07, R09, R11 |
| Event ordering | 06 ordering; platform T16 | Causal commit order does not establish publication/delivery order; neither ordering option is accepted here | T16-dependent tests remain open |

R01–R20 are specifications in [validation-and-tests.md](validation-and-tests.md), not execution evidence. Legacy v1.0.0–v1.0.7 correction history can be inspected in the archived YAML's own notes; this table does not authenticate that history or recreate its missing citations.

## 4. Outstanding decisions and closure evidence

| Gate | Required closure evidence | State |
|---|---|---|
| GOV | Explicit applicable ADR approvals with attributable scope and commit references | Open |
| T16 | Accepted ordering/consumer contract and retry/redelivery disposition; consumer/workload evidence | Open |
| SESSION | Security-reviewed rotation, refresh and absolute/sliding lifetime policy, propagated consistently | Open |
| POLICY | Approved password/KDF/TTL/retry settings with benchmark and operational rationale | Open |
| MATERIAL | Concrete protected staging, transfer, access and bounded cleanup design plus race/security evidence | Open |
| CONSOLIDATION | Reconciled active 00–16 and machine/API/schema package; applicable 065 results | Open |
| DEPLOYMENT | Provider, database, KMS/service trust, operator grants, finite budgets and tested runbooks | Open |
| BEHAVIOR | Implemented R01–R20 plus resolved Session/T16-specific tests with reproducible results | Open |
| HISTORICAL EVIDENCE | Original validation report or independent revalidation of relevant old claims | Unavailable; not repaired by this new note |

The owner is the decision authority; adding a gate or this note is not the owner's approval of a technical choice. Follow the review register for the concrete competing policy values. Do not silently choose the newest or shortest source.

## 5. Reproducible limited checks

Run from the repository root:

```sh
python3 tools/check_registration_docs.py
```

This checks the 20 source files against recorded byte hashes, current Markdown fence balance, local relative-link targets, archival non-normative status and the generation-blocked manifest. It does not check remote anchors, render appearance, full YAML/schema consistency, 065 compliance, implementations or security behavior. The new archival pointer is checked as a current companion, not counted as an original source.

For this revision, the command passed on 2026-09-28 after adding this note and its links. Re-run it for later changes; a previous pass is not evidence for a different revision. No CI/Actions or runtime result is asserted here.

## 6. Change history

| Version | Date | Change |
|---|---|---|
| 0.1.0 | 2026-09-28 | Create new registration-scoped validation notes and explicit historical-report gap; no old PASS claim adopted |
