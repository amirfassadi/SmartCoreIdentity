BEGIN;
CREATE TABLE auth_recovery_codes (
 person_id uuid PRIMARY KEY REFERENCES persons(id),
 version bigint NOT NULL CHECK(version>0), enrolled_epoch bigint NOT NULL CHECK(enrolled_epoch>=0),
 enrollment_session_id uuid NOT NULL REFERENCES auth_sessions(id),
 verifier bytea NOT NULL CHECK(octet_length(verifier)=32), enrolled_at timestamptz NOT NULL
);
CREATE TABLE auth_recovery_enrollments (
 operation_id uuid PRIMARY KEY, person_id uuid NOT NULL REFERENCES persons(id),
 session_id uuid NOT NULL REFERENCES auth_sessions(id), client_id text NOT NULL,
 version bigint NOT NULL CHECK(version>0), enrolled_epoch bigint NOT NULL CHECK(enrolled_epoch>=0),
 request_mac bytea NOT NULL CHECK(octet_length(request_mac)=32), occurred_at timestamptz NOT NULL,
 UNIQUE(person_id,version)
);
-- Internal delivery tasks, not a public domain-event schema extension.
-- Templates contain no recovery code, verifier, password or proof.
CREATE TABLE auth_account_notifications (
 id uuid PRIMARY KEY, person_id uuid NOT NULL REFERENCES persons(id), operation_id uuid NOT NULL,
 kind text NOT NULL CHECK(kind IN ('RecoveryCodeRegistered','RecoveryCodeReplaced','PasswordResetCompleted')),
 created_at timestamptz NOT NULL, delivered_at timestamptz,
 UNIQUE(operation_id,kind)
);
CREATE TABLE auth_reset_initiation_budgets (
 person_id uuid PRIMARY KEY REFERENCES persons(id), window_start timestamptz NOT NULL,
 hits integer NOT NULL CHECK(hits BETWEEN 1 AND 4)
);
CREATE TABLE auth_reset_challenges (
 id text PRIMARY KEY CHECK(length(id)=43), operation_id uuid NOT NULL UNIQUE,
 person_id uuid REFERENCES persons(id), client_id text NOT NULL,
 request_mac bytea NOT NULL CHECK(octet_length(request_mac)=32), binding_mac bytea NOT NULL CHECK(octet_length(binding_mac)=32),
 observed_epoch bigint, recovery_version bigint,
 created_at timestamptz NOT NULL, expires_at timestamptz NOT NULL,
 CHECK(expires_at=created_at+interval '600 seconds'),
 CHECK((person_id IS NULL AND observed_epoch IS NULL AND recovery_version IS NULL)
    OR (person_id IS NOT NULL AND observed_epoch IS NOT NULL AND recovery_version IS NOT NULL AND observed_epoch>=0 AND recovery_version>0))
);
CREATE TABLE auth_reset_delivery (
 id uuid PRIMARY KEY, challenge_id text NOT NULL UNIQUE REFERENCES auth_reset_challenges(id),
 person_id uuid NOT NULL REFERENCES persons(id), sealed_code bytea,
 verifier bytea NOT NULL CHECK(octet_length(verifier)=32), created_at timestamptz NOT NULL, expires_at timestamptz NOT NULL,
 CHECK(expires_at=created_at+interval '600 seconds')
);
CREATE TRIGGER auth_reset_challenge_immutable BEFORE UPDATE OR DELETE ON auth_reset_challenges
 FOR EACH ROW EXECUTE FUNCTION credential_preserve_receipt();
CREATE FUNCTION auth_preserve_recovery_version() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF NEW.person_id IS DISTINCT FROM OLD.person_id OR OLD.version=9223372036854775807
   OR NEW.version<>OLD.version+1 OR NEW.enrolled_epoch<OLD.enrolled_epoch OR NEW.enrolled_at<OLD.enrolled_at THEN
   RAISE EXCEPTION 'Recovery replacement requires a new monotonic version' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER auth_recovery_version_immutable BEFORE UPDATE ON auth_recovery_codes
 FOR EACH ROW EXECUTE FUNCTION auth_preserve_recovery_version();
CREATE TRIGGER auth_recovery_no_version_reset BEFORE DELETE ON auth_recovery_codes
 FOR EACH ROW EXECUTE FUNCTION credential_preserve_receipt();
CREATE TRIGGER auth_recovery_enrollment_immutable BEFORE UPDATE OR DELETE ON auth_recovery_enrollments
 FOR EACH ROW EXECUTE FUNCTION credential_preserve_receipt();
INSERT INTO schema_versions(version) VALUES(8);
COMMIT;
