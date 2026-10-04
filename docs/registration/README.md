# Registration-first Blueprint reconciliation

Version: 0.1.1 — Status: DRAFT / generation BLOCKED — 2026-10-04

## Scope and authority

The owner selected registration and usable login as the first delivery milestone; Kimia salon creation, staff, booking and finance follow later. Identity remains reusable: personal Organization and Owner Membership are not the Kimia Business or a staff relationship.

This package reconciles the registration requirements of the uploaded Blueprint with SmartCorePlatform PR #5 at **91375b192dd0ec778529c4c7d8a785c94231269a**, against SmartCoreIdentity main **f72a4fdf9f29edd3970b4bd719bfbf8040089534**. It is a review proposal, not approval to generate or deploy. ADR-0004 v1.1.1 is architecturally Accepted within the owner's signed scope; ADR-0002 v1.7.1 and applicable ADR-0003 decisions remain Proposed. T16 remains open. Narrowing the product milestone does not waive recovery/security/governance requirements or accept a transport-order option.

Source of truth for this review: accepted decisions within their scope, proposed upstream decisions explicitly labelled as such, then this registration reconciliation. The preserved uploaded docs are historical evidence, not a second executable specification. Other registration interpretations in the three repository overview documents yield to this package. This does not choose unresolved Session policy or migrate all unrelated Blueprint details.

## Reading order

1. [Workflow, persistence and API](workflow-contract.md)
2. [Security, validation, policy and test requirements](validation-and-tests.md)
3. [Implementation work plan and completion evidence](implementation-plan.md)
4. [GOV review dossier for ADR-0002/0003](gov-review-dossier.md)
5. [Source review, preserved details and open decisions](reconciliation-review.md)
6. [Byte-preserved uploaded Blueprint](../reference/uploaded-blueprint/README.md)
7. [Source hashes](source-manifest.json)
8. [Current validation notes and evidence limits](capability.validation_notes.md)

## Pinned upstream references

All following links are review inputs from an unmerged platform proposal, not a claim of main acceptance:

- [ADR-0002](https://github.com/amirfassadi/SmartCorePlatform/blob/91375b192dd0ec778529c4c7d8a785c94231269a/SmartCore_Platform_Docs_v1/ADR-0002_Identity_Foundation_Clarifications.md): ownership and Decisions 8–9.
- [ADR-0004](https://github.com/amirfassadi/SmartCorePlatform/blob/91375b192dd0ec778529c4c7d8a785c94231269a/SmartCore_Platform_Docs_v1/ADR-0004_Identity_Credential_Provisioning_Protocol.md): accepted provisioning/recovery architecture.
- [Approval record](https://github.com/amirfassadi/SmartCorePlatform/blob/16b720c9cb9afdd60769dcad7b2c4d8c1e2e983c/_Copilot_Reports/Identity_ADR-0004_Acceptance_Decision_Record.md): approval scope, not runtime verification.
- [08 REST](https://github.com/amirfassadi/SmartCorePlatform/blob/91375b192dd0ec778529c4c7d8a785c94231269a/SmartCore_Platform_Docs_v1/Identity/08_API.md) and [OpenAPI](https://github.com/amirfassadi/SmartCorePlatform/blob/91375b192dd0ec778529c4c7d8a785c94231269a/SmartCore_Platform_Docs_v1/Identity/openapi.yaml): exact proposed wire encodings.
- [07 services](https://github.com/amirfassadi/SmartCorePlatform/blob/91375b192dd0ec778529c4c7d8a785c94231269a/SmartCore_Platform_Docs_v1/Identity/07_Contracts.md) and [service schemas](https://github.com/amirfassadi/SmartCorePlatform/blob/91375b192dd0ec778529c4c7d8a785c94231269a/SmartCore_Platform_Docs_v1/Identity/services.schema.json): internal provisioning/administration.
- [Event schemas](https://github.com/amirfassadi/SmartCorePlatform/blob/91375b192dd0ec778529c4c7d8a785c94231269a/SmartCore_Platform_Docs_v1/Identity/events.schema.json) and [machine index](https://github.com/amirfassadi/SmartCorePlatform/blob/91375b192dd0ec778529c4c7d8a785c94231269a/SmartCore_Platform_Docs_v1/Identity/capability.machine.yaml): Draft, not an independent approval source.

Use those pinned schema references rather than introducing another divergent schema copy here. The uploaded underscore-named capability_machine.yaml is archived; it is not interchangeable with the dot-named platform index or silently converted. A consolidated active 00–16/schema generation package remains a separate integration gate. Before acceptance revalidate the decision candidate SHA, and reconcile the final merged SHA after merge.

## Delivery exit gates

- [ ] Required upstream decisions and T16 disposition recorded; no inferred acceptance.
- [ ] Registration contract and concrete encodings reviewed across repositories.
- [ ] Session and security-policy conflicts in reconciliation-review resolved.
- [ ] Full active Blueprint/machine package reconciled and applicable 065 validation completed.
- [ ] Implementation and required failure/race/security tests exist and pass.
- [ ] Delivery/KMS/database/service trust, operator recovery and finite operational values configured and reviewed.
- [ ] Kimia client completes verification → Pending/Ready → explicit login → self read → logout without a duplicate identity store.

No implementation or running service is delivered by this documentation change.

## Integrated review follow-up — 2026-10-04

[Latest Platform review status](platform-review-status.md) pins the unaccepted integrated candidate and remaining code-entry conditions. Older pins above preserve historical review provenance. Select final authoritative revisions and complete all four applicable 064 gates plus 065 quality/governance evidence before implementation; no archived readiness claim or latest proposal pin grants permission.
