BEGIN;
ALTER TABLE bff_refresh_circuit ADD COLUMN next_probe_at timestamptz, ADD COLUMN probe_session uuid;
UPDATE bff_refresh_circuit SET next_probe_at=opened_at+interval '60 seconds' WHERE opened_at IS NOT NULL;
ALTER TABLE bff_sessions DROP CONSTRAINT bff_sessions_status_check;
ALTER TABLE bff_sessions DROP CONSTRAINT bff_sessions_check1;
ALTER TABLE bff_sessions ADD CHECK(status IN ('Active','LogoutRequested','Closed','ReauthenticationRequired'));
ALTER TABLE bff_sessions ADD CHECK((status IN ('Active','LogoutRequested'))=(sealed_tokens IS NOT NULL));
CREATE OR REPLACE FUNCTION bff_session_monotonic() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF NEW.id<>OLD.id OR NEW.cookie_mac<>OLD.cookie_mac OR NEW.identity_session_id<>OLD.identity_session_id
 OR NEW.expires_at<>OLD.expires_at OR NEW.version<OLD.version OR NEW.last_foreground_at<OLD.last_foreground_at
 OR (OLD.status='LogoutRequested' AND NEW.status NOT IN ('LogoutRequested','Closed'))
 OR (OLD.status IN ('Closed','ReauthenticationRequired') AND NEW.status<>OLD.status)
 THEN RAISE EXCEPTION 'immutable BFF session evidence' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
INSERT INTO bff_schema_versions VALUES(4);
COMMIT;
