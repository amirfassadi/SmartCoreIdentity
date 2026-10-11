BEGIN;
CREATE TABLE auth_reset_decoys (
 operation_id uuid PRIMARY KEY, id text NOT NULL UNIQUE, client_id text NOT NULL,
 request_mac bytea NOT NULL CHECK(octet_length(request_mac)=32),
 created_at timestamptz NOT NULL, expires_at timestamptz NOT NULL,
 CHECK(expires_at=created_at+interval '600 seconds')
);
CREATE INDEX auth_reset_decoy_expiry ON auth_reset_decoys(expires_at);
DROP TRIGGER auth_reset_challenge_immutable ON auth_reset_challenges;
INSERT INTO auth_reset_decoys SELECT operation_id,id,client_id,request_mac,created_at,expires_at
 FROM auth_reset_challenges WHERE person_id IS NULL;
DELETE FROM auth_reset_challenges WHERE person_id IS NULL;
ALTER TABLE auth_reset_challenges ALTER COLUMN person_id SET NOT NULL;
ALTER TABLE auth_reset_challenges ALTER COLUMN observed_epoch SET NOT NULL;
ALTER TABLE auth_reset_challenges ALTER COLUMN recovery_version SET NOT NULL;
CREATE TRIGGER auth_reset_challenge_immutable BEFORE UPDATE ON auth_reset_challenges
 FOR EACH ROW EXECUTE FUNCTION credential_preserve_receipt();
ALTER TABLE auth_reset_delivery DROP CONSTRAINT auth_reset_delivery_challenge_id_fkey;
ALTER TABLE auth_reset_delivery ADD FOREIGN KEY(challenge_id) REFERENCES auth_reset_challenges(id) ON DELETE CASCADE;
CREATE INDEX auth_reset_challenge_expiry ON auth_reset_challenges(expires_at);
CREATE TABLE auth_reset_failures (
 person_id uuid PRIMARY KEY REFERENCES persons(id), window_start timestamptz NOT NULL,
 failures integer NOT NULL CHECK(failures BETWEEN 0 AND 5)
);
CREATE TABLE auth_reset_attempts (
 challenge_id text PRIMARY KEY REFERENCES auth_reset_challenges(id) ON DELETE CASCADE,
 attempts integer NOT NULL CHECK(attempts BETWEEN 1 AND 5)
);
ALTER TABLE auth_change_material ALTER COLUMN session_id DROP NOT NULL;
CREATE TABLE auth_reset_acceptances (
 intent_id uuid PRIMARY KEY REFERENCES auth_credential_change_intents(id),
 challenge_id text NOT NULL UNIQUE REFERENCES auth_reset_challenges(id), recovery_version bigint NOT NULL,
 proof_mac bytea NOT NULL CHECK(octet_length(proof_mac)=32)
);
CREATE TRIGGER auth_reset_acceptance_immutable BEFORE UPDATE OR DELETE ON auth_reset_acceptances
 FOR EACH ROW EXECUTE FUNCTION credential_preserve_receipt();
ALTER TABLE auth_recovery_codes ALTER COLUMN verifier DROP NOT NULL;
ALTER TABLE auth_recovery_codes ADD COLUMN reserved_intent_id uuid REFERENCES auth_credential_change_intents(id);
ALTER TABLE auth_recovery_codes ADD COLUMN consumed_at timestamptz;
ALTER TABLE auth_recovery_codes ADD COLUMN consumed_operation_id uuid REFERENCES auth_credential_change_intents(id);
ALTER TABLE auth_recovery_codes ADD CHECK((verifier IS NULL)=(consumed_at IS NOT NULL));
ALTER TABLE auth_recovery_codes ADD CHECK((consumed_at IS NULL)=(consumed_operation_id IS NULL));
ALTER TABLE auth_recovery_codes ADD CHECK(consumed_at IS NULL OR reserved_intent_id IS NULL);
CREATE OR REPLACE FUNCTION auth_preserve_recovery_version() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF NEW.person_id IS DISTINCT FROM OLD.person_id THEN RAISE EXCEPTION 'Immutable recovery Person' USING ERRCODE='23514'; END IF;
 IF NEW.version=OLD.version+1 AND OLD.version<9223372036854775807 AND OLD.reserved_intent_id IS NULL
   AND NEW.enrolled_epoch>=OLD.enrolled_epoch AND NEW.enrolled_at>=OLD.enrolled_at
   AND NEW.verifier IS NOT NULL AND NEW.reserved_intent_id IS NULL AND NEW.consumed_at IS NULL AND NEW.consumed_operation_id IS NULL THEN
   IF OLD.consumed_operation_id IS NOT NULL AND NOT EXISTS (
     SELECT 1 FROM auth_sessions s JOIN auth_credential_change_intents i ON i.id=OLD.consumed_operation_id
     WHERE s.id=NEW.enrollment_session_id AND s.person_id=NEW.person_id AND s.status='Active'
       AND s.issuance_epoch>=i.target_epoch AND NEW.enrolled_epoch>=i.target_epoch) THEN
     RAISE EXCEPTION 'Replacement requires a fresh post-reset Session' USING ERRCODE='23514';
   END IF;
   RETURN NEW;
 END IF;
 IF (NEW.version,NEW.enrolled_epoch,NEW.enrollment_session_id,NEW.enrolled_at)
   IS DISTINCT FROM (OLD.version,OLD.enrolled_epoch,OLD.enrollment_session_id,OLD.enrolled_at) THEN
   RAISE EXCEPTION 'Immutable recovery enrollment' USING ERRCODE='23514';
 END IF;
 IF OLD.reserved_intent_id IS NULL AND OLD.verifier IS NOT NULL AND NEW.reserved_intent_id IS NOT NULL
   AND NEW.verifier IS NOT DISTINCT FROM OLD.verifier AND NEW.consumed_at IS NULL AND NEW.consumed_operation_id IS NULL
   AND EXISTS(SELECT 1 FROM auth_reset_acceptances a JOIN auth_credential_change_intents i ON i.id=a.intent_id
     WHERE a.intent_id=NEW.reserved_intent_id AND i.person_id=NEW.person_id AND a.recovery_version=NEW.version) THEN RETURN NEW; END IF;
 IF OLD.reserved_intent_id IS NOT NULL AND NEW.reserved_intent_id IS NULL
   AND EXISTS(SELECT 1 FROM auth_credential_change_intents i JOIN credential_change_receipts r ON r.operation_id=i.credential_operation_id
     WHERE i.id=OLD.reserved_intent_id AND i.stage='Reconciled' AND r.person_id=NEW.person_id
       AND ((NEW.verifier IS NULL AND r.outcome='Applied') OR (NEW.verifier IS NOT NULL AND r.outcome='RejectedNotApplied')))
   AND ((NEW.verifier IS NULL AND NEW.consumed_at IS NOT NULL AND NEW.consumed_operation_id=OLD.reserved_intent_id)
      OR (NEW.verifier IS NOT DISTINCT FROM OLD.verifier AND NEW.consumed_at IS NULL AND NEW.consumed_operation_id IS NULL)) THEN RETURN NEW; END IF;
 RAISE EXCEPTION 'Invalid recovery reservation/consumption' USING ERRCODE='23514';
END $$;
ALTER TABLE auth_account_notifications DROP CONSTRAINT auth_account_notifications_kind_check;
ALTER TABLE auth_account_notifications ADD CHECK(kind IN ('RecoveryCodeRegistered','RecoveryCodeReplaced','PasswordResetCompleted','ResetRequestsLimited'));
INSERT INTO schema_versions(version) VALUES(9);
COMMIT;
