-- Existing plaintext replay verifiers are discarded, never used after upgrade.
-- Independent encrypted recovery material and immutable receipts remain intact.
BEGIN;
ALTER TABLE auth_change_material DROP COLUMN replay_password_hash;
ALTER TABLE auth_change_material ADD COLUMN sealed_replay_hash bytea;
ALTER TABLE auth_change_material ADD CHECK(sealed_replay_hash IS NULL OR
 (replay_until IS NOT NULL AND octet_length(sealed_replay_hash)>28));
CREATE OR REPLACE FUNCTION auth_preserve_change_material() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF (NEW.intent_id,NEW.session_id,NEW.client_id,NEW.expected_hash_mac,NEW.replacement_hash_mac,NEW.expires_at,NEW.replay_until)
   IS DISTINCT FROM (OLD.intent_id,OLD.session_id,OLD.client_id,OLD.expected_hash_mac,OLD.replacement_hash_mac,OLD.expires_at,OLD.replay_until)
   OR (NEW.sealed_hash IS NOT NULL AND NEW.sealed_hash IS DISTINCT FROM OLD.sealed_hash)
   OR (NEW.sealed_replay_hash IS NOT NULL AND NEW.sealed_replay_hash IS DISTINCT FROM OLD.sealed_replay_hash) THEN
   RAISE EXCEPTION 'Immutable change material' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
INSERT INTO schema_versions(version) VALUES(7);
COMMIT;
