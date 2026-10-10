BEGIN;
ALTER TABLE auth_credential_change_intents ADD COLUMN request_version smallint NOT NULL DEFAULT 1 CHECK(request_version IN (1,2));
ALTER TABLE auth_change_material ADD COLUMN replay_password_hash text;
ALTER TABLE auth_change_material ADD COLUMN replay_until timestamptz;
ALTER TABLE auth_change_material ADD CHECK(replay_password_hash IS NULL OR replay_until IS NOT NULL);
ALTER TABLE auth_change_operator_audit ADD COLUMN effective_outcome text NOT NULL DEFAULT 'LegacyUnspecified'
 CHECK(effective_outcome IN ('LegacyUnspecified','Requeued','Applied','RejectedNotApplied'));
-- One-time removal of legacy raw-password HMAC oracles. Preserve linked operation outcomes.
-- Legacy intents continue recovery but are not eligible for HTTP request replay.
ALTER TABLE auth_credential_change_intents DISABLE TRIGGER auth_change_binding_immutable;
ALTER TABLE credential_change_receipts DISABLE TRIGGER credential_receipt_immutable;
WITH changed AS (
 UPDATE auth_credential_change_intents SET request_mac=uuid_send(gen_random_uuid())||uuid_send(gen_random_uuid())
 RETURNING credential_operation_id,request_mac
) UPDATE credential_change_receipts r SET request_mac=c.request_mac FROM changed c WHERE r.operation_id=c.credential_operation_id;
ALTER TABLE auth_credential_change_intents ENABLE TRIGGER auth_change_binding_immutable;
ALTER TABLE credential_change_receipts ENABLE TRIGGER credential_receipt_immutable;
CREATE OR REPLACE FUNCTION auth_preserve_change_binding() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF (NEW.id,NEW.person_id,NEW.kind,NEW.request_mac,NEW.request_version,NEW.verified_at,NEW.created_at,NEW.target_epoch,
     NEW.credential_operation_id,NEW.expected_credential_id,NEW.protected_material_reference)
   IS DISTINCT FROM (OLD.id,OLD.person_id,OLD.kind,OLD.request_mac,OLD.request_version,OLD.verified_at,OLD.created_at,OLD.target_epoch,
     OLD.credential_operation_id,OLD.expected_credential_id,OLD.protected_material_reference)
   OR (OLD.stage='Reconciled' AND NEW IS DISTINCT FROM OLD) THEN
   RAISE EXCEPTION 'Immutable change binding' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE OR REPLACE FUNCTION auth_preserve_change_material() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF (NEW.intent_id,NEW.session_id,NEW.client_id,NEW.expected_hash_mac,NEW.replacement_hash_mac,NEW.expires_at,NEW.replay_until)
   IS DISTINCT FROM (OLD.intent_id,OLD.session_id,OLD.client_id,OLD.expected_hash_mac,OLD.replacement_hash_mac,OLD.expires_at,OLD.replay_until)
   OR (NEW.sealed_hash IS NOT NULL AND NEW.sealed_hash IS DISTINCT FROM OLD.sealed_hash)
   OR (NEW.replay_password_hash IS NOT NULL AND NEW.replay_password_hash IS DISTINCT FROM OLD.replay_password_hash) THEN
   RAISE EXCEPTION 'Immutable change material' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TABLE auth_password_failures (
 person_id uuid PRIMARY KEY REFERENCES persons(id), failures integer NOT NULL CHECK(failures BETWEEN 0 AND 100),
 last_failure_at timestamptz, blocked_until timestamptz NOT NULL
);
CREATE TABLE auth_admission_buckets (
 bucket_key bytea PRIMARY KEY CHECK(octet_length(bucket_key)=32),
 window_start timestamptz NOT NULL, hits integer NOT NULL CHECK(hits>=1)
);
CREATE INDEX auth_admission_expiry ON auth_admission_buckets(window_start);
INSERT INTO schema_versions(version) VALUES(6);
COMMIT;
