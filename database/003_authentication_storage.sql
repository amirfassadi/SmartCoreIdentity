-- Session/coordination storage only; no public authentication or password endpoint is enabled.
-- Session module owns these writes. Credential replacements remain independent commits.
BEGIN;
CREATE TABLE auth_credential_change_intents (
 id uuid PRIMARY KEY, person_id uuid NOT NULL REFERENCES persons(id),
 kind text NOT NULL CHECK(kind IN ('ChangePassword','ResetPassword')),
 request_mac bytea NOT NULL CHECK(octet_length(request_mac)=32),
 verified_at timestamptz NOT NULL, created_at timestamptz NOT NULL,
 target_epoch bigint NOT NULL CHECK(target_epoch>0),
 credential_operation_id uuid NOT NULL UNIQUE,
 expected_credential_id uuid NOT NULL,
 protected_material_reference text NOT NULL CHECK(length(protected_material_reference) BETWEEN 1 AND 256),
 stage text NOT NULL CHECK(stage IN ('Fenced','AwaitingCredential','Reconciled','FailedClosed')),
 attempts integer NOT NULL DEFAULT 0 CHECK(attempts>=0),
 next_attempt_at timestamptz NOT NULL, last_classification text
   CHECK(last_classification IN ('DependencyUnavailable','OutcomeUnknown','IntegrityConflict','DefinitivelyRejected')),
 resolved_at timestamptz,
 UNIQUE(person_id,id,target_epoch), UNIQUE(person_id,target_epoch),
 CHECK(verified_at<=created_at),
 CHECK((stage IN ('Reconciled','FailedClosed'))=(resolved_at IS NOT NULL))
);
CREATE INDEX auth_change_due ON auth_credential_change_intents(next_attempt_at)
 WHERE stage IN ('Fenced','AwaitingCredential');
CREATE TABLE auth_issuance_state (
 person_id uuid PRIMARY KEY REFERENCES persons(id),
 epoch bigint NOT NULL DEFAULT 0 CHECK(epoch>=0), pending_operation_id uuid,
 FOREIGN KEY(person_id,pending_operation_id,epoch) REFERENCES auth_credential_change_intents(person_id,id,target_epoch)
);
CREATE TABLE auth_sessions (
 id uuid PRIMARY KEY, person_id uuid NOT NULL REFERENCES persons(id),
 issuance_epoch bigint NOT NULL CHECK(issuance_epoch>=0),
 client_id text NOT NULL CHECK(length(client_id) BETWEEN 1 AND 100),
 created_at timestamptz NOT NULL, expires_at timestamptz NOT NULL,
 last_foreground_refresh_at timestamptz NOT NULL,
 status text NOT NULL CHECK(status IN ('Active','Closed','Expired')),
 closed_at timestamptz, close_reason text CHECK(close_reason IN
   ('Logout','RefreshReuse','PasswordChanged','PasswordReset','IdleDeadline','AbsoluteDeadline','EligibilityLost')),
 UNIQUE(id,expires_at),
 CHECK(expires_at=created_at+interval '86400 seconds'),
 CHECK(last_foreground_refresh_at>=created_at AND last_foreground_refresh_at<expires_at),
 CHECK((status='Active' AND closed_at IS NULL AND close_reason IS NULL)
    OR (status<>'Active' AND closed_at IS NOT NULL AND close_reason IS NOT NULL AND closed_at>=created_at))
);
CREATE INDEX auth_sessions_person_active ON auth_sessions(person_id) WHERE status='Active';
CREATE TABLE auth_refresh_families (
 id uuid PRIMARY KEY, session_id uuid NOT NULL UNIQUE,
 expires_at timestamptz NOT NULL, current_generation bigint NOT NULL CHECK(current_generation>=0),
 status text NOT NULL CHECK(status IN ('Active','Revoked')), revoked_at timestamptz,
 UNIQUE(id,expires_at), UNIQUE(session_id,id),
 FOREIGN KEY(session_id,expires_at) REFERENCES auth_sessions(id,expires_at),
 CHECK((status='Revoked')=(revoked_at IS NOT NULL))
);
CREATE TABLE auth_refresh_generations (
 family_id uuid NOT NULL, generation bigint NOT NULL CHECK(generation>=0),
 verifier bytea NOT NULL CHECK(octet_length(verifier)=32),
 key_id text NOT NULL CHECK(length(key_id) BETWEEN 1 AND 100),
 status text NOT NULL CHECK(status IN ('Current','Consumed')),
 issued_at timestamptz NOT NULL, consumed_at timestamptz, recognition_until timestamptz NOT NULL,
 PRIMARY KEY(family_id,generation),
 FOREIGN KEY(family_id,recognition_until) REFERENCES auth_refresh_families(id,expires_at),
 CHECK(issued_at<recognition_until),
 CHECK((status='Current' AND consumed_at IS NULL)
   OR (status='Consumed' AND consumed_at IS NOT NULL AND consumed_at>=issued_at AND consumed_at<recognition_until))
);
CREATE UNIQUE INDEX auth_one_current_refresh ON auth_refresh_generations(family_id) WHERE status='Current';
-- Fixed fields only: no arbitrary JSON payload, token, verifier, hash, proof or provider error.
-- Retention for audit is separately governed; recognition_until is not audit retention.
CREATE TABLE auth_refresh_security_events (
 id uuid PRIMARY KEY, session_id uuid NOT NULL REFERENCES auth_sessions(id),
 family_id uuid NOT NULL REFERENCES auth_refresh_families(id),
 generation bigint NOT NULL CHECK(generation>=0), occurred_at timestamptz NOT NULL,
 type text NOT NULL CHECK(type IN ('RefreshTokenRotated','RefreshTokenReuseDetected')),
 reason text NOT NULL,
 FOREIGN KEY(session_id,family_id) REFERENCES auth_refresh_families(session_id,id),
 CHECK((type='RefreshTokenRotated' AND reason='RefreshAccepted' AND generation>=1)
   OR (type='RefreshTokenReuseDetected' AND reason='PreviouslyConsumedGeneration'))
);
CREATE FUNCTION auth_preserve_session() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF (NEW.id,NEW.person_id,NEW.issuance_epoch,NEW.client_id,NEW.created_at,NEW.expires_at)
   IS DISTINCT FROM (OLD.id,OLD.person_id,OLD.issuance_epoch,OLD.client_id,OLD.created_at,OLD.expires_at)
   OR NEW.last_foreground_refresh_at<OLD.last_foreground_refresh_at
   OR (OLD.status<>'Active' AND NEW.status IS DISTINCT FROM OLD.status) THEN
   RAISE EXCEPTION 'Immutable authentication state' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER auth_session_immutable BEFORE UPDATE ON auth_sessions FOR EACH ROW EXECUTE FUNCTION auth_preserve_session();
CREATE FUNCTION auth_preserve_family() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF (NEW.id,NEW.session_id,NEW.expires_at) IS DISTINCT FROM (OLD.id,OLD.session_id,OLD.expires_at)
   OR NEW.current_generation<OLD.current_generation OR (OLD.status='Revoked' AND NEW.status<>'Revoked') THEN
   RAISE EXCEPTION 'Immutable authentication state' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER auth_family_immutable BEFORE UPDATE ON auth_refresh_families FOR EACH ROW EXECUTE FUNCTION auth_preserve_family();
CREATE FUNCTION auth_preserve_generation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF (NEW.family_id,NEW.generation,NEW.verifier,NEW.key_id,NEW.issued_at,NEW.recognition_until)
   IS DISTINCT FROM (OLD.family_id,OLD.generation,OLD.verifier,OLD.key_id,OLD.issued_at,OLD.recognition_until)
   OR (OLD.status='Consumed' AND NEW.status<>'Consumed') THEN
   RAISE EXCEPTION 'Immutable authentication state' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER auth_generation_immutable BEFORE UPDATE ON auth_refresh_generations FOR EACH ROW EXECUTE FUNCTION auth_preserve_generation();
CREATE FUNCTION auth_preserve_issuance_epoch() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF NEW.person_id<>OLD.person_id OR NEW.epoch<OLD.epoch
   OR (OLD.pending_operation_id IS NOT NULL AND NEW.pending_operation_id IS NOT NULL
       AND NEW.pending_operation_id<>OLD.pending_operation_id)
   OR (OLD.pending_operation_id IS NULL AND NEW.pending_operation_id IS NOT NULL AND NEW.epoch<>OLD.epoch+1)
   OR (OLD.pending_operation_id IS NOT NULL AND NEW.epoch<>OLD.epoch)
   OR (NEW.pending_operation_id IS NULL AND NEW.epoch<>OLD.epoch) THEN
   RAISE EXCEPTION 'Immutable authentication state' USING ERRCODE='23514';
 END IF;
 IF OLD.pending_operation_id IS NOT NULL AND NEW.pending_operation_id IS NULL
   AND NOT EXISTS(SELECT 1 FROM auth_credential_change_intents WHERE id=OLD.pending_operation_id AND stage='Reconciled') THEN
   RAISE EXCEPTION 'Unresolved Credential operation' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER auth_issuance_epoch_monotonic BEFORE UPDATE ON auth_issuance_state FOR EACH ROW EXECUTE FUNCTION auth_preserve_issuance_epoch();
INSERT INTO schema_versions(version) VALUES(3);
COMMIT;
