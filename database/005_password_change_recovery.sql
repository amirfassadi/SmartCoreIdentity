-- Development password-change coordinator. Credential receipts are independent commits.
BEGIN;
CREATE TABLE auth_change_material (
 intent_id uuid PRIMARY KEY REFERENCES auth_credential_change_intents(id),
 session_id uuid NOT NULL REFERENCES auth_sessions(id), client_id text NOT NULL,
 expected_hash_mac bytea NOT NULL CHECK(octet_length(expected_hash_mac)=32),
 replacement_hash_mac bytea NOT NULL CHECK(octet_length(replacement_hash_mac)=32),
 sealed_hash bytea, expires_at timestamptz NOT NULL
);
CREATE TABLE credential_change_receipts (
 operation_id uuid PRIMARY KEY, person_id uuid NOT NULL REFERENCES persons(id),
 credential_id uuid NOT NULL REFERENCES credentials(id), request_mac bytea NOT NULL CHECK(octet_length(request_mac)=32),
 expected_hash_mac bytea NOT NULL CHECK(octet_length(expected_hash_mac)=32),
 replacement_hash_mac bytea NOT NULL CHECK(octet_length(replacement_hash_mac)=32),
 outcome text NOT NULL CHECK(outcome IN ('Applied','RejectedNotApplied')), occurred_at timestamptz NOT NULL
);
CREATE TABLE credential_change_outbox (
 id uuid PRIMARY KEY, operation_id uuid NOT NULL UNIQUE REFERENCES credential_change_receipts(operation_id),
 payload jsonb NOT NULL, occurred_at timestamptz NOT NULL, published_at timestamptz
);
CREATE TABLE auth_change_operator_audit (
 id uuid PRIMARY KEY, intent_id uuid NOT NULL REFERENCES auth_credential_change_intents(id),
 action text NOT NULL CHECK(action IN ('Retry','ResolveNotApplied')), occurred_at timestamptz NOT NULL
);
CREATE FUNCTION auth_preserve_change_binding() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF (NEW.id,NEW.person_id,NEW.kind,NEW.request_mac,NEW.verified_at,NEW.created_at,NEW.target_epoch,
     NEW.credential_operation_id,NEW.expected_credential_id,NEW.protected_material_reference)
   IS DISTINCT FROM (OLD.id,OLD.person_id,OLD.kind,OLD.request_mac,OLD.verified_at,OLD.created_at,OLD.target_epoch,
     OLD.credential_operation_id,OLD.expected_credential_id,OLD.protected_material_reference)
   OR (OLD.stage='Reconciled' AND NEW IS DISTINCT FROM OLD) THEN
   RAISE EXCEPTION 'Immutable change binding' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER auth_change_binding_immutable BEFORE UPDATE ON auth_credential_change_intents
 FOR EACH ROW EXECUTE FUNCTION auth_preserve_change_binding();
CREATE FUNCTION auth_preserve_change_material() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF (NEW.intent_id,NEW.session_id,NEW.client_id,NEW.expected_hash_mac,NEW.replacement_hash_mac,NEW.expires_at)
   IS DISTINCT FROM (OLD.intent_id,OLD.session_id,OLD.client_id,OLD.expected_hash_mac,OLD.replacement_hash_mac,OLD.expires_at)
   OR (NEW.sealed_hash IS NOT NULL AND NEW.sealed_hash IS DISTINCT FROM OLD.sealed_hash) THEN
   RAISE EXCEPTION 'Immutable change material' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER auth_change_material_immutable BEFORE UPDATE ON auth_change_material
 FOR EACH ROW EXECUTE FUNCTION auth_preserve_change_material();
CREATE FUNCTION credential_preserve_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 RAISE EXCEPTION 'Immutable Credential outcome' USING ERRCODE='23514';
END $$;
CREATE TRIGGER credential_receipt_immutable BEFORE UPDATE OR DELETE ON credential_change_receipts
 FOR EACH ROW EXECUTE FUNCTION credential_preserve_receipt();
INSERT INTO schema_versions(version) VALUES(5);
COMMIT;
