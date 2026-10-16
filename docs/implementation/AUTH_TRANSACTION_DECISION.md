# Authentication transaction boundary — implementation decision

Date: 2026-10-10. Status: **A selected by Amir (@amirfassadi), project/architecture owner**, in the project conversation on this date. B remains deferred and requires evidence and a separate scoped ADR revision.

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

A is selected. Preserve the accepted boundary. Reconsider B only with evidence about persistence topology or recovery cost and a separate scoped ADR revision. The owner's decision is a design disposition, not a line-by-line C# security audit or full-MVP release acceptance. Existing registration tests do not prove authentication fencing/recovery: that protocol needs its own runtime evidence.

## Implementation work after disposition

1. Record the selected transaction owner, module write authority and exact Platform/Identity source revisions. For B, publish a scoped ADR candidate preserving the registration guard and history before claiming acceptance.
2. Add Session-owned family/generation storage, purpose-bound verifiers and recognition of spent generations until the immutable family deadline. **Grace window: zero**. Recognition retention is not permission to replay, recover a successor or retry a consumed token; authenticated same-client reuse revokes the family even after a lost response. Add Person issuance coordination behind an interface; no sixth domain Aggregate is introduced. See [storage and transaction ownership](AUTH_STORAGE.md).
3. Implement Ready/active-Person/active-Credential login gates, authenticated self read, rotating refresh and logout. Define distinct access and Session deadlines in a versioned development contract. BFF testing uses a clearly identified authenticated test client; no production BFF integration is claimed.
4. Test wrong-client presentations without revoking another family, same-generation races, lost response/reuse, strict idle and absolute boundaries, logout/refresh races, old-password login crossing a fence, and recovery at each durable boundary. No tokens or password/proof material in logs/events.
5. Implement authenticated password change and verified reset with the same guard, then delivery, browser/BFF integration and release evidence. Mobile reset must address reassigned-number risk; contact possession is not silently made sufficient for existing-account takeover.

Upstream context: [co-location owner worksheet](https://github.com/amirfassadi/SmartCorePlatform/blob/724ff849a9ddd6741fc5d84d6b54c366dcb774be/_Copilot_Reports/Identity_ADR0004_Colocation_Evaluation_Questions.md) and [SESSION failure protocol candidate](https://github.com/amirfassadi/SmartCorePlatform/blob/724ff849a9ddd6741fc5d84d6b54c366dcb774be/_Copilot_Reports/Identity_SESSION_Contract_Completion_Candidate.md). These older SESSION drafts retain no-idle text; the latest owner direction above must be propagated coherently rather than rewriting history.

## Owner disposition

**A selected; B may be reconsidered only with evidence and a separate ADR.** Recorded at Amir's explicit instruction, 2026-10-10; no cryptographic signature or independent code audit is asserted. ADR-0004 and existing registration transaction boundaries remain authoritative.

Conditions accepted with this implementation direction:

- Put Person-scoped issuance acquisition/epoch behind an interface shared by login, refresh and sensitive operations.
- Activate a persistent fence only after valid change/reset proof has been verified and bound to the operation. Reset initiation, OTP delivery, unknown accounts and invalid proof cannot activate it or revoke Sessions. Revalidate admission against the current epoch/Credential under the gate before fencing; discard proof from a superseded epoch.
- Reconcile stalled fences through durable scheduled work and restricted operator-visible status. Login/refresh outward failures must not reveal that reset or password replacement is in progress. Timeout alone must never reopen issuance after an uncertain Credential result.
- Sensitive actions (change password, authorized reset completion and future deletion) require online Session status/deadline, current epoch equality and no pending fence. This closes the residual-access window at those endpoints; general self-contained resource access still has its bounded 900-second residual validity. Reset completion may use a separate verified recovery proof instead of an access token but must bind/revalidate its observed epoch before mutation.
