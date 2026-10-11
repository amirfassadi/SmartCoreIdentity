# Identity review stack — 2026-10-11

PR #3 remains an integration reference. Merge the smaller Draft PRs in dependency
order; do not also merge #3 or its registration-backend base as duplicate work.
No PR has been merged by this task.

Platform [PR #11](https://github.com/amirfassadi/SmartCorePlatform/pull/11), schema
1.2.2, must land first and Identity provenance must be verified against the canonical
Platform revision before release. Existing Identity documentation/preparation
PRs #1 and #2 precede the implementation stack below.

| Draft PR | Branch | Base | Scope | Fresh native CI |
| --- | --- | --- | --- | --- |
| [#4](https://github.com/amirfassadi/SmartCoreIdentity/pull/4) | review/identity-registration | implementation/backend-preparation | Verified-contact registration, provisioning/setup recovery, lock order | [38127605864](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38127605864): 37 checks, 33 events, 2 HTTP smokes |
| [#5](https://github.com/amirfassadi/SmartCoreIdentity/pull/5) | review/identity-authentication | review/identity-registration | Issuance gate, sessions, login/refresh/logout, password-change recovery, admission | [38127608395](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38127608395): 123 checks, 108 events, 4 HTTP smokes |
| [#6](https://github.com/amirfassadi/SmartCoreIdentity/pull/6) | review/identity-password-reset | review/identity-authentication | Recovery enrollment, two-factor reset, timing and one-hour cooldown | [38127611188](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38127611188): 199 checks, 188 events, 5 HTTP smokes |
| [#7](https://github.com/amirfassadi/SmartCoreIdentity/pull/7) | review/identity-bff | review/identity-password-reset | BFF core, journal/circuit, crash recovery, logout maintenance and telemetry | [38127635293](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38127635293): 303 checks, 104 BFF checks, 327 events, 5 HTTP smokes |

All four native runs and their documentation checks completed successfully. These
are cumulative stage counts, not counts of changes added by each PR. Fresh #5
reports 123; the earlier snapshot report of 122 is superseded by this execution.
The BFF count is the 104 actual `PASS: BFF` lines in its completed job log; prior
reporting of 100 at the preceding runtime overstated that count by one (99).

## Snapshot and ancestry proof

The stages use newly created commits with dependency parents and exact reviewed
historical trees. They are snapshot extractions, not preserved original commit
history. Each stage can build independently; after merging a predecessor, retarget
its successor to the canonical branch, resolve any ancestry conflicts and rerun CI.

| Stage | Verified runtime commit | Tree |
| --- | --- | --- |
| Registration | 45211193e1bdf3aed7ad10b5ae0ee9a40ec6e147 | bc5bbbd3ae57a82b891f6a7d6a7eb1c6b6ab4f98 |
| Authentication | 110eb7286fc01946a3c2ef28ae6b9300735e0393 | 436a54abfc249139b6959a6b7e1460926737353f |
| Reset | dd477ced8e022f8505e82aaa10cb1382bc21fddf | 06f4ef81106d76c6d06309da8d39fa30c222f731 |
| BFF | f1127696db7726f52a0ab598d1b77101b55f0ae7 | 5ff4a12c89874b2f6225811cb4428e2bd1893c51 |
| Integration reference | 27a12b4c2f797736f15fd84969129e051b7faaf7 | 5ff4a12c89874b2f6225811cb4428e2bd1893c51 |

Registration's parent is 0cec3994077b63990a9d0c2d69656e1294e5e33c (PR #2).
Each following stage's parent is the previous stage's verified runtime commit.
The identical final BFF/integration trees prove that extraction omitted no files.
Subsequent documentary/contract commits add this manifest, candidate browser
contract and verification evidence to both final branches with the same complete
tree; runtime evidence
above remains tied to the explicitly listed runtime revisions.

The single current release checklist and SMS ambiguity/abuse requirements are in
[RELEASE_GATES.md](RELEASE_GATES.md). Product stages A–G and demonstrable completion
criteria are in [PRODUCT_ROADMAP.md](PRODUCT_ROADMAP.md). Core tests do not certify
these future release gates. Country, SMS provider, framework and domain strategy
remain open; these documents add no new runtime/browser CI evidence.

The B1 browser contract (Draft 0.1.0) now exists before host code. Its OpenAPI/schema
checks are distinct from the historical C# runtime evidence above. The host,
generated client and Playwright acceptance remain open.
