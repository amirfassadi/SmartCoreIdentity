# Registration workflow and boundary contract

Version: 0.1.0 — DRAFT. Authority and pinned sources: [index](README.md).

## 1. Input and pre-commit verification

The proposed MVP encoding accepts exactly one email OR E.164 mobile, password and DisplayName. ADR-0002 permits broader future contact handling; exactly-one is the current Blueprint choice, not a newly accepted architectural restriction. PersonId remains identity. Optional email/mobile properties are omitted, not null. DisplayName changes are the proposed profile mutation; unverified contact change is not introduced through PATCH /me.

Normalize contact consistently for lookup, verification, rate limits and storage uniqueness. Create an opaque verificationSessionId, absolute expiry, cumulative attempt/resend budget and request binding. No Person, Organization, Membership or active Credential is created by initiation. Store only protected provisioning material and keyed proof/binding/request verifiers; never durable plaintext password, raw code or an unkeyed password/OTP digest. The ID and Idempotency-Key alone are not authorization.

Initial and resend responses must not expose contact existence through status/body/timing/delivery/throttling. Server-side uniqueness is authoritative at commit, not an early public duplicate check. Resend rotates an unconsumed code without extending expiry/resetting attempts; consumed verification cannot be reopened by resend.

## 2. Atomic ownership boundary

After valid current contact proof and request binding, RegistrationApplicationService commits in one Unit of Work:

- Person Active with selected verified contact and DisplayName;
- Personal Organization Active and Owner Membership Active;
- registrationId/PersonId-bound RegistrationWorkflow in PendingCredential;
- consumption and verificationSessionId → registrationId replay mapping;
- durable transfer of protected material-reference ownership to registrationId;
- Credential provisioning Outbox work and ownership-event Outbox entries.

The three Aggregate repositories do not independently commit. Failure rolls back the ownership triple and all associated workflow/bind/Outbox writes. Already staged material remains under pre-commit expiry/disposal; rollback must not leave a registration-owned orphan. Physical secret movement need not be a distributed transaction, but its reference/owner protocol must never commit a dangling reference. Cleanup of a consumed verification session cannot delete material already transferred to registration.

This is only the RegisterPerson multi-Aggregate exception; supporting records do not create a sixth Aggregate or a general exception for other commands. Uniqueness guards for canonical contacts and concurrency-safe challenge consumption prevent duplicate ownership under parallel verification.

## 3. Replay, uncertain results and new conflicting attempts

Within the original proof validity, the same successful code and binding may read the current existing registration outcome under the cumulative attempt limits. Mutation consumption and bounded replay retention are separate. Retain keyed low-entropy proof verifier only until replay expiry; purpose/session-domain-separated HMAC keys are server-controlled. Changed bound request is rejected/audited. An unknown network outcome is not proof of rollback; retry the same identity/proof, not another ownership creation.

After expiry, proof replay fails safely. A newly verified conflicting attempt cannot create another Person. If the existing registration is PendingCredential, direct the verified holder to the distinct setup challenge; contact possession or registrationId alone is insufficient to set a password. Immediately invalidate and dispose of the second attempt's staged password under that attempt's cleanup bound. It is never bound to or substituted into the existing registration. For an already Ready registration, direct to sign-in; this protocol is not forgotten-password recovery.

## 4. Credential winner and readiness

The authenticated EnsureInitialCredential/GetInitialCredentialResult protocol reconciles durable results by registrationId with a distinct setup operation key when used. Credential storage atomically enforces C01 (one active Credential), C02 (immutable initial winner at Credential commit) and C03 (all supported mutations consult the durable pre-Ready guard). A pre-check or process-local lock is insufficient.

An active Credential may exist while Identity still says PendingCredential. Login and Session issuance remain denied until BOTH Ready and a current active Credential hold. Confirm guarded active winner/person/registration/provisioningVersion evidence outside the Identity transaction. Historical AlreadyCompleted is not current-active evidence; PendingCredential plus an acknowledged historical result is an integrity conflict.

Identity then atomically CASes PendingCredential → Ready, persists ReadyAt, immutable ReadyFactId and winner evidence, and enqueues one logical PersonRegistered plus acknowledgment Outbox work. Stable EventId/unique registration event binding prevents duplicate logical events; at-least-once delivery may still duplicate messages. No Person Aggregate lifecycle transition is fabricated. A concurrent loser observes the existing result.

AcknowledgeRegistrationReady validates committed Ready fact and all winner/generation bindings at Credential, durably records acknowledgment, and releases ProvisionedAwaitingReady → ReadyAcknowledged. Matching stored acknowledgment is checked before current Credential state so replay after a later password change cannot restore an old password. Timeouts never release the guard. Ready can permit login while acknowledgment is delayed; ChangePassword remains retryably blocked with no mutation/event until release.

## 5. Recovery and disposal

Finite provisioning attempts/backoff preserve ownership and PendingCredential on exhaustion, flag recovery-needed and alert. Separate setup proof is short-lived, purpose/registration/request-bound and consumed once for mutation; bounded identical authorized replay returns the recorded result. Setup never overwrites the early winner. Completion reports CandidateSelected, ExistingWinner or pending Undetermined, and must not claim the most recently submitted password necessarily won. Unused candidate material is invalidated/disposed; retry cannot extend secret or challenge lifetime.

Accepted administrative design has only InvalidatePreCommitAttempt and ReconcileCommittedRegistration. Operators cannot supply a replacement password, fabricate Ready, cancel committed ownership or bypass guards. Pre-commit invalidation races on the same consumption/material-owner guard; AlreadyCommitted must preserve transferred material. Reconciliation reuses canonical Ready/ack operations; missing winner/material yields normal-provisioning or user-setup guidance, inconsistent evidence yields IntegrityConflict.

Admission requires current operator MFA/step-up and action/target/environment grant, a server-bound expiring permit, immutable request identity and bounded durable job. Recheck authority before new effectful dispatch; retries do not extend budgets/deadlines. Each local effect and its audit journal/Outbox commit together, including Credential-side acknowledgment evidence. No durable audit means no mutation. External backlog has finite approved bounds; operator roles cannot erase evidence. No automatic rollback of already committed normal work follows operator expiry. See pinned ADR-0004 §4 for exact outcomes and support requirements.

## 6. Proposed public wire flow

These are protocol operations of the existing RegisterPerson command, not new business Commands. Exact shapes/errors are the pinned OpenAPI and 08, not the archived 07 examples.

| Operation | Result and meaning |
|---|---|
| POST /auth/register | 202 verificationSessionId/expiresAt/AwaitingVerification; Idempotency-Key and bindingSecret required; no account-success claim |
| POST /auth/register/resend | 202 generic Accepted; no expiry or attempt reset |
| POST /auth/register/verify | 201 RegistrationResult on first ownership commit, 200 authorized replay; status explicitly PendingCredential or Ready |
| POST /auth/register/setup | Separate setup challenge through verified-conflict proof; generic 202; no public GET-by-registrationId |
| POST /auth/register/complete | 200 Ready CompletionResult or 202 PendingCredential CompletionResult; no Session tokens |
| POST /auth/login | Explicit credential login after Ready; Person and SessionTokens on success |
| GET /me | Authenticated self profile; not a public Person lookup |
| POST /auth/logout | Authenticated Session termination; 204 |

RegistrationResult contains registrationId, status and ownershipCommittedAt; readyAt only when Ready. CompletionResult also reports credentialOutcome; Ready cannot have Undetermined. A 201 here means ownership creation, not login, and must be interpreted together with status. Before proof, uniqueness is not disclosed; after proof, CONTACT_UNAVAILABLE may direct RequestSetup or SignIn. Verification failure and unknown/expired proof share safe outward failure; existing account/password/pending login failures share AUTHENTICATION_FAILED. Secret-bearing requests/results are no-store, over TLS, never in URL/log/audit.

The client preserves bound retry inputs within their validity, presents verification and Pending separately, and signs in explicitly after Ready. If the proof window ends before completion, it uses the verified-conflict/setup path rather than an unauthenticated status endpoint. Lost acknowledgment is an operations issue, not a reason to re-register. Refresh/profile/ChangePassword remain in the module command catalog; their deployment policy conflicts are tracked separately, not resolved by this registration milestone.

## 7. Event semantics and consumer boundary

PersonRegistered OccurredAt is Ready commit, OwnershipCommittedAt is original ownership commit. Email is optional; no Mobile snapshot or SessionReference is added. ActorIdentity/ExecutionContext describe the actual Ready trigger; automated/admin workers use System, with operator identity in restricted audit. Initial request IP/device must not be falsely attributed to a worker.

OrganizationCreated and MembershipCreated follow ownership commit even if Ready never occurs. They are not proof of a usable Credential/login. A future ownership-commit signal must not move PersonRegistered backward. Keep the ten-event catalog; internal provisioning/ack/audit are not extra public events. LoginFailed is an Identity Security Event, not a successful state transition.

Current self-profile commands need an authenticated Session, hence Ready commits before their PersonUpdated. This proves causal commit order only. T16 publication/application ordering is still open; neither a shared allocator nor unordered consumption is accepted here. Kimia's registration UI is an API consumer; it is not automatically a subscriber to either event. First milestone scope does not silently remove durable event production obligations.
