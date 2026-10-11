# Authentication transaction boundary — implementation decision

Date: 2026-10-10. Status: prepared for owner disposition; neither alternative is newly accepted by this document.

Registration/setup is implemented and native-tested at `c49e3ee776565c2e2e9adcae2e47ae417c67cf8e`. Credential, Ready and acknowledgment still commit separately. Preserve the immutable initial winner, pre-Ready mutation guard, distinct ownership/Ready timestamps and recovery evidence under either authentication alternative.

The next slice is login, authenticated self read, refresh and logout. Its writers must share the eventual serialization mechanism for password change and reset; otherwise a login checked against an old password or a racing refresh can create a usable Session after all-Session revocation. Choosing this mechanism now avoids implementing a disposable Session write path.

## Existing owner directions

- Access lifetime at most 900 seconds; immutable Session deadline 86400 seconds from login. Successful refresh never extends the absolute deadline.
- S2 refresh rotation, strict consumed-token reuse closes the family/Session, including a lost-response retry. Refresh secrets belong in the server-side BFF, not browser JavaScript.
- Latest idle direction: 30 minutes since successful foreground refresh; login initializes that timestamp. This supersedes the older no-idle draft. It measures server-observed foreground refresh, not actual user attention. A trusted BFF assertion and final wire contract are still required.
- Password change and reset close all Sessions and refresh families. Existing self-contained access can remain usable until its bounded expiry unless an endpoint explicitly checks current state.
- Reset belongs in MVP before real-user registration. VPS and original local source remain untouched.

## Concrete alternatives

| Item | A — retain separate authoritative commits | B — adopt one transaction for password/Session mutation |
|---|---|---|
| Authority | Preserve current ADR-0004 boundary; complete its authentication coordination design | Owner explicitly selects a scoped D7/ADR revision before implementing the changed boundary |
| Process/modules | Current host can contain separate Identity/Session and Credential adapters; no cross-module atomicity is assumed | Identity application is the single transaction coordinator; Credential module owns Credential writes and Session module owns Session/family writes through the shared transaction |
| Login/refresh serialization | Person-scoped durable issuance fence/epoch in the Session store; check under the same guard used to close all Sessions | Person-scoped database guard in the shared transaction; re-read eligible Person, Ready and current Credential under that guard before issuance |
| Password change/reset | Persist uniquely bound intent; fence issuance, advance epoch and close families; independently commit idempotent Credential replacement; reconcile result; release fence only after durable confirmation | In one local PostgreSQL transaction replace Credential, close every Session/family, record bound operation outcome and audit/outbox facts |
| Crash/uncertain result | Leave issuance blocked while unresolved; worker resumes the same operation without lowering epoch or restoring Sessions | Database rollback or committed outcome; retry/query the same bound operation, never repeat an uncertain mutation blindly |
| Registration | Keep current ownership, Credential, Ready and acknowledgment transactions | Keep current registration transactions for this scoped alternative; combining Credential and Ready would require a separate explicit invariant review |
| Trade-off | More durable phases and recovery tests; preserves separation if stores split later | Fewer partial-commit phases for password mutation; couples these modules to one transaction owner and requires migration design before later store separation |

Sharing a PostgreSQL server alone is neither alternative B nor a guarantee that independent commits are atomic. Neither alternative uses 2PC. Operator recovery and production service authorization remain separate deliverables.

A is the continuity recommendation: preserve the accepted boundary unless the owner deliberately selects B and its scoped architectural revision. This recommendation is not a recorded owner acceptance.

## Implementation work after disposition

1. Record the selected transaction owner, module write authority and exact Platform/Identity source revisions. For B, publish a scoped ADR candidate preserving the registration guard and history before claiming acceptance.
2. Add Session-owned family/generation storage, purpose-bound verifiers and bounded predecessor recognition. Add the Person issuance coordination required by the selected alternative; no sixth domain Aggregate is introduced.
3. Implement Ready/active-Person/active-Credential login gates, authenticated self read, rotating refresh and logout. Define distinct access and Session deadlines in a versioned development contract. BFF testing uses a clearly identified authenticated test client; no production BFF integration is claimed.
4. Test wrong-client presentations without revoking another family, same-generation races, lost response/reuse, strict idle and absolute boundaries, logout/refresh races, old-password login crossing a fence, and recovery at each durable boundary. No tokens or password/proof material in logs/events.
5. Implement authenticated password change and verified reset with the same guard, then delivery, browser/BFF integration and release evidence. Mobile reset must address reassigned-number risk; contact possession is not silently made sufficient for existing-account takeover.

Upstream context: [co-location owner worksheet](https://github.com/amirfassadi/SmartCorePlatform/blob/724ff849a9ddd6741fc5d84d6b54c366dcb774be/_Copilot_Reports/Identity_ADR0004_Colocation_Evaluation_Questions.md) and [SESSION failure protocol candidate](https://github.com/amirfassadi/SmartCorePlatform/blob/724ff849a9ddd6741fc5d84d6b54c366dcb774be/_Copilot_Reports/Identity_SESSION_Contract_Completion_Candidate.md). These older SESSION drafts retain no-idle text; the latest owner direction above must be propagated coherently rather than rewriting history.

## Owner disposition

Pending: A or B. No response, elapsed time or generic implementation authorization is recorded as an acceptance of B. Until explicit disposition, ADR-0004 and the existing registration transaction boundaries remain authoritative.
